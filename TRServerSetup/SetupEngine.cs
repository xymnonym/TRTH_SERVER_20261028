using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;

namespace TRServerSetup
{
	/// <summary>Everything the setup does, with no reference to the UI.</summary>
	internal sealed class SetupEngine
	{
		public const string GameDb = "tr_game_db";
		public const string LogDb = "tr_game_db_log";

		/// <summary>Keys in settings.ini that hold the address handed to clients.</summary>
		private static readonly string[] AddressKeys = { "AgentServerIP", "RelayAdvertiseIP", "RoomServerIP" };

		private readonly Action<string> _log;
		private readonly Action<int> _progress;

		public SetupEngine(Action<string> log, Action<int> progress)
		{
			_log = log;
			_progress = progress;
		}

		public sealed class Options
		{
			public string BundleRoot;
			public string MysqlExe;
			public string GameScript;
			public string LogScript;
			public string DbHost;
			public int DbPort;
			public string DbUser;
			public string DbPassword;
			public string PublicIp;
			public bool DropExisting;
		}

		// ---------- discovery ----------

		/// <summary>
		/// Walks up from the executable looking for the folder that holds
		/// settings.ini, so the tool works from the bundle root or a subfolder.
		/// </summary>
		public static string FindBundleRoot()
		{
			DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			for (int i = 0; i < 4 && dir != null; i++)
			{
				if (File.Exists(Path.Combine(dir.FullName, "settings.ini"))
					|| File.Exists(Path.Combine(dir.FullName, "AgentServer.exe")))
				{
					return dir.FullName.TrimEnd(Path.DirectorySeparatorChar);
				}
				dir = dir.Parent;
			}
			return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
		}

		/// <summary>Newest file matching any pattern, looking in database\ then the root.</summary>
		public static string FindScript(string root, params string[] patterns)
		{
			if (string.IsNullOrEmpty(root)) return null;
			string[] folders = { Path.Combine(root, "database"), root };
			foreach (string pattern in patterns)
			{
				foreach (string folder in folders)
				{
					if (!Directory.Exists(folder)) continue;
					string match = Directory.GetFiles(folder, pattern)
						.OrderByDescending(File.GetLastWriteTimeUtc)
						.FirstOrDefault();
					if (match != null) return match;
				}
			}
			return null;
		}

		public static string FindMysqlClient()
		{
			foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
			{
				if (dir.Trim().Length == 0) continue;
				string candidate;
				try { candidate = Path.Combine(dir.Trim(), "mysql.exe"); }
				catch (ArgumentException) { continue; }
				if (File.Exists(candidate)) return candidate;
			}

			List<string> bases = new List<string>();
			foreach (string pf in new[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
			})
			{
				if (string.IsNullOrEmpty(pf) || !Directory.Exists(pf)) continue;
				bases.AddRange(SafeDirectories(pf, "MariaDB*"));
				foreach (string mysqlRoot in SafeDirectories(pf, "MySQL"))
				{
					bases.AddRange(SafeDirectories(mysqlRoot, "MySQL Server*"));
				}
			}

			return bases
				.Select(b => Path.Combine(b, "bin", "mysql.exe"))
				.Where(File.Exists)
				.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault();
		}

		private static IEnumerable<string> SafeDirectories(string parent, string pattern)
		{
			try { return Directory.GetDirectories(parent, pattern); }
			catch (IOException) { return new string[0]; }
			catch (UnauthorizedAccessException) { return new string[0]; }
		}

		public static string FileSize(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
			double mb = new FileInfo(path).Length / 1024.0 / 1024.0;
			return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", mb);
		}

		// ---------- the run ----------

