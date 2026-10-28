using System;
using System.Collections.Generic;
using LocalCommons.Network;
using LocalCommons.Utilities;
using AgentServer.Structuring.Opcode;

namespace AgentServer.Packet.Send
{
	public sealed class NewAttendanceGetUserInfoAck : NetPacket
	{
		public NewAttendanceGetUserInfoAck(List<Tuple<DateTime, bool, bool>> claims, byte last)
		{
			ns.WriteOP(Opcodes.eServer_NEW_ATTENDANCE_GET_USER_INFO_ACK);
			ns.Write(0); // result
			ns.Write(claims.Count);
			foreach (Tuple<DateTime, bool, bool> claim in claims)
			{
				ns.Write(Utility.ConvertToTimestamp(claim.Item1));
				ns.Write(claim.Item2);
				ns.Write(claim.Item3);
			}
			_ = last;
		}
	}
}
