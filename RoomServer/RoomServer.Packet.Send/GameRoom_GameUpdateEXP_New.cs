using LocalCommons.Network;
using RoomServer.Holders;
using RoomServer.Structuring;
using RoomServer.Structuring.Opcode;

namespace RoomServer.Packet.Send
{
	public sealed class GameRoom_GameUpdateEXP_New : NetPacket
	{
		public GameRoom_GameUpdateEXP_New(int roomKind, Account User, byte last)
		{
			ns.WriteOP(Opcodes.eServer_GAME_RESULT_ACK);
			// Current client 0x14073a320 always reads 4+4+8+8+12 bytes.
			// Keep the first field even when ThankOffering is disabled.
			bool thankOffering = ServerSettingHolder.ServerSettings.useThankOfferingSystem;
			ns.Write(thankOffering && (roomKind == 183 || roomKind == 184) ? roomKind : 0);
			ns.Write(User.LadderPoint);
			ns.Write(User.Exp);
			ns.Write(User.TR);
			ns.Fill(12);
			_ = last;
		}
	}
}
