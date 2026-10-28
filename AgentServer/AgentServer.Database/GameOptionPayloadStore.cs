using System;
using System.Text;
using MySql.Data.MySqlClient;
using Serilog;
using TRCommon;

namespace AgentServer.Database
{
	internal sealed class GameOptionPayload
	{
		public byte[] Macros = Array.Empty<byte>();
		public byte[] Options = Array.Empty<byte>();
		public byte[] Extra = Array.Empty<byte>();
		public int Flag1;
		public int Flag2;
	}

	internal static class GameOptionPayloadStore
	{
		private static readonly object SchemaLock = new object();
		private static bool SchemaReady;

		private static void EnsureSchema(MySqlConnection connection)
		{
			if (SchemaReady)
				return;
			lock (SchemaLock)
			{
				if (SchemaReady)
					return;
				using (var command = new MySqlCommand(@"CREATE TABLE IF NOT EXISTS usergameoptionpayload (
	fdUserNum INT NOT NULL PRIMARY KEY,
	fdMacros BLOB NOT NULL,
	fdOptions BLOB NOT NULL,
	fdExtra BLOB NOT NULL,
	fdFlag1 INT NOT NULL DEFAULT 0,
	fdFlag2 INT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", connection))
				{
					command.ExecuteNonQuery();
				}
				using (var command = new MySqlCommand(@"ALTER TABLE usergameoptionpayload
	ADD COLUMN IF NOT EXISTS fdFlag1 INT NOT NULL DEFAULT 0,
	ADD COLUMN IF NOT EXISTS fdFlag2 INT NOT NULL DEFAULT 0", connection))
				{
					command.ExecuteNonQuery();
				}
				SchemaReady = true;
			}
		}

		public static bool Load(int userNum, out GameOptionPayload payload)
		{
			payload = new GameOptionPayload();
			using (var connection = new MySqlConnection(Conf.Connstr))
			{
				connection.Open();
				EnsureSchema(connection);
				using (var command = new MySqlCommand(
					"SELECT fdMacros, fdOptions, fdExtra, fdFlag1, fdFlag2 FROM usergameoptionpayload WHERE fdUserNum=@u", connection))
				{
					command.Parameters.Add("@u", MySqlDbType.Int32).Value = userNum;
					using (var reader = command.ExecuteReader())
					{
						if (!reader.Read())
							return false;
						payload.Macros = (byte[])reader.GetValue(0);
						payload.Options = (byte[])reader.GetValue(1);
						payload.Extra = (byte[])reader.GetValue(2);
						if (!IsValidResolution(payload.Extra))
						{
							Log.Warning("GAME_OPTION_GET invalid resolution payload userNum={UserNum} bytes={Bytes}; omitting field", userNum, payload.Extra.Length);
							payload.Extra = Array.Empty<byte>();
						}
						payload.Flag1 = reader.GetInt32(3);
						payload.Flag2 = reader.GetInt32(4);
						return true;
					}
				}
			}
		}

		public static void Save(int userNum, GameOptionPayload payload)
		{
			using (var connection = new MySqlConnection(Conf.Connstr))
			{
				connection.Open();
				EnsureSchema(connection);
				if (!IsValidResolution(payload.Extra))
				{
					byte[] storedResolution = Array.Empty<byte>();
					using (var read = new MySqlCommand("SELECT fdExtra FROM usergameoptionpayload WHERE fdUserNum=@u", connection))
					{
						read.Parameters.Add("@u", MySqlDbType.Int32).Value = userNum;
						object value = read.ExecuteScalar();
						if (value != null && value != DBNull.Value)
						{
							byte[] existing = (byte[])value;
							if (IsValidResolution(existing))
								storedResolution = existing;
						}
					}
					Log.Warning("GAME_OPTION_SET invalid resolution payload userNum={UserNum} bytes={Bytes}; retaining last valid value", userNum, payload.Extra == null ? 0 : payload.Extra.Length);
					payload.Extra = storedResolution;
				}
				using (var command = new MySqlCommand(@"INSERT INTO usergameoptionpayload
	(fdUserNum, fdMacros, fdOptions, fdExtra, fdFlag1, fdFlag2)
	VALUES (@u, @m, @o, @e, @f1, @f2)
ON DUPLICATE KEY UPDATE fdMacros=VALUES(fdMacros),
	fdOptions=VALUES(fdOptions), fdExtra=VALUES(fdExtra),
	fdFlag1=VALUES(fdFlag1), fdFlag2=VALUES(fdFlag2)", connection))
				{
					command.Parameters.Add("@u", MySqlDbType.Int32).Value = userNum;
					command.Parameters.Add("@m", MySqlDbType.Blob).Value = payload.Macros;
					command.Parameters.Add("@o", MySqlDbType.Blob).Value = payload.Options;
					command.Parameters.Add("@e", MySqlDbType.Blob).Value = payload.Extra;
					command.Parameters.Add("@f1", MySqlDbType.Int32).Value = payload.Flag1;
					command.Parameters.Add("@f2", MySqlDbType.Int32).Value = payload.Flag2;
					command.ExecuteNonQuery();
				}
			}
		}

		internal static bool IsValidResolution(byte[] value)
		{
			if (value == null || value.Length < 7 || value.Length > 11)
				return false;

			string text = Encoding.ASCII.GetString(value);
			int separator = text.IndexOf('X');
			if (separator < 3 || separator > 5 || text.Length - separator - 1 < 3 || text.Length - separator - 1 > 5)
				return false;

			for (int i = 0; i < text.Length; i++)
			{
				if (i == separator)
					continue;
				if (text[i] < '0' || text[i] > '9')
					return false;
			}
			int width;
			int height;
			return int.TryParse(text.Substring(0, separator), out width) && width > 0
				&& int.TryParse(text.Substring(separator + 1), out height) && height > 0;
		}
	}
}
