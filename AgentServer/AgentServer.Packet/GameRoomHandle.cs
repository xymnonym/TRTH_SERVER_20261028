using System;
using System.Collections.Generic;
using System.Linq;
using AgentServer;
using AgentServer.Holders;
using AgentServer.Network.Connections;
using AgentServer.Packet.RoomServer;
using AgentServer.Packet.Send;
using AgentServer.Structuring;
using AgentServer.Structuring.Opcode;
using AgentServer.Structuring.Room;
using LocalCommons.Network;
using LocalCommons.Utilities;
using NetMsg.Room;
using Serilog;

namespace AgentServer.Packet
{
	public class GameRoomHandle
	{
		public static void Handle_CreateGameRoom(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			if (currentAccount.InGame)
			{
				return;
			}
			RoomSettings requested;
			try
			{
				requested = ReadCreateRoomRequest(reader);
			}
			catch (FormatException)
			{
				Client.SendAsync(new GameRoom_CreateRoomError(9, last));
				Log.Warning("Malformed MAKE_ROOM userNum: {0}", currentAccount.UserNum);
				return;
			}
			string name = requested.Name;
			string password = requested.Password;
			int num = requested.MapNum;
			int num3 = requested.RoomKindID;
			int isTeamPlay = requested.IsTeamPlay;
			int itemType = requested.ItemType;
			bool isStepOn = requested.IsStepOn;
			if (num3 == 79 && (currentAccount.GuildNum <= 0 || currentAccount.GuildInfo == null))
			{
				Client.SendAsync(new GameRoom_CreateRoomError(9, last));
				return;
			}
			if (!RoomHolder.RoomKindInfos.TryGetValue(num3, out var value))
			{
				Client.SendAsync(new GameRoom_CreateRoomError(9, last));
				Log.Warning("Invalid RoomKind ID:{0} userNum: {1}", num3, currentAccount.UserNum);
				return;
			}

			if (value.GameMode == 39)
			{
				if (!ServerSettingHolder.ServerSettings.useThankOfferingSystem)
				{
					Client.SendAsync(new GameRoom_CreateRoomError(9, last));
					return;
				}
				if (!ThankOfferingSystem.ThankOfferingSchedule.TryGetValue(ServerSettingHolder.ServerSettings.ThankOfferingSchedule_CurNum, out var value2))
				{
					Client.SendAsync(new GameRoom_CreateRoomError(9, last));
					return;
				}
				if (!(value2.StartTime <= DateTime.Now) || !(DateTime.Now <= value2.EndTime))
				{
					Client.SendAsync(new GameRoom_CreateRoomError(9, last));
					return;
				}
			}
			if (!Rooms.CheckRoomServer())
			{
				Client.SendAsync(new GameRoom_CreateRoomError(9, last));
				Log.Error("No room server connected!");
				return;
			}
			RoomSettings roomsetting = new RoomSettings
			{
				Name = name,
				Password = password,
				IsTeamPlay = isTeamPlay,
				ItemType = itemType,
				IsStepOn = isStepOn,
				// Official park ENTER numMap=1 (shu maps). MapNum=0 broke Shu park.
				MapNum = ((num3 == ParkRoomKindId) ? 1 : ((value.GameMode == 55) ? ((num <= 0) ? 0 : num) : ((num <= 0) ? 1 : num))),
				RoomKindID = num3,
				roomkindinfo = value
			};
			ServerStatus.ToRoomServer(new RM_SendPlayerInfo(currentAccount, roomsetting, 0, last), 0);
		}

		// Current client writer 0x14189b430: four int32s, THREE ushort-length
		// strings (name, auxiliary, password), then kind/options. The auxiliary
		// string is separate even when empty; it is not part of password length.
		internal static RoomSettings ReadCreateRoomRequest(PacketReader reader)
		{
			if (reader.Remaining < 16) throw new FormatException("MAKE_ROOM header");
			reader.Offset += 12;
			int map = reader.ReadLEInt32();
			string name = ReadCreateRoomString(reader);
			ReadCreateRoomString(reader); // auxiliary string, normally empty
			string password = ReadCreateRoomString(reader);
			if (reader.Remaining < 16) throw new FormatException("MAKE_ROOM options");
			int kind = reader.ReadLEInt32();
			reader.ReadLEInt32();
			int team = reader.ReadLEInt32();
			reader.Offset += 2;
			int item = reader.ReadByte();
			bool step = reader.ReadBoolean();
			return new RoomSettings { Name = name, Password = password, MapNum = map,
				RoomKindID = kind, IsTeamPlay = team, ItemType = item, IsStepOn = step };
		}

