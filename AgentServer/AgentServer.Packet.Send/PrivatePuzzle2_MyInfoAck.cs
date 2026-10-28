using System;
using System.Collections.Generic;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using MySql.Data.MySqlClient;
using Serilog;

namespace AgentServer.Packet.Send
{
	/// <summary>
	/// Wire 606, measured against R186632: int result, int reserved, int count, then
	/// count entries of two ints. A non-zero result sends the client into
	/// onRecvPrivatePuzzle2_Failed, and raising the count by one grew the consumed
	/// length by eight bytes.
	/// Entries come from essenprivatepuzzle2 for the board kind the request named.
	/// </summary>
	public sealed class PrivatePuzzle2_MyInfoAck : NetPacket
	{
		public PrivatePuzzle2_MyInfoAck(int board, byte last)
		{
			ns.WriteOP(Opcodes.eServer_PRIVATE_PUZZLE2_GET_MYINFO_ACK);
			// Measured layout: int result, int reserved, int count, then count entries of
			// (level, claimed). Writing 1 in the claimed field made every step render as
			// already received and hid the board, so unclaimed steps send 0.
			List<int> levels = LoadLevels(board);
			ns.Write(0);
			// This field is the page the board opens on, zero based: sending 1 opened
			// page 2 of 5. Keep it at the first page.
			ns.Write(0);
			ns.Write(levels.Count);
			foreach (int level in levels)
			{
				ns.Write(level);
				ns.Write(0);
			}
			_ = last;
		}

		private static List<int> LoadLevels(int board)
		{
			List<int> list = new List<int>();
			try
			{
				using MySqlConnection conn = new MySqlConnection(Conf.Connstr);
				conn.Open();
				using MySqlCommand cmd = new MySqlCommand(
					"SELECT fdLevel FROM essenprivatepuzzle2 WHERE fdKind=@k ORDER BY fdLevel", conn);
				cmd.Parameters.AddWithValue("@k", board);
				using MySqlDataReader reader = cmd.ExecuteReader();
				while (reader.Read())
				{
					list.Add(Convert.ToInt32(reader["fdLevel"]));
				}
			}
			catch (Exception ex)
			{
				Log.Warning("PrivatePuzzle2 levels board={0}: {1}", board, ex.Message);
			}
			return list;
		}
	}
}
