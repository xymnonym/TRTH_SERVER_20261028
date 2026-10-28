using AgentServer.Network.Connections;
using AgentServer.Packet.Send;
using LocalCommons.Network;
using Serilog;

namespace AgentServer.Packet
{
	/// <summary>
	/// Jab pick board, called PrivatePuzzle2 by the client. The board data lives in
	/// essenprivatepuzzle2 and essenprivatepuzzle2_reward; the live board is fdKind 32.
	/// The reply layout is still being measured, so the ACK is a sized probe.
	/// </summary>
	public class PrivatePuzzle2Handle
	{
		public static void Handle_GetMyInfo(ClientConnection client, PacketReader reader, byte last)
		{
			int board = reader.Remaining >= 4 ? reader.ReadLEInt32() : 0;
			Log.Information("PrivatePuzzle2 myInfo board={Board} remaining={Remaining}", board, reader.Remaining);
			client.SendAsync(new PrivatePuzzle2_MyInfoAck(board, last));
		}

		public static void Handle_Pick(ClientConnection client, PacketReader reader, byte last)
		{
			int slot = reader.Remaining >= 4 ? reader.ReadLEInt32() : 0;
			Log.Information("PrivatePuzzle2 pick slot={Slot} remaining={Remaining}", slot, reader.Remaining);
			client.SendAsync(new PrivatePuzzle2_PickAck(slot, last));
		}
	}
}
