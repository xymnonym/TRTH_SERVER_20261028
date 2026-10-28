using AgentServer.Structuring;
using AgentServer.Structuring.Opcode;
using AgentServer.Structuring.Park;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	/// <summary>
	/// Capsule V2 GetInfo ACK (wire 2179, ack 5).
	/// Official open: GetMachineInfo (machine)(0)(0)(1) → hasReward=1.
	/// Empty 790a80 Overpop wanted 16 more bytes after groupCount=0.
	/// </summary>
	public sealed class CapsuleMachineV2InfoAck : NetPacket
	{
		public CapsuleMachineV2InfoAck(Account user, int reqMachineNum, CapsuleMachineData machine, byte last)
		{
			_ = last;
			_ = user;
			ns.WriteOP(Opcodes.eServer_CapsuleMachineV2Protocol);
			ns.Write(5);
			if (machine == null)
			{
				ns.Write(1);
				ns.Write(0);
				ns.Write(0);
				ns.Write((byte)0);
				WriteCost(reqMachineNum);
				return;
			}
			ns.Write(0);
			ns.Write(0);
			ns.Write(0);
			ns.Write((byte)1);
			WriteCost(reqMachineNum);
			var rewards = machine.ItemList;
			ns.Write(rewards.Count);
			foreach (CapsuleMachineItemNew item in rewards)
			{
				// V2 item record widths match the result-item record in PickUp ACK.
				ns.Write((long)item.ItemNum);
				ns.Write((int)item.ItemCount);
				ns.Write((int)item.ItemMax);
				ns.Write(item.Level);
				ns.Write((byte)0);
			}
			// Fixed tail expected by the V2 info parser after the reward list.
			for (int i = 0; i < 18; i++)
			{
				ns.Write((byte)0);
			}
			// R186632 client log: Overpop packet at end of ACK 2179, wants 4 bytes.
			ns.Write(0);
		}

		private void WriteCost(int machineNum)
		{
			ns.Write(machineNum);
			ns.Write(0L);
			ns.Write(0);
			ns.Write(0);
		}
	}
}
