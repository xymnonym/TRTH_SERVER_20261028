using LocalCommons.Network;
using AgentServer.Structuring.Opcode;

namespace AgentServer.Packet.Send
{
	public sealed class PieroPostboxUserInfoAck : NetPacket
	{
		public PieroPostboxUserInfoAck(byte last)
		{
			ns.WriteOP(Opcodes.eServer_PIERO_POSTBOX_USER_INFO_ACK);
			ns.Write(0);
			_ = last;
		}
	}
}
