using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TRServerSetup
{
	internal sealed class MainForm : Form
	{
		private TextBox _bundleBox, _gameScriptBox, _logScriptBox, _mysqlBox;
		private TextBox _hostBox, _portBox, _userBox, _publicIpBox;
		private TextBox _passwordBox;
		private CheckBox _dropBox;
		private Button _installButton, _testButton;
		private ProgressBar _progress;
		private TextBox _logBox;
		private Thread _worker;

		public MainForm()
		{
			Text = "TalesRunner Server Setup";
			Font = new Font("Segoe UI", 9f);
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(760, 640);
			MinimumSize = new Size(700, 560);

			Build();
			Detect();
		}

		// ---------- layout ----------

		private void Build()
		{
			Label title = new Label
			{
				Text = "TalesRunner Server Setup",
				Font = new Font("Segoe UI", 15f, FontStyle.Bold),
				AutoSize = true,
				Location = new Point(16, 14)
			};
			Label subtitle = new Label
			{
				Text = "Imports the database, then writes settings.ini with your MariaDB login "
					+ "and the address clients connect to.",
				AutoSize = false,
				Size = new Size(720, 20),
				ForeColor = SystemColors.GrayText,
				Location = new Point(18, 44)
			};
			Controls.Add(title);
			Controls.Add(subtitle);

			GroupBox files = new GroupBox
			{
				Text = "Files",
				Location = new Point(16, 72),
				Size = new Size(728, 150),
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			_bundleBox = AddPathRow(files, 0, "Bundle folder", BrowseFolder);
			_gameScriptBox = AddPathRow(files, 1, "Game database", () => BrowseFile(_gameScriptBox));
			_logScriptBox = AddPathRow(files, 2, "Log database", () => BrowseFile(_logScriptBox));
			_mysqlBox = AddPathRow(files, 3, "mysql.exe", () => BrowseFile(_mysqlBox));
			Controls.Add(files);

			GroupBox db = new GroupBox
			{
				Text = "MariaDB",
				Location = new Point(16, 232),
				Size = new Size(360, 150)
			};
			_hostBox = AddFieldRow(db, 0, "Host", "127.0.0.1", 200);
			_portBox = AddFieldRow(db, 1, "Port", "3306", 80);
			_userBox = AddFieldRow(db, 2, "User", "root", 200);
			_passwordBox = AddFieldRow(db, 3, "Password", "", 200);
			_passwordBox.UseSystemPasswordChar = true;
			Controls.Add(db);

			GroupBox net = new GroupBox
			{
				Text = "Address clients connect to",
				Location = new Point(388, 232),
				Size = new Size(356, 150),
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			_publicIpBox = AddFieldRow(net, 0, "Public IP", "127.0.0.1", 200);
			Label hint = new Label
			{
				Text = "Replaces 127.0.0.1 in AgentServerIP, RelayAdvertiseIP\r\n"
					+ "and RoomServerIP. Keep 127.0.0.1 for a local test.\r\n"
					+ "The MariaDB host above is set separately, so a database\r\n"
					+ "on this same machine stays on localhost.",
				Location = new Point(14, 58),
				Size = new Size(330, 70),
				ForeColor = SystemColors.GrayText
			};
			net.Controls.Add(hint);
			Controls.Add(net);

			_dropBox = new CheckBox
			{
				Text = "Drop tr_game_db and tr_game_db_log first (deletes everything in them)",
				Location = new Point(20, 392),
				AutoSize = true
			};
			Controls.Add(_dropBox);

			_testButton = new Button
			{
				Text = "Test connection",
				Location = new Point(16, 418),
				Size = new Size(130, 30)
			};
			_testButton.Click += OnTestConnection;
			Controls.Add(_testButton);

			_installButton = new Button
			{
				Text = "Install",
				Location = new Point(154, 418),
				Size = new Size(130, 30),
				Font = new Font("Segoe UI", 9f, FontStyle.Bold)
			};
			_installButton.Click += OnInstall;
			Controls.Add(_installButton);

			_progress = new ProgressBar
			{
				Location = new Point(296, 418),
				Size = new Size(448, 30),
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			Controls.Add(_progress);

			_logBox = new TextBox
			{
				Location = new Point(16, 458),
				Size = new Size(728, 164),
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = Color.FromArgb(30, 30, 30),
				ForeColor = Color.Gainsboro,
				Font = new Font("Consolas", 9f),
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
			};
			Controls.Add(_logBox);
		}

		private static TextBox AddPathRow(Control parent, int row, string label, Action browse)
		{
			int y = 24 + row * 30;
			parent.Controls.Add(new Label
			{
				Text = label,
				Location = new Point(14, y + 3),
				Size = new Size(96, 18)
			});
			TextBox box = new TextBox
			{
				Location = new Point(114, y),
				Size = new Size(520, 23),
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};
			parent.Controls.Add(box);
			Button button = new Button
			{
				Text = "...",
				Location = new Point(642, y - 1),
				Size = new Size(72, 25),
				Anchor = AnchorStyles.Top | AnchorStyles.Right
			};
			button.Click += (s, e) => browse();
			parent.Controls.Add(button);
			return box;
		}

		private static TextBox AddFieldRow(Control parent, int row, string label, string value, int width)
		{
			int y = 24 + row * 30;
			parent.Controls.Add(new Label
			{
				Text = label,
				Location = new Point(14, y + 3),
				Size = new Size(80, 18)
			});
			TextBox box = new TextBox
			{
				Location = new Point(98, y),
				Size = new Size(width, 23),
				Text = value
			};
			parent.Controls.Add(box);
			return box;
		}

		// ---------- detection ----------

		private void Detect()
		{
			string root = SetupEngine.FindBundleRoot();
			_bundleBox.Text = root;
			FillScriptBoxes(root);
			_mysqlBox.Text = SetupEngine.FindMysqlClient() ?? "";

			Log("Bundle folder : " + root);
			Log("Game database : " + Describe(_gameScriptBox.Text));
			Log("Log database  : " + Describe(_logScriptBox.Text));
			Log("mysql.exe     : " + (_mysqlBox.Text.Length == 0 ? "NOT FOUND" : _mysqlBox.Text));
			if (_mysqlBox.Text.Length == 0)
			{
				Log("");
				Log("mysql.exe ships with MariaDB, normally at");
				Log(@"  C:\Program Files\MariaDB 10.4\bin\mysql.exe");
				Log("Point the mysql.exe box at it before installing.");
			}
		}

		private void FillScriptBoxes(string root)
		{
			_gameScriptBox.Text = SetupEngine.FindScript(root, "tr_game_db_starter*.sql", "tr_game_db*utf8mb4.sql") ?? "";
			_logScriptBox.Text = SetupEngine.FindScript(root, "tr_game_db_log*.sql") ?? "";
		}

		private static string Describe(string path)
		{
			if (string.IsNullOrEmpty(path)) return "NOT FOUND";
			return path + " (" + SetupEngine.FileSize(path) + ")";
		}

		// ---------- browsing ----------

		private void BrowseFolder()
		{
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = "Pick the folder that holds AgentServer.exe and settings.ini";
				if (Directory.Exists(_bundleBox.Text)) dialog.SelectedPath = _bundleBox.Text;
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				_bundleBox.Text = dialog.SelectedPath;
				FillScriptBoxes(dialog.SelectedPath);
				Log("Bundle folder set to " + dialog.SelectedPath);
			}
		}

		private void BrowseFile(TextBox target)
		{
			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				bool wantsExe = ReferenceEquals(target, _mysqlBox);
				dialog.Filter = wantsExe ? "mysql.exe|mysql.exe|Programs (*.exe)|*.exe" : "SQL script (*.sql)|*.sql";
				try
				{
					if (target.Text.Length > 0 && File.Exists(target.Text))
					{
						dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
						dialog.FileName = Path.GetFileName(target.Text);
					}
				}
				catch (ArgumentException) { }
				if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
			}
		}

		// ---------- actions ----------

		private void OnTestConnection(object sender, EventArgs e)
		{
			SetupEngine.Options options = ReadOptions(requireScripts: false);
			if (options == null) return;
			try
			{
				string version;
				SetupEngine.TestConnection(options.DbHost, options.DbPort, options.DbUser, options.DbPassword, out version);
				Log("");
				Log("    OK  Connected to MariaDB " + version);
			}
			catch (Exception ex)
			{
				Log("");
				Log("    ERR " + ex.Message);
				MessageBox.Show(this, ex.Message, "Connection failed",
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void OnInstall(object sender, EventArgs e)
		{
			SetupEngine.Options options = ReadOptions(requireScripts: true);
			if (options == null) return;

			string warning = "Import " + Path.GetFileName(options.GameScript) + " into "
				+ SetupEngine.GameDb + " on " + options.DbHost + ":"
				+ options.DbPort.ToString(CultureInfo.InvariantCulture) + "?"
				+ Environment.NewLine + Environment.NewLine
				+ "settings.ini will be rewritten, with a backup kept next to it.";
			if (options.DropExisting)
			{
				warning += Environment.NewLine + Environment.NewLine
					+ "Drop is ticked: everything currently in " + SetupEngine.GameDb
					+ " and " + SetupEngine.LogDb + " will be deleted first.";
			}
			if (MessageBox.Show(this, warning, "Confirm", MessageBoxButtons.OKCancel,
				options.DropExisting ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}

			SetBusy(true);
			_progress.Value = 0;
			_logBox.Clear();

			_worker = new Thread(() =>
			{
				bool ok = false;
				try
				{
					new SetupEngine(Log, SetProgress).Run(options);
					ok = true;
				}
				catch (Exception ex)
				{
					Log("");
					Log("    ERR " + ex.Message);
					BeginInvoke((Action)(() => MessageBox.Show(this, ex.Message, "Setup failed",
						MessageBoxButtons.OK, MessageBoxIcon.Error)));
				}
				finally
				{
					BeginInvoke((Action)(() =>
					{
						SetBusy(false);
						if (ok)
						{
							MessageBox.Show(this,
								"The database is imported and settings.ini is written."
								+ Environment.NewLine + Environment.NewLine
								+ "Start the server with START_SERVER.bat.",
								"Finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
						}
					}));
				}
			});
			_worker.IsBackground = true;
			_worker.Start();
		}

		/// <summary>Validates the form and turns it into engine options, or null.</summary>
		private SetupEngine.Options ReadOptions(bool requireScripts)
		{
			string host = _hostBox.Text.Trim();
			if (!IsHost(host))
			{
				Complain(_hostBox, "The MariaDB host must be an IP address or a host name.");
				return null;
			}

			int port;
			if (!int.TryParse(_portBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
				|| port < 1 || port > 65535)
			{
				Complain(_portBox, "The port must be a number between 1 and 65535.");
				return null;
			}

			string user = _userBox.Text.Trim();
			if (user.Length == 0)
			{
				Complain(_userBox, "Enter the MariaDB user.");
				return null;
			}

			string publicIp = _publicIpBox.Text.Trim();
			if (!IsHost(publicIp))
			{
				Complain(_publicIpBox, "The public IP must be an IP address or a host name.");
				return null;
			}

			string root = _bundleBox.Text.Trim();
			if (requireScripts)
			{
				if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "settings.ini")))
				{
					Complain(_bundleBox, "settings.ini is not in that folder. Pick the folder that holds AgentServer.exe.");
					return null;
				}
				if (!File.Exists(_gameScriptBox.Text.Trim()))
				{
					Complain(_gameScriptBox, "Pick the game database script.");
					return null;
				}
				if (!File.Exists(_mysqlBox.Text.Trim()))
				{
					Complain(_mysqlBox, "Pick mysql.exe. It ships with MariaDB.");
					return null;
				}
			}

			return new SetupEngine.Options
			{
				BundleRoot = root,
				MysqlExe = _mysqlBox.Text.Trim(),
				GameScript = _gameScriptBox.Text.Trim(),
				LogScript = _logScriptBox.Text.Trim(),
				DbHost = host,
				DbPort = port,
				DbUser = user,
				DbPassword = _passwordBox.Text,
				PublicIp = publicIp,
				DropExisting = _dropBox.Checked
			};
		}

		private static bool IsHost(string value)
		{
			if (string.IsNullOrEmpty(value)) return false;
			IPAddress ignored;
			if (IPAddress.TryParse(value, out ignored)) return true;
			return Regex.IsMatch(value, @"^[A-Za-z0-9]([A-Za-z0-9\-\.]*[A-Za-z0-9])?$");
		}

		private void Complain(Control focus, string message)
		{
			MessageBox.Show(this, message, "Check the form", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			focus.Focus();
		}

		private void SetBusy(bool busy)
		{
			_installButton.Enabled = !busy;
			_testButton.Enabled = !busy;
			_installButton.Text = busy ? "Working..." : "Install";
			UseWaitCursor = busy;
		}

		// ---------- thread-safe output ----------

		private void Log(string message)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action<string>)Log, message);
				return;
			}
			_logBox.AppendText(message + Environment.NewLine);
		}

		private void SetProgress(int percent)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action<int>)SetProgress, percent);
				return;
			}
			_progress.Value = Math.Max(0, Math.Min(100, percent));
		}
	}
}