		public void Run(Options options)
		{
			string settingsPath = Path.Combine(options.BundleRoot, "settings.ini");
			if (!File.Exists(settingsPath))
			{
				throw new FileNotFoundException(
					"settings.ini is not in " + options.BundleRoot
					+ ". Point the bundle folder at the place that holds AgentServer.exe.");
			}
			if (string.IsNullOrEmpty(options.GameScript) || !File.Exists(options.GameScript))
			{
				throw new FileNotFoundException("The game database script was not found.");
			}
			if (string.IsNullOrEmpty(options.MysqlExe) || !File.Exists(options.MysqlExe))
			{
				throw new FileNotFoundException(
					"mysql.exe was not found. It ships with MariaDB, normally at "
					+ @"C:\Program Files\MariaDB 10.4\bin\mysql.exe");
			}

			_progress(0);

			string serverConn = BuildConnectionString(options, null);
			Step("Connecting to MariaDB at " + options.DbHost + ":" + options.DbPort + " as " + options.DbUser);
			using (MySqlConnection probe = new MySqlConnection(serverConn))
			{
				probe.Open();
				Ok("Connected to MariaDB " + probe.ServerVersion);
			}
			_progress(5);

			Step("Preparing databases");
			if (options.DropExisting)
			{
				foreach (string db in new[] { GameDb, LogDb })
				{
					if (DatabaseExists(serverConn, db))
					{
						Execute(serverConn, "DROP DATABASE `" + db + "`;");
						Ok("Dropped " + db);
					}
				}
			}
			foreach (string db in new[] { GameDb, LogDb })
			{
				Execute(serverConn,
					"CREATE DATABASE IF NOT EXISTS `" + db + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;");
			}
			Ok("Databases ready: " + GameDb + ", " + LogDb);
			_progress(10);

			string defaults = WriteDefaultsFile(options);
			try
			{
				RunImport(options.MysqlExe, defaults, GameDb, options.GameScript, 10, 80);
				if (!string.IsNullOrEmpty(options.LogScript) && File.Exists(options.LogScript))
				{
					RunImport(options.MysqlExe, defaults, LogDb, options.LogScript, 80, 88);
				}
				else
				{
					Warn("No log database script found. " + LogDb + " was created empty.");
				}
			}
			finally
			{
				TryDelete(defaults);
			}

			Step("Verifying the import");
			string gameConn = BuildConnectionString(options, GameDb);
			int tables = ScalarInt(gameConn,
				"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_type = 'BASE TABLE';",
				GameDb);
			int routines = ScalarInt(gameConn,
				"SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @db;", GameDb);
			_log("    tables   " + tables.ToString(CultureInfo.InvariantCulture));
			_log("    routines " + routines.ToString(CultureInfo.InvariantCulture));
			if (tables == 0)
			{
				throw new InvalidOperationException(
					"The import left no tables behind. Check the messages above for MariaDB errors.");
			}
			Ok("Import verified");
			_progress(92);

			Step("Rewriting addresses stored in tblServerSettingInfo");
			UpdateStoredAddresses(gameConn, options.PublicIp);
			_progress(95);

			Step("Writing settings.ini");
			SettingsResult result = PatchSettings(settingsPath, options);
			Ok("Backup saved as " + Path.GetFileName(result.BackupPath));
			if (result.Changes.Count == 0)
			{
				_log("    nothing needed changing");
			}
			else
			{
				foreach (string change in result.Changes) _log("    " + change);
			}
			_progress(100);

			Step("Done");
			_log("Start the server with START_SERVER.bat.");
			_log("It is up once Logs\\Agent\\Info\\<timestamp>_Agent.log says");
			_log("    Set Server Ready : true");
			if (!options.PublicIp.Equals("127.0.0.1", StringComparison.Ordinal))
			{
				_log("");
				_log("Open these ports on the VPS firewall: 9500 TCP for clients,");
				_log("9153 and 9499 TCP for the agent, 9155 UDP for the relay,");
				_log("9000 TCP for the community server.");
			}
		}

		// ---------- import ----------

		/// <summary>
		/// Credentials go in a temporary option file rather than the command line,
		/// so the password never shows in the process list and special characters
		/// need no quoting.
		/// </summary>
		private static string WriteDefaultsFile(Options options)
		{
			string path = Path.Combine(Path.GetTempPath(), "trsetup_" + Guid.NewGuid().ToString("N") + ".cnf");
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("[client]");
			sb.AppendLine("host=" + options.DbHost);
			sb.AppendLine("port=" + options.DbPort.ToString(CultureInfo.InvariantCulture));
			sb.AppendLine("user=" + options.DbUser);
			sb.AppendLine("password=\"" + options.DbPassword + "\"");
			sb.AppendLine("default-character-set=utf8mb4");
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
			return path;
		}

		private void RunImport(string mysqlExe, string defaultsFile, string database, string scriptPath,
			int progressFrom, int progressTo)
		{
			FileInfo info = new FileInfo(scriptPath);
			Step(string.Format(CultureInfo.InvariantCulture, "Importing {0} into {1} ({2:0.0} MB)",
				Path.GetFileName(scriptPath), database, info.Length / 1024.0 / 1024.0));

			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = mysqlExe,
				Arguments = "--defaults-extra-file=\"" + defaultsFile + "\" " + database,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardError = true
			};

			Stopwatch clock = Stopwatch.StartNew();
			using (Process proc = Process.Start(psi))
			{
				StringBuilder errors = new StringBuilder();
				proc.ErrorDataReceived += (s, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data)) errors.AppendLine(e.Data);
				};
				proc.BeginErrorReadLine();

