using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using AgentServer.Network.Connections;
using AgentServer.Packet.Send;
using AgentServer.Structuring;
using AgentServer.Structuring.Attendance;
using LocalCommons.Network;
using LocalCommons.Utilities;
using MySql.Data.MySqlClient;
using Serilog;

namespace AgentServer.Packet
{
	public class AttendanceHandle
	{
		public static void Handle_AttendanceInfo(ClientConnection Client, PacketReader reader, byte last)
		{
			attendanceInfo(Client.CurrentAccount.UserNum, out var attendanceInfos, out var attendanceRewards);
			Client.SendAsync(new Attendance_Info_ACK(attendanceInfos, attendanceRewards, last));
		}

		public static void Handle_NewAttendanceGetUserInfo(ClientConnection Client, byte last)
		{
			var claimTypes = new SortedDictionary<DateTime, int>();
			try
			{
				using MySqlConnection connection = new MySqlConnection(Conf.Connstr);
				connection.Open();
				EnsureNewAttendanceTable(connection);
				using MySqlCommand command = new MySqlCommand(
					"SELECT fdClaimDate, fdRewardType FROM usernewattendanceclaim " +
					"WHERE fdUserNum=@user AND fdClaimDate>=@start AND fdClaimDate<@end", connection);
				command.Parameters.AddWithValue("@user", Client.CurrentAccount.UserNum);
				command.Parameters.AddWithValue("@start", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
				command.Parameters.AddWithValue("@end", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1));
				using MySqlDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					DateTime day = reader.GetDateTime(0).Date;
					int type = reader.GetInt32(1);
					if (!claimTypes.ContainsKey(day)) claimTypes.Add(day, 0);
					if (type == 1) claimTypes[day] |= 1;
					else if (type == 2) claimTypes[day] |= 2;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "NewAttendance user info failed userNum={UserNum}", Client.CurrentAccount?.UserNum);
			}
			var claims = new List<Tuple<DateTime, bool, bool>>();
			foreach (var claim in claimTypes)
				claims.Add(Tuple.Create(claim.Key, (claim.Value & 1) != 0, (claim.Value & 2) != 0));
			Client.SendAsync(new NewAttendanceGetUserInfoAck(claims, last));
		}

		public static void Handle_NewAttendanceReward(ClientConnection client, PacketReader reader, byte last)
		{
			int rewardType = reader.Remaining >= 1 ? reader.ReadByte() : 0;
			if (client.CurrentAccount == null || (rewardType != 1 && rewardType != 2))
			{
				client.SendAsync(new NewAttendanceRecvRewardAck(1, rewardType, last));
				return;
			}
			var items = new List<int>();
			bool claimed = TryClaimNewAttendance(client.CurrentAccount.UserNum, rewardType, items);
			client.SendAsync(new NewAttendanceRecvRewardAck(claimed ? 0 : 1, rewardType, last));
			if (claimed)
			{
				foreach (int item in items) ItemHandle.SendAvatarItemOne(client.CurrentAccount, item, last);
				client.CurrentAccount.TRNeedUpdateFromDB = true;
				client.CurrentAccount.EXPNeedUpdateFromDB = true;
				Log.Information("NewAttendance claimed userNum={UserNum} type={Type} items={Count}",
					client.CurrentAccount.UserNum, rewardType, items.Count);
			}
		}

