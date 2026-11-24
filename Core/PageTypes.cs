using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineAI
{
	/// <summary>
	/// The type of the pages of a book (story, cover, advertisement...), kept in the book's page list. Written together with the metadata:
	/// every page is a story page, the page that is the cover (when the cover was compared and matched) is the front cover, and the types
	/// that the user chose in the manual review win. Only the information of the book changes, never the file. Runs on the UI thread.
	/// </summary>
	public static class PageTypes
	{
		/// <summary>The types that the page menu offers, in the order of ComicRack.</summary>
		public static readonly ComicPageType[] Choices =
		{
			ComicPageType.FrontCover, ComicPageType.InnerCover, ComicPageType.Roundup, ComicPageType.Story, ComicPageType.Advertisement,
			ComicPageType.Editorial, ComicPageType.Letters, ComicPageType.Preview, ComicPageType.BackCover, ComicPageType.Other, ComicPageType.Deleted
		};

		/// <summary>
		/// Sets the page types of a book.
		/// </summary>
		/// <param name="coverPage">The page that is the cover and was matched with the Comic Vine cover, or -1 when nothing is known about it.</param>
		/// <param name="changes">The types the user chose (page index, 0 based); they win over everything else. May be null.</param>
		/// <param name="setDefaults">Every page that the user did not change becomes a story page (a page that was deleted stays deleted), the cover page the front cover.</param>
		public static void Apply(ComicBook book, int coverPage, IDictionary<int, ComicPageType> changes, bool setDefaults)
		{
			int count = book.PageCount;
			if (setDefaults)
			{
				for (int i = 0; i < count; i++)
				{
					if (changes != null && changes.ContainsKey(i))
					{
						continue;
					}
					ComicPageInfo info = book.GetPage(i, false);
					if (info != null && info.PageType == ComicPageType.Deleted)
					{
						continue;
					}
					Set(book, i, i == coverPage ? ComicPageType.FrontCover : ComicPageType.Story);
				}
			}
			if (changes != null)
			{
				foreach (KeyValuePair<int, ComicPageType> change in changes)
				{
					if (change.Key >= 0 && (count == 0 || change.Key < count))
					{
						Set(book, change.Key, change.Value);
					}
				}
			}
		}

		private static void Set(ComicBook book, int index, ComicPageType type)
		{
			ComicPageInfo info = book.GetPage(index, false);
			if (info == null || info.PageType != type)
			{
				book.UpdatePageType(index, type);
			}
		}
	}
}
