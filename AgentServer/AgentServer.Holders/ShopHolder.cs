using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using AgentServer.Database;
using AgentServer.Structuring;
using AgentServer.Structuring.Item;
using Akka.Actor;
using Akka.Quartz.Actor.Commands;
using LocalCommons.Utilities;
using MySql.Data.MySqlClient;
using Quartz;
using Serilog;

namespace AgentServer.Holders
{
	public static class ShopHolder
	{
		private static IActorRef ShopActor;

		public static List<ShopCategory> ShopCategoryList { get; set; } = new List<ShopCategory>();


		public static List<ShopCategoryPurchasing> ShopCategoryPurchasingList { get; set; } = new List<ShopCategoryPurchasing>();


		public static List<ShopDisplayItem> ShopDisplayItemList { get; set; } = new List<ShopDisplayItem>();


		public static Dictionary<int, ShopDisplayItem> ShopDisplayItemListDict { get; set; } = new Dictionary<int, ShopDisplayItem>();


		public static Dictionary<int, ShopItemSell> ShopItemSellList { get; set; } = new Dictionary<int, ShopItemSell>();


		public static Dictionary<int, ShopDisplayDateLimit> ShopDisplayDateLimitList { get; set; } = new Dictionary<int, ShopDisplayDateLimit>();


		public static List<ShopBuyLimitCount> ShopBuyLimitCountList { get; set; } = new List<ShopBuyLimitCount>();


		public static List<ShopBuyAddBenefit> ShopBuyAddBenefitList { get; set; } = new List<ShopBuyAddBenefit>();


		public static HashSet<int> NotForSaleList { get; set; } = new HashSet<int>();


		public static List<GameDataShopPurchasingLimit> ShopPurchasingLimitList { get; set; } = new List<GameDataShopPurchasingLimit>();


		public static List<ShopCategoryLimit> ShopCategoryLimits { get; set; } = new List<ShopCategoryLimit>();


		public static Dictionary<int, List<int>> SelectivePackageList { get; set; } = new Dictionary<int, List<int>>();


		// Client crashes on huge 1608 (~156KB). Keep ~40–50KB while filling every Category1 tab.
		private const int PackedShopMaxTotal = 900;

		private const int PackedShopMinPerCategory = 6;

		private const int PackedShopPerCategory = 18;

		private const int PackedShopSpecialtyPerCategory = 40;

		// Sale / event / newbie / collab / PC-cafe style top tabs.
		private static readonly HashSet<int> PackedShopSpecialtyCategory1 = new HashSet<int>
		{
			50000, 60000, 70000, 90000, 120000, 130000, 190000, 200000
		};

		private static List<ShopDisplayItem> packedShopDisplayItems = new List<ShopDisplayItem>();

		private static HashSet<int> packedShopDisplayNums = new HashSet<int>();

		private static HashSet<int> packedShopSellItemNums = new HashSet<int>();

		public static IReadOnlyList<ShopDisplayItem> PackedShopDisplayItems => packedShopDisplayItems;

		public static HashSet<int> PackedShopDisplayNums => packedShopDisplayNums;

		public static HashSet<int> PackedShopSellItemNums => packedShopSellItemNums;

		// Display numbers whose sale window the server reopens on every shop load.
		//
		// The client ships the whole catalog in script\shop\, and it hides any
		// item whose window in essenshopdisplaydatelimit.txt is closed. Every
		// window in that snapshot ended on 2026-10-11, leaving 230 items visible
		// out of 4529 live ones, 5 of them in the whole of category 10000. That
		// is why the category tabs rendered nothing. A row sent in the date limit
		// ACK replaces the client's own, so this is the set we reopen. The cap
		// keeps the packet near the sizes the client is known to accept.
		// 2000 rows is 72010 bytes and the client stops responding in the lobby
		// on that packet. 900 rows is 32410 bytes, which it accepts.
		private const int PackedShopDateWindowMax = 900;

		private static List<int> packedShopDateWindowNums = new List<int>();

		public static IReadOnlyList<int> PackedShopDateWindowNums => packedShopDateWindowNums;

		// The ranked list the client shows in the left hand panel of a category.
		//
		// This comes from gamedatashoprank, which the server had never read.
		// EssenShopCategoryPurchasing was used instead, and all 44 of its rows are
		// zero in this database, so the panel always rendered 0 / 0.
		private static Dictionary<string, List<ShopCategoryPurchasing>> shopRankIndex
			= new Dictionary<string, List<ShopCategoryPurchasing>>();

		public static List<ShopCategoryPurchasing> GetShopRankList(int cat1, int cat2, int cat3)
		{
			if (shopRankIndex.TryGetValue(cat1 + "," + cat2 + "," + cat3, out List<ShopCategoryPurchasing> bucket))
			{
				return bucket;
			}
			return new List<ShopCategoryPurchasing>();
		}

