namespace AgentServer.Structuring.Item
{
	public class ShopCategory
	{
		public int Category1;

		public int Category2;

		public int Category3;

		public string CategoryName;

		public int Category1Sub;

		public int Category2Sub;

		public int OriginCategory;

		/// <summary>
		/// essenshopcategory.fdCategory2Btn: one based position of the top tab.
		/// </summary>
		public int Category2Btn;

		/// <summary>
		/// essenshopcategory.fdCategory3Btn: one based position of the sub tab,
		/// zero on the row that is the top tab itself.
		/// </summary>
		public int Category3Btn;

		/// <summary>essenshopcategory.fdCategoryType.</summary>
		public int CategoryType;
	}
}
