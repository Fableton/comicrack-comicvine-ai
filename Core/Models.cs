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
		public bool IsReprint;
		public string Language = "";
		public double Confidence;
		public string Notes = "";

		public bool IsUsable => !string.IsNullOrWhiteSpace(Series) && Confidence > 0;

		public override string ToString() =>
			IsUsable ? Loc.T("claude.text", Series, Number, VolumeYear, Year, Publisher, Confidence.ToString("P0"), Notes) : Loc.T("claude.none");
	}
}