		private static void LoadShopRank()
		{
			Dictionary<string, List<ShopCategoryPurchasing>> next = new Dictionary<string, List<ShopCategoryPurchasing>>();
			try
			{
				using MySqlConnection connection = new MySqlConnection(Conf.Connstr);
				connection.Open();
				using MySqlCommand command = new MySqlCommand(
					"SELECT fdCategory1, fdCategory2, fdCategory3, fdRank, fdShopDisplayNum "
					+ "FROM gamedatashoprank ORDER BY fdCategory1, fdCategory2, fdCategory3, fdRank", connection);
				using MySqlDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					ShopCategoryPurchasing entry = new ShopCategoryPurchasing
					{
						Category1 = Convert.ToInt32(reader["fdCategory1"]),
						Category2 = Convert.ToInt32(reader["fdCategory2"]),
						Category3 = Convert.ToInt32(reader["fdCategory3"]),
						Order = Convert.ToInt32(reader["fdRank"]),
						ShopDisplayNum = Convert.ToInt32(reader["fdShopDisplayNum"])
					};
					string key = entry.Category1 + "," + entry.Category2 + "," + entry.Category3;
					if (!next.TryGetValue(key, out List<ShopCategoryPurchasing> bucket))
					{
						bucket = new List<ShopCategoryPurchasing>();
						next.Add(key, bucket);
					}
					bucket.Add(entry);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("LoadShopRank: {0}", ex.Message);
				return;
			}
			shopRankIndex = next;
			BuildSpecialShopRanks(next);
			Log.Information("Shop rank paths={0}", shopRankIndex.Count);
		}

		// essenshopcategory marks these two with fdCategoryType 4 and 1.
		private const int NewShopCategory1 = 80000;

		private const int NewShopCategory2 = 80100;

		private const int PopularShopCategory2 = 80200;

		private const int SpecialShopRankMax = 100;

		/// <summary>
		/// "ใหม่100" and "นิยม100" have no rows of their own in EssenShopDisplayItem,
		/// so the server composes them. Both are limited to the display numbers
		/// whose sale window this server reopens, because the client hides anything
		/// outside that set regardless of where it is listed.
		/// </summary>
		private static void BuildSpecialShopRanks(Dictionary<string, List<ShopCategoryPurchasing>> index)
		{
			HashSet<int> visible = new HashSet<int>(packedShopDateWindowNums);
			if (visible.Count == 0)
			{
				return;
			}
			AddSpecialRank(index, NewShopCategory2, BuildNewestDisplayNums(visible));
			AddSpecialRank(index, PopularShopCategory2, BuildPopularDisplayNums(visible));
		}

		private static void AddSpecialRank(Dictionary<string, List<ShopCategoryPurchasing>> index, int category2, List<int> displayNums)
		{
			string key = NewShopCategory1 + "," + category2 + ",0";
			if (index.ContainsKey(key) || displayNums.Count == 0)
			{
				return;
			}
			List<ShopCategoryPurchasing> bucket = new List<ShopCategoryPurchasing>();
			for (int i = 0; i < displayNums.Count && i < SpecialShopRankMax; i++)
			{
				bucket.Add(new ShopCategoryPurchasing
				{
					Category1 = NewShopCategory1,
					Category2 = category2,
					Category3 = 0,
					Order = i + 1,
					ShopDisplayNum = displayNums[i]
				});
			}
			index[key] = bucket;
			Log.Information("Shop special rank {0} entries={1}", key, bucket.Count);
		}

		/// <summary>Most recently opened for sale first.</summary>
		private static List<int> BuildNewestDisplayNums(HashSet<int> visible)
		{
			List<int> ordered = QueryDisplayNums(
				"SELECT SD.fdShopDisplayNum AS n FROM EssenShopDisplayItem SD "
				+ "LEFT JOIN EssenShopDisplayDateLimit SL ON SL.fdShopDisplayNum = SD.fdShopDisplayNum "
				+ "GROUP BY SD.fdShopDisplayNum "
				+ "ORDER BY MAX(SL.fdDisplayStartDate) DESC, SD.fdShopDisplayNum DESC", visible);
			AppendDistinct(ordered, packedShopDateWindowNums);
			return ordered;
		}

		/// <summary>
		/// Purchase counts first, then anything else that is on sale, so the tab is
		/// never empty on a server that has barely been played.
		/// </summary>
		private static List<int> BuildPopularDisplayNums(HashSet<int> visible)
		{
			List<int> ordered = QueryDisplayNums(
				"SELECT fdShopDisplayNum AS n FROM gamedatashopbuytodaycount "
				+ "ORDER BY fdCount DESC, IFNULL(fdCash,0) + IFNULL(fdTR,0) DESC, fdShopDisplayNum", visible);
			AppendDistinct(ordered, QueryDisplayNums(
				"SELECT fdShopDisplayNum AS n FROM usershopbuydisplaylist "
				+ "GROUP BY fdShopDisplayNum ORDER BY COUNT(*) DESC, MAX(fdLastSellDate) DESC", visible));
			AppendDistinct(ordered, packedShopDateWindowNums);
			return ordered;
		}