				using (FileStream input = File.OpenRead(scriptPath))
				using (Stream stdin = proc.StandardInput.BaseStream)
				{
					byte[] buffer = new byte[1 << 20];
					long copied = 0;
					int lastReported = -1;
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						stdin.Write(buffer, 0, read);
						copied += read;
						int percent = progressFrom
							+ (int)((progressTo - progressFrom) * copied / Math.Max(1L, info.Length));
						if (percent != lastReported)
						{
							lastReported = percent;
							_progress(percent);
						}
					}
					stdin.Flush();
				}

				proc.WaitForExit();

				if (proc.ExitCode != 0)
				{
					throw new InvalidOperationException(
						"mysql.exe failed on " + Path.GetFileName(scriptPath)
						+ " with exit code " + proc.ExitCode + Environment.NewLine + errors);
				}
				if (errors.Length > 0)
				{
					foreach (string line in errors.ToString()
						.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
					{
						Warn(line);
					}
				}
			}
			_progress(progressTo);
			Ok(string.Format(CultureInfo.InvariantCulture, "{0} imported in {1:0} s",
				database, clock.Elapsed.TotalSeconds));
		}

		// ---------- addresses stored in the database ----------

		/// <summary>
		/// tblServerSettingInfo carries its own copy of the server address, and
		/// RelayServerIP goes out to the client in GET_SERVERSETTING_INFO_ACK.
		/// Every row still holding 127.0.0.1 after the import is moved to the
		/// address clients connect to, and RelayServerIP is written even when the
		/// row is missing. AgentServer refreshes RelayServerIP from settings.ini
		/// on every start as well, so this mainly fixes the value a client could
		/// read before the first start.
		/// </summary>
		private void UpdateStoredAddresses(string gameConn, string publicIp)
		{
			if (publicIp.Equals("127.0.0.1", StringComparison.Ordinal))
			{
				_log("    nothing to do, the public IP is 127.0.0.1");
				return;
			}

			List<string> keys = new List<string>();
			using (MySqlConnection conn = new MySqlConnection(gameConn))
			{
				conn.Open();
				using (MySqlCommand find = new MySqlCommand(
					"SELECT fdKey FROM tblServerSettingInfo WHERE fdValue = '127.0.0.1';", conn))
				using (MySqlDataReader reader = find.ExecuteReader())
				{
					while (reader.Read()) keys.Add(reader.GetString(0));
				}

				using (MySqlCommand update = new MySqlCommand(
					"UPDATE tblServerSettingInfo SET fdValue = @ip WHERE fdValue = '127.0.0.1';", conn))
				{
					update.Parameters.AddWithValue("@ip", publicIp);
					update.ExecuteNonQuery();
				}

				// RelayServerIP reaches the client, so make sure the row exists.
				using (MySqlCommand relay = new MySqlCommand(
					"UPDATE tblServerSettingInfo SET fdValue = @ip WHERE fdKey = 'RelayServerIP';", conn))
				{
					relay.Parameters.AddWithValue("@ip", publicIp);
					if (relay.ExecuteNonQuery() == 0)
					{
						using (MySqlCommand insert = new MySqlCommand(
							"INSERT INTO tblServerSettingInfo (fdKey, fdValue, fdOnlyServerSetting) "
							+ "VALUES ('RelayServerIP', @ip, 0);", conn))
						{
							insert.Parameters.AddWithValue("@ip", publicIp);
							insert.ExecuteNonQuery();
						}
						keys.Add("RelayServerIP (row created)");
					}
					else if (!keys.Contains("RelayServerIP"))
					{
						keys.Add("RelayServerIP");
					}
				}
			}

			if (keys.Count == 0)
			{
				_log("    no row held 127.0.0.1");
				return;
			}
			foreach (string key in keys)
			{
				_log("    " + key + " -> " + publicIp);
			}
			Ok(keys.Count.ToString(CultureInfo.InvariantCulture) + " row(s) updated");
		}

		// ---------- database helpers ----------

		private static string BuildConnectionString(Options options, string database)
		{
			MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
			{
				Server = options.DbHost,
				Port = (uint)options.DbPort,
				UserID = options.DbUser,
				Password = options.DbPassword,
				CharacterSet = "utf8mb4",
				ConnectionTimeout = 15,
				DefaultCommandTimeout = 0
			};
			if (database != null) builder.Database = database;
			return builder.ConnectionString;
		}

		public static void TestConnection(string host, int port, string user, string password, out string version)
		{
			Options probe = new Options { DbHost = host, DbPort = port, DbUser = user, DbPassword = password };
			using (MySqlConnection conn = new MySqlConnection(BuildConnectionString(probe, null)))
			{
				conn.Open();
				version = conn.ServerVersion;
			}
		}

		private static bool DatabaseExists(string connectionString, string database)
		{
			using (MySqlConnection conn = new MySqlConnection(connectionString))
			{
				conn.Open();
				using (MySqlCommand cmd = new MySqlCommand(
					"SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @db;", conn))
				{
					cmd.Parameters.AddWithValue("@db", database);
					return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
				}
			}
		}

		private static void Execute(string connectionString, string sql)
		{
			using (MySqlConnection conn = new MySqlConnection(connectionString))
			{
				conn.Open();
				using (MySqlCommand cmd = new MySqlCommand(sql, conn)) cmd.ExecuteNonQuery();
			}
		}

		private static int ScalarInt(string connectionString, string sql, string dbParam)
		{
			using (MySqlConnection conn = new MySqlConnection(connectionString))
			{
				conn.Open();
				using (MySqlCommand cmd = new MySqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("@db", dbParam);
					return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
				}
			}
		}

		// ---------- settings.ini ----------

		public sealed class SettingsResult
		{
			public string BackupPath;
			public readonly List<string> Changes = new List<string>();
		}

		/// <summary>
		/// Rewrites the address keys and the connection string, then replaces any
		/// other leftover 127.0.0.1. MySQLConnection is rebuilt from the database
		/// answers instead, so a MariaDB running on the VPS itself keeps pointing
		/// at localhost.
		/// </summary>
		public static SettingsResult PatchSettings(string path, Options options)
		{
			SettingsResult result = new SettingsResult();

			bool hasBom;
			using (FileStream fs = File.OpenRead(path))
			{
				int b0 = fs.ReadByte(), b1 = fs.ReadByte(), b2 = fs.ReadByte();
				hasBom = b0 == 0xEF && b1 == 0xBB && b2 == 0xBF;
			}

			string original = File.ReadAllText(path, Encoding.UTF8);
			string newline = original.Contains("\r\n") ? "\r\n" : "\n";
			string[] lines = original.Replace("\r\n", "\n").Split('\n');

			result.BackupPath = path + ".bak-"
				+ DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
			File.Copy(path, result.BackupPath, false);

			string connectionValue = string.Format(CultureInfo.InvariantCulture,
				"server={0};port={1};user id={2};password={3};database={4};charset=utf8mb4;",
				options.DbHost, options.DbPort, options.DbUser, options.DbPassword, GameDb);

			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				string trimmed = line.TrimStart();
				if (trimmed.Length == 0
					|| trimmed.StartsWith(";", StringComparison.Ordinal)
					|| trimmed.StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}

				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				string key = line.Substring(0, eq).Trim();
				string value = line.Substring(eq + 1);

				if (key.Equals("MySQLConnection", StringComparison.OrdinalIgnoreCase))
				{
					if (!value.Equals(connectionValue, StringComparison.Ordinal))
					{
						lines[i] = key + "=" + connectionValue;
						result.Changes.Add("MySQLConnection -> " + options.DbHost + ":"
							+ options.DbPort.ToString(CultureInfo.InvariantCulture) + " as " + options.DbUser);
					}
					continue;
				}

				if (AddressKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
				{
					if (!value.Trim().Equals(options.PublicIp, StringComparison.Ordinal))
					{
						lines[i] = key + "=" + options.PublicIp;
						result.Changes.Add(key + ": " + value.Trim() + " -> " + options.PublicIp);
					}
					continue;
				}

				// Any other key still pointing at loopback, for example one added later.
				if (Regex.IsMatch(value, @"\b127\.0\.0\.1\b"))
				{
					lines[i] = key + "=" + Regex.Replace(value, @"\b127\.0\.0\.1\b", options.PublicIp);
					result.Changes.Add(key + ": 127.0.0.1 -> " + options.PublicIp);
				}
			}

			File.WriteAllText(path, string.Join(newline, lines), new UTF8Encoding(hasBom));
			return result;
		}

		// ---------- misc ----------

		private static void TryDelete(string path)
		{
			try { if (File.Exists(path)) File.Delete(path); }
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		private void Step(string message) { _log(""); _log("> " + message); }
		private void Ok(string message) { _log("    OK  " + message); }
		private void Warn(string message) { _log("    !   " + message); }
	}
}
