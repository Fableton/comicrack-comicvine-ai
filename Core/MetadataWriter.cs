using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineAI
{
	/// <summary>
	/// Writes a Comic Vine issue into a ComicBook. This is a faithful port of what the Python Comic Vine Scraper does
	/// (ComicBook.update in comicbook.py, PluginBookData.update, and the parsing of cvdb.py), including its quirks,
	/// so that both tools produce the same data for the same options. Blank is -1 for numbers and "" for text.
	/// </summary>
	public static class MetadataWriter
	{
		private const int Blank = -1;
		private const string CvdbSkipKey = "CVDB";

		// cvdb.py: __issue_parse_summary
		private static readonly Regex overview = new Regex("Overview");
		private static readonly Regex paragraph = new Regex(@"<[bB][rR] ?/?>|<[Pp] ?>");
		private static readonly Regex nbsp = new Regex("&nbsp;?");
		private static readonly Regex multiSpaces = new Regex(" {2,}");
		private static readonly Regex stripTags = new Regex("<.*?>");
		private static readonly Regex listOfCovers = new Regex("(?is)list of covers.*$");

		/// <summary>Tag of the books whose data came from the suggestion of Claude (the research with AI of the manual review), not from Comic Vine.</summary>
		public const string ClaudeTag = "ClaudeInfo";

		/// <summary>Adds a tag to the Tags of the book if it is not there yet.</summary>
		public static void AddTag(ComicBook book, string tag)
		{
			List<string> tags = Split(book.Tags).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			if (!tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
			{
				tags.Add(tag);
				book.Tags = JoinCleaned(tags);
			}
		}

		public static void Write(ComicBook book, VolumeInfo volume, IssueInfo issue, WriteOptions o)
		{
			// ---- what Comic Vine says (cvdb.py) ----------------------------------------------------------------
			string newPublisher = ImprintMap.FindParentPublisher(volume.Publisher ?? "");
			string newImprint = newPublisher != (volume.Publisher ?? "") ? volume.Publisher : "";
			int volumeYear = volume.StartYear > 0 ? volume.StartYear : Blank;
			string webpage = (issue.SiteUrl ?? "").StartsWith("http") ? issue.SiteUrl : "";
			int[] coverDate = ParseDate(issue.CoverDate);
			int[] storeDate = ParseDate(issue.StoreDate);
			Dictionary<string, List<string>> roles = ParseRoles(issue);

			// ---- series, number, title, crossovers, summary ----------------------------------------------------
			string value = MassageString(o.Updates("series"), o.OverwriteExisting, true, volume.Name, book.Series);
			if (value != null) { book.Series = value; }

			value = MassageString(o.Updates("number"), o.OverwriteExisting, true, issue.Number, book.Number);
			if (value != null) { book.Number = value; }

			value = MassageString(o.Updates("title"), o.OverwriteExisting, o.IgnoreBlanks, issue.Name, book.Title);
			if (value != null) { book.Title = value; }

			List<string> list = MassageList(o.Updates("crossovers"), o.OverwriteExisting, o.IgnoreBlanks, issue.StoryArcs, Split(book.AlternateSeries));
			if (list != null) { book.AlternateSeries = JoinCleaned(list); }

			value = MassageString(o.Updates("summary"), o.OverwriteExisting, o.IgnoreBlanks, ParseSummary(issue.Description), book.Summary);
			if (value != null) { book.Summary = value; }

			// ---- dates -----------------------------------------------------------------------------------------
			int[] oldReleased = { book.ReleasedTime.Year, book.ReleasedTime.Month, book.ReleasedTime.Day };
			int[] released = MassageDate(o.Updates("released"), o.OverwriteExisting, o.IgnoreBlanks, storeDate, oldReleased);
			if (released != null && released[0] != Blank && released[1] != Blank && released[2] != Blank)
			{
				book.ReleasedTime = new DateTime(released[0], released[1], released[2]);
			}

			int[] oldPublished = { book.Year, book.Month, book.Day };
			int[] published = MassageDate(o.Updates("published"), o.OverwriteExisting, o.IgnoreBlanks, coverDate, oldPublished);
			if (published != null)
			{
				if (published[0] != Blank)
				{
					book.Year = published[0];
				}
				if (published[0] != Blank && published[1] != Blank)
				{
					book.Month = published[1];
				}
				if (published[0] != Blank && published[1] != Blank && published[2] != Blank)
				{
					book.Day = published[2];
				}
			}

			// ---- volume ----------------------------------------------------------------------------------------
			int? volumeValue = MassageNumber(o.Updates("volume"), o.OverwriteExisting, o.IgnoreBlanks, volumeYear, book.ShadowVolume, x => x > 0);
			if (volumeValue != null) { book.Volume = volumeValue.Value; }

			// ---- publisher and imprint -------------------------------------------------------------------------
			string publisher = newPublisher;
			string imprint = newImprint ?? "";
			string key = imprint.Length > 0 ? imprint : publisher;
			if (!string.IsNullOrEmpty(key) && o.UserImprints.TryGetValue(key.ToLowerInvariant(), out string userPublisher))
			{
				publisher = userPublisher;
				imprint = key;
			}
			if (!o.ConvertImprints && imprint.Length > 0)
			{
				publisher = imprint;
				imprint = "";
			}
			if (o.PublisherAliases.TryGetValue(publisher.ToLowerInvariant(), out string alias)) { publisher = alias; }
			if (o.PublisherAliases.TryGetValue(imprint.ToLowerInvariant(), out alias)) { imprint = alias; }
			if (publisher == imprint) { imprint = ""; }

			value = MassageString(o.Updates("imprint"), o.OverwriteExisting, o.IgnoreBlanks, imprint, book.Imprint);
			if (value != null) { book.Imprint = value; }
			value = MassageString(o.Updates("publisher"), o.OverwriteExisting, o.IgnoreBlanks, publisher, book.Publisher);
			if (value != null) { book.Publisher = value; }

			// ---- characters, teams, locations ------------------------------------------------------------------
			list = MassageList(o.Updates("characters"), o.OverwriteExisting, o.IgnoreBlanks, issue.Characters, Split(book.Characters));
			if (list != null) { book.Characters = JoinCleaned(list); }
			list = MassageList(o.Updates("teams"), o.OverwriteExisting, o.IgnoreBlanks, issue.Teams, Split(book.Teams));
			if (list != null) { book.Teams = JoinCleaned(list); }
			list = MassageList(o.Updates("locations"), o.OverwriteExisting, o.IgnoreBlanks, issue.Locations, Split(book.Locations));
			if (list != null) { book.Locations = JoinCleaned(list); }

			// ---- creators --------------------------------------------------------------------------------------
			list = MassageList(o.Updates("writer"), o.OverwriteExisting, o.IgnoreBlanks, roles["writers"], Split(book.Writer));
			if (list != null) { book.Writer = JoinCleaned(list); }
			list = MassageList(o.Updates("penciller"), o.OverwriteExisting, o.IgnoreBlanks, roles["pencillers"], Split(book.Penciller));
			if (list != null) { book.Penciller = JoinCleaned(list); }
			list = MassageList(o.Updates("inker"), o.OverwriteExisting, o.IgnoreBlanks, roles["inkers"], Split(book.Inker));
			if (list != null) { book.Inker = JoinCleaned(list); }
			list = MassageList(o.Updates("colorist"), o.OverwriteExisting, o.IgnoreBlanks, roles["colorists"], Split(book.Colorist));
			if (list != null) { book.Colorist = JoinCleaned(list); }
			list = MassageList(o.Updates("letterer"), o.OverwriteExisting, o.IgnoreBlanks, roles["letterers"], Split(book.Letterer));
			if (list != null) { book.Letterer = JoinCleaned(list); }
			list = MassageList(o.Updates("coverartist"), o.OverwriteExisting, o.IgnoreBlanks, roles["cover_artists"], Split(book.CoverArtist));
			if (list != null) { book.CoverArtist = JoinCleaned(list); }
			list = MassageList(o.Updates("editor"), o.OverwriteExisting, o.IgnoreBlanks, roles["editors"], Split(book.Editor));
			if (list != null) { book.Editor = JoinCleaned(list); }

			// ---- webpage ---------------------------------------------------------------------------------------
			value = MassageString(o.Updates("webpage"), o.OverwriteExisting, o.IgnoreBlanks, webpage, book.Web);
			if (value != null) { book.Web = value; }

			// ---- tags and notes: the key of the issue (an issue typed by hand has none: it is not on Comic Vine) ----
			if (issue.Id == 0)
			{
				return;
			}
			List<string> newTags = AddKeyToTags(Split(book.Tags), issue.Id);
			list = MassageList(o.KeyInTags, true, false, newTags, Split(book.Tags));
			if (list != null) { book.Tags = JoinCleaned(list); }

			string newNotes = AddKeyToNotes(book.Notes, issue.Id, o.NoteScrapeDate);
			value = MassageString(o.KeyInNotes, true, false, newNotes, book.Notes);
			if (value != null) { book.Notes = value; }

			// ---- keys (always written) -------------------------------------------------------------------------
			book.SetCustomValue(Identifier.IssueKey, issue.Id.ToString());
			book.SetCustomValue(Identifier.VolumeKey, volume.Id.ToString());
		}

		// ---- cvdb.py parsing -----------------------------------------------------------------------------------

		public static string ParseSummary(string description)
		{
			if (string.IsNullOrEmpty(description))
			{
				return "";
			}
			string s = overview.Replace(description, "");
			s = paragraph.Replace(s, "\n");
			s = stripTags.Replace(s, "");
			s = multiSpaces.Replace(s, " ");
			s = nbsp.Replace(s, " ");
			s = paragraph.Replace(s, "\n");
			s = s.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
			s = listOfCovers.Replace(s, "");
			return s.Trim();
		}

		/// <summary>"YYYY-MM-DD" into three ints, blank (-1) for what is missing. Any parse error blanks everything.</summary>
		private static int[] ParseDate(string text)
		{
			int[] date = { Blank, Blank, Blank };
			if (string.IsNullOrEmpty(text) || text.Length <= 1)
			{
				return date;
			}
			string[] parts = text.Split('-');
			int[] numbers = new int[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				if (!int.TryParse(parts[i], out numbers[i]))
				{
					return date;
				}
			}
			if (numbers.Length >= 1) { date[0] = numbers[0]; }
			if (numbers.Length >= 2) { date[1] = numbers[1]; }
			if (numbers.Length >= 3) { date[2] = numbers[2]; }
			return date;
		}

		internal static Dictionary<string, List<string>> ParseRoles(IssueInfo issue)
		{
			Dictionary<string, List<string>> map = new Dictionary<string, List<string>>
			{
				["writers"] = new List<string>(), ["pencillers"] = new List<string>(), ["inkers"] = new List<string>(),
				["cover_artists"] = new List<string>(), ["editors"] = new List<string>(), ["colorists"] = new List<string>(),
				["letterers"] = new List<string>()
			};
			foreach (Credit person in issue.Persons)
			{
				foreach (string role in (person.Role ?? "").Split(',').Select(r => r.Trim()))
				{
					switch (role) // exact match, same table as ROLE_DICT in cvdb.py
					{
						case "writer": map["writers"].Add(person.Name); break;
						case "penciler": map["pencillers"].Add(person.Name); break;
						case "artist": map["pencillers"].Add(person.Name); map["inkers"].Add(person.Name); break;
						case "inker": map["inkers"].Add(person.Name); break;
						case "cover": map["cover_artists"].Add(person.Name); break;
						case "editor": map["editors"].Add(person.Name); break;
						case "colorer":
						case "colorist": map["colorists"].Add(person.Name); break;
						case "letterer": map["letterers"].Add(person.Name); break;
					}
				}
			}
			return map;
		}

		// ---- comicbook.py: massage ----------------------------------------------------------------------------

		private static string MassageString(bool update, bool overwrite, bool ignoreBlanks, string newValue, string oldValue)
		{
			string n = (newValue ?? "").Trim();
			string old = (oldValue ?? "").Trim();
			return update && (overwrite || old.Length == 0) && !(ignoreBlanks && n.Length == 0) ? n : null;
		}

		private static List<string> MassageList(bool update, bool overwrite, bool ignoreBlanks, IEnumerable<string> newList, IEnumerable<string> oldList)
		{
			List<string> n = (newList ?? Enumerable.Empty<string>()).Where(x => x != null && x.Trim().Length > 0).ToList();
			List<string> old = (oldList ?? Enumerable.Empty<string>()).Where(x => x != null && x.Trim().Length > 0).ToList();
			return update && (overwrite || old.Count == 0) && (!ignoreBlanks || n.Count > 0) ? n : null;
		}

		private static int? MassageNumber(bool update, bool overwrite, bool ignoreBlanks, int newValue, int oldValue, Func<int, bool> isValid)
		{
			if (!isValid(newValue))
			{
				newValue = Blank;
			}
			return update && (overwrite || oldValue == Blank) && !(ignoreBlanks && newValue == Blank) ? newValue : (int?)null;
		}

		private static int[] MassageDate(bool update, bool overwrite, bool ignoreBlanks, int[] newValue, int[] oldValue)
		{
			bool newBlank = newValue.All(x => x == Blank);
			bool oldBlank = oldValue.All(x => x == Blank);
			return update && (overwrite || oldBlank) && !(ignoreBlanks && newBlank) ? newValue : null;
		}

		// ---- pluginbookdata.py -----------------------------------------------------------------------------------

		/// <summary>ComicRack stores a list as comma separated text. The scraper splits at "," without trimming.</summary>
		private static List<string> Split(string text) => string.IsNullOrEmpty(text) ? new List<string>() : text.Split(',').ToList();

		/// <summary>The scraper removes the commas that are inside a name before joining.</summary>
		private static string JoinCleaned(IEnumerable<string> values) =>
			string.Join(", ", values.Select(v => Regex.Replace(Regex.Replace(v ?? "", @",(\s+)", "$1"), ",", " ")));

		// ---- cvdb.py / comicbook.py: key tag and key note --------------------------------------------------------

		private static string CreateKeyTag(int issueKey) => "CVDB" + issueKey;

		private static int? ParseKeyTag(string text)
		{
			Match m = Regex.Match(text ?? "", @"(?i)CVDB(\d{1,})");
			if (!m.Success)
			{
				m = Regex.Match(text ?? "", @"(?i)ComicVine.?\[(\d{1,})");
			}
			return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
		}

		private static List<string> AddKeyToTags(List<string> tags, int issueKey)
		{
			string tagString = string.Join(", ", tags).Trim();
			string keyTag = CreateKeyTag(issueKey);
			string updated;
			if (tagString.Length > 0)
			{
				bool replaced = false;
				updated = null;
				int? previous = ParseKeyTag(tagString);
				if (previous != null)
				{
					Regex regex = new Regex("(?i)" + CreateKeyTag(previous.Value));
					if (regex.IsMatch(tagString))
					{
						updated = regex.Replace(tagString, keyTag);
						replaced = true;
					}
				}
				if (!replaced)
				{
					if (tagString[tagString.Length - 1] == ',')
					{
						tagString = tagString.Substring(0, tagString.Length - 1);
					}
					updated = tagString + ", " + keyTag;
				}
			}
			else
			{
				updated = keyTag;
			}
			return updated.Split(new[] { ", " }, StringSplitOptions.None).ToList();
		}

		private static string AddKeyToNotes(string notes, int issueKey, bool withDate)
		{
			string noteString = (notes ?? "").Trim();
			string keyTag = CreateKeyTag(issueKey);
			string date = withDate ? " on " + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") : "";
			string keyNote = $"Scraped metadata from ComicVine [{keyTag}]{date}.";
			if (noteString.Length == 0)
			{
				return keyNote;
			}

			int? previous = ParseKeyTag(noteString);
			if (previous != null)
			{
				string previousTag = CreateKeyTag(previous.Value);
				Regex whole = new Regex(@"(?i)Scraped.*?" + previousTag + @"(]\.|.*?[\d\.]{8,} [\d:]{6,}\.)");
				if (whole.IsMatch(noteString))
				{
					return whole.Replace(noteString, keyNote);
				}
				Regex alone = new Regex("(?i)" + previousTag);
				if (alone.IsMatch(noteString))
				{
					return alone.Replace(noteString, keyTag);
				}
			}
			return noteString + "\n\n" + keyNote;
		}
	}
}
