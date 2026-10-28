using System.Collections.Generic;
using System.Linq;
using AgentServer.Holders;
using AgentServer.Structuring.Item;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	public sealed class GetShopCategoryList : NetPacket
	{
		public GetShopCategoryList(byte last)
		{
			ns.WriteOP(Opcodes.eServer_SHOP_CATEGORY_ACK);
			ns.Write(0);
			// Official RenewalShop CategoryACK (158)(44). Filtering to packed displays
			// dropped tabs (140/42). Send full category + limit tables.
			// Search and cart are local UI views, not server shop categories.
			var categories = ShopHolder.ShopCategoryList
				.Where(category => category.Category1 != 100000)
				.OrderBy(category => category.Category1)
				.ThenBy(category => category.Category2)
				.ThenBy(category => category.Category3)
				.ToList();

			// The three ints after the name are fdCategory2Btn, fdCategory3Btn and
			// fdCategoryType, not fdCategory1Sub/fdCategory2Sub/fdOriginCategory.
			// Those three are zero on all 160 rows of this table and were what the
			// server used to send, so every tab arrived at position 0: the client
			// hides a tab at top position 0 and promotes a sub category at sub
			// position 0 into the top tab row, which left the shop with no usable
			// menu at all. The Btn columns are one based and populated, and they
			// are not in category number order, so they cannot be derived either.
			//
			//   fdCategory3Btn 0 .. this row is the top tab
			//   fdCategory3Btn 1 and up .. position within that tab
			//
			// ShopHolder had never read these columns.
			bool hasTabPositions = categories.Any(category => category.Category2Btn != 0 || category.Category3Btn != 0);
			var tabPosition = new Dictionary<string, int>();
			var subPosition = new Dictionary<string, int>();
			if (!hasTabPositions)
			{
				foreach (ShopCategory category in categories)
				{
					if (category.Category2 == 0)
					{
						continue;
					}
					string fallbackTabKey = category.Category1 + "," + category.Category2;
					if (!tabPosition.ContainsKey(fallbackTabKey))
					{
						tabPosition.Add(fallbackTabKey, 1 + tabPosition.Count(entry => entry.Key.StartsWith(category.Category1 + ",")));
					}
					if (category.Category3 == 0)
					{
						continue;
					}
					string fallbackSubKey = fallbackTabKey + "," + category.Category3;
					if (!subPosition.ContainsKey(fallbackSubKey))
					{
						subPosition.Add(fallbackSubKey, 1 + subPosition.Count(entry => entry.Key.StartsWith(fallbackTabKey + ",")));
					}
				}
			}

			ns.Write(categories.Count);
			foreach (ShopCategory shopCategory in categories)
			{
				string tabKey = shopCategory.Category1 + "," + shopCategory.Category2;
				int tabIndex = shopCategory.Category2Btn;
				int subIndex = shopCategory.Category3Btn;
				if (!hasTabPositions)
				{
					if (shopCategory.Category2 != 0)
					{
						tabPosition.TryGetValue(tabKey, out tabIndex);
					}
					if (shopCategory.Category3 != 0)
					{
						subPosition.TryGetValue(tabKey + "," + shopCategory.Category3, out subIndex);
					}
				}
				ns.Write(shopCategory.Category1);
				ns.Write(shopCategory.Category2);
				ns.Write(shopCategory.Category3);
				ns.WriteAnsiFixed_intSize(shopCategory.CategoryName);
				ns.Write(tabIndex);
				ns.Write(subIndex);
				ns.Write(shopCategory.CategoryType);
			}
			ns.Write(ShopHolder.ShopCategoryLimits.Count);
			foreach (ShopCategoryLimit shopCategoryLimit in ShopHolder.ShopCategoryLimits)
			{
				ns.Write(shopCategoryLimit.CategoryNum);
				ns.Write(shopCategoryLimit.Type);
				ns.Write(shopCategoryLimit.OperatorType);
				ns.Write(shopCategoryLimit.Value);
			}
			_ = last;
		}
	}
}
