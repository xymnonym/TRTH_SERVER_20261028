using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	/// <summary>
	/// Wire 46. Reply to a board tile pick. Layout not measured yet, so the first int
	/// is the result code (zero for success, matching wire 606) and the tail is padded.
	/// </summary>
	public sealed class PrivatePuzzle2_PickAck : NetPacket
	{
		public PrivatePuzzle2_PickAck(int slot, byte last)
		{
			ns.WriteOP(Opcodes.eServer_PRIVATE_PUZZLE2_PICK_ACK);
			ns.Write(0);
			ns.Fill(64);
			_ = slot;
			_ = last;
		}
	}
}
