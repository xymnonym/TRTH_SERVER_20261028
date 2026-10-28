using LocalCommons.Network;
using LocalCommons.Utilities;
using RoomServer.Structuring.Opcode;

namespace RoomServer.Packet.Send
{
	public sealed class ReloadFarmTypeNum : NetPacket
	{
		public ReloadFarmTypeNum(int FarmUniqueNum, int FarmTypeNum, byte last)
		{
			ns.WriteOP(Opcodes.eServer_FARM_ACK);
			ns.WriteOP(FarmProtocol.ReloadFarmMapInfo_ACK_3);
			ns.Write(0);
			ns.Write(Utility.PackFarmUnique(FarmUniqueNum));
			// Real type. The old farm_65536.trv came from the two byte shift in
			// ENTER_ROOM_ACK, not from type 1, so type 1 no longer needs zeroing.
			ns.Write(FarmTypeNum < 0 ? 0 : FarmTypeNum);
			_ = last;
		}
	}
}
