using System.Linq;
using AgentServer.Holders;
using AgentServer.Structuring.Item;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;

namespace AgentServer.Packet.Send
{
	public sealed class GetShopDisplayItemList : NetPacket
	{
		public GetShopDisplayItemList(byte last)
		{
			ns.WriteOP(Opcodes.eServer_SHOP_DISPLAY_LIST_ACK);
			ns.Write(0);
			// Send only edited rows, the way the live server does. The client ships
			// the whole catalog in script\shop\essenshopdisplayitem.txt, already
			// translated, and replaces its table whenever this list is not empty.
			// Sending our own rows therefore swapped the Thai item tags for the
			// Korean ones in the database and broke the hashtag view as well.
			var edits = ShopHolder.ShopDisplayItemList.Where(item => item.isEdit).ToList();
			ns.Write(edits.Count);
			foreach (ShopDisplayItem item in edits)
			{
				ns.Write(item.ShopDisplayNum);
				ns.Write(item.Category1);
				ns.Write(item.Category2);
				ns.Write(item.Category3);
				ns.Write(item.DisplaySortNum);
				ns.Write(item.ItemNum);
				ns.Write(item.Gift);
				ns.WriteAnsiFixed_intSize(item.ItemTag);
				ns.WriteAnsiFixed_intSize(item.Desc);
			}
			_ = last;
		}
	}
}
