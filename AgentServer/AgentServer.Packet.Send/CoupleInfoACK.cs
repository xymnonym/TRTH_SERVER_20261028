using System;
using System.Collections.Generic;
using System.Data;
using AgentServer.Structuring;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using LocalCommons.Utilities;
using MySql.Data.MySqlClient;
using Serilog;
using TRCommon;

namespace AgentServer.Packet.Send
{
	public sealed class CoupleInfoACK : NetPacket
	{
		private sealed class CoupleMember
		{
			public string NickName;
			public ushort[] Wear;
		}

		public CoupleInfoACK(Account user, int coupleNum, byte flag, byte last)
		{
			ns.WriteOP(Opcodes.eServer_COUPLE_GET_USER_COUPLE_INFO_ACK);
			bool wroteBody = false;
			List<CoupleMember> members = new List<CoupleMember>();
			using (MySqlConnection mySqlConnection = new MySqlConnection(Conf.Connstr))
			{
				mySqlConnection.Open();
				using MySqlCommand mySqlCommand = new MySqlCommand(string.Empty, mySqlConnection);
				mySqlCommand.Parameters.Clear();
				mySqlCommand.CommandType = CommandType.StoredProcedure;
				mySqlCommand.CommandText = "usp_coupleGetCoupleInfo";
				mySqlCommand.Parameters.Add("couplenum", MySqlDbType.Int32).Value = coupleNum;
				using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();
				if (mySqlDataReader.HasRows)
				{
					ns.Write(flag);
					mySqlDataReader.Read();
					ns.WriteAnsiFixed_intSize(mySqlDataReader["coupleName"].ToString());
					ns.WriteAnsiFixed_intSize(mySqlDataReader["coupleDesc"].ToString());
					ns.Write(coupleNum);
					ns.Write(Convert.ToInt32(mySqlDataReader["coupleType"]));
					ns.Write((short)0);
					ns.Write((mySqlDataReader["createtime"].ToString() == "0") ? 1842465389770955L : Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["createtime"])));
					ns.Write((mySqlDataReader["marriedDatetime"].ToString() == "0") ? 1842465389770955L : Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["marriedDatetime"])));
					ns.Write((mySqlDataReader["ringChangedTime"].ToString() == "0") ? 1842465389770955L : Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["ringChangedTime"])));
					ns.Write(Convert.ToInt32(mySqlDataReader["coupleRingNum"]));
					ns.Write(Convert.ToInt32(mySqlDataReader["coupleRequireDays"]));
					ns.Write(Convert.ToInt32(mySqlDataReader["coupleLevel"]));
					ns.Write(-1);
					ns.Write(Convert.ToInt32(mySqlDataReader["accumulateExp"]));
					ns.Write(0);
					ns.Write(Convert.ToInt32(mySqlDataReader["couplePoint"]));
					ns.Write(Convert.ToInt32(mySqlDataReader["coupleRank"]));
					ns.Write((byte)0);
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						members.Add(new CoupleMember
						{
							NickName = mySqlDataReader["fdNickname"].ToString(),
							Wear = new ushort[15]
							{
								Convert.ToUInt16(mySqlDataReader["character"]),
								Convert.ToUInt16(mySqlDataReader["head"]),
								Convert.ToUInt16(mySqlDataReader["topBody"]),
								Convert.ToUInt16(mySqlDataReader["downBody"]),
								Convert.ToUInt16(mySqlDataReader["foot"]),
								Convert.ToUInt16(mySqlDataReader["acHead"]),
								Convert.ToUInt16(mySqlDataReader["acFace"]),
								Convert.ToUInt16(mySqlDataReader["acHand"]),
								Convert.ToUInt16(mySqlDataReader["acBack"]),
								Convert.ToUInt16(mySqlDataReader["acNeck"]),
								Convert.ToUInt16(mySqlDataReader["pet"]),
								Convert.ToUInt16(mySqlDataReader["expansion"]),
								Convert.ToUInt16(mySqlDataReader["acWrist"]),
								Convert.ToUInt16(mySqlDataReader["acBooster"]),
								Convert.ToUInt16(mySqlDataReader["acTail"])
							}
						});
					}
					wroteBody = true;
				}
			}
			if (!wroteBody)
			{
				// Empty couple response expected when the DB procedure returns no rows.
				ns.Write(flag);
				ns.WriteAnsiFixed_intSize("");
				ns.WriteAnsiFixed_intSize("");
				ns.Write(coupleNum);
				ns.Write(0);
				ns.Write((short)0);
				ns.Write(0L);
				ns.Write(0L);
				ns.Write(0L);
				ns.Write(0);
				ns.Write(0);
				ns.Write(0);
				ns.Write(-1);
				ns.Write(0);
				ns.Write(0);
				ns.Write(0);
				ns.Write(0);
				ns.Write((byte)0);
				// The request always carries coupleNum 0: it asks for the requester's own
				// couple page. With no couple row the page still has to show the requester's
				// own card, so send that player as the single member.
				CoupleMember self = BuildSelfMember(user);
				if (self != null)
				{
					members.Add(self);
				}
			}
			// R186632 reads two reserved ints after the header byte and takes the member
			// array count from the third int. Evidence: the empty ACK stops at 86 bytes
			// (Overpop at 78, 82, 86), and a member written straight after the first int
			// was skipped by the client.
			ns.Write(0);
			ns.Write(0);
			ns.Write(members.Count);
			foreach (CoupleMember member in members)
			{
				ns.WriteAnsiFixed_intSize(member.NickName ?? string.Empty);
				for (int i = 0; i < 15; i++)
				{
					ns.Write(member.Wear[i]);
				}
				// R186632 pops one 176 byte raw block per member after the nickname:
				// 15 worn parts, 16 reserved, 12 dyeing records, 10 reserved. The same
				// avatar block shape as ACK 1408. Evidence: Overpop 98 264 176 53.
				ns.Fill(16);
				ns.Fill(120);
				ns.Fill(10);
			}
			Log.Information("CoupleInfo ACK coupleNum={CoupleNum} flag={Flag} rows={Rows} members={Members}",
				coupleNum, flag, wroteBody, members.Count);
			_ = last;
		}

		private static CoupleMember BuildSelfMember(Account user)
		{
			if (user == null)
			{
				return null;
			}
			AdvancedAvatarInfo advanced = user.advancedAvatarInfo;
			AvatarInfo visible = advanced == null
				? new AvatarInfo(0)
				: (advanced.m_bIsUseCostume ? advanced.m_costumeAvatarInfo : advanced.m_realAvatarInfo);
			ushort[] wear = new ushort[15];
			for (int i = 0; i < 15; i++)
			{
				wear[i] = visible.GetWear(i);
			}
			return new CoupleMember
			{
				NickName = user.NickName ?? string.Empty,
				Wear = wear
			};
		}
	}
}
