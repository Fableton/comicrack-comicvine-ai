using System.Collections.Generic;

namespace ComicVineAI
{
	public class VolumeInfo
	{
		public int Id;
		public string Name = "";
		public int StartYear;
		public string Publisher = "";
		public int IssueCount;
		public string ImageUrl = "";

		/// <summary>Comes from the local database (see LocalDb), not from the API.</summary>
		public bool Local;

		/// <summary>Number of the first and the last issue of the volume (NaN when unknown). A volume can start at #300.</summary>
		public double FirstNumber = double.NaN;
		public double LastNumber = double.NaN;

		public override string ToString() => $"{Name} ({StartYear}) - {Publisher}";

		/// <summary>A copy that can be changed (by a manual edit) without touching the cached original.</summary>
		public VolumeInfo Copy() => (VolumeInfo)MemberwiseClone();
	}

	public class Credit
	{
		public string Name = "";
		public string Role = "";
	}

	public class IssueInfo
	{
		public int Id;
		public int VolumeId;
		public string VolumeName = "";
		public string Number = "";
		public string Name = "";
		public string CoverDate = "";
		public string StoreDate = "";
		public string Description = "";
		public string SiteUrl = "";
		public string ImageUrl = "";
		public List<string> AltImageUrls = new List<string>();
		public List<Credit> Persons = new List<Credit>();
		public List<string> Characters = new List<string>();
		public List<string> Teams = new List<string>();
		public List<string> Locations = new List<string>();
		public List<string> StoryArcs = new List<string>();

		/// <summary>True when the credits and description were loaded (issue detail call).</summary>
		public bool HasDetails;

		/// <summary>Comes from the local database (see LocalDb), not from the API.</summary>
		public bool Local;

		/// <summary>A copy that can be changed (by a manual edit) without touching the cached original.</summary>
		public IssueInfo Copy() => (IssueInfo)MemberwiseClone();
	}

	/// <summary>
	/// Values typed or corrected by the user in the edit window of the manual review. They replace what Comic Vine says (or Claude
	/// suggested) just before the book is written. Number is free text: "1-2" for a file that holds two issues.
	/// </summary>
	public class ManualEdit
	{
		public string Series = "";
		public string Number = "";
		public string Title = "";
		public int Year;
		public string Publisher = "";

		// The extra fields of the research with AI. Empty means "nothing to add": it never blanks what Comic Vine or the book has.
		public string Writer = "", Penciller = "", Inker = "", Colorist = "", Letterer = "", CoverArtist = "", Editor = "";
		public string CoverDate = "", StoreDate = "", Summary = "";
		public string Characters = "", Teams = "", Locations = "", StoryArcs = "";
		public string Web = "";

		/// <summary>The fields were filled from a Comic Vine issue (the "Edit data" window): what is in them replaces the issue's data, an empty list or credit clears it.</summary>
		public bool FromComicVine;
	}

	/// <summary>What is known about a book before the search.</summary>
	public class BookHints
	{
		public List<string> SeriesNames = new List<string>();
		public List<string> Numbers = new List<string>();
		public int Year;
		public int Volume;
		public string Publisher = "";
		public string FileName = "";

		public string Series => SeriesNames.Count > 0 ? SeriesNames[0] : "";
		public string Number => Numbers.Count > 0 ? Numbers[0] : "";

		public override string ToString() => $"'{Series}' #{Number} ({(Year > 0 ? Year.ToString() : "?")}) {Publisher}";
	}

	/// <summary>A candidate issue with the evidence for it.</summary>
	public class IssueMatch
	{
		public VolumeInfo Volume;
		public IssueInfo Issue;
		public double NameScore;
		/// <summary>0..1 cover similarity, or -1 when the covers could not be compared.</summary>
		public double CoverScore = -1;
		public double Confidence;
		public string Source = "";

		/// <summary>How well the volume fits (name, year, publisher), before the cover.</summary>
		public double VolumeScore;

		/// <summary>1.0, or less for a collected edition when individual issues are preferred.</summary>
		public double Penalty = 1.0;

		/// <summary>The alternative covers of the issue were already compared (nothing new to find in them).</summary>
		public bool AlternatesChecked;

		/// <summary>The cover score comes from an alternative (variant) cover of the issue, not from the main one.</summary>
		public bool FromAlternate;

		/// <summary>Found by comparing covers: the issue whose cover matches is not the one the file name says.</summary>
		public bool NumberDiffers;

		/// <summary>1.0, or less when the issue was found by its cover alone and the number is not the file's, or the cover does not stand out from its neighbours.</summary>
		public double NumberFactor = 1.0;

		public override string ToString() =>
			Loc.T("match.text", Volume?.Name, Volume?.StartYear, Issue?.Number, Volume?.Publisher, Confidence.ToString("P0"), NameScore.ToString("P0"), CoverScore < 0 ? Loc.T("common.na") : CoverScore.ToString("P0"));
	}

	/// <summary>The answer of the Claude CLI.</summary>
	public class ClaudeSuggestion
	{
		public string Series = "";
		public string Number = "";
		public int VolumeYear;
		public int Year;
		public string Publisher = "";
		public string Title = "";

		// What the research with AI can add when its sources say it (the plain identification does not ask for these). Lists are
		// separated by commas, dates are "YYYY-MM-DD" (or just the year / the year and month).
		public string Writer = "", Penciller = "", Inker = "", Colorist = "", Letterer = "", CoverArtist = "", Editor = "";
		public string CoverDate = "", StoreDate = "", Summary = "";
		public string Characters = "", Teams = "", Locations = "", StoryArcs = "";
		public string SourceUrl = "";
		public bool IsReprint;
		public string Language = "";
		public double Confidence;
		public string Notes = "";

		public bool IsUsable => !string.IsNullOrWhiteSpace(Series) && Confidence > 0;

		public ClaudeSuggestion Copy() => (ClaudeSuggestion)MemberwiseClone();

		public override string ToString() =>
			IsUsable ? Loc.T("claude.text", Series, Number, VolumeYear, Year, Publisher, Confidence.ToString("P0"), Notes) : Loc.T("claude.none");
	}
}
