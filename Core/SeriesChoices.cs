using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>
	/// Remembers which Comic Vine volume a series name of the library was matched with, so the next book with that name goes
	/// straight to the volume without a search: once "abc 4.cbr" turned out to be Batman vol. 1, "abc 5.cbr" is looked for there.
	/// This is what the Python scraper did with its scrape_cache (keyed by the series of the book, not by the name of the volume),
	/// with two differences: the choice is kept between runs (series-choices.json in the plugin folder, 90 days), and the cover
	/// still has to agree before a book is accepted with it.
	/// </summary>
	public sealed class SeriesChoices
	{
		private const int MaxEntries = 2000;
		private static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

		public sealed class Item
		{
			public int Id;
			public string Name = "";
			public int StartYear;
			public string Publisher = "";
			public int IssueCount;
			public double? First;
			public double? Last;
			public bool Local; // the volume comes from the local database
			public long Saved;
		}

		private readonly object sync = new object();
		private readonly string file;
		private readonly Dictionary<string, Item> items = new Dictionary<string, Item>();

		public SeriesChoices(string folder)
		{
			file = folder == null ? null : Path.Combine(folder, "series-choices.json");
			try
			{
				if (file != null && File.Exists(file))
				{
					Dictionary<string, Item> stored = new JavaScriptSerializer().Deserialize<Dictionary<string, Item>>(File.ReadAllText(file, Encoding.UTF8));
					long cut = DateTimeOffset.UtcNow.Subtract(MaxAge).ToUnixTimeSeconds();
					if (stored != null)
					{
						foreach (KeyValuePair<string, Item> pair in stored.Where(p => p.Value != null && p.Value.Id != 0 && p.Value.Saved > cut))
						{
							items[pair.Key] = pair.Value;
						}
					}
				}
			}
			catch (Exception)
			{
				// a broken file is just an empty memory
			}
		}

		public bool TryGet(string key, out VolumeInfo volume)
		{
			lock (sync)
			{
				if (items.TryGetValue(key, out Item item))
				{
					volume = new VolumeInfo
					{
						Id = item.Id, Name = item.Name, StartYear = item.StartYear, Publisher = item.Publisher, IssueCount = item.IssueCount,
						FirstNumber = item.First ?? double.NaN, LastNumber = item.Last ?? double.NaN, Local = item.Local
					};
					return true;
				}
			}
			volume = null;
			return false;
		}

		public void Set(string key, VolumeInfo volume)
		{
			if (string.IsNullOrEmpty(key) || volume == null || volume.Id == 0)
			{
				return;
			}
			lock (sync)
			{
				items[key] = new Item
				{
					Id = volume.Id, Name = volume.Name, StartYear = volume.StartYear, Publisher = volume.Publisher, IssueCount = volume.IssueCount,
					First = double.IsNaN(volume.FirstNumber) ? (double?)null : volume.FirstNumber,
					Last = double.IsNaN(volume.LastNumber) ? (double?)null : volume.LastNumber,
					Local = volume.Local,
					Saved = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
				};
				if (items.Count > MaxEntries)
				{
					foreach (string old in items.OrderBy(p => p.Value.Saved).Take(items.Count - MaxEntries).Select(p => p.Key).ToList())
					{
						items.Remove(old);
					}
				}
				Save();
			}
		}

		public void Remove(string key)
		{
			lock (sync)
			{
				if (items.Remove(key))
				{
					Save();
				}
			}
		}

		private void Save()
		{
			try
			{
				if (file != null)
				{
					File.WriteAllText(file, new JavaScriptSerializer().Serialize(items), new UTF8Encoding(false));
				}
			}
			catch (IOException)
			{
			}
		}
	}
}
