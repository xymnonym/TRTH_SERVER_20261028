using LocalCommons.Network;
using LocalCommons.Utilities;
using RoomServer.Structuring;
using RoomServer.Structuring.Opcode;
using Serilog;

namespace RoomServer.Packet.Send
{
	/// <summary>
	/// ENTER_ROOM_ACK (909) for a farm room.
	///
	/// The layout below is read straight off client R186632. The handler is
	/// trgame_unpacked.exe!0x14075DF60; it parses the shared room header, then
	/// for RoomKindID 0x4B (farm) calls 0x140684130 (farm block) and
	/// 0x14072B0B0 (public farm block), then one trailing flag byte.
	/// Guild farms (0x4C) get the farm block only. Strings are read by
	/// 0x140685740: short byte count, then the bytes.
	///
	/// The farm block is byte for byte the same structure AgentServer sends in
	/// LoginFarm_GetMyFarmInfo, including the unique packed as unique &lt;&lt; 16,
	/// which is what the client compares against its own farm id.
	/// </summary>
	public sealed class SendFarmInfo : NetPacket
	{
		private const long PremiumUnsetMagic = 1842465389770955L;

		public SendFarmInfo(Account User, NormalRoom room, int FarmUniqueNum, byte last)
		{
			int mapWire = FarmMapNum(room.FarmRoomInfo.FarmTypeNum);
			bool ownFarm = (User.MyFarmUniqueNum > 0 && User.MyFarmUniqueNum == FarmUniqueNum)
				|| room.FarmRoomInfo.IsMaster;
			if (ownFarm && User.MyFarmUniqueNum <= 0 && FarmUniqueNum > 0)
			{
				User.MyFarmUniqueNum = FarmUniqueNum;
			}

			// Shared room header, same order as GameRoom_SendRoomInfo.
			ns.WriteOP(Opcodes.eServer_ENTER_ROOM_ACK);
			ns.Write(0);
			ns.Write(room.RoomKindID);
			ns.WriteBIG5Fixed_shortSize(room.Name);
			ns.Write(room.MaxPlayersCount);
			ns.WriteBIG5Fixed_shortSize(room.Password);
			ns.Write(2);
			ns.Write(room.ID);
			ns.Write(User.RoomPos);
			ns.Write(mapWire);
			ns.Write(30);
			ns.Write(room.IsTeamPlay);
			ns.Write(room.IsStepOn);
			ns.Write((byte)room.ItemType);
			ns.Write(mapWire);
			ns.Write((byte)0);
			ns.Write(Utility.CurrentTimeMilliseconds());
			ns.Write(room.PosWeight);

			// Farm block, 0x140684130. No flag byte between PosWeight and this:
			// one extra byte here put the farm name length at offset 93 while the
			// client read it at 92, popped 5376 bytes out of 171 and crashed
			// (Overpop packet (popRawData) 94 171 5376 909).
			ns.Write(Utility.PackFarmUnique(FarmUniqueNum));
			ns.Write(room.FarmRoomInfo.FarmWeatherTypeNum);
			ns.Write(room.FarmRoomInfo.FarmSkyTypeNum);
			ns.Write(room.FarmRoomInfo.FarmTypeNum);
			ns.WriteAnsiFixed_intSize(room.FarmRoomInfo.FarmName);
			ns.WriteAnsiFixed_intSize(room.FarmRoomInfo.MasterName);
			ns.Write(FixFarmTime(room.FarmRoomInfo.ExpireTime));
			ns.Write(FixFarmTime(room.FarmRoomInfo.CreateTime));
			ns.Write(room.FarmRoomInfo.Password == string.Empty);
			ns.Write(room.FarmRoomInfo.isPublic);
			ns.Write((byte)(ownFarm ? 1 : 0));
			ns.Write(room.FarmRoomInfo.TotalCount);
			ns.Write(room.FarmRoomInfo.TodaysVisitorCount);
			// One int: mine byte, two pad, premium flag.
			ns.Write((byte)(ownFarm ? 1 : 0));
			ns.Fill(2);
			ns.Write(room.FarmRoomInfo.PremiumFarmUsing);
			long premExp = room.FarmRoomInfo.PremiumFarmExpireDateTime;
			ns.Write(premExp > 0L ? FixFarmTime(premExp) : PremiumUnsetMagic);
			int farmExp = room.FarmRoomInfo.farmExp;
			if (farmExp < 1 && User.MyFarmInfo != null)
			{
				farmExp = User.MyFarmInfo.farmExp;
			}
			// One int64: exp low, zero high.
			ns.Write(Utility.FarmHudExp(farmExp));
			ns.Write(0);

			// Public farm block, 0x14072B0B0. Same three bytes plus name the
			// client sends in CreatePublicFarm.
			ns.Write(room.FarmRoomInfo.Type);
			ns.Write(room.FarmRoomInfo.MaxUserLimit);
			ns.Write(room.FarmRoomInfo.unk1);
			ns.WriteAnsiFixed_intSize(room.FarmRoomInfo.FarmName);
			ns.Write((byte)0);

			// Trailing flag the handler pops for every room kind.
			ns.Write((byte)0);
			_ = last;
			Log.Information("SendFarmInfo bytes={0} farm={1} own={2} my={3} kind={4} map={5} type={6} visits={7}/{8}",
				ns.Length, FarmUniqueNum, ownFarm, User.MyFarmUniqueNum, room.RoomKindID,
				mapWire, room.FarmRoomInfo.FarmTypeNum, room.FarmRoomInfo.TodaysVisitorCount,
				room.FarmRoomInfo.TotalCount);
		}

		internal static int FarmTypeWire(int farmTypeNum = 1)
		{
			if (farmTypeNum <= 0)
			{
				farmTypeNum = 1;
			}
			return farmTypeNum;
		}

		internal static int PackedFarmTypeNum(int farmTypeNum = 1)
		{
			return FarmTypeWire(farmTypeNum);
		}

		internal static int FarmMapNum(int farmTypeNum)
		{
			if (farmTypeNum <= 0)
			{
				farmTypeNum = 1;
			}
			return 69 + farmTypeNum;
		}

		private static long FixFarmTime(long timeMs)
		{
			const long minOk = 946684800000L; // 2000-01-01
			const long maxOk = 2145916800000L; // ~2038
			if (timeMs <= 0L || timeMs < minOk)
			{
				return maxOk;
			}
			if (timeMs > maxOk)
			{
				return maxOk;
			}
			return timeMs;
		}
	}
}
