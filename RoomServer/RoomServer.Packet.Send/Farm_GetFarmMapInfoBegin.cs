using LocalCommons.Network;
using RoomServer.Structuring.Opcode;

namespace RoomServer.Packet.Send
{
	/// <summary>
	/// Opens a GetFarmMapInfo reply. The client clears its farm object list here
	/// (trgame_unpacked.exe!0x1406E1031), then fills it from 0x33 rows and
	/// applies the map type from 0x34.
	/// </summary>
	public sealed class Farm_GetFarmMapInfoBegin : NetPacket
	{
		public Farm_GetFarmMapInfoBegin(byte last)
		{
			ns.WriteOP(Opcodes.eServer_FARM_ACK);
			ns.WriteOP(FarmProtocol.GetFarmMapInfo_ACK);
			_ = last;
		}
	}
}
