using LocalCommons.Network;
using RoomServer.Structuring.Opcode;

namespace RoomServer.Packet.Send
{
	public sealed class GameRoom_StartLoading : NetPacket
	{
		public GameRoom_StartLoading(int mapid, int randseed, byte loadingFlag)
		{
			ns.WriteOP(RoomOpcodes.eRoom_START_LOADING_ACK);
			ns.Write(mapid);
			ns.Write(randseed);
			ns.Write((byte)(loadingFlag == 1 ? 1 : 0));
			// R186632 consumes a UInt32 list count after the loading flag.
			// Zero is an empty list, not an unidentified reserved field.
			ns.Write(0);
		}
	}
}
