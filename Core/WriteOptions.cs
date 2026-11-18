using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicVineAI
{
	/// <summary>
	/// Which fields are written to a book and how. They come from the settings of this plugin (Metadata tab) and nothing
	/// else: the plugin does not read the configuration of any other plugin. The rules are the same ones the Python Comic
	/// Vine Scraper has (what to update, overwrite, ignore blanks, convert imprints, where the key goes...), so this plugin can
	/// replace it and give the same results.
	/// </summary>
	public sealed class WriteOptions
	{
		/// <summary>The names of the fields that can be switched off.</summary>
		public static readonly string[] Fields =
		{
			"series", "number", "title", "crossovers", "summary", "released", "published", "volume", "publisher", "imprint",
			"characters", "teams", "locations", "writer", "penciller", "inker", "colorist", "letterer", "coverartist", "editor", "webpage"
		};

		public readonly HashSet<string> Update = new HashSet<string>(Fields, StringComparer.OrdinalIgnoreCase);

		/// <summary>Replace values that already exist.</summary>
		public bool OverwriteExisting = true;

		/// <summary>Never replace an existing value with an empty one.</summary>
		public bool IgnoreBlanks = true;

		/// <summary>Store an imprint (Vertigo) as its parent publisher (DC Comics) plus the imprint.</summary>
		public bool ConvertImprints = true;

		/// <summary>Add the CVDB key to the Tags field.</summary>
		public bool KeyInTags = true;

		/// <summary>Add "Scraped metadata from ComicVine [CVDB...]" to the Notes field.</summary>
		public bool KeyInNotes;

		public bool NoteScrapeDate;

		public Dictionary<string, string> UserImprints = new Dictionary<string, string>();
		public Dictionary<string, string> PublisherAliases = new Dictionary<string, string>();

		/// <summary>Publishers (lower case, trimmed) of the local editions that are not wanted.</summary>
		public HashSet<string> IgnoredPublishers = new HashSet<string>();

		public int IgnoreBeforeYear;
		public int IgnoreAfterYear = 9999999;
		public int NeverIgnoreThreshold = 9999999;

		public bool Updates(string field) => Update.Contains(field);

		public override string ToString() =>
			$"overwrite={OverwriteExisting}, ignore blanks={IgnoreBlanks}, convert imprints={ConvertImprints}, key in Tags={KeyInTags}, key in Notes={KeyInNotes}, " +
			$"{IgnoredPublishers.Count} local publisher(s) skipped, fields off: {(Fields.Where(f => !Update.Contains(f)).DefaultIfEmpty("none").Aggregate((a, b) => a + "," + b))}";

		public static WriteOptions Load(PluginSettings settings)
		{
			WriteOptions o = new WriteOptions
			{
				OverwriteExisting = settings.OverwriteExisting,
				IgnoreBlanks = settings.IgnoreBlanks,
				ConvertImprints = settings.ConvertImprints,
				KeyInTags = settings.KeyInTags,
				KeyInNotes = settings.KeyInNotes,
				NoteScrapeDate = settings.NoteScrapeDate
			};
			foreach (string field in settings.DisabledFields ?? new List<string>())
			{
				o.Update.Remove(field);
			}
			foreach (string line in Lines(settings.LocalPublishers))
			{
				o.IgnoredPublishers.Add(line.ToLowerInvariant());
			}
			foreach (string line in Lines(settings.PublisherAliases))
			{
				if (TryPair(line, out string publisher, out string alias))
				{
					o.PublisherAliases[publisher.ToLowerInvariant()] = alias;
				}
			}
			foreach (string line in Lines(settings.CustomImprints))
			{
				if (TryPair(line, out string imprint, out string publisher))
				{
					o.UserImprints[imprint.ToLowerInvariant()] = publisher;
				}
			}
			return o;
		}

		private static IEnumerable<string> Lines(string text) =>
			(text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);

		/// <summary>"Marvel UK --> Marvel": the two names, at most 50 characters each.</summary>
		private static bool TryPair(string line, out string left, out string right)
		{
			left = right = null;
			Match m = Regex.Match(line, @"^\s*(\S.*?)\s*[-=]+>\s*(\S.*?)\s*$");
			if (!m.Success)
			{
				return false;
			}
			left = m.Groups[1].Value.Trim(' ', '\'', '"');
			right = m.Groups[2].Value.Trim(' ', '\'', '"');
			return left.Length > 0 && right.Length > 0 && left.Length <= 50 && right.Length <= 50;
		}
	}
}
