using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>User settings, stored as settings.json in the plugin folder.</summary>
	public class PluginSettings
	{
		/// <summary>Personal Comic Vine API key (https://comicvine.gamespot.com/api/).</summary>
		public string ComicVineApiKey { get; set; } = "";

		/// <summary>Minimum confidence (0..1) to accept a match without asking the user.</summary>
		public double AcceptThreshold { get; set; } = 0.80;

		/// <summary>Look for the original publication of a localized reprint.</summary>
		public bool PreferOriginal { get; set; } = true;

		/// <summary>Compare the first page with the Comic Vine covers.</summary>
		public bool UseCoverComparison { get; set; } = true;

		/// <summary>Ask the Claude CLI to identify the cover when the normal search fails.</summary>
		public bool UseClaude { get; set; } = true;

		/// <summary>Full path of the claude executable. Empty searches the PATH.</summary>
		public string ClaudePath { get; set; } = "";

		/// <summary>Optional --model value for the Claude CLI.</summary>
		public string ClaudeModel { get; set; } = "";

		/// <summary>Language of the plugin windows: "auto" (the language of ComicRack), or a code such as "en", "es", "fr".</summary>
		public string Language { get; set; } = "auto";

		/// <summary>Text size of the plugin windows, as a multiplier of the system font (1.0 = system size).</summary>
		public double FontScale { get; set; } = 1.25;

		/// <summary>Prefer the volume of the individual issues over collected editions (TPB, hardcover, omnibus...).</summary>
		public bool PreferIndividualIssues { get; set; } = true;

		/// <summary>Path of a local copy of the Comic Vine data (SQLite). Empty: every query goes to the API.</summary>
		public string LocalDbPath { get; set; } = "";

		/// <summary>Publishers of local editions (Panini, Zinco...) to skip when the original is preferred, one per line.</summary>
		public string LocalPublishers { get; set; } = "";

		// ---- what is written to the books (Metadata tab) ----

		/// <summary>Fields that are not written. Names as in <see cref="WriteOptions.Fields"/>; empty writes everything.</summary>
		public List<string> DisabledFields { get; set; } = new List<string>();

		/// <summary>Replace values that already exist.</summary>
		public bool OverwriteExisting { get; set; } = true;

		/// <summary>Never replace an existing value with an empty one.</summary>
		public bool IgnoreBlanks { get; set; } = true;

		/// <summary>Store an imprint (Vertigo) as its parent publisher (DC Comics) plus the imprint.</summary>
		public bool ConvertImprints { get; set; } = true;

		/// <summary>Add the Comic Vine key (CVDB id) to the Tags.</summary>
		public bool KeyInTags { get; set; } = true;

		/// <summary>Add "Scraped metadata from ComicVine [CVDB id]" to the Notes.</summary>
		public bool KeyInNotes { get; set; }

		/// <summary>Put the date in that note.</summary>
		public bool NoteScrapeDate { get; set; }

		/// <summary>Publisher aliases, one per line: "name --> alias".</summary>
		public string PublisherAliases { get; set; } = "";

		/// <summary>Imprints that Comic Vine does not know, one per line: "imprint --> publisher".</summary>
		public string CustomImprints { get; set; } = "";

		/// <summary>Skip books that were already scraped (they have a comicvine_issue value).</summary>
		public bool SkipAlreadyScraped { get; set; } = true;

		/// <summary>How many candidate volumes are checked for each search.</summary>
		public int MaxVolumeCandidates { get; set; } = 4;

		/// <summary>How long Comic Vine answers stay in the disk cache.</summary>
		public int CacheDays { get; set; } = 30;

		private static string FilePath(string folder) => Path.Combine(folder, "settings.json");

		public static PluginSettings Load(string folder)
		{
			try
			{
				string file = FilePath(folder);
				if (File.Exists(file))
				{
					return new JavaScriptSerializer().Deserialize<PluginSettings>(File.ReadAllText(file)) ?? new PluginSettings();
				}
			}
			catch (Exception)
			{
			}
			return new PluginSettings();
		}

		public void Save(string folder)
		{
			Directory.CreateDirectory(folder);
			File.WriteAllText(FilePath(folder), new JavaScriptSerializer().Serialize(this));
		}
	}
}