		private static List<int> QueryDisplayNums(string sql, HashSet<int> visible)
		{
			List<int> result = new List<int>();
			try
			{
				using MySqlConnection connection = new MySqlConnection(Conf.Connstr);
				connection.Open();
				using MySqlCommand command = new MySqlCommand(sql, connection);
				using MySqlDataReader reader = command.ExecuteReader();
				while (reader.Read() && result.Count < SpecialShopRankMax)
				{
					int displayNum = Convert.ToInt32(reader["n"]);
					if (visible.Contains(displayNum) && !result.Contains(displayNum))
					{
						result.Add(displayNum);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warning("QueryDisplayNums: {0}", ex.Message);
			}
			return result;
		}

		private static void AppendDistinct(List<int> target, IEnumerable<int> source)
		{
			foreach (int displayNum in source)
			{
				if (target.Count >= SpecialShopRankMax)
				{
					return;
				}
				if (!target.Contains(displayNum))
				{
					target.Add(displayNum);
				}
			}
		}

		// Per-category grid the client asks for with SHOP_RANK_REQ.
		//
		// EssenShopCategoryPurchasing is meant to hold the category -> display
		// mapping. In this database all 44 of its rows carry zero in fdCategory1,
		// fdCategory2, fdCategory3, fdOrder and fdShopDisplayNum, and the real
		// content of the table is category limit data. Every category request
		// therefore matched nothing, the client received an empty list, and it
		// cleared its shop grid for the rest of the session. Build the mapping
		// from the live display catalog instead.
		private const int CategoryDisplayMax = 2500;

		private static Dictionary<string, List<ShopCategoryPurchasing>> categoryDisplayIndex
			= new Dictionary<string, List<ShopCategoryPurchasing>>();

		/// <summary>
		/// Display numbers for one category path, ordered by display sort number.
		/// Zero in Category2 or Category3 means every child of that path, because
		/// the client sends a parent path when the player clicks a top level tab.
		/// </summary>
		public static List<ShopCategoryPurchasing> GetCategoryDisplayList(int cat1, int cat2, int cat3)
		{
			if (categoryDisplayIndex.TryGetValue(cat1 + "," + cat2 + "," + cat3, out List<ShopCategoryPurchasing> bucket))
			{
				return bucket;
			}
			return new List<ShopCategoryPurchasing>();
		}

		private static void RebuildCategoryDisplayIndex(HashSet<int> sellableDisplayNums)
		{
			Dictionary<string, List<ShopCategoryPurchasing>> nextIndex = new Dictionary<string, List<ShopCategoryPurchasing>>();
			Dictionary<string, HashSet<int>> seen = new Dictionary<string, HashSet<int>>();
			void Register(string key, ShopCategoryPurchasing entry)
			{
				if (!nextIndex.TryGetValue(key, out List<ShopCategoryPurchasing> bucket))
				{
					bucket = new List<ShopCategoryPurchasing>();
					nextIndex.Add(key, bucket);
					seen.Add(key, new HashSet<int>());
				}
				// The same item reaches a key once per granularity when its own
				// Category2 or Category3 is already zero, so dedupe per key.
				if (bucket.Count < CategoryDisplayMax && seen[key].Add(entry.ShopDisplayNum))
				{
					bucket.Add(entry);
				}
			}
			foreach (ShopDisplayItem item in ShopDisplayItemList
				.Where((ShopDisplayItem i) => sellableDisplayNums.Contains(i.ShopDisplayNum))
				.OrderBy((ShopDisplayItem i) => i.DisplaySortNum))
			{
				ShopCategoryPurchasing entry = new ShopCategoryPurchasing
				{
					Category1 = item.Category1,
					Category2 = item.Category2,
					Category3 = item.Category3,
					Order = item.DisplaySortNum,
					ShopDisplayNum = item.ShopDisplayNum
				};
				Register(item.Category1 + "," + item.Category2 + "," + item.Category3, entry);
				Register(item.Category1 + "," + item.Category2 + ",0", entry);
				Register(item.Category1 + ",0,0", entry);
			}
			categoryDisplayIndex = nextIndex;
		}

		public static void RebuildPackedShopCatalog()
		{
			List<ShopDisplayItem> nextItems = new List<ShopDisplayItem>();
			HashSet<int> nextNums = new HashSet<int>();
			HashSet<int> featured = new HashSet<int>();
			HashSet<int> sellableDisplayNums = new HashSet<int>(ShopItemSellList.Values
				.Where((ShopItemSell s) => s.ShopDisplay)
				.Select((ShopItemSell s) => s.ShopDisplayNum));
			List<IGrouping<string, ShopDisplayItem>> groups = ShopDisplayItemList
				.Where((ShopDisplayItem i) => sellableDisplayNums.Contains(i.ShopDisplayNum))
				.GroupBy((ShopDisplayItem i) => i.Category1 + "," + i.Category2 + "," + i.Category3)
				.OrderBy((IGrouping<string, ShopDisplayItem> g) => g.First().Category1)
				.ThenBy((IGrouping<string, ShopDisplayItem> g) => g.First().Category2)
				.ThenBy((IGrouping<string, ShopDisplayItem> g) => g.First().Category3)
				.ToList();
			foreach (ShopCategoryPurchasing purchasing in ShopCategoryPurchasingList)
			{
				featured.Add(purchasing.ShopDisplayNum);
			}
			void AddItem(ShopDisplayItem item)
			{
				if (nextItems.Count >= PackedShopMaxTotal)
				{
					return;
				}
				if (nextNums.Add(item.ShopDisplayNum))
				{
					nextItems.Add(item);
				}
			}
			foreach (int displayNum in featured)
			{
				if (ShopDisplayItemListDict.TryGetValue(displayNum, out ShopDisplayItem featuredItem)
					&& sellableDisplayNums.Contains(featuredItem.ShopDisplayNum))
				{
					AddItem(featuredItem);
				}
			}
			// Give each populated category path items before filling popular paths.
			// Reserving only Category1 allowed specialty tabs to consume the
			// packet budget before the ordinary shop's subcategories were sent.
			foreach (IGrouping<string, ShopDisplayItem> group in groups)
			{
				int added = 0;
				foreach (ShopDisplayItem item in group.OrderBy((ShopDisplayItem i) => i.DisplaySortNum))
				{
					if (added >= PackedShopMinPerCategory || nextItems.Count >= PackedShopMaxTotal)
					{
						break;
					}
					int before = nextNums.Count;
					AddItem(item);
					if (nextNums.Count > before)
					{
						added++;
					}
				}
			}
			void TakeGroups(bool specialtyOnly)
			{
				foreach (IGrouping<string, ShopDisplayItem> group in groups)
				{
					if (nextItems.Count >= PackedShopMaxTotal)
					{
						break;
					}
					int cat1 = group.First().Category1;
					bool specialty = PackedShopSpecialtyCategory1.Contains(cat1);
					if (specialtyOnly != specialty)
					{
						continue;
					}
					int limit = specialty ? PackedShopSpecialtyPerCategory : PackedShopPerCategory;
					int taken = 0;
					foreach (ShopDisplayItem item2 in group.OrderBy((ShopDisplayItem c) => c.DisplaySortNum))
					{
						if (nextItems.Count >= PackedShopMaxTotal || taken >= limit)
						{
							break;
						}
						int before = nextNums.Count;
						AddItem(item2);
						if (nextNums.Count > before)
						{
							taken++;
						}
					}
				}
			}
			TakeGroups(specialtyOnly: true);
			TakeGroups(specialtyOnly: false);
			HashSet<int> nextSell = new HashSet<int>();
			foreach (ShopItemSell sell in ShopItemSellList.Values)
			{
				if (nextNums.Contains(sell.ShopDisplayNum))
				{
					nextSell.Add(sell.SellItemNum);
				}
			}
			packedShopDisplayItems = nextItems;
			packedShopDisplayNums = nextNums;
			packedShopSellItemNums = nextSell;
			RebuildCategoryDisplayIndex(sellableDisplayNums);
			packedShopDateWindowNums = BuildDateWindowSet(groups);
			Log.Information("Packed shop catalog display={0} sell={1} featured={2}", packedShopDisplayItems.Count, packedShopSellItemNums.Count, featured.Count);
			Log.Information("Shop category display index paths={0}", categoryDisplayIndex.Count);
			Log.Information("Shop reopened sale windows={0}", packedShopDateWindowNums.Count);
		}


		/// <summary>
		/// One display number per category path per round, so every tab is filled
		/// before any tab gets a second helping. Taking paths in order instead let
		/// the first few paths use the whole budget.
		/// </summary>
		private static List<int> BuildDateWindowSet(List<IGrouping<string, ShopDisplayItem>> groups)
		{
			List<List<ShopDisplayItem>> paths = groups
				.Select((IGrouping<string, ShopDisplayItem> g) => g.OrderBy((ShopDisplayItem i) => i.DisplaySortNum).ToList())
				.ToList();
			HashSet<int> taken = new HashSet<int>();
			List<int> result = new List<int>();
			int round = 0;
			bool any = true;
			while (any && result.Count < PackedShopDateWindowMax)
			{
				any = false;
				foreach (List<ShopDisplayItem> path in paths)
				{
					if (round >= path.Count)
					{
						continue;
					}
					any = true;
					if (taken.Add(path[round].ShopDisplayNum))
					{
						result.Add(path[round].ShopDisplayNum);
					}
					if (result.Count >= PackedShopDateWindowMax)
					{
						break;
					}
				}
				round++;
			}
			return result;
		}

		public static void LoadShopInfo()
		{
			LoadSchedule();
			LoadNewShopInfo();
			LoadSelectivePackageInfo();
		}

		private static void LoadSchedule()
		{
			ShopActor = ServerStatus.MainActorSystem.ActorOf(Props.Create(() => new ShopManager()), "ShopManager");
			ServerStatus.QuartzActor.Tell(new CreateJob(ShopActor, 1, TriggerBuilder.Create().WithCronSchedule("2 0 0 ? * * *").Build()));
			ServerStatus.QuartzActor.Tell(new CreateJob(ShopActor, 2, TriggerBuilder.Create().WithCronSchedule("5 0 0 ? * * *").Build()));
		}

		public static void LoadNewShopInfo2()
		{
			try
			{
				ShopCategoryList.Clear();
				using (MySqlConnection mySqlConnection = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection.Open();
					using MySqlCommand mySqlCommand = new MySqlCommand("SELECT * FROM essenshopcategory", mySqlConnection);
					mySqlCommand.Parameters.Clear();
					using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();
					while (mySqlDataReader.Read())
					{
						ShopCategory item = new ShopCategory
						{
							Category1 = Convert.ToInt32(mySqlDataReader["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader["fdCategory3"]),
							CategoryName = mySqlDataReader["fdCategoryName"].ToString(),
							Category1Sub = Convert.ToInt32(mySqlDataReader["fdCategory1Sub"]),
							Category2Sub = Convert.ToInt32(mySqlDataReader["fdCategory2Sub"]),
							OriginCategory = Convert.ToInt32(mySqlDataReader["fdOriginCategory"]),
							Category2Btn = ReadCategoryInt(mySqlDataReader, "fdCategory2Btn"),
							Category3Btn = ReadCategoryInt(mySqlDataReader, "fdCategory3Btn"),
							CategoryType = ReadCategoryInt(mySqlDataReader, "fdCategoryType")
						};
						ShopCategoryList.Add(item);
					}
				}
				ShopCategoryPurchasingList.Clear();
				using (MySqlConnection mySqlConnection2 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection2.Open();
					using MySqlCommand mySqlCommand2 = new MySqlCommand("SELECT * FROM EssenShopCategoryPurchasing", mySqlConnection2);
					mySqlCommand2.Parameters.Clear();
					using MySqlDataReader mySqlDataReader2 = mySqlCommand2.ExecuteReader();
					while (mySqlDataReader2.Read())
					{
						ShopCategoryPurchasing item2 = new ShopCategoryPurchasing
						{
							Category1 = Convert.ToInt32(mySqlDataReader2["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader2["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader2["fdCategory3"]),
							Order = Convert.ToInt32(mySqlDataReader2["fdOrder"]),
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader2["fdShopDisplayNum"])
						};
						ShopCategoryPurchasingList.Add(item2);
					}
				}
				ShopDisplayItemList.Clear();
				ShopDisplayItemListDict.Clear();
				using (MySqlConnection mySqlConnection3 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection3.Open();
					using MySqlCommand mySqlCommand3 = new MySqlCommand("SELECT * FROM essenshopdisplayitem", mySqlConnection3);
					mySqlCommand3.Parameters.Clear();
					using MySqlDataReader mySqlDataReader3 = mySqlCommand3.ExecuteReader();
					while (mySqlDataReader3.Read())
					{
						int num = Convert.ToInt32(mySqlDataReader3["fdShopDisplayNum"]);
						ShopDisplayItem shopDisplayItem = new ShopDisplayItem
						{
							ShopDisplayNum = num,
							Category1 = Convert.ToInt32(mySqlDataReader3["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader3["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader3["fdCategory3"]),
							DisplaySortNum = Convert.ToInt32(mySqlDataReader3["fdDisplaySortNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader3["fdItemNum"]),
							Gift = Convert.ToBoolean(mySqlDataReader3["fdGift"]),
							ItemTag = mySqlDataReader3["fdItemTag"].ToString(),
							Desc = mySqlDataReader3["fdDesc"].ToString()
						};
						ShopDisplayItemList.Add(shopDisplayItem);
						ShopDisplayItemListDict.Add(num, shopDisplayItem);
					}
				}
				ShopItemSellList.Clear();
				using (MySqlConnection mySqlConnection4 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection4.Open();
					using MySqlCommand mySqlCommand4 = new MySqlCommand("SELECT t1.fdSellItemNum, fdShopDisplayNum, \r\n                                                            t1.fdItemNum, fdOriginCostType, fdOriginPrice, \r\n                                                            IFNULL(t2.fdCostType, t1.fdCostType) AS fdCostType, \r\n                                                            IFNULL(t2.fdPrice, t1.fdPrice) AS fdPrice, \r\n                                                            fdMileageType, fdMileage, \r\n                                                            IFNULL(t2.fdShopDisplay, t1.fdShopDisplay) AS fdShopDisplay, \r\n\t\t\t\t\t\t\t\t\t\t\t\t\t\t\tCASE WHEN t2.fdCostType IS NULL THEN 0 ELSE 1 END AS isEdit \r\n                                                        FROM EssenShopItemSelllist t1 LEFT Join EssenShopItemSelllist_2 t2 \r\n                                                        ON t1.fdSellItemNum = t2.fdSellItemNum;", mySqlConnection4);
					mySqlCommand4.Parameters.Clear();
					using MySqlDataReader mySqlDataReader4 = mySqlCommand4.ExecuteReader();
					while (mySqlDataReader4.Read())
					{
						int num2 = Convert.ToInt32(mySqlDataReader4["fdSellItemNum"]);
						ShopItemSell value = new ShopItemSell
						{
							SellItemNum = num2,
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader4["fdShopDisplayNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader4["fdItemNum"]),
							OriginCostType = Convert.ToByte(mySqlDataReader4["fdOriginCostType"]),
							OriginPrice = Convert.ToInt32(mySqlDataReader4["fdOriginPrice"]),
							CostType = Convert.ToByte(mySqlDataReader4["fdCostType"]),
							Price = Convert.ToInt32(mySqlDataReader4["fdPrice"]),
							MileageType = Convert.ToByte(mySqlDataReader4["fdMileageType"]),
							Mileage = Convert.ToSingle(mySqlDataReader4["fdMileage"]),
							ShopDisplay = Convert.ToBoolean(mySqlDataReader4["fdShopDisplay"]),
							isEdit = Convert.ToBoolean(mySqlDataReader4["isEdit"])
						};
						ShopItemSellList.Add(num2, value);
					}
				}
				ShopDisplayDateLimitList.Clear();
				using (MySqlConnection mySqlConnection5 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection5.Open();
					using MySqlCommand mySqlCommand5 = new MySqlCommand("SELECT * FROM essenshopdisplaydatelimit", mySqlConnection5);
					mySqlCommand5.Parameters.Clear();
					using MySqlDataReader mySqlDataReader5 = mySqlCommand5.ExecuteReader();
					while (mySqlDataReader5.Read())
					{
						int num3 = Convert.ToInt32(mySqlDataReader5["fdShopDisplayNum"]);
						ShopDisplayDateLimit value2 = new ShopDisplayDateLimit
						{
							ShopDisplayNum = num3,
							DisplayStartDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader5["fdDisplayStartDate"])),
							DisplayEndDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader5["fdDisplayEndDate"])),
							BuyStartDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader5["fdBuyStartDate"])),
							BuyEndDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader5["fdBuyEndDate"]))
						};
						ShopDisplayDateLimitList.Add(num3, value2);
					}
				}
				ShopBuyLimitCountList.Clear();
				using (MySqlConnection mySqlConnection6 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection6.Open();
					using MySqlCommand mySqlCommand6 = new MySqlCommand("SELECT * FROM essenshopbuylimitcount", mySqlConnection6);
					mySqlCommand6.Parameters.Clear();
					using MySqlDataReader mySqlDataReader6 = mySqlCommand6.ExecuteReader();
					while (mySqlDataReader6.Read())
					{
						ShopBuyLimitCount item3 = new ShopBuyLimitCount
						{
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader6["fdShopDisplayNum"]),
							DayPurchasingLimit = Convert.ToInt32(mySqlDataReader6["fdDayPurchasingLimit"]),
							MonthPurchasingLimit = Convert.ToInt32(mySqlDataReader6["fdMonthPurchasingLimit"]),
							TotalPurchasingLimit = Convert.ToInt32(mySqlDataReader6["fdTotalPurchasingLimit"]),
							AccountPurchasingLimit = mySqlDataReader6["fdAccountPurchasingLimit"] == DBNull.Value
								? 0
								: Convert.ToInt32(mySqlDataReader6["fdAccountPurchasingLimit"])
						};
						ShopBuyLimitCountList.Add(item3);
					}
				}
				ShopBuyAddBenefitList.Clear();
				using (MySqlConnection mySqlConnection7 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection7.Open();
					using MySqlCommand mySqlCommand7 = new MySqlCommand("SELECT * FROM essenshopbuyaddbenefit", mySqlConnection7);
					mySqlCommand7.Parameters.Clear();
					using MySqlDataReader mySqlDataReader7 = mySqlCommand7.ExecuteReader();
					while (mySqlDataReader7.Read())
					{
						ShopBuyAddBenefit item4 = new ShopBuyAddBenefit
						{
							SellItemNum = Convert.ToInt32(mySqlDataReader7["fdSellItemNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader7["fdItemNum"]),
							Count = Convert.ToInt32(mySqlDataReader7["fdCount"])
						};
						ShopBuyAddBenefitList.Add(item4);
					}
				}
				NotForSaleList.Clear();
				using (MySqlConnection mySqlConnection8 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection8.Open();
					using MySqlCommand mySqlCommand8 = new MySqlCommand("SELECT * FROM EssenShopNotForSale", mySqlConnection8);
					mySqlCommand8.Parameters.Clear();
					using MySqlDataReader mySqlDataReader8 = mySqlCommand8.ExecuteReader();
					while (mySqlDataReader8.Read())
					{
						NotForSaleList.Add(Convert.ToInt32(mySqlDataReader8["fdItemNum"]));
					}
				}
				ShopPurchasingLimitList.Clear();
				using (MySqlConnection mySqlConnection9 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection9.Open();
					using MySqlCommand mySqlCommand9 = new MySqlCommand("select fdShopDisplayNum, fdTotalPurchasingLimit \r\n                                                        from GameDataShopPurchasingLimit\r\n                                                        WHERE fdShopDisplayNum in (select fdShopDisplayNum \r\n                                                            from EssenShopBuyLimitCount WHERE fdTotalPurchasingLimit > 0)", mySqlConnection9);
					mySqlCommand9.Parameters.Clear();
					using MySqlDataReader mySqlDataReader9 = mySqlCommand9.ExecuteReader();
					while (mySqlDataReader9.Read())
					{
						GameDataShopPurchasingLimit item5 = new GameDataShopPurchasingLimit
						{
							ShopDisplayNum = mySqlDataReader9.GetInt32("fdShopDisplayNum"),
							TotalPurchasingLimit = mySqlDataReader9.GetInt32("fdTotalPurchasingLimit")
						};
						ShopPurchasingLimitList.Add(item5);
					}
				}
				ShopCategoryLimits.Clear();
				using (MySqlConnection mySqlConnection10 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection10.Open();
					using MySqlCommand mySqlCommand10 = new MySqlCommand("SELECT * FROM essenshopcategorylimit", mySqlConnection10);
					mySqlCommand10.Parameters.Clear();
					using MySqlDataReader mySqlDataReader10 = mySqlCommand10.ExecuteReader();
					while (mySqlDataReader10.Read())
					{
						ShopCategoryLimit item6 = new ShopCategoryLimit
						{
							CategoryNum = mySqlDataReader10.GetInt32("fdCategoryNum"),
							Type = mySqlDataReader10.GetByte("fdType"),
							OperatorType = mySqlDataReader10.GetByte("fdOperatorType"),
							Value = mySqlDataReader10.GetInt32("fdValue")
						};
						ShopCategoryLimits.Add(item6);
					}
				}
				Log.Information("Load NewShopInfo Done!");
			}
			catch (Exception ex)
			{
				Log.Error("Load NewShopInfo Error: {0}", ex.Message);
			}
		}

		public static void LoadNewShopInfo()
		{
			try
			{
				ShopDisplayItemList.Clear();
				ShopDisplayItemListDict.Clear();
				ShopDisplayDateLimitList.Clear();
				ShopItemSellList.Clear();
				ShopBuyLimitCountList.Clear();
				ShopBuyAddBenefitList.Clear();
				ShopPurchasingLimitList.Clear();
				using (MySqlConnection mySqlConnection = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection.Open();
					using MySqlCommand mySqlCommand = new MySqlCommand(string.Empty, mySqlConnection);
					mySqlCommand.Parameters.Clear();
					mySqlCommand.CommandType = CommandType.StoredProcedure;
					mySqlCommand.CommandText = "usp_renewalShop_getDisplayList";
					mySqlCommand.Parameters.Add("CurrentDate", MySqlDbType.VarString).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
					using MySqlDataReader mySqlDataReader = mySqlCommand.ExecuteReader();
					while (mySqlDataReader.Read())
					{
						int num = Convert.ToInt32(mySqlDataReader["fdShopDisplayNum"]);
						ShopDisplayItem shopDisplayItem = new ShopDisplayItem
						{
							ShopDisplayNum = num,
							Category1 = Convert.ToInt32(mySqlDataReader["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader["fdCategory3"]),
							DisplaySortNum = Convert.ToInt32(mySqlDataReader["fdDisplaySortNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader["fdItemNum"]),
							Gift = Convert.ToBoolean(mySqlDataReader["fdGift"]),
							ItemTag = mySqlDataReader.GetString("fdItemTag"),
							Desc = mySqlDataReader.GetString("fdDesc"),
							isEdit = mySqlDataReader.GetBoolean("isEdit")
						};
						ShopDisplayItemList.Add(shopDisplayItem);
						ShopDisplayItemListDict.Add(num, shopDisplayItem);
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						int num2 = Convert.ToInt32(mySqlDataReader["fdShopDisplayNum"]);
						ShopDisplayDateLimit value = new ShopDisplayDateLimit
						{
							ShopDisplayNum = num2,
							DisplayStartDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["fdDisplayStartDate"])),
							DisplayEndDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["fdDisplayEndDate"])),
							BuyStartDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["fdBuyStartDate"])),
							BuyEndDate = Utility.ConvertToTimestamp(Convert.ToDateTime(mySqlDataReader["fdBuyEndDate"])),
							isEdit = mySqlDataReader.GetBoolean("isEdit")
						};
						ShopDisplayDateLimitList.Add(num2, value);
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						int num3 = Convert.ToInt32(mySqlDataReader["fdSellItemNum"]);
						ShopItemSell value2 = new ShopItemSell
						{
							SellItemNum = num3,
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader["fdShopDisplayNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader["fdItemNum"]),
							OriginCostType = Convert.ToByte(mySqlDataReader["fdOriginCostType"]),
							OriginPrice = Convert.ToInt32(mySqlDataReader["fdOriginPrice"]),
							CostType = Convert.ToByte(mySqlDataReader["fdCostType"]),
							Price = Convert.ToInt32(mySqlDataReader["fdPrice"]),
							MileageType = Convert.ToByte(mySqlDataReader["fdMileageType"]),
							Mileage = Convert.ToSingle(mySqlDataReader["fdMileage"]),
							ShopDisplay = Convert.ToBoolean(mySqlDataReader["fdShopDisplay"]),
							isEdit = Convert.ToBoolean(mySqlDataReader["isEdit"])
						};
						ShopItemSellList.Add(num3, value2);
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						ShopBuyLimitCount item = new ShopBuyLimitCount
						{
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader["fdShopDisplayNum"]),
							DayPurchasingLimit = Convert.ToInt32(mySqlDataReader["fdDayPurchasingLimit"]),
							MonthPurchasingLimit = Convert.ToInt32(mySqlDataReader["fdMonthPurchasingLimit"]),
							TotalPurchasingLimit = Convert.ToInt32(mySqlDataReader["fdTotalPurchasingLimit"]),
							AccountPurchasingLimit = ReaderHasColumn(mySqlDataReader, "fdAccountPurchasingLimit") && mySqlDataReader["fdAccountPurchasingLimit"] != DBNull.Value
								? Convert.ToInt32(mySqlDataReader["fdAccountPurchasingLimit"])
								: 0,
							isEdit = mySqlDataReader.GetBoolean("isEdit")
						};
						ShopBuyLimitCountList.Add(item);
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						ShopBuyAddBenefit item2 = new ShopBuyAddBenefit
						{
							SellItemNum = Convert.ToInt32(mySqlDataReader["fdSellItemNum"]),
							ItemNum = Convert.ToInt32(mySqlDataReader["fdItemNum"]),
							Count = Convert.ToInt32(mySqlDataReader["fdCount"]),
							isEdit = mySqlDataReader.GetBoolean("isEdit")
						};
						ShopBuyAddBenefitList.Add(item2);
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
					}
					mySqlDataReader.NextResult();
					while (mySqlDataReader.Read())
					{
						GameDataShopPurchasingLimit item3 = new GameDataShopPurchasingLimit
						{
							ShopDisplayNum = mySqlDataReader.GetInt32("fdShopDisplayNum"),
							TotalPurchasingLimit = mySqlDataReader.GetInt32("fdTotalPurchasingLimit")
						};
						ShopPurchasingLimitList.Add(item3);
					}
				}
				NotForSaleList.Clear();
				using (MySqlConnection mySqlConnection2 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection2.Open();
					using MySqlCommand mySqlCommand2 = new MySqlCommand("SELECT * FROM EssenShopNotForSale", mySqlConnection2);
					mySqlCommand2.Parameters.Clear();
					using MySqlDataReader mySqlDataReader2 = mySqlCommand2.ExecuteReader();
					while (mySqlDataReader2.Read())
					{
						NotForSaleList.Add(Convert.ToInt32(mySqlDataReader2["fdItemNum"]));
					}
				}
				ShopCategoryList.Clear();
				using (MySqlConnection mySqlConnection3 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection3.Open();
					using MySqlCommand mySqlCommand3 = new MySqlCommand("SELECT * FROM essenshopcategory", mySqlConnection3);
					mySqlCommand3.Parameters.Clear();
					using MySqlDataReader mySqlDataReader3 = mySqlCommand3.ExecuteReader();
					while (mySqlDataReader3.Read())
					{
						ShopCategory item4 = new ShopCategory
						{
							Category1 = Convert.ToInt32(mySqlDataReader3["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader3["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader3["fdCategory3"]),
							CategoryName = mySqlDataReader3["fdCategoryName"].ToString(),
							Category1Sub = Convert.ToInt32(mySqlDataReader3["fdCategory1Sub"]),
							Category2Sub = Convert.ToInt32(mySqlDataReader3["fdCategory2Sub"]),
							OriginCategory = Convert.ToInt32(mySqlDataReader3["fdOriginCategory"]),
							Category2Btn = ReadCategoryInt(mySqlDataReader3, "fdCategory2Btn"),
							Category3Btn = ReadCategoryInt(mySqlDataReader3, "fdCategory3Btn"),
							CategoryType = ReadCategoryInt(mySqlDataReader3, "fdCategoryType")
						};
						ShopCategoryList.Add(item4);
					}
				}
				ShopCategoryPurchasingList.Clear();
				using (MySqlConnection mySqlConnection4 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection4.Open();
					using MySqlCommand mySqlCommand4 = new MySqlCommand("SELECT * FROM EssenShopCategoryPurchasing", mySqlConnection4);
					mySqlCommand4.Parameters.Clear();
					using MySqlDataReader mySqlDataReader4 = mySqlCommand4.ExecuteReader();
					while (mySqlDataReader4.Read())
					{
						ShopCategoryPurchasing item5 = new ShopCategoryPurchasing
						{
							Category1 = Convert.ToInt32(mySqlDataReader4["fdCategory1"]),
							Category2 = Convert.ToInt32(mySqlDataReader4["fdCategory2"]),
							Category3 = Convert.ToInt32(mySqlDataReader4["fdCategory3"]),
							Order = Convert.ToInt32(mySqlDataReader4["fdOrder"]),
							ShopDisplayNum = Convert.ToInt32(mySqlDataReader4["fdShopDisplayNum"])
						};
						ShopCategoryPurchasingList.Add(item5);
					}
				}
				ShopCategoryLimits.Clear();
				using (MySqlConnection mySqlConnection5 = new MySqlConnection(Conf.Connstr))
				{
					mySqlConnection5.Open();
					using MySqlCommand mySqlCommand5 = new MySqlCommand("SELECT * FROM essenshopcategorylimit", mySqlConnection5);
					mySqlCommand5.Parameters.Clear();
					using MySqlDataReader mySqlDataReader5 = mySqlCommand5.ExecuteReader();
					while (mySqlDataReader5.Read())
					{
						ShopCategoryLimit item6 = new ShopCategoryLimit
						{
							CategoryNum = mySqlDataReader5.GetInt32("fdCategoryNum"),
							Type = mySqlDataReader5.GetByte("fdType"),
							OperatorType = mySqlDataReader5.GetByte("fdOperatorType"),
							Value = mySqlDataReader5.GetInt32("fdValue")
						};
						ShopCategoryLimits.Add(item6);
					}
				}
				int edited = ShopDisplayItemList.Count((ShopDisplayItem c) => c.isEdit);
				if (edited > 200)
				{
					// Old KR tool wiped the whole catalog here and left packed shop empty.
					Log.Warning("ShopDisplayItemList isEdit count={0} (not clearing packed catalog)", edited);
				}
				RebuildPackedShopCatalog();
				LoadShopRank();
				Log.Information("Renewal Shop load OK");
			}
			catch (Exception ex)
			{
				Log.Error("Renewal Shop load Error: {0}", ex.Message);
			}
		}

