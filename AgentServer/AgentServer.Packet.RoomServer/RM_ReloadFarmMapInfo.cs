using AgentServer.Structuring;
using LocalCommons.Network;
using NetMsg.Room;

namespace AgentServer.Packet.RoomServer
{
	public sealed class RM_ReloadFarmMapInfo : NetPacket
	{
		/// <param name="beginAck">
		/// Farm sub opcode that opens the reply: 0x32 for ReloadFarmMapInfo,
		/// 0x30 for GetFarmMapInfo. The client clears its object list on either.
		/// </param>
		public RM_ReloadFarmMapInfo(Account User, int FarmUniqueNum, byte last, int beginAck = 0x32)
		{
			ns.WriteOP(RMProtocol.RM_ReloadFarmMapInfo_REQ);
			ns.Write(User.Session);
			ns.Write(User.CurrentRoomId);
			ns.Write(FarmUniqueNum);
			ns.Write(beginAck);
			ns.Write(last);
		}
	}
}
