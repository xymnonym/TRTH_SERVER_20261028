using System;
using System.Data;
using AgentServer.Structuring;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using MySql.Data.MySqlClient;
using Serilog;

namespace AgentServer.Packet.Send
{
	/// <summary>
	/// Wire 1398 STORAGE_USER_COUNT_ACK: result, keeping capacity, fishing
	/// reward count. The third value is displayed beside the fishing icon.
	/// </summary>
	public sealed class StorageUserCountAck : NetPacket
	{
		public StorageUserCountAck(Account user, byte last)
		{
			_ = last;
			ns.WriteOP(Opcodes.eServer_STORAGE_USER_COUNT_ACK);
			int fishingRewards = 0;
			int maxKeep = 20;
			try
			{
				if (user != null)
				{
					using MySqlConnection conn = new MySqlConnection(Conf.Connstr);
					conn.Open();
					using (MySqlCommand cmd = new MySqlCommand(
						"SELECT COUNT(*) FROM userstoragekeepingitem k " +
						"WHERE k.fdUserNum=@u AND EXISTS " +
						"(SELECT 1 FROM userstorageitemattr a " +
						"WHERE a.fdUniqueNum=k.fdUniqueNum AND a.fdType=2 AND a.fdValue=1)", conn))
					{
						cmd.Parameters.AddWithValue("@u", user.UserNum);
						fishingRewards = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
					}
					using (MySqlCommand cmd = new MySqlCommand(string.Empty, conn))
					{
						cmd.CommandType = CommandType.StoredProcedure;
						cmd.CommandText = "usp_storage_getUserDesc";
						cmd.Parameters.Add("userNum", MySqlDbType.Int32).Value = user.UserNum;
						using MySqlDataReader rd = cmd.ExecuteReader(CommandBehavior.SingleRow);
						if (rd.Read())
						{
							maxKeep = ClampCount(rd["maxSavableCount"], 20);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warning("StorageUserCountAck: {0}", ex.Message);
			}
			if (fishingRewards < 0)
			{
				fishingRewards = 0;
			}
			// The list ACK supplies the ordinary keeping count. Only fishing
			// rewards belong in this additional count.
			ns.Write(0);
			ns.Write(maxKeep);
			ns.Write(fishingRewards);
		}

		private static int ClampCount(object raw, int fallback)
		{
			try
			{
				int v = Convert.ToInt32(raw);
				if (v < 0)
				{
					// signed tinyint overflow / unset → use fallback, never wire negative
					return fallback;
				}
				return v;
			}
			catch
			{
				return fallback;
			}
		}
	}
}