		private static string ReadCreateRoomString(PacketReader reader)
		{
			if (reader.Remaining < 2) throw new FormatException("MAKE_ROOM string length");
			int length = reader.ReadLEUInt16();
			if (length > reader.Remaining) throw new FormatException("MAKE_ROOM string data");
			return reader.ReadBig5StringSafe(length);
		}

		public static void Handle_LeaveRoom(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			NormalRoom room = Rooms.GetRoom(currentAccount.CurrentRoomId);
			try
			{
				if (currentAccount.InGame && room != null)
				{
					ServerStatus.ToRoomServer(new RM_PlayerLeaveRoom(currentAccount, isDisconnect: false, last), room.RoomServerID);
				}
			}
			catch (Exception ex)
			{
				Log.Error("Player [{0}] error on leave room:\r\n{1}", currentAccount.NickName, ex.ToString());
			}
		}

		public static void Handle_RoomControl(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int num = reader.Size - reader.Offset;
			byte[] array = new byte[num];
			Buffer.BlockCopy(reader.Buffer, reader.Offset, array, 0, num);
			ServerStatus.ToRoomServer(new RM_Packet
			{
				Session = currentAccount.Session,
				data = array
			}, currentAccount.RoomServerID);
		}

		public static void Handle_PackedRoomWrap(ClientConnection Client, PacketReader reader, byte last)
		{
			_ = last;
			Account currentAccount = Client.CurrentAccount;
			int num = reader.Size - reader.Offset;
			if (num < 2 || currentAccount == null)
			{
				return;
			}
			byte[] array = new byte[num];
			Buffer.BlockCopy(reader.Buffer, reader.Offset, array, 0, num);
			ushort innerWire = (ushort)(array[0] | (array[1] << 8));
			if (Conf.ProtocolDebug)
			{
				Log.Information("Packed room wrap 1040 wire={0} bytes={1} user={2}", innerWire, num, currentAccount.UserID);
			}
			ServerStatus.ToRoomServer(new RM_Packet
			{
				Session = currentAccount.Session,
				data = array
			}, currentAccount.RoomServerID);
		}

		public static void Handle_GameEndInfo(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			float racedistance = reader.ReadLESingle();
			float mapMaxDistance = reader.ReadLESingle();
			short gameendtype = reader.ReadLEInt16();
			Utility.CurrentTimeMilliseconds();
			UserGameEndInfo info = new UserGameEndInfo
			{
				racedistance = racedistance,
				MapMaxDistance = mapMaxDistance,
				gameendtype = gameendtype
			};
			ServerStatus.ToRoomServer(new RM_GameEndInfo(currentAccount, info, last), currentAccount.RoomServerID);
		}

		public static void Handle_PlayerList(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			NormalRoom room = Rooms.GetRoom(currentAccount.CurrentRoomId);
			if (room != null)
			{
				ServerStatus.ToRoomServer(new RM_GetPlayerPosList(currentAccount, last), room.RoomServerID);
			}
		}

		public static void Handle_GetRoomList(ClientConnection Client, PacketReader reader, byte last)
		{
			Account User = Client.CurrentAccount;
			int roomkindid = reader.ReadLEInt32();
			reader.ReadLEInt32();
			int page = reader.ReadLEInt32();
			byte getCount = reader.ReadByte();
			List<NormalRoom> list = Rooms.RoomList.Values.Where((NormalRoom room) => room.RoomKindID == roomkindid).ToList();
			if (roomkindid == 79)
			{
				list = list.Where((NormalRoom room) => room.guildName != User.GuildInfo.guildName).ToList();
			}
			Client.SendAsync(new GameRoom_GetRoomList(list, roomkindid, page, getCount, last));
		}

		public static void Handle_EnterRoom(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int roomsession = reader.ReadLEInt32();
			int num = reader.ReadLEInt16();
			string pw = string.Empty;
			if (num > 0)
			{
				pw = reader.ReadBig5StringSafe(num);
			}
			NormalRoom room = Rooms.GetRoom(roomsession);
			if (room != null)
			{
				ServerStatus.ToRoomServer(new RM_PlayerEnterRoom(currentAccount, pw, room.ID, room.RoomKindID, 0, 0, last), room.RoomServerID);
			}
			else
			{
				Client.SendAsync(new GameRoom_EnterRoomError(1, 0, last));
			}
		}

