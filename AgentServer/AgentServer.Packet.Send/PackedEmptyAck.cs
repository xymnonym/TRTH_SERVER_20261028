using System.Collections.Generic;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	public sealed class PackedEmptyAck : NetPacket
	{
		public PackedEmptyAck(ushort packedAckWire, byte last, int extraIntCount = 2)
			: this(packedAckWire, last, extraIntCount, writeLast: true, extraBytes: 0)
		{
		}

		public PackedEmptyAck(ushort packedAckWire, byte last, int extraIntCount, bool writeLast)
			: this(packedAckWire, last, extraIntCount, writeLast, extraBytes: 0)
		{
		}

		public PackedEmptyAck(ushort packedAckWire, byte last, int extraIntCount, bool writeLast, int extraBytes)
		{
			_ = last;
			ns.Write((ushort)packedAckWire);
			for (int i = 0; i < extraIntCount; i++)
			{
				ns.Write(0);
			}
			for (int i = 0; i < extraBytes; i++)
			{
				ns.Write((byte)0);
			}
			if (writeLast)
			{
				_ = last;
			}
		}
	}

	/// <summary>
	/// ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY (1481).
	/// Wire: int + byte + long (trgame 0x762840). byte==1 means open.
	/// </summary>
	public sealed class Packed1481Ack : NetPacket
	{
		public Packed1481Ack(byte last)
			: this(0, last)
		{
		}

		public Packed1481Ack(int shopOrPeriodNum, byte last)
		{
			_ = last;
			ns.WriteOP(Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY);
			ns.Write(shopOrPeriodNum);
			ns.Write((byte)1);
			// End/open stamp far in the future (Gregorian-safe ms).
			ns.Write(LocalCommons.Utilities.Utility.CurrentTimeMilliseconds() + 86400000L * 365L * 50L);
		}
	}

	/// <summary>
	/// Packed wire 396. Client sends period id on wire 214 at lobby open.
	/// Without an open window the Special Event tab stays empty.
	/// </summary>
	public sealed class PackedEntrySystemPeriodNotify : NetPacket
	{
		public PackedEntrySystemPeriodNotify(int period, long openTime, long closeTime, byte last)
		{
			ns.WriteOP(Opcodes.eServer_ENTRY_SYSTEM_PERIOD_SCHEDULE_NOTIFY);
			ns.Write(period);
			ns.Write(1);
			ns.Write(openTime);
			ns.Write(closeTime);
			// Size 30 Remain 3: extra int was too much; one byte fills Overpop 23 26 4.
			ns.Write((byte)0);
			_ = last;
		}
	}

	public sealed class PackedItemCollectionAck : NetPacket
	{
		public PackedItemCollectionAck(int value, byte last)
		{
			ns.Write((ushort)2177);
			ns.Write(value);
			_ = last;
		}

		public PackedItemCollectionAck(int rank, int point, byte last)
		{
			_ = rank;
			ns.Write((ushort)2177);
			ns.Write(point);
			_ = last;
		}
	}

	/// <summary>
	/// ItemCollection USER_INFO (inner 3). First int after inner is status;
	/// non-zero triggers OnRecvItemCollection_Failed. Do not append TCP last.
	/// Must keep kind + mode-byte + userNum(long) before point fields — omitting
	/// them Overpops (39/46) and My Info stays at 99999.
	/// </summary>
	public sealed class PackedItemCollectionUserInfoAck : NetPacket
	{
		public PackedItemCollectionUserInfoAck(long userNum, int monthPoint, int totalPoint, int heldPoint, byte noticedLevel, int rank, byte last)
			: this(userNum, monthPoint, totalPoint, heldPoint, 0, noticedLevel, rank, last)
		{
		}

		public PackedItemCollectionUserInfoAck(long userNum, int monthPoint, int totalPoint, int heldPoint, int itemCount, byte noticedLevel, int rank, byte last)
			: this(userNum, monthPoint, 0, totalPoint > 0 ? totalPoint : heldPoint, 0, itemCount, noticedLevel, rank, 0, last)
		{
		}

		/// <summary>
		/// 52 byte USER_INFO. The three ints after the month pair were all sent as
		/// the same total, which is why "this month" showed the lifetime score.
		/// </summary>
		public PackedItemCollectionUserInfoAck(long userNum, int monthPoint, int monthGoal, int totalPoint, int levelPoint, int itemCount, byte noticedLevel, int rank, int monthRewardItem, byte last)
		{
			_ = last;
			int total = totalPoint;
			// Month goal from settingitem_collectionrenewal_monthdata, falling back
			// to the lifetime total when no month is configured.
			int goal = monthGoal > 0 ? monthGoal : total;
			// A negative month means there is no month row to report, so fall back
			// to the lifetime score rather than claiming zero progress. The window
			// never shows more than the goal, so clamp rather than let the bar run
			// past its own maximum.
			int month = monthPoint >= 0 ? monthPoint : total;
			if (month > goal)
			{
				month = goal;
			}
			ns.Write((ushort)2177);
			// Mode 3 is USER_INFO. Anything else here and the client treats the
			// packet as unknown and shows 99999 in every field.
			ns.Write(3);
			// Every int in this packet was tried as the month reward item and none
			// of them feeds the reward box. Both header ints and the last two of
			// the four trailing ints make the client drop the packet and fall back
			// to its 99999 placeholders unless they are zero; the first trailing
			// int accepts a value but is not read. The client requests nothing of
			// its own when the box opens, so the number arrives in a packet this
			// server does not send yet.
			ns.Write(0);
			ns.Write(0);
			ns.Write((byte)1);
			// My Info level bar reads the 8 bytes after mode as points (low int).
			// userNum=1 made UI show 1/150 (Rookie). Put total points here instead.
			_ = userNum;
			// This is the number the percentage is measured with: sending the whole
			// collection score here read 100.93 per cent against a base pool of
			// 312,358. The fourth int is not what the percentage uses.
			_ = levelPoint;
			ns.Write((long)total);
			ns.Write(month);
			ns.Write(goal);
			ns.Write(total);
			ns.Write(noticedLevel);
			_ = itemCount;
			// 52 byte USER_INFO: four ints after noticedLevel.
			// 48-byte packet Overpop +4; 56-byte left Remain 8.
			//
			// The first of the four is the month reward item, not the rank. Sending
			// the rank there put item number 1 in the reward box, which drew empty,
			// and left the rank reading the zero in the second int. A number that is
			// not a real item makes the client drop the whole packet and fall back
			// to its 99999 placeholders, which is what the 7101 probe did.
			// Slot 1 is the rank: putting it there made the window show 1 instead of
			// 0. Slots 2 and 3 must stay zero or the client drops the packet.
			ns.Write(0);
			ns.Write(rank > 0 ? rank : (total > 0 ? 1 : 0));
			ns.Write(0);
			ns.Write(0);
		}
	}

	/// <summary>
	/// ItemCollection MapAddItemNotify (inner 10). CollectionNum table
	/// rejects DB fdIDs — do not send on open until ids match client data.
	/// </summary>
	public sealed class PackedItemCollectionOwnedListAck : NetPacket
	{
		public PackedItemCollectionOwnedListAck(IList<int> itemNums, byte last)
		{
			_ = last;
			ns.Write((ushort)2177);
			ns.Write(10);
			ns.Write(0);
			int count = (itemNums != null) ? itemNums.Count : 0;
			ns.Write(count);
			if (itemNums != null)
			{
				for (int i = 0; i < count; i++)
				{
					ns.Write(itemNums[i]);
				}
			}
		}
	}

	/// <summary>
	/// ItemCollection month/theme value (inner 5): status + one int.
	/// </summary>
	public sealed class PackedItemCollectionValueAck : NetPacket
	{
		public PackedItemCollectionValueAck(int value, byte last)
		{
			_ = last;
			ns.Write((ushort)2177);
			ns.Write(5);
			ns.Write(0);
			ns.Write(value);
		}
	}

	/// <summary>
	/// ItemCollection level/month reward claim (REQ inner 4, 7-byte). Echoing USER_INFO
	/// reopened LuckyBagItemCollectionLevelUpReward.gui (purple) with empty slots.
	/// ACK inner 4: status 0 + empty reward count closes the popup.
	/// </summary>
	public sealed class PackedItemCollectionRewardAck : NetPacket
	{
		public PackedItemCollectionRewardAck(byte last)
			: this(0, last)
		{
		}

		/// <summary>
		/// Mode 4 is the answer to the month reward claim. It used to carry two
		/// zeroes, which left the window showing the same empty box after the item
		/// had already landed in the inventory.
		/// </summary>
		public PackedItemCollectionRewardAck(int rewardItem, byte last)
		{
			_ = last;
			ns.Write((ushort)2177);
			ns.Write(4);
			ns.Write(rewardItem);
			ns.Write(0);
		}
	}
}
