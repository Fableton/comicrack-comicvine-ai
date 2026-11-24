using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>
	/// Remembers what Claude said about a cover, so the same cover is never sent twice: a book that is processed again, or
	/// a second copy of the same issue. The cover is recognized by its fingerprint (see <see cref="CoverFingerprint"/>), not by
	/// the file, so a different scan of the same cover that is almost identical also matches.
	/// An answer is kept for the choices that were made with it (prefer the original, language of the notes). Answers that the
	/// cover later confirmed (the book was accepted with them) are marked as validated.
	/// </summary>
	public sealed class ClaudeCache
	{
		/// <summary>How alike two covers have to be (0..1) to be the same cover. Two scans of one cover score 0.93 to 0.98, different issues far less.</summary>
		private const double SameCover = 0.92;

		private const int MaxEntries = 5000;
		private static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);

		public sealed class Entry
		{
			public string Print = "";
			public bool PreferOriginal;
			public string Language = "";
			public bool Validated;
			public long Saved;
			public ClaudeSuggestion Suggestion = new ClaudeSuggestion();

			[ScriptIgnore]
			public CoverFingerprint Decoded;
		}

		private readonly object sync = new object();
		private readonly string file;
		private readonly List<Entry> entries = new List<Entry>();

		public ClaudeCache(string folder)
		{
			file = Path.Combine(folder, "claude-cache.json");
			try
			{
				if (File.Exists(file))
				{
					List<Entry> stored = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<Entry>>(File.ReadAllText(file, Encoding.UTF8));
					long cut = DateTimeOffset.UtcNow.Subtract(MaxAge).ToUnixTimeSeconds();
					if (stored != null)
					{
						entries.AddRange(stored.Where(e => e != null && e.Saved > cut && e.Suggestion != null));
					}
				}
			}
			catch (Exception)
			{
				// a broken cache is just an empty one
			}
		}

		/// <summary>
		/// Deletes the saved answers that are older than the given number of days (0 or less: all of them), straight in the file, for the
		/// settings window. With dryRun nothing is changed, it only says how many would go. Returns how many are removed and how many stay.
		/// </summary>
		public static void Purge(string folder, int olderThanDays, bool dryRun, out int removed, out int kept)
		{
			removed = 0;
			kept = 0;
			string path = Path.Combine(folder, "claude-cache.json");
			if (!File.Exists(path))
			{
				return;
			}
			JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
			List<Entry> stored = json.Deserialize<List<Entry>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<Entry>();
			long cut = olderThanDays > 0 ? DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToUnixTimeSeconds() : long.MaxValue;
			List<Entry> keep = stored.Where(e => e != null && e.Saved >= cut).ToList(); // Saved < cut is older than the limit
			removed = stored.Count - keep.Count;
			kept = keep.Count;
			if (!dryRun && removed > 0)
			{
				File.WriteAllText(path, json.Serialize(keep), new UTF8Encoding(false));
			}
		}

		public int Count
		{
			get
			{
				lock (sync)
				{
					return entries.Count;
				}
			}
		}

		public bool TryGet(CoverFingerprint cover, bool preferOriginal, string language, out ClaudeSuggestion suggestion)
		{
			suggestion = null;
			lock (sync)
			{
				Entry best = Find(cover, preferOriginal, language, out double _);
				if (best != null)
				{
					suggestion = best.Suggestion;
				}
				return suggestion != null;
			}
		}

		public void Put(CoverFingerprint cover, bool preferOriginal, string language, ClaudeSuggestion suggestion)
		{
			lock (sync)
			{
				Entry existing = Find(cover, preferOriginal, language, out double _);
				if (existing != null)
				{
					existing.Suggestion = suggestion;
					existing.Saved = Now();
				}
				else
				{
					entries.Add(new Entry { Print = cover.Encode(), Decoded = cover, PreferOriginal = preferOriginal, Language = language, Suggestion = suggestion, Saved = Now() });
					if (entries.Count > MaxEntries)
					{
						entries.RemoveAll(e => entries.OrderBy(x => x.Saved).Take(entries.Count - MaxEntries).Contains(e));
					}
				}
				Save();
			}
		}

		/// <summary>The cover confirmed the answer: the book was accepted with it.</summary>
		public void Validate(CoverFingerprint cover, bool preferOriginal, string language)
		{
			lock (sync)
			{
				Entry entry = Find(cover, preferOriginal, language, out double _);
				if (entry != null && !entry.Validated)
				{
					entry.Validated = true;
					Save();
				}
			}
		}

		private Entry Find(CoverFingerprint cover, bool preferOriginal, string language, out double similarity)
		{
			Entry best = null;
			similarity = 0;
			foreach (Entry entry in entries)
			{
				if (entry.PreferOriginal != preferOriginal || entry.Language != language)
				{
					continue;
				}
				if (entry.Decoded == null)
				{
					entry.Decoded = CoverFingerprint.Decode(entry.Print);
				}
				if (entry.Decoded == null)
				{
					continue;
				}
				double s = cover.LegacySimilarity(entry.Decoded);
				if (s >= SameCover && s > similarity)
				{
					similarity = s;
					best = entry;
				}
			}
			return best;
		}

		private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		private void Save()
		{
			try
			{
				File.WriteAllText(file, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(entries), new UTF8Encoding(false));
			}
			catch (IOException)
			{
			}
		}
	}
}