		private static void LoadSelectivePackageInfo()
		{
			SelectivePackageList.Clear();
			try
			{
				using MySqlCommandHelper mySqlCommandHelper = new MySqlCommandHelper("usp_getSelectivePackage");
				mySqlCommandHelper.Execute();
				while (mySqlCommandHelper.HasResult())
				{
					int @int = mySqlCommandHelper.GetInt("fdPackageItemNum");
					int int2 = mySqlCommandHelper.GetInt("fdBoxItemNum");
					if (SelectivePackageList.ContainsKey(@int))
					{
						SelectivePackageList[@int].Add(int2);
						continue;
					}
					SelectivePackageList.Add(@int, new List<int> { int2 });
				}
			}
			catch (Exception ex)
			{
				Log.Error("usp_getSelectivePackag Error: {0}", ex.Message);
			}
		}

		/// <summary>
		/// essenshopcategory keeps fdCategory2Btn, fdCategory3Btn and fdCategoryType
		/// nullable, and an older schema may not have them at all.
		/// </summary>
		private static int ReadCategoryInt(IDataRecord reader, string columnName)
		{
			if (!ReaderHasColumn(reader, columnName))
			{
				return 0;
			}
			object value = reader[columnName];
			return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
		}

		private static bool ReaderHasColumn(IDataRecord reader, string columnName)
		{
			for (int i = 0; i < reader.FieldCount; i++)
			{
				if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}
}
