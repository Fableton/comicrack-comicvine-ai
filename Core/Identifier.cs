using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineAI
{
	public sealed class IdentifyResult
	{
		public ComicBook Book;
		public BookHints Hints;
		public List<IssueMatch> Candidates = new List<IssueMatch>();
		public ClaudeSuggestion Claude;
		public bool Accepted;
		public bool Skipped;
		public int Index;
		public string Message = "";

		public IssueMatch Best => Candidates.FirstOrDefault();
	}

	/// <summary>
	/// Finds the Comic Vine issue of a book:
	/// 1. the current metadata and the file name drive a Comic Vine search,
	/// 2. the candidates are checked against the cover (and the alternative covers),
	/// 3. when nothing is sure enough, the Claude CLI looks at the cover and the search is repeated with its answer.
	/// Comic Vine calls are kept to a minimum: everything is cached, books of one series share the volume that
	/// was found for the previous one, and alternative covers are only fetched when the main cover does not match.
	/// </summary>
	public sealed class Identifier
	{
		public const string IssueKey = "comicvine_issue";
		public const string VolumeKey = "comicvine_volume";

		private const double StrongCoverMatch = 0.80;
		private const double CoverFloor = 0.62; // below this similarity the covers are considered different (the next issue of a series reaches 0.75 at most)
		private const double CoverRange = 0.28; // floor + range = a safe match

		private static readonly System.Text.RegularExpressions.Regex collectionWords = new System.Text.RegularExpressions.Regex(
			@"(tpb|hc|hardcover|hard cover|trade paperback|paperback|omnibus|compendium|collection|collected|library edition|treasury)",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

		private readonly PluginSettings settings;
		private readonly WriteOptions options;
		private readonly ComicVineClient comicVine;
		private readonly Func<ComicBook, Bitmap> coverProvider;
		private readonly Action<string> log;
		private readonly List<VolumeInfo> acceptedVolumes = new List<VolumeInfo>(); // most recent first
		private readonly ClaudeCache claudeCache;

		/// <summary>
		/// Called with every candidate whose cover was just compared, together with that cover (the caller must not keep or dispose it).
		/// A window uses it to show the best candidate so far while the search goes on, instead of an empty space.
		/// </summary>
		public Action<IssueMatch, Bitmap> CandidateCompared { get; set; }
		private readonly SeriesChoices choices;
		private CoverFingerprint claudeKey;
		private bool claudeDisabled;

		public Identifier(PluginSettings settings, WriteOptions options, ComicVineClient comicVine, SeriesChoices choices, ClaudeCache claudeCache, Func<ComicBook, Bitmap> coverProvider, Action<string> log)
		{
			this.settings = settings;
			this.options = options;
			this.claudeCache = claudeCache;
			this.choices = choices ?? new SeriesChoices(null);
			this.comicVine = comicVine;
			this.coverProvider = coverProvider;
			this.log = log ?? (_ => { });
		}

		public static BookHints BuildHints(ComicBook book)
		{
			ParsedName parsed = FileNameParser.Parse(book.FileNameWithExtension);
			BookHints hints = new BookHints
			{
				Year = book.Year > 0 ? book.Year : parsed.Year,
				Volume = book.Volume > 0 ? book.Volume : parsed.Volume,
				Publisher = book.Publisher ?? "",
				FileName = book.FileNameWithExtension ?? ""
			};
			foreach (string name in new[] { book.Series, parsed.Series, book.ShadowSeries })
			{
				AddName(hints.SeriesNames, name);
			}
			foreach (string number in new[] { book.Number, parsed.Number })
			{
				if (!string.IsNullOrWhiteSpace(number) && !hints.Numbers.Any(n => TextMatch.NormalizeNumber(n) == TextMatch.NormalizeNumber(number)))
				{
					hints.Numbers.Add(number.Trim());
				}
			}
			return hints;
		}

		private static void AddName(List<string> names, string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return;
			}
			name = name.Trim();
			if (!names.Any(n => TextMatch.Normalize(n) == TextMatch.Normalize(name)))
			{
				names.Add(name);
			}
		}

		// ---- Identification ----------------------------------------------------------------------------------

		/// <param name="coverOverride">
		/// The page to use as the cover instead of the first one (the file is not well put together: the cover is at the end, or in the
		/// middle). Identify takes it over and disposes it. When it is given, a book that was scraped before is searched anyway.
		/// </param>
		public IdentifyResult Identify(ComicBook book, CancellationToken token, Bitmap coverOverride = null)
		{
			IdentifyResult result = new IdentifyResult { Book = book };
			if (coverOverride == null && settings.SkipAlreadyScraped && !string.IsNullOrEmpty(book.GetCustomValue(IssueKey)))
			{
				result.Skipped = true;
				result.Message = Loc.T("id.alreadyScraped");
				return result;
			}

			result.Hints = BuildHints(book);
			Bitmap cover = null;
			try
			{
				if (coverOverride != null)
				{
					cover = coverOverride;
				}
				else if (settings.UseCoverComparison || settings.UseClaude)
				{
					cover = coverProvider(book);
				}
				CoverFingerprint fingerprint = settings.UseCoverComparison ? CoverFingerprint.From(cover) : null;

				// stage 1: the data of the book itself
				if (result.Hints.SeriesNames.Count > 0)
				{
					log(Loc.T("id.searching", result.Hints));
					result.Candidates.AddRange(Resolve(result.Hints, fingerprint, 0, "metadata", token));
				}
				Decide(result);

				// stage 2: Claude looks at the cover
				if (!result.Accepted && settings.UseClaude && !claudeDisabled && cover != null)
				{
					token.ThrowIfCancellationRequested();
					result.Claude = AskClaude(cover, result.Hints, token);
					if (result.Claude != null && result.Claude.IsUsable)
					{
						BookHints claudeHints = new BookHints { Year = result.Claude.Year, Publisher = result.Claude.Publisher, FileName = result.Hints.FileName };
						claudeHints.SeriesNames.Add(result.Claude.Series);
						if (!string.IsNullOrWhiteSpace(result.Claude.Number))
						{
							claudeHints.Numbers.Add(result.Claude.Number);
						}
						log(Loc.T("id.claudeSuggests", claudeHints, result.Claude.VolumeYear, result.Claude.Confidence.ToString("P0")));
						result.Candidates.AddRange(Resolve(claudeHints, fingerprint, result.Claude.VolumeYear, "claude", token));
						Decide(result);
					}
				}
			}
			finally
			{
				cover?.Dispose();
			}

			if (result.Accepted)
			{
				Accept(result.Hints, result.Best.Volume);
				if (claudeKey != null && result.Best.Source == "claude")
				{
					claudeCache?.Validate(claudeKey, settings.PreferOriginal, Loc.Code); // the cover confirmed what Claude said
				}
			}
			return result;
		}

		private void Decide(IdentifyResult result)
		{
			result.Candidates = result.Candidates
				.GroupBy(m => m.Issue.Id)
				.Select(g => g.OrderByDescending(m => m.Confidence).First())
				.OrderByDescending(m => m.Confidence)
				.ToList();
			result.Accepted = result.Best != null && result.Best.Confidence >= settings.AcceptThreshold;
			result.Message = result.Best == null ? Loc.T("id.noCandidate") : result.Best.ToString();
		}

		private ClaudeSuggestion AskClaude(Bitmap cover, BookHints hints, CancellationToken token)
		{
			try
			{
				// the same cover (a book asked again, a second copy of the issue) is not sent to Claude twice
				claudeKey = CoverFingerprint.From(cover);
				if (claudeKey != null && claudeCache != null && claudeCache.TryGet(claudeKey, settings.PreferOriginal, Loc.Code, out ClaudeSuggestion cached))
				{
					log(Loc.T("id.claudeCached"));
					return cached;
				}
				log(Loc.T("id.askingClaude"));
				ClaudeSuggestion suggestion = ClaudeCli.Identify(cover, hints, settings, log, token);
				if (claudeKey != null && suggestion != null && suggestion.IsUsable)
				{
					claudeCache?.Put(claudeKey, settings.PreferOriginal, Loc.Code, suggestion);
				}
				return suggestion;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				log(Loc.T("id.claudeUnavailable", e.Message));
				if (e is InvalidOperationException)
				{
					claudeDisabled = true; // not installed or not logged in: do not try again for every book
				}
				return null;
			}
		}

		// ---- Search ------------------------------------------------------------------------------------------

		/// <summary>Candidate issues for the hints, best first.</summary>
		public List<IssueMatch> Resolve(BookHints hints, CoverFingerprint cover, int volumeYearHint, string source, CancellationToken token)
		{
			comicVine.Stage = source;
			// The local database costs no calls, so it is searched much harder than the API: every series that looks like the cleaned
			// name of the file (and the shorter forms of it) is a candidate, and many more of them are compared. Claude is only asked
			// when all of that finds nothing.
			bool local = comicVine.UsesLocalDb;
			int candidateLimit = local ? Math.Max(settings.MaxVolumeCandidates, 30) : settings.MaxVolumeCandidates;
			int coverLimit = local ? 15 : settings.MaxVolumeCandidates; // comparing a cover downloads an image
			double nameFloor = local ? 0.4 : 0.5;
			int evaluated = 0;
			List<IssueMatch> matches = new List<IssueMatch>();
			List<Tuple<VolumeInfo, double, double>> volumes = new List<Tuple<VolumeInfo, double, double>>();

			void AddVolume(VolumeInfo volume)
			{
				if (volume != null && volume.Id != 0 && !IsExcluded(volume) && !volumes.Any(t => t.Item1.Id == volume.Id))
				{
					ScoreVolume(volume, hints, volumeYearHint, out double name, out double total);
					volumes.Add(Tuple.Create(volume, name, total));
				}
			}

			// The volumes that were accepted for the previous books cost no search at all. A series that is written differently in
			// every file name ("Catwoman Officer Down IV", "Catwoman Year Two") is still the same volume, so the ones whose name
			// relates to the hints are tried first, and the cover has to agree. Their issue lists are already in the cache.
			HashSet<int> tried = new HashSet<int>();
			bool resolvedByKnown = false;

			// First of all the volume that an earlier book with the same series name in the library was matched with
			// ("abc 4" was Batman vol. 1, so "abc 5" is looked for there): no search, the cover still has to agree.
			IssueMatch remembered = TryRemembered(hints, cover, source, token);
			if (remembered != null)
			{
				matches.Add(remembered);
				tried.Add(remembered.Volume.Id);
				resolvedByKnown = remembered.Confidence >= settings.AcceptThreshold;
			}
			foreach (VolumeInfo known in acceptedVolumes.Where(v => !resolvedByKnown && !tried.Contains(v.Id) && !IsExcluded(v) && NameRelated(v, hints)).Take(3).ToList())
			{
				token.ThrowIfCancellationRequested();
				AddVolume(known);
				Tuple<VolumeInfo, double, double> entry = volumes.First(t => t.Item1.Id == known.Id);
				tried.Add(known.Id);
				IssueMatch quick = TryVolume(known, entry.Item2, entry.Item3, hints, cover, source, token);
				if (quick != null)
				{
					matches.Add(quick);
					if (quick.Confidence >= settings.AcceptThreshold)
					{
						resolvedByKnown = true;
						break;
					}
				}
			}

			if (!resolvedByKnown)
			{
				foreach (string name in SearchNames(hints, local))
				{
					token.ThrowIfCancellationRequested();
					foreach (VolumeInfo volume in comicVine.SearchVolumes(name, token))
					{
						AddVolume(volume);
					}
				}
				List<Tuple<VolumeInfo, double, double>> candidates = volumes
					.Where(t => t.Item2 >= nameFloor && !tried.Contains(t.Item1.Id))
					.OrderByDescending(t => t.Item3)
					.ToList();
				// The volumes whose numbering contains the number are asked first, so the right one is usually found on the first
				// call. No volume is ever left out: a volume of 100 issues can perfectly well run from #300 to #399.
				foreach (Tuple<VolumeInfo, double, double> candidate in candidates
					.OrderBy(t => volumeYearHint > 0 && t.Item1.StartYear == volumeYearHint ? 0 : 1) // the year Claude gave, if any
					.ThenBy(t => FitRank(t.Item1, hints))
					.ThenByDescending(t => t.Item3)
					.Take(Math.Max(1, candidateLimit)))
				{
					token.ThrowIfCancellationRequested();
					IssueMatch match = TryVolume(candidate.Item1, candidate.Item2, candidate.Item3, hints, evaluated < coverLimit ? cover : null, source, token);
					evaluated++;
					if (match != null)
					{
						matches.Add(match);
						if (match.Confidence >= settings.AcceptThreshold)
						{
							break; // good enough to be accepted: do not spend more calls on other volumes
						}
					}
				}
			}
			List<IssueMatch> ranked = matches.OrderByDescending(m => m.Confidence).ToList();
			if (cover != null && ranked.Count > 0 && ranked[0].Confidence < settings.AcceptThreshold)
			{
				// Nothing is sure yet. The cover of the book may be a variant that looks nothing like the main cover, so the
				// alternative covers of the two best candidates of the right series are checked (one call each).
				foreach (IssueMatch candidate in ranked.Where(m => m.NameScore >= 0.8).Take(2).ToList())
				{
					ImproveWithAlternates(candidate, cover, token);
				}
				ranked = ranked.OrderByDescending(m => m.Confidence).ToList();
			}
			return ranked;
		}

		/// <summary>
		/// 0 when the number is inside the numbering of the volume, 1 when that is not known, 2 when it is outside.
		/// It only decides the order the volumes are asked in, never whether one is asked: the numbering of Comic Vine
		/// can start anywhere, and its first and last issue can lag behind a volume that is still being published.
		/// </summary>
		private static int FitRank(VolumeInfo volume, BookHints hints)
		{
			List<double> values = hints.Numbers
				.Select(n => double.TryParse(TextMatch.NormalizeNumber(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
				.Where(v => !double.IsNaN(v)).ToList();
			bool range = !double.IsNaN(volume.FirstNumber) && !double.IsNaN(volume.LastNumber);
			if (values.Count == 0 || (!range && volume.IssueCount <= 0))
			{
				return 1;
			}
			foreach (double value in values)
			{
				bool inside = range
					? value >= volume.FirstNumber - 2 && value <= volume.LastNumber + Math.Max(5, (volume.LastNumber - volume.FirstNumber) / 10)
					: value <= volume.IssueCount + Math.Max(10, volume.IssueCount / 5); // no range known: assume it starts at #1
				if (inside)
				{
					return 0;
				}
			}
			return 2;
		}

		private static IEnumerable<string> SearchNames(BookHints hints, bool local)
		{
			List<string> names = new List<string>();
			foreach (string name in hints.SeriesNames.Take(local ? 4 : 2))
			{
				AddName(names, name);
				int cut = name.IndexOfAny(new[] { ':', '-' });
				if (cut > 3)
				{
					AddName(names, name.Substring(0, cut)); // "Batman: The Long Halloween" -> "Batman"
					if (local && cut + 1 < name.Length)
					{
						AddName(names, name.Substring(cut + 1)); // "Civil War - Amazing Spider-Man" -> "Amazing Spider-Man"
					}
				}
				if (local)
				{
					// "Catwoman Officer Down IV" -> "Catwoman Officer Down" -> "Catwoman Officer" -> "Catwoman": the series is usually
					// the start of what the file name says
					string[] words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					for (int count = words.Length - 1; count >= 1; count--)
					{
						AddName(names, string.Join(" ", words.Take(count)));
					}
				}
			}
			return names.Take(local ? 12 : 3);
		}

		private IssueMatch TryVolume(VolumeInfo volume, double nameScore, double volumeScore, BookHints hints, CoverFingerprint cover, string source, CancellationToken token)
		{
			IssueInfo issue = null;
			List<string> numbers = hints.Numbers.Count > 0 ? hints.Numbers : (volume.IssueCount == 1 ? new List<string> { "1" } : new List<string>());
			foreach (string number in numbers)
			{
				issue = comicVine.FindIssue(volume, number, token);
				if (issue != null)
				{
					break;
				}
			}
			if (issue == null)
			{
				return null;
			}
			IssueMatch match = new IssueMatch { Volume = volume, Issue = issue, NameScore = nameScore, Source = source, VolumeScore = volumeScore };
			Bitmap candidateCover = null;
			try
			{
				if (cover != null)
				{
					match.CoverScore = Similarity(cover, issue.ImageUrl, token, out candidateCover);
				}
				if (IsUnwantedCollection(volume, hints))
				{
					match.Penalty = 0.75; // an individual issue is preferred: a collected edition has to be much better to win
				}
				match.Confidence = Combine(volumeScore, match.CoverScore) * match.Penalty;
				if (candidateCover != null)
				{
					CandidateCompared?.Invoke(match, candidateCover);
				}
			}
			finally
			{
				candidateCover?.Dispose();
			}
			return match;
		}

		/// <summary>Compares the cover of the book with the alternative (variant) covers of the issue. One call for the details.</summary>
		private void ImproveWithAlternates(IssueMatch match, CoverFingerprint cover, CancellationToken token)
		{
			comicVine.LoadDetails(match.Issue, token); // the details are needed to write the metadata anyway
			double best = match.CoverScore;
			Bitmap bestCover = null; // the alternative cover that matches best, to show it
			try
			{
				foreach (string url in match.Issue.AltImageUrls.Where(u => u != match.Issue.ImageUrl).Take(6))
				{
					double similarity = Similarity(cover, url, token, out Bitmap image);
					if (similarity > best)
					{
						best = similarity;
						bestCover?.Dispose();
						bestCover = image;
					}
					else
					{
						image?.Dispose();
					}
					if (best >= StrongCoverMatch)
					{
						break;
					}
				}
				if (best > match.CoverScore)
				{
					match.CoverScore = best;
					match.Confidence = Combine(match.VolumeScore, best) * match.Penalty;
					if (bestCover != null)
					{
						CandidateCompared?.Invoke(match, bestCover);
					}
				}
			}
			finally
			{
				bestCover?.Dispose();
			}
		}

		/// <summary>The similarity of the cover with the image at the url, which is returned too (null if it could not be downloaded): the caller disposes it.</summary>
		private double Similarity(CoverFingerprint cover, string url, CancellationToken token, out Bitmap image)
		{
			image = comicVine.DownloadImage(url, token);
			CoverFingerprint other = CoverFingerprint.From(image);
			return other == null ? -1 : cover.Similarity(other);
		}

		/// <summary>The cover decides when it is available; the name alone is never fully sure.</summary>
		private static double Combine(double volumeScore, double coverScore)
		{
			if (coverScore < 0)
			{
				return 0.85 * volumeScore;
			}
			double coverNorm = Math.Max(0, Math.Min(1, (coverScore - CoverFloor) / CoverRange));
			return 0.30 * volumeScore + 0.70 * coverNorm;
		}

		private void ScoreVolume(VolumeInfo volume, BookHints hints, int volumeYearHint, out double name, out double total)
		{
			name = hints.SeriesNames.Count == 0 ? 0 : hints.SeriesNames.Max(n => TextMatch.Similarity(n, volume.Name));

			double year = 0.5;
			if (volumeYearHint > 0 && volume.StartYear > 0)
			{
				int diff = Math.Abs(volume.StartYear - volumeYearHint);
				year = diff == 0 ? 1 : diff == 1 ? 0.7 : 0.2;
			}
			else if (hints.Volume > 1900 && volume.StartYear == hints.Volume)
			{
				year = 1;
			}
			else if (hints.Year > 0 && volume.StartYear > 0)
			{
				int diff = hints.Year - volume.StartYear;
				if (settings.PreferOriginal)
				{
					year = diff >= 0 ? 0.7 : 0.3; // a reprint is never older than the original
				}
				else
				{
					year = diff < 0 ? 0.1 : diff <= Math.Max(2, volume.IssueCount / 10) ? 1 : 0.4;
				}
			}

			double publisher = 0.5;
			if (!settings.PreferOriginal && !string.IsNullOrWhiteSpace(hints.Publisher) && !string.IsNullOrWhiteSpace(volume.Publisher))
			{
				publisher = TextMatch.Similarity(hints.Publisher, volume.Publisher);
			}
			total = 0.7 * name + 0.2 * year + 0.1 * publisher;
		}

		/// <summary>
		/// Editions that are not wanted: the local publishers the user listed (same list as the Python scraper's
		/// IGNORE_PUBLISHER) when the original is preferred, and the years outside the scraper's IGNORE_*_YEAR range.
		/// A series with many issues is never excluded (NEVER_IGNORE_THRESHOLD).
		/// </summary>
		private bool IsExcluded(VolumeInfo volume)
		{
			if (volume.IssueCount >= options.NeverIgnoreThreshold)
			{
				return false;
			}
			if (volume.StartYear > 0 && (volume.StartYear < options.IgnoreBeforeYear || volume.StartYear > options.IgnoreAfterYear))
			{
				return true;
			}
			return options.IgnoredPublishers.Contains((volume.Publisher ?? "").ToLowerInvariant().Trim()); // the unwanted publishers of the user
		}

		/// <summary>A TPB / hardcover / omnibus volume when the book itself does not look like one.</summary>
		private bool IsUnwantedCollection(VolumeInfo volume, BookHints hints)
		{
			if (!settings.PreferIndividualIssues || !collectionWords.IsMatch(volume.Name ?? ""))
			{
				return false;
			}
			return !hints.SeriesNames.Any(n => collectionWords.IsMatch(n)) && !collectionWords.IsMatch(hints.FileName ?? "");
		}

		// ---- Series remembered from earlier books ------------------------------------------------------------

		/// <summary>The key of a series name as it is written in the library: the name, and the volume if the file says so.</summary>
		private static IEnumerable<string> Keys(BookHints hints) =>
			hints.SeriesNames.Select(n => TextMatch.Normalize(n) + (hints.Volume > 1900 ? "|" + hints.Volume : "")).Where(k => k.Length > 0).Distinct();

		/// <summary>
		/// A book was matched with this volume (by the search, by Claude or by hand in the review). What that says about the series is used
		/// for the books that come after: its series name goes to the volume, and the volume is a candidate for related names.
		/// </summary>
		public void Accept(BookHints hints, VolumeInfo volume)
		{
			acceptedVolumes.RemoveAll(v => v.Id == volume.Id);
			acceptedVolumes.Insert(0, volume);
			Remember(hints, volume);
		}

		/// <summary>
		/// Looks again at a book with what has been learned since: the volume remembered for its series name and the volumes that were
		/// accepted for related names. No search, so it costs nothing: it is for the books that are still waiting for a review.
		/// Returns the best match (the caller checks the threshold), or null.
		/// </summary>
		public IssueMatch Reconsider(BookHints hints, CoverFingerprint cover, CancellationToken token)
		{
			IssueMatch best = TryRemembered(hints, cover, "series", token);
			foreach (VolumeInfo known in acceptedVolumes.Where(v => !IsExcluded(v) && NameRelated(v, hints) && (best == null || v.Id != best.Volume.Id)).Take(3).ToList())
			{
				if (best != null && best.Confidence >= settings.AcceptThreshold)
				{
					break;
				}
				ScoreVolume(known, hints, 0, out double name, out double total);
				IssueMatch match = TryVolume(known, name, total, hints, cover, "series", token);
				if (match != null && (best == null || match.Confidence > best.Confidence))
				{
					best = match;
				}
			}
			return best;
		}

		/// <summary>The volume that the review can offer for a book: the one remembered for its series name, or one accepted for a related name.</summary>
		public VolumeInfo SuggestedVolumeFor(BookHints hints) =>
			RememberedVolume(hints) ?? acceptedVolumes.FirstOrDefault(v => !IsExcluded(v) && NameRelated(v, hints));

		/// <summary>The volume remembered for the series name of the book, or null.</summary>
		public VolumeInfo RememberedVolume(BookHints hints)
		{
			foreach (string key in Keys(hints))
			{
				if (choices.TryGet(key, out VolumeInfo volume))
				{
					return volume;
				}
			}
			return null;
		}

		/// <summary>The book was matched with this volume (by the search, Claude or the user): its series name goes there from now on.</summary>
		public void Remember(BookHints hints, VolumeInfo volume)
		{
			foreach (string key in Keys(hints))
			{
				choices.Set(key, volume);
			}
		}

		/// <summary>
		/// Looks for the issue in the volume remembered for the series name of the book. The name of the library ("abc") says nothing
		/// about the name of the volume ("Batman"), so the choice itself counts as the name evidence. Like the Python scraper,
		/// a volume that does not have the number is forgotten: it was probably the wrong series. Null when there is no choice or
		/// it does not fit.
		/// </summary>
		public IssueMatch TryRemembered(BookHints hints, CoverFingerprint cover, string source, CancellationToken token)
		{
			foreach (string key in Keys(hints))
			{
				if (!choices.TryGet(key, out VolumeInfo volume) || IsExcluded(volume))
				{
					continue;
				}
				ScoreVolume(volume, hints, 0, out double name, out double total);
				IssueMatch match = TryVolume(volume, 1.0, total + 0.7 * (1.0 - name), hints, cover, source, token);
				if (match == null)
				{
					choices.Remove(key);
					continue;
				}
				if (cover == null)
				{
					match.Confidence = Math.Max(match.Confidence, settings.AcceptThreshold); // nothing to compare: the earlier choice stands
				}
				return match;
			}
			return null;
		}

		/// <summary>The name of the volume is the series in the hints, or the start of it ("Catwoman Year Two" for "Catwoman").</summary>
		private static bool NameRelated(VolumeInfo volume, BookHints hints)
		{
			string v = TextMatch.Normalize(volume.Name);
			if (v.Length == 0)
			{
				return false;
			}
			foreach (string name in hints.SeriesNames)
			{
				string n = TextMatch.Normalize(name);
				if (n == v || n.StartsWith(v + " ") || v.StartsWith(n + " ") || TextMatch.Similarity(n, v) >= 0.8)
				{
					return true;
				}
			}
			return false;
		}
	}
}
