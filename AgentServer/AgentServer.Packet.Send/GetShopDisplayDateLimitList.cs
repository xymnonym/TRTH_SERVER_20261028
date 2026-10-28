using System;
using System.Collections.Generic;
using System.Linq;
using AgentServer.Holders;
using AgentServer.Structuring.Item;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using LocalCommons.Utilities;

namespace AgentServer.Packet.Send
{
	public sealed class GetShopDisplayDateLimitList : NetPacket
	{
		public GetShopDisplayDateLimitList(byte last)
		{
			ns.WriteOP(Opcodes.eServer_SHOP_DISPLAY_DATE_LIST_ACK);
			ns.Write(0);
			// The client keeps its own copy of this table in
			// script\shop\essenshopdisplaydatelimit.txt and hides any item whose
			// display window is closed. Every window in that snapshot ended on
			// 2026-10-11, which emptied every category tab while the local search
			// index still matched. A row sent here replaces the client's own, so
			// reopen the window for everything in the packed catalog.
			long windowStart = Utility.ConvertToTimestamp(new DateTime(2017, 1, 1));
			long windowEnd = Utility.ConvertToTimestamp(new DateTime(2037, 12, 31, 23, 59, 59));
			List<ShopDisplayDateLimit> list = ShopHolder.PackedShopDateWindowNums
				.Select((int displayNum) => new ShopDisplayDateLimit
				{
					ShopDisplayNum = displayNum,
					DisplayStartDate = windowStart,
					DisplayEndDate = windowEnd,
					BuyStartDate = windowStart,
					BuyEndDate = windowEnd
				})
				.ToList();
			ns.Write(list.Count);
			foreach (ShopDisplayDateLimit item in list)
			{
				ns.Write(item.ShopDisplayNum);
				ns.Write(item.DisplayStartDate);
				ns.Write(item.DisplayEndDate);
				ns.Write(item.BuyStartDate);
				ns.Write(item.BuyEndDate);
			}
			_ = last;
		}
	}
}