		private static bool TryClaimNewAttendance(int userNum, int rewardType, List<int> awardedItems)
		{
			DateTime today = DateTime.Today;
			string schedule = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "script",
				"attendancerenewal", "essennewattendancereward.txt");
			if (!File.Exists(schedule))
			{
				Log.Error("NewAttendance schedule missing: {Path}", schedule);
				return false;
			}
			int groupId = 0;
			foreach (string line in File.ReadLines(schedule))
			{
				if (!line.StartsWith(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
				string[] fields = line.Split(',');
				if (fields.Length >= 3) int.TryParse(fields[rewardType], out groupId);
				break;
			}
			if (groupId <= 0) return false;
			try
			{
				using MySqlConnection connection = new MySqlConnection(Conf.Connstr);
				connection.Open();
				EnsureNewAttendanceTable(connection);
				var rewards = new List<Tuple<int, int>>();
				using (MySqlCommand lookup = new MySqlCommand(
					"SELECT fdRewardType, fdRewardItem, fdRewardCount FROM essenrewardgroupdetail WHERE fdRewardGroupID=@group", connection))
				{
					lookup.Parameters.AddWithValue("@group", groupId);
					using MySqlDataReader result = lookup.ExecuteReader();
					while (result.Read())
					{
						if (result.GetInt32(0) != 100 || result.GetInt32(1) <= 0 || result.GetInt32(2) <= 0)
						{
							Log.Error("NewAttendance unsupported reward group={Group} type={Type}", groupId, result.GetInt32(0));
							return false;
						}
						rewards.Add(Tuple.Create(result.GetInt32(1), result.GetInt32(2)));
					}
				}
				if (rewards.Count == 0) return false;
				using MySqlTransaction transaction = connection.BeginTransaction();
				try
				{
					using (MySqlCommand insert = new MySqlCommand(
						"INSERT INTO usernewattendanceclaim (fdUserNum, fdClaimDate, fdRewardType) VALUES (@user, @date, @type)", connection, transaction))
					{
						insert.Parameters.AddWithValue("@user", userNum);
						insert.Parameters.AddWithValue("@date", today);
						insert.Parameters.AddWithValue("@type", rewardType);
						insert.ExecuteNonQuery();
					}
					foreach (Tuple<int, int> reward in rewards)
					{
						using MySqlCommand give = new MySqlCommand("usp_giveItemByDescNum", connection, transaction);
						give.CommandType = CommandType.StoredProcedure;
						give.Parameters.Add("usernum", MySqlDbType.Int32).Value = userNum;
						give.Parameters.Add("itemDescNum", MySqlDbType.Int32).Value = reward.Item1;
						give.Parameters.Add("pcheck", MySqlDbType.Int32).Value = 1;
						give.Parameters.Add("pGiveCount", MySqlDbType.Int32).Value = reward.Item2;
						give.Parameters.Add("ret", MySqlDbType.Int32).Direction = ParameterDirection.Output;
						give.ExecuteNonQuery();
						if (Convert.ToInt32(give.Parameters["ret"].Value) != 0)
							throw new InvalidOperationException("NewAttendance item grant failed item=" + reward.Item1);
						awardedItems.Add(reward.Item1);
					}
					transaction.Commit();
					return true;
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
			catch (MySqlException ex) when (ex.Number == 1062)
			{
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(ex, "NewAttendance claim failed userNum={UserNum} type={Type} group={Group}", userNum, rewardType, groupId);
				return false;
			}
		}

		private static void EnsureNewAttendanceTable(MySqlConnection connection)
		{
			using MySqlCommand command = new MySqlCommand(
				"CREATE TABLE IF NOT EXISTS usernewattendanceclaim (" +
				"fdUserNum INT NOT NULL, fdClaimDate DATE NOT NULL, fdRewardType TINYINT NOT NULL, " +
				"PRIMARY KEY (fdUserNum, fdClaimDate, fdRewardType)) ENGINE=InnoDB", connection);
			command.ExecuteNonQuery();
		}

		public static void Handle_AttendanceReward(ClientConnection Client, PacketReader reader, byte last)
		{
			Account currentAccount = Client.CurrentAccount;
			int num = reader.ReadLEInt32();
			if (attendanceReward(num, currentAccount.UserNum, out var Reward))
			{
				Client.SendAsync(new Attendance_Reward_ACK(0, num, Reward, last));
			}
			else
			{
				Client.SendAsync(new Attendance_Reward_ACK(1, 0, null, last));
			}
		}

		private static void attendanceInfo(int UserNum, out Dictionary<int, AttendanceInfo> attendanceInfos, out List<AttendanceReward> attendanceRewards)
		{
			attendanceInfos = new Dictionary<int, AttendanceInfo>();
			attendanceRewards = new List<AttendanceReward>();
			try
			{
				using MySqlConnection mySqlConnection = new MySqlConnection(Conf.Connstr);
				mySqlConnection.Open();
				using MySqlCommand mySqlCommand = new MySqlCommand(string.Empty, mySqlConnection);
				mySqlCommand.Parameters.Clear();
				mySqlCommand.CommandType = CommandType.StoredProcedure;
				mySqlCommand.CommandText = "usp_attendanceInfo";
				mySqlCommand.Parameters.Add("userNum", MySqlDbType.Int32).Value = UserNum;
				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();
				while (mySqlDataReader.Read())
				{
					AttendanceInfo attendanceInfo = new AttendanceInfo
					{
						AttendanceKey = mySqlDataReader.GetInt32("fdAttendanceKey"),
						AttendanceType = mySqlDataReader.GetInt32("fdAttendanceType"),
						AttendanceRewardGroupKey = mySqlDataReader.GetInt32("fdAttendanceRewardGroupKey"),
						Start = Utility.ConvertToTimestamp(mySqlDataReader.GetDateTime("fdStart")),
						EndS = Utility.ConvertToTimestamp(mySqlDataReader.GetDateTime("fdEndS")),
						EndU = Utility.ConvertToTimestamp(mySqlDataReader.GetDateTime("fdEndU")),
						AttendancePoint = mySqlDataReader.GetInt32("fdAttendancePoint")
					};
					attendanceInfos.Add(attendanceInfo.AttendanceKey, attendanceInfo);
				}
				mySqlDataReader.NextResult();
				while (mySqlDataReader.Read())
				{
					AttendanceReward item = new AttendanceReward
					{
						AttendanceRewardGroupKey = mySqlDataReader.GetInt32("fdAttendanceRewardGroupKey"),
						AttendanceRewardIndex = mySqlDataReader.GetInt32("fdAttendanceRewardIndex"),
						Item = mySqlDataReader.GetInt32("fdItem")
					};
					attendanceRewards.Add(item);
				}
				mySqlDataReader.NextResult();
				while (mySqlDataReader.Read())
				{
					UserAttendanceInfo userAttendanceInfo = new UserAttendanceInfo
					{
						AttendanceKey = mySqlDataReader.GetInt32("fdAttendanceKey"),
						AttendanceRewardIndex = mySqlDataReader.GetInt32("fdAttendanceRewardIndex"),
						LastDate = Utility.ConvertToTimestamp(mySqlDataReader.GetDateTime("fdLastDate"))
					};
					if (attendanceInfos.ContainsKey(userAttendanceInfo.AttendanceKey))
					{
						attendanceInfos[userAttendanceInfo.AttendanceKey].userAttendanceInfos.AttendanceKey = userAttendanceInfo.AttendanceKey;
						attendanceInfos[userAttendanceInfo.AttendanceKey].userAttendanceInfos.AttendanceRewardIndex = userAttendanceInfo.AttendanceRewardIndex;
						attendanceInfos[userAttendanceInfo.AttendanceKey].userAttendanceInfos.LastDate = userAttendanceInfo.LastDate;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("usp_attendanceInfo Error:{0}", ex.ToString());
			}
		}

		private static bool attendanceReward(int attendanceKey, int UserNum, out AttendanceReward Reward)
		{
			Reward = null;
			try
			{
				using MySqlConnection mySqlConnection = new MySqlConnection(Conf.Connstr);
				mySqlConnection.Open();
				using MySqlCommand mySqlCommand = new MySqlCommand(string.Empty, mySqlConnection);
				mySqlCommand.Parameters.Clear();
				mySqlCommand.CommandType = CommandType.StoredProcedure;
				mySqlCommand.CommandText = "usp_attendanceReward";
				mySqlCommand.Parameters.Add("attendanceKey", MySqlDbType.Int32).Value = attendanceKey;
				mySqlCommand.Parameters.Add("userNum", MySqlDbType.Int32).Value = UserNum;
				mySqlCommand.Parameters.Add("is_debug", MySqlDbType.Int32).Value = 0;
				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();
				if (mySqlDataReader.HasRows)
				{
					mySqlDataReader.Read();
					Reward = new AttendanceReward
					{
						AttendanceRewardGroupKey = mySqlDataReader.GetInt32("fdRewardGroupKey"),
						AttendanceRewardIndex = mySqlDataReader.GetInt32("fdRewardIndex"),
						Item = mySqlDataReader.GetInt32("fdItem")
					};
					return true;
				}
			}
			catch (Exception ex)
			{
				Log.Error("usp_attendanceReward Error:{0}", ex.ToString());
			}
			return false;
		}
	}
}