		public static void Handle_EnterRoomForGuild(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int guildmatchroomid = reader.ReadLEInt32();
			int roomsession = reader.ReadLEInt32();
			reader.ReadLEInt32();
			int fixedLength = reader.ReadLEInt16();
			string pw = reader.ReadBig5StringSafe(fixedLength);
			NormalRoom room = Rooms.GetRoom(roomsession);
			if (room != null)
			{
				ServerStatus.ToRoomServer(new RM_PlayerEnterRoom(currentAccount, pw, room.ID, room.RoomKindID, 0, guildmatchroomid, last), room.RoomServerID);
			}
			else
			{
				Client.SendAsync(new GameRoom_EnterRoomError(1, 155, last));
			}
		}

		public static void Handle_KickPlayer(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int fixedLength = reader.ReadLEInt16();
			string kickedplayername = reader.ReadBig5StringSafe(fixedLength);
			NormalRoom room = Rooms.GetRoom(currentAccount.CurrentRoomId);
			if (room != null)
			{
				ServerStatus.ToRoomServer(new RM_KickPlayer(currentAccount, kickedplayername, last), room.RoomServerID);
			}
		}

		public static void Handle_RandomEnterRoom(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int roomkindid = reader.ReadLEInt32();
			reader.ReadByte();
			reader.ReadByte();
			reader.ReadByte();
			int mapnum = reader.ReadLEInt32();
			reader.ReadLEInt32();
			IEnumerable<NormalRoom> source = Rooms.RoomList.Values.Where((NormalRoom rm) => rm.RoomKindID == roomkindid && rm.PlayerCount < rm.SlotCount && !rm.isPlaying && !rm.HasPassword && !rm.hasAfreecaTV);
			if (RoomHolder.RoomKindInfos.TryGetValue(roomkindid, out var value))
			{
				if (value.Channel == 46 || value.Channel == 47)
				{
					int num = currentAccount.PartyType & 1;
					if (RoomHolder.RoomKindPlayerNum.TryGetValue(roomkindid, out var value2))
					{
						int teammaxcount = value2.MaxUser / 2;
						switch (num)
						{
						case 1:
							source = Rooms.RoomList.Values.Where((NormalRoom rm) => rm.RoomKindID == roomkindid && rm.RedTeamCount < teammaxcount && !rm.isPlaying && !rm.HasPassword && !rm.hasAfreecaTV);
							break;
						case 0:
							source = Rooms.RoomList.Values.Where((NormalRoom rm) => rm.RoomKindID == roomkindid && rm.BlueTeamCount < teammaxcount && !rm.isPlaying && !rm.HasPassword && !rm.hasAfreecaTV);
							break;
						}
					}
				}
				if (mapnum > 0)
				{
					source = source.Where((NormalRoom w) => w.MapNum == mapnum);
				}
			}
			if (source.Count() == 0)
			{
				// Park (roomkind 74) always quick-joins. With no room up, auto-create
				// instead of fail ACK bodies that left Remain 17 and froze the lobby.
				if (TryCreateRoomForQuickJoin(Client, roomkindid, last))
				{
					return;
				}
				Client.SendAsync(new GameRoom_RandomEnterRoomError(roomkindid, last));
				return;
			}
			NormalRoom normalRoom = source.OrderBy((NormalRoom _) => Guid.NewGuid()).FirstOrDefault();
			ServerStatus.ToRoomServer(new RM_PlayerEnterRoom(currentAccount, string.Empty, normalRoom.ID, normalRoom.RoomKindID, 0, 0, last), normalRoom.RoomServerID);
		}

		private const int ParkRoomKindId = 74;
		// EventChannel / summer (eRoomKind_SUMMER_CHANNEL_2025_*): channel 61.
		private const int EventChannelId = 61;

		private static bool TryCreateRoomForQuickJoin(ClientConnection Client, int roomkindid, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			if (currentAccount.InGame)
			{
				return false;
			}
			if (!RoomHolder.RoomKindInfos.TryGetValue(roomkindid, out var value))
			{
				Log.Error("Invalid RoomKind ID:{0} userNum: {1}", roomkindid, currentAccount.UserNum);
				return false;
			}
			bool isPark = roomkindid == ParkRoomKindId;
			// EventChannel (61) + competition PlayerCount kinds (summer 381+).
			bool isEvent = value.Channel == EventChannelId
				|| (roomkindid >= 310 && RoomHolder.RoomKindPlayerNum.ContainsKey(roomkindid));
			if (!isPark && !isEvent)
			{
				return false;
			}
			if (!Rooms.CheckRoomServer())
			{
				Log.Error("No room server connected!");
				return false;
			}
			int mapNum = 1;
			string roomName = isPark ? "Park" : ("Event " + roomkindid);
			if (!isPark)
			{
				mapNum = ResolveEventMapNum(roomkindid, mapNum);
			}
			RoomSettings roomsetting = new RoomSettings
			{
				Name = roomName,
				Password = string.Empty,
				IsTeamPlay = 0,
				ItemType = 0,
				IsStepOn = false,
				MapNum = mapNum,
				RoomKindID = roomkindid,
				roomkindinfo = value
			};
			Log.Information(
				"Creating quick-join room kind={0} map={1} userNum={2}",
				roomkindid, mapNum, currentAccount.UserNum);
			try
			{
				ServerStatus.ToRoomServer(new RM_SendPlayerInfo(currentAccount, roomsetting, 0, last), 0);
				return true;
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Creating quick-join room failed kind={0} userNum={1}", roomkindid, currentAccount.UserNum);
				return false;
			}
		}

