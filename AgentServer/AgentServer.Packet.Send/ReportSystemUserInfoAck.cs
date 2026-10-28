using LocalCommons.Network;
using AgentServer.Structuring.Opcode;

namespace AgentServer.Packet.Send
{
	public sealed class ReportSystemUserInfoAck : NetPacket
	{
		public ReportSystemUserInfoAck(byte last)
		{
			_ = last;
			// Wire 737 from debug_trgame 0x77F850: i32 + u8 + i32 + i32 (size 15).
			ns.WriteOP(Opcodes.eServer_REPORT_SYSTEM_USER_INFO_ACK);
			ns.Write(0);
			ns.Write((byte)0);
			ns.Write(0);
			ns.Write(0);
		}
	}
}
