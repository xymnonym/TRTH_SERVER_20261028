using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using LocalCommons.Utilities;

namespace AgentServer.Packet.Send
{
	public sealed class ExpiredFarmItem_Ack : NetPacket
	{
		public ExpiredFarmItem_Ack(int FarmUniqueNum, int skyType, byte last)
		{
			ns.WriteOP(Opcodes.eServer_FARM_ACK);
			ns.WriteOP(FarmProtocol.Expired_farm_item_ACK);
			ns.Write(0);
			ns.Write(Utility.PackFarmUnique(FarmUniqueNum));
			ns.Write(skyType);
			_ = last;
		}
	}
}
