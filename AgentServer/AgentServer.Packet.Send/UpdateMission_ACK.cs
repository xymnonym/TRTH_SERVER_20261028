using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	public sealed class UpdateMission_ACK : NetPacket
	{
		public UpdateMission_ACK(int kind, int missionNum, int conditionNum, int achievedPoint, byte last)
		{
			// R186632 reads an 8-byte mission key and an 8-byte condition value.
			ns.WriteOP(Opcodes.eServer_MISSION_USER_MISSION_UPDATE_ACK);
			ns.Write(kind);
			ns.Write(missionNum);
			ns.Write(conditionNum);
			ns.Write(achievedPoint);
			_ = last;
		}
	}

	public sealed class UpdateMissionFailed_ACK : NetPacket
	{
		public UpdateMissionFailed_ACK(int kind, int missionNum, int conditionNum, short reason, byte last)
		{
			// Current opcode 257 reads mission key8, condition4, reason2.
			ns.WriteOP((short)257);
			ns.Write(kind);
			ns.Write(missionNum);
			ns.Write(conditionNum);
			ns.Write(reason);
			_ = last;
		}
	}
}
