using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AgentServer.Holders;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using LocalCommons.Utilities;
using Serilog;

namespace AgentServer.Packet.Send
{
	public sealed class SEASON_CHANNEL_SCHEDULE_ACK : NetPacket
	{
		public SEASON_CHANNEL_SCHEDULE_ACK(byte last)
		{
			ns.WriteOP(Opcodes.eServer_SEASON_CHANNEL_SCHEDULE_ACK);
			// debug_trgame 0x77CC00: count + (i32 id + str + str + str).
			// Wire dates are always Gregorian (2026…). GameDate converts BE→CE if DB/OS gave 2569.
			string kinds = ServerSettingHolder.ServerSettings.competitionEventUsingRoomKind;
			if (string.IsNullOrEmpty(kinds))
			{
				kinds = "381,382,383,384,385,386,310,311,312,313,314,369,370,371,372,373,374";
			}
			string start = FormatPeriod("CompetitionEventPeriod_StartDateTime", "2026-01-01 00:00:00");
			string end = FormatPeriod("CompetitionEventPeriod_EndDateTime", "2099-12-31 23:59:59");
			// The Animal Village map buttons stay hidden and the meaning of the entry id
			// is still unknown, so allow the whole entry list to come from a file while
			// that is being traced. One line per entry: id|start|end|roomKinds.
			// Delete the file to fall back to the single default entry.
			List<ScheduleEntry> entries = LoadOverride();
			if (entries == null)
			{
				entries = new List<ScheduleEntry>
				{
					new ScheduleEntry { Id = 1, Start = start, End = end, Kinds = kinds }
				};
			}
			ns.Write(entries.Count);
			foreach (ScheduleEntry entry in entries)
			{
				ns.Write(entry.Id);
				ns.WriteAnsiFixed_intSize(entry.Start);
				ns.WriteAnsiFixed_intSize(entry.End);
				ns.WriteAnsiFixed_intSize(entry.Kinds);
			}
			Log.Information("SeasonChannelSchedule entries={Count} first={First}",
				entries.Count, entries.Count > 0 ? entries[0].Id : -1);
			_ = last;
		}

		private sealed class ScheduleEntry
		{
			public int Id;
			public string Start;
			public string End;
			public string Kinds;
		}

		private static List<ScheduleEntry> LoadOverride()
		{
			try
			{
				string path = Path.Combine("Logs", "Agent", "season_schedule.txt");
				if (!File.Exists(path))
				{
					return null;
				}
				List<ScheduleEntry> list = new List<ScheduleEntry>();
				foreach (string line in File.ReadAllLines(path))
				{
					string text = line.Trim();
					if (text.Length == 0 || text.StartsWith("#"))
					{
						continue;
					}
					string[] parts = text.Split('|');
					if (parts.Length < 4)
					{
						continue;
					}
					int id;
					if (!int.TryParse(parts[0].Trim(), out id))
					{
						continue;
					}
					list.Add(new ScheduleEntry
					{
						Id = id,
						Start = parts[1].Trim(),
						End = parts[2].Trim(),
						Kinds = parts[3].Trim()
					});
				}
				return list.Count > 0 ? list : null;
			}
			catch (Exception ex)
			{
				Log.Warning("SeasonChannelSchedule override failed: {0}", ex.Message);
				return null;
			}
		}

		private static string FormatPeriod(string key, string fallback)
		{
			string raw = fallback;
			try
			{
				foreach (var item in ServerSettingHolder.ServerSettingList)
				{
					if (item != null && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)
						&& !string.IsNullOrWhiteSpace(item.Value))
					{
						raw = item.Value.Trim();
						break;
					}
				}
			}
			catch
			{
			}
			DateTime dt;
			if (GameDate.TryParse(raw, out dt))
			{
				return GameDate.Format(dt);
			}
			return fallback;
		}
	}
}
