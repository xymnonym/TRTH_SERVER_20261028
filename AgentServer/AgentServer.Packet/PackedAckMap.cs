using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgentServer.Database;
using AgentServer.Holders;
using AgentServer.Network.Connections;
using AgentServer.Packet.Send;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using LocalCommons.Utilities;
using Serilog;

namespace AgentServer.Packet
{
	/// <summary>
		/// Lobby REQs with no dedicated handler (empty ACK stubs).
		/// Only _REQ→_ACK pairs that exist in tools/trgame_NATIVE_OPCODE_TABLE.csv.
		/// Stub body is N ints + last; dbgtrace Remain/Overpop is the layout source.
	/// </summary>
	public static class PackedAckMap
	{
		private static readonly Dictionary<ushort, ushort> ReqWireToAckWire = new Dictionary<ushort, ushort>
		{
			{ (ushort)Opcodes.eServer_MESSAGE_GET_FRIEND_ADD_COUNT_REQ, (ushort)Opcodes.eServer_MESSAGE_GET_FRIEND_ADD_COUNT_ACK },
			{ (ushort)Opcodes.eServer_FASHION_COORDI_KING_BOOKMARK_REQ, (ushort)Opcodes.eServer_FASHION_COORDI_KING_BOOKMARK_ACK },
			{ (ushort)Opcodes.eServer_FASHION_COORDI_KING_TOP3RANK_REQ, (ushort)Opcodes.eServer_FASHION_COORDI_KING_TOP3RANK_ACK },
			{ (ushort)Opcodes.eServer_COMBINATION_SHOP_GET_SCHEDULE_INFO_REQ, (ushort)Opcodes.eServer_COMBINATION_SHOP_GET_SCHEDULE_INFO_ACK },
			{ (ushort)Opcodes.eServer_CUSTOM_RANDOMBOX_DATA_REQ, (ushort)Opcodes.eServer_CUSTOM_RANDOMBOX_DATA_ACK },
			{ (ushort)Opcodes.eServer_CUSTOM_RANDOMBOX_USERDATA_REQ, (ushort)Opcodes.eServer_CUSTOM_RANDOMBOX_USERDATA_ACK },
			{ (ushort)Opcodes.eServer_STORAGE_USER_COUNT_REQ, (ushort)Opcodes.eServer_STORAGE_USER_COUNT_ACK },
			{ (ushort)Opcodes.eServer_PRIVATE_PUZZLE_GET_MYINFO_REQ, (ushort)Opcodes.eServer_PRIVATE_PUZZLE_GET_MYINFO_ACK },
			{ (ushort)Opcodes.eServer_PRIVATE_PUZZLE_GET_TRY_REWARD_LIST_REQ, (ushort)Opcodes.eServer_PRIVATE_PUZZLE_GET_TRY_REWARD_LIST_ACK },
			{ (ushort)Opcodes.eServer_MAP_BOOKMARK_GET_USER_INFO_REQ, (ushort)Opcodes.eServer_MAP_BOOKMARK_GET_USER_INFO_ACK },
			{ (ushort)Opcodes.eServer_LENS_GET_USER_INFO_REQ, (ushort)Opcodes.eServer_LENS_GET_USER_INFO_ACK },
			{ (ushort)Opcodes.eServer_SELECT_SHOP_TRADE_INFO_REQ, (ushort)Opcodes.eServer_SELECT_SHOP_TRADE_INFO_ACK },
			{ (ushort)Opcodes.eServer_SELECT_SHOP_USER_INFO_REQ, (ushort)Opcodes.eServer_SELECT_SHOP_USER_INFO_ACK },
			{ (ushort)Opcodes.eServer_STAT_SYSTEM_MYINFO_REQ, (ushort)Opcodes.eServer_STAT_SYSTEM_MYINFO_ACK },
			// StatSystem save/set (native table wires).
			{ (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_PAGE_REQ, (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_PAGE_ACK },
			{ (ushort)Opcodes.eServer_STAT_SYSTEM_SET_PAGENUM_REQ, (ushort)Opcodes.eServer_STAT_SYSTEM_SET_PAGENUM_ACK },
			{ (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_TITLE_REQ, (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_TITLE_ACK },
			{ (ushort)Opcodes.eServer_COLLECTION_HELPER_MISSION_GET_USER_MISSION_REQ, (ushort)Opcodes.eServer_COLLECTION_HELPER_MISSION_GET_USER_MISSION_ACK },
			{ (ushort)Opcodes.eServer_NEW_ATTENDANCE_RECV_REWARD_REQ, (ushort)Opcodes.eServer_NEW_ATTENDANCE_RECV_REWARD_ACK },
			{ (ushort)Opcodes.eServer_FAIRY_TALE_BOOK_GET_TOP_50_RANKER_LIST_REQ, (ushort)Opcodes.eServer_FAIRY_TALE_BOOK_GET_TOP_50_RANKER_LIST_ACK },
			{ (ushort)Opcodes.eServer_SPECIAL_ASSIST_ITEM_REQ, (ushort)Opcodes.eServer_SPECIAL_ASSIST_ITEM_ACK },
			{ (ushort)Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_REQ, (ushort)Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY },
			{ (ushort)Opcodes.eServer_DOM_INFO_REQ, (ushort)Opcodes.eServer_DOM_INFO_ACK },
			{ (ushort)Opcodes.eServer_CUSTOM_PACKAGE_DATA_REQ, (ushort)Opcodes.eServer_CUSTOM_PACKAGE_DATA_ACK },
		};

		// Default stub is two ints (size 11 Remain 1). 19:23 dbgtrace:
		// Overpop 10 11 4 = need a third int; Remain 5 size 11 = one int only.
		private static readonly Dictionary<ushort, int> AckWireIntCount = new Dictionary<ushort, int>
		{
			{ (ushort)Opcodes.eServer_FASHION_COORDI_KING_TOP3RANK_ACK, 3 },
			{ (ushort)Opcodes.eServer_STORAGE_USER_COUNT_ACK, 3 },
			{ (ushort)Opcodes.eServer_LENS_GET_USER_INFO_ACK, 3 },
			{ (ushort)Opcodes.eServer_SELECT_SHOP_USER_INFO_ACK, 5 },
			{ (ushort)Opcodes.eServer_STAT_SYSTEM_MYINFO_ACK, 4 },
			{ (ushort)Opcodes.eServer_SELECT_SHOP_TRADE_INFO_ACK, 1 },
			{ (ushort)Opcodes.eServer_COLLECTION_HELPER_MISSION_GET_USER_MISSION_ACK, 1 },
			{ (ushort)Opcodes.eServer_NEW_ATTENDANCE_RECV_REWARD_ACK, 2 },
			{ (ushort)Opcodes.eServer_FAIRY_TALE_BOOK_GET_TOP_50_RANKER_LIST_ACK, 1 },
			{ (ushort)Opcodes.eServer_SPECIAL_ASSIST_ITEM_ACK, 1 },
			// ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY: 2 ints Overpop; 4 ints RemainSize=4 → 3 ints no last.
			// Overpop 7 14 8 — client pops byte+int then long; need OP(2)+byte(1)+int(4)+long(8)=15 min.
			{ (ushort)Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY, 3 },
			// CUSTOM_PACKAGE_DATA_ACK: 2 ints RemainSize=4 on size 10 → one int.
			{ (ushort)Opcodes.eServer_CUSTOM_PACKAGE_DATA_ACK, 1 },
		};

		public static bool TrySendEmptyAck(ClientConnection client, ushort wire, PacketReader reader, byte last)
		{
			if (wire == (ushort)Opcodes.eServer_STAT_SYSTEM_MYINFO_REQ)
			{
				StatSystemSp.SendMyInfo(client, last);
				return true;
			}
			// StatSystem SET_PAGENUM — third int is payPageResult (0=OK), NOT Hold SP.
			if (wire == (ushort)Opcodes.eServer_STAT_SYSTEM_SET_PAGENUM_REQ)
			{
				int page = 0;
				if (reader.Remaining >= 4)
				{
					page = reader.ReadLEInt32();
				}
				if (page < 1 || page > 10)
				{
					page = 1;
				}
				int maxPage = client.CurrentAccount != null ? client.CurrentAccount.StatSystemPageCount : 1;
				if (maxPage < 1)
				{
					maxPage = 1;
				}
				if (page > maxPage)
				{
					page = maxPage;
				}
				if (client.CurrentAccount != null)
				{
				try { StatSystemSp.SetActivePage(client.CurrentAccount, page); }
				catch (Exception ex)
				{
					Log.Warning(ex, "StatSystem SetActivePage failed userNum={UserNum} page={Page}", client.CurrentAccount.UserNum, page);
					return true;
				}
				}
				client.SendAsync(new StatSystemSetPageNumAck(page, last));
				if (client.CurrentAccount != null)
				{
					client.SendAsync(new eServer_GET_USER_CHARACTER_ATTR_ACK(client.CurrentAccount, last));
					client.SendAsync(new GET_AVATAR_ACK(client.CurrentAccount, false, last));
					_ = RefreshProfileAfterStatWindowClose(client, client.CurrentAccount, page, last);
				}
				return true;
			}
			// R186632 SAVE_PAGE: page, node count, (attribute id, level) pairs, remaining SP, selected flag.
			if (wire == (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_PAGE_REQ)
			{
				try
				{
					if (client.CurrentAccount == null || reader.Remaining < 13)
						throw new InvalidOperationException("Incomplete StatSystem SavePage request");
					int page = reader.ReadLEInt32();
					int count = reader.ReadLEInt32();
					if (count < 0 || count > 150 || reader.Remaining < count * 8 + 9)
						throw new InvalidOperationException("Invalid StatSystem node count: " + count);
					var desired = new Dictionary<int, int>();
					for (int i = 0; i < count; i++)
					{
						int node = reader.ReadLEInt32();
						int level = reader.ReadLEInt32();
						if (desired.ContainsKey(node)) throw new InvalidOperationException("Duplicate StatSystem node: " + node);
						desired.Add(node, level);
					}
					int claimedRemain = reader.ReadLEInt32();
					int selected = reader.ReadLEInt32();
					if (reader.Remaining != 1) throw new InvalidOperationException("Trailing StatSystem SavePage data");
					int remain = StatSystemSp.SavePage(client.CurrentAccount, page, desired, claimedRemain);
					StatSystemSnapshot saved = StatSystemSp.Load(client.CurrentAccount);
					client.SendAsync(new StatSystemSavePageAck(page, saved, selected, last));
					client.SendAsync(new eServer_GET_USER_CHARACTER_ATTR_ACK(client.CurrentAccount, last));
					client.SendAsync(new GET_AVATAR_ACK(client.CurrentAccount, false, last));
					Log.Information("StatSystem SavePage ACK userNum={UserNum} page={Page} nodes={NodeCount} remainSp={RemainSp}",
						client.CurrentAccount.UserNum, page, count, remain);
				}
				catch (Exception ex)
				{
					Log.Warning(ex, "StatSystem SavePage rejected for userNum={UserNum}", client.CurrentAccount?.UserNum);
					client.SendAsync(new StatSystemSavePageFailedAck());
				}
				return true;
			}
			// R186632 sends visible page ids 1..10; page 0 title rows from older packets remain readable.
			if (wire == (ushort)Opcodes.eServer_STAT_SYSTEM_SAVE_TITLE_REQ)
			{
				int page = 0;
				string title = string.Empty;
				if (reader.Remaining >= 4)
				{
					page = reader.ReadLEInt32();
				}
				if (reader.Remaining >= 2)
				{
					short titleLen = reader.ReadLEInt16();
					if (titleLen > 0 && reader.Remaining >= titleLen)
					{
						title = reader.ReadBig5StringSafe(titleLen) ?? string.Empty;
					}
				}
				if (client.CurrentAccount != null)
				{
					int idx = page - 1;
					// Reject page ids outside the unlocked pages; never unlock a page by renaming it.
					int maxPage = client.CurrentAccount.StatSystemPageCount;
					if (maxPage < 1)
					{
						maxPage = 1;
					}
					if (idx < 0 || idx >= maxPage || idx >= 10)
					{
						Log.Warning("StatSystem SaveTitle invalid page userNum={UserNum} page={Page} unlocked={Unlocked}", client.CurrentAccount.UserNum, page, maxPage);
						return true;
					}
					client.CurrentAccount.StatSystemTitles[idx] = title;
					try
					{
						using MySqlCommandHelper cmd = new MySqlCommandHelper("usp_StatSystem_SetPageTitle");
						cmd.AddParamInt("usernum", client.CurrentAccount.UserNum);
						cmd.AddParamInt("pageNum", page);
						cmd.AddParamVarString("title", title ?? string.Empty);
						cmd.ExecuteNonQuery();
					}
					catch (Exception ex)
					{
						Log.Warning("usp_StatSystem_SetPageTitle: {0}", ex.Message);
					}
				}
				client.SendAsync(new StatSystemSaveTitleAck(page, title, last));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_CASH_POINT_REQ && client.CurrentAccount != null)
			{
				client.SendAsync(new CashPointAck(client.CurrentAccount, last));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_STORAGE_USER_COUNT_REQ)
			{
				// STORAGE_USER_COUNT_REQ → real keeping/max counts (not zero stub).
				client.SendAsync(new StorageUserCountAck(client.CurrentAccount, last));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_DOM_INFO_REQ)
			{
				// 885 = DOM_INFO_REQ → 1562. Live empty ACK was 48 bytes; size 6 Overpops +4.
				client.SendAsync(new PackedEmptyAck((ushort)Opcodes.eServer_DOM_INFO_ACK, last, 11, writeLast: false, extraBytes: 2));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_ENTRY_SYSTEM_PERIOD_SCHEDULE_REQ)
			{
				int period = 0;
				if (reader.Remaining >= 4)
				{
					period = reader.ReadLEInt32();
				}
				// Open window from Gregorian competition period (DB) or now±span.
				// Always InvariantCulture / BE→CE via GameDate — never CurrentCulture parse.
				long open;
				long close;
				if (!TryCompetitionPeriodTimestamps(out open, out close))
				{
					long now = Utility.CurrentTimeMilliseconds();
					open = now - 3600000L;
					close = now + 86400000L * 365L * 50L;
				}
				client.SendAsync(new PackedEntrySystemPeriodNotify(period, open, close, last));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_REQ)
			{
				int shopNum = 0;
				if (reader.Remaining >= 4)
				{
					shopNum = reader.ReadLEInt32();
				}
				// debug_trgame 0x762840: i32 + u8 openFlag + i64. openFlag must be 1.
				client.SendAsync(new Packed1481Ack(shopNum, last));
				return true;
			}
			if (wire == (ushort)Opcodes.eServer_NEW_ATTENDANCE_RECV_REWARD_REQ)
			{
				// REQ CE-00-<reward type>-<last>; grant and persist before success ACK.
				AttendanceHandle.Handle_NewAttendanceReward(client, reader, last);
				return true;
			}
			if (!ReqWireToAckWire.TryGetValue(wire, out ushort ack))
			{
				return false;
			}
			if (ack == (ushort)Opcodes.eServer_ENTRY_SYSTEM_SHOP_SCHEDULE_NOTIFY)
			{
				client.SendAsync(new Packed1481Ack(last));
				return true;
			}
			int ints = 2;
			if (AckWireIntCount.TryGetValue(ack, out int n))
			{
				ints = n;
			}
			client.SendAsync(new PackedEmptyAck(ack, last, ints));
			return true;
		}

		// Refresh the MyRoom status panel after the StatSystem close transition.
		// The character-setting ACK triggers recalculation, and the following avatar
		// ACK restores the current worn items in an already-open MyRoom.
		private static async Task RefreshProfileAfterStatWindowClose(ClientConnection client,
			global::AgentServer.Structuring.Account account, int requestedPage, byte last)
		{
			try
			{
				await Task.Delay(400).ConfigureAwait(false);
				if (!ReferenceEquals(client.CurrentAccount, account) || account.isDisconnected)
					return;
				client.SendAsync(new eServer_GET_USER_CHARACTER_ATTR_ACK(account, last));
				client.SendAsync(new MyRoom_GetCharacterSetting_ACK(account.advancedAvatarInfo.m_realAvatarInfo, last));
				await Task.Delay(80).ConfigureAwait(false);
				if (!ReferenceEquals(client.CurrentAccount, account) || account.isDisconnected)
					return;
				client.SendAsync(new GET_AVATAR_ACK(account, false, last));
				Log.Information("StatSystem post-close character and avatar refresh userNum={UserNum} requestedPage={Page}",
					account.UserNum, requestedPage);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "StatSystem post-close profile refresh failed userNum={UserNum}", account.UserNum);
			}
		}

		private static bool TryCompetitionPeriodTimestamps(out long open, out long close)
		{
			open = 0;
			close = 0;
			string startText = null;
			string endText = null;
			try
			{
				foreach (var item in ServerSettingHolder.ServerSettingList)
				{
					if (item == null || string.IsNullOrWhiteSpace(item.Value))
					{
						continue;
					}
					if (string.Equals(item.Key, "CompetitionEventPeriod_StartDateTime", StringComparison.OrdinalIgnoreCase))
					{
						startText = item.Value;
					}
					else if (string.Equals(item.Key, "CompetitionEventPeriod_EndDateTime", StringComparison.OrdinalIgnoreCase))
					{
						endText = item.Value;
					}
				}
			}
			catch
			{
				return false;
			}
			DateTime startDt;
			DateTime endDt;
			if (!GameDate.TryParse(startText ?? "2026-01-01 00:00:00", out startDt)
				|| !GameDate.TryParse(endText ?? "2099-12-31 23:59:59", out endDt))
			{
				return false;
			}
			open = GameDate.ToTimestamp(startDt);
			close = GameDate.ToTimestamp(endDt);
			return close > open;
		}
	}
}