		private static int ResolveEventMapNum(int roomkindid, int fallback)
		{
			try
			{
				using MySql.Data.MySqlClient.MySqlConnection conn = new MySql.Data.MySqlClient.MySqlConnection(Conf.Connstr);
				conn.Open();
				using MySql.Data.MySqlClient.MySqlCommand cmd = new MySql.Data.MySqlClient.MySqlCommand(
					"SELECT fdMapNum FROM EssenMapNumByRoomKind WHERE fdRoomKindID=@k ORDER BY fdMapNum LIMIT 1",
					conn);
				cmd.Parameters.AddWithValue("@k", roomkindid);
				object o = cmd.ExecuteScalar();
				if (o != null && o != DBNull.Value)
				{
					return Convert.ToInt32(o);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("ResolveEventMapNum kind={0}: {1}", roomkindid, ex.Message);
			}
			// Official summer channel 1 = map 5308.
			if (roomkindid == 381)
			{
				return 5308;
			}
			return fallback;
		}

		public static void Handle_PlayTogether(ClientConnection Client, PacketReader packet, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			packet.ReadLEInt32();
			int roomsession = packet.ReadLEInt32();
			int num = packet.ReadLEInt32();
			int num2 = packet.ReadLEInt16();
			string pw = string.Empty;
			if (num2 > 0)
			{
				pw = packet.ReadBig5StringSafe(num2);
			}
			if (!Rooms.ExistRoom(roomsession))
			{
				Client.SendAsync(new GameRoom_EnterRoomError(1, num, last));
				return;
			}
			if (RoomHolder.RoomKindInfos.TryGetValue(num, out var value) && (value.Channel == 46 || value.Channel == 47))
			{
				Client.SendAsync(new GameRoom_EnterRoomError(1, num, last));
				return;
			}
			NormalRoom room = Rooms.GetRoom(roomsession);
			if (RoomHolder.RoomKindInfos.TryGetValue(room.RoomKindID, out var value2) && (value2.Channel == 46 || value2.Channel == 47))
			{
				Client.SendAsync(new GameRoom_EnterRoomError(1, num, last));
			}
			else
			{
				ServerStatus.ToRoomServer(new RM_PlayerEnterRoom(currentAccount, pw, room.ID, room.RoomKindID, 0, 0, last), room.RoomServerID);
			}
		}

		public static void Handle_FF3E02(ClientConnection Client, PacketReader reader, byte last)
		{
			_ = Client.CurrentAccount;
			int unk = reader.ReadLEInt32();
			int unk2 = reader.ReadLEInt32();
			reader.ReadLEInt32();
			int unk3 = reader.ReadLEInt32();
			int unk4 = reader.ReadLEInt32();
			Client.SendAsync(new GameRoom_FF3F02(unk, unk2, unk3, unk4, last));
		}

		public static void Handle_GetRoomKindAttr(ClientConnection Client, PacketReader reader, byte last)
		{
			int num = reader.ReadLEInt32();
			if (RoomHolder.RoomKindAttr.TryGetValue(num, out var value))
			{
				Client.SendAsync(new GetRoomKindAttr_ACK(flag: true, num, value, last));
			}
			else
			{
				Client.SendAsync(new GetRoomKindAttr_ACK(flag: false, num, null, last));
			}
		}

		public static void Handle_GetVertification(ClientConnection Client, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			currentAccount.VertificationCode = (byte)new Random(Guid.NewGuid().GetHashCode()).Next(0, 100);
			currentAccount.NeedVertificated = true;
			Client.SendAsync(new GameRoom_GetVertificationInfo(currentAccount, last));
		}

		public static void Handle_PassVertification(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			byte code = reader.ReadByte();
			ServerStatus.ToRoomServer(new RM_PassVertification(currentAccount, code, last), currentAccount.RoomServerID);
		}

		public static void Handle_JoinGuildMatch(ClientConnection Client, byte last)
		{
			_ = Client.CurrentAccount;
			Client.SendAsync(new GameRoom_JoinGuildMatch(last));
		}
	}
}
