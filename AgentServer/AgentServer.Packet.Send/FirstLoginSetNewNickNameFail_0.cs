using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	public sealed class FirstLoginSetNewNickNameFail_0X4C : NetPacket
	{
		public FirstLoginSetNewNickNameFail_0X4C(byte last)
		{
			ns.WriteOP(Opcodes.eServer_GET_NICKNAME_ENTER_MY_NICKNAME_ACK);
			ns.Write(58);
			_ = last;
		}
	}
	public sealed class FirstLoginSetNewNickNameInvalid : NetPacket
	{
		public FirstLoginSetNewNickNameInvalid(byte last)
		{
			// Reply to the request opcode 1383 even when the name is invalid.
			// ACK 958 belongs to a separate name-availability check and leaves
			// the client's 1383 protocol lock active.
			ns.WriteOP(Opcodes.eServer_GET_NICKNAME_ENTER_MY_NICKNAME_ACK);
			ns.Write((int)eServerResult.eServerResult_GET_NICKNAME_ENTER_INPUT_CONTAIN_ABUSE_ACK);
			_ = last;
		}
	}
}
