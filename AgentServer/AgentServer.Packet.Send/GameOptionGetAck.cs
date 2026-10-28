using LocalCommons.Network;
using AgentServer.Database;
using AgentServer.Structuring.Opcode;

namespace AgentServer.Packet.Send
{
	public sealed class GameOptionGetAck : NetPacket
	{
		internal GameOptionGetAck(GameOptionPayload payload, byte last)
		{
			// R186632 reads all three stored settings fields on GET_ACK. Leaving macros
			// and options empty makes the client replace its saved resolution with the
			// 1280x960 default. GameOptionPayloadStore validates the resolution field.
			ns.WriteOP(Opcodes.eServer_GAME_OPTION_GET_ACK);
			WriteField(payload.Macros);
			WriteField(payload.Options);
			WriteField(payload.Extra);
			ns.Write(payload.Flag1);
			ns.Write(payload.Flag2);
			_ = last;
		}

		private void WriteField(byte[] bytes)
		{
			byte[] value = bytes ?? new byte[0];
			ns.Write((ushort)value.Length);
			if (value.Length > 0)
				ns.Write(value, 0, value.Length);
		}
	}
}
