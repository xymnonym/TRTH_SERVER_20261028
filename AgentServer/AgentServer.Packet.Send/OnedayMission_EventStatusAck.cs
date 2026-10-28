using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	/// <summary>
	/// Wire 1985. The server used to answer this request with the FAILED reply, so the
	/// event mission panel stayed empty. Layout not measured yet: send a padded tail and
	/// read Overpop or RemainSize from the client log.
	/// </summary>
	public sealed class OnedayMission_EventStatusAck : NetPacket
	{
		public OnedayMission_EventStatusAck(int group, byte last)
		{
			ns.WriteOP(Opcodes.eServer_ONEDAY_MISSION_GET_EVENT_MISSION_STATUS_ACK);
			// Body is 27 bytes once the group is echoed: a padded 70 byte reply left
			// RemainSize 41, and a 6 byte reply raised Overpop 6 6 4 1985. Field
			// meanings beyond the echoed group are still unknown.
			ns.Write(group);
			ns.Fill(23);
			_ = last;
		}
	}
}
