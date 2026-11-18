using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace ComicVineAI
{
	/// <summary>
	/// Goes through the selected books one by one. Books that are identified with enough certainty are tagged
	/// right away; the others are left for the end and shown in the manual search form.
	/// The search runs on a worker thread; everything that touches ComicRack runs on the UI thread.
	/// Cancel stops the search but not the work already done: the books that were not sure enough (and the one that was
	/// in progress) are still offered for manual review.
	/// </summary>
	public sealed class BatchRunner
	{
		private const int CoverHeight = 320;

		private readonly IWin32Window owner;
		private readonly IApplication app;
		private readonly PluginSettings settings;
		private readonly string pluginFolder;
		private readonly Action<string> consoleLog;

		private ProgressForm form;
		private ComicVineClient comicVine;
		private WriteOptions options;
		private LocalDb localDb;
		private readonly HashSet<IdentifyResult> taggedInReview = new HashSet<IdentifyResult>(); // so a book that is tagged twice is counted once
		private readonly Dictionary<IdentifyResult, CoverFingerprint> fingerprints = new Dictionary<IdentifyResult, CoverFingerprint>(); // each cover is read once
		private Identifier identifier;
		private int currentIndex;
		private int tagged, skipped, review;

		public BatchRunner(IWin32Window owner, IApplication app, PluginSettings settings, string pluginFolder, Action<string> consoleLog)
		{
			this.owner = owner;
			this.app = app;
			this.settings = settings;
			this.pluginFolder = pluginFolder;
			this.consoleLog = consoleLog;
		}

		/// <summary>Call on the UI thread. Returns when the progress window is closed.</summary>
		public void Run(ComicBook[] books)
		{
			form = new ProgressForm(settings.AcceptThreshold);
			options = WriteOptions.Load(settings);
			DiskCache cache = new DiskCache(Path.Combine(pluginFolder, "cache"), settings.CacheDays);
			localDb = LocalDb.TryOpen(settings.LocalDbPath, out string dbError);
			comicVine = new ComicVineClient(settings.ComicVineApiKey, cache, new UsageTracker(pluginFolder), Status, localDb);
			form.Shown += (s, e) =>
			{
				if (localDb != null)
				{
					Log(Loc.T("batch.localDb", localDb.Volumes.ToString("N0"), localDb.Issues.ToString("N0"), localDb.SyncDate));
				}
				else if (!string.IsNullOrWhiteSpace(settings.LocalDbPath))
				{
					Log(Loc.T("batch.localDbFailed", dbError)); // the API is used, as if no database had been chosen
				}
				Task.Run(() => Work(books));
			};
			form.ShowDialog(owner);
			form.Dispose();
			localDb?.Dispose();
		}

		/// <summary>A step of the current book: goes to the log and to the tile of the book.</summary>
		private Action<string> statusSink; // where the progress of a new search from the review window goes

		private void Status(string message)
		{
			Log(message);
			form?.SetStatus(currentIndex, message);
			statusSink?.Invoke(message);
		}

		private void Log(string message)
		{
			form?.Log(message);
			consoleLog?.Invoke(message);
		}

		private void Counters()
		{
			form.SetCounters(Loc.T("batch.counters", tagged, review, skipped, comicVine.RunSummary(), comicVine.CacheHits, comicVine.Usage.Summary()));
		}

		private void Work(ComicBook[] books)
		{
			CancellationToken token = form.Token;
			Log("Write options - " + options);
			identifier = new Identifier(settings, options, comicVine, new SeriesChoices(pluginFolder), new ClaudeCache(pluginFolder), GetCover, Status);
			identifier.CandidateCompared = OnCandidate;
			List<IdentifyResult> deferred = new List<IdentifyResult>();
			int processed = 0;
			string summary;
			try
			{
				for (int i = 0; i < books.Length; i++)
				{
					if (token.IsCancellationRequested)
					{
						break;
					}
					ComicBook book = books[i];
					string name = Describe(book);
					currentIndex = i;
					shownIndex = -1;
					comicVine.Context = name;
					form.SetProgress(i, books.Length, name);
					form.BeginBook(i, name);
					try
					{
						IdentifyResult result = identifier.Identify(book, token);
						result.Index = i;
						if (result.Skipped)
						{
							skipped++;
							Log(Loc.T("batch.skipped", name, result.Message));
							form.EndBook(i, Outcome.Skipped, "", Loc.T("batch.skippedStatus", result.Message), -1, null);
						}
						else if (result.Accepted)
						{
							// once it is accepted the book is finished even if the user cancels now
							Apply(book, result.Best, CancellationToken.None);
							tagged++;
							Log(Loc.T("batch.ok", name, result.Best));
							form.EndBook(i, Outcome.Tagged, Title(result.Best), Loc.T("batch.tagged", result.Best.Source, ClaudeNote(result)), result.Best.Confidence, IssueCover(result.Best, CancellationToken.None));
						}
						else
						{
							deferred.Add(result);
							review++;
							Log(Loc.T("batch.notSure", name, result.Message));
							string guess = result.Best == null ? Loc.T("batch.noCandidate") : Loc.T("batch.bestGuess", Title(result.Best));
							form.EndBook(i, Outcome.Review, guess, Loc.T("batch.needsHelp", ClaudeNote(result)), result.Best?.Confidence ?? -1, IssueCover(result.Best, CancellationToken.None));
						}
						processed = i + 1;
					}
					catch (ComicVineException e) when (e.StatusCode == 100)
					{
						Log(e.Message); // no (valid) API key: nothing can be searched, nothing to review
						throw;
					}
					catch (ComicVineException e) when (e.StatusCode == 420)
					{
						// Comic Vine does not accept more calls for now: stop searching; the book is left for the manual review
						processed = i + 1;
						AddForReview(deferred, book, i, e.Message, Outcome.Review, Loc.T("batch.interrupted"));
						Log(Loc.T("batch.limitStop", name));
						break;
					}
					catch (OperationCanceledException)
					{
						// the user stopped the search in the middle of this book: it can be finished by hand
						processed = i + 1;
						AddForReview(deferred, book, i, Loc.T("batch.interrupted"), Outcome.Review, Loc.T("batch.interruptedStatus"));
						Counters();
						break;
					}
					catch (Exception e)
					{
						processed = i + 1;
						Log(Loc.T("batch.error", name, e.Message));
						AddForReview(deferred, book, i, Loc.T("batch.errorTitle"), Outcome.Error, e.Message);
					}
					Counters();
				}

				int notProcessed = books.Length - processed;
				form.SetProgress(processed, books.Length, "");
				if (notProcessed > 0)
				{
					Log(Loc.T("batch.stoppedByUser", notProcessed));
				}

				// The books below the threshold are offered for review in every case, also after Cancel: that work is not lost.
				if (deferred.Count > 0)
				{
					Log(Loc.T("batch.needHelp", deferred.Count));
					form.Invoke(new Action(() => ManualPhase(deferred)));
				}
				summary = notProcessed > 0
					? Loc.T("batch.summaryStopped", tagged, notProcessed, comicVine.RunSummary(), comicVine.CacheHits)
					: Loc.T("batch.summaryDone", tagged, skipped, review, comicVine.RunSummary(), comicVine.CacheHits);
			}
			catch (ComicVineException e)
			{
				summary = Loc.T("batch.summaryFatal", e.Message);
			}
			catch (Exception e)
			{
				Log(Loc.T("batch.unexpected", e));
				summary = Loc.T("batch.summaryError", e.Message);
			}
			Log(summary);
			Counters();
			form.Finish(summary);
		}

		private int shownIndex = -1;
		private double shownConfidence;

		/// <summary>A candidate was compared: if it is the best so far for this book, its cover goes to the window now.</summary>
		private void OnCandidate(IssueMatch match, Bitmap image)
		{
			if (match.Volume == null || match.Issue == null || (shownIndex == currentIndex && match.Confidence <= shownConfidence))
			{
				return;
			}
			shownIndex = currentIndex;
			shownConfidence = match.Confidence;
			form.SetCandidate(currentIndex, Title(match), match.Confidence, ImageUtil.Encode(image, CoverHeight));
		}

		private void AddForReview(List<IdentifyResult> deferred, ComicBook book, int index, string message, Outcome outcome, string status)
		{
			deferred.Add(new IdentifyResult { Book = book, Index = index, Hints = Identifier.BuildHints(book), Message = message });
			review++;
			form.EndBook(index, outcome, outcome == Outcome.Error ? Loc.T("batch.errorTitle") : message, status, -1, null);
		}

		/// <summary>A publisher of the unwanted list (its series are skipped), also when it was added in this run.</summary>
		private bool IsUnwanted(string publisher) =>
			!string.IsNullOrWhiteSpace(publisher) && options.IgnoredPublishers.Contains(publisher.Trim().ToLowerInvariant());

		/// <summary>Adds a publisher to the unwanted list (or removes it): it takes effect at once and is saved in the settings.</summary>
		private void SetUnwanted(string publisher, bool add)
		{
			string name = (publisher ?? "").Trim();
			if (name.Length == 0)
			{
				return;
			}
			string lower = name.ToLowerInvariant();
			List<string> lines = (settings.LocalPublishers ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
			if (add)
			{
				options.IgnoredPublishers.Add(lower);
				if (!lines.Any(l => l.ToLowerInvariant() == lower))
				{
					lines.Add(name);
				}
			}
			else
			{
				options.IgnoredPublishers.Remove(lower);
				lines.RemoveAll(l => l.ToLowerInvariant() == lower);
			}
			settings.LocalPublishers = string.Join("\r\n", lines);
			try
			{
				settings.Save(pluginFolder);
			}
			catch (IOException)
			{
				// the list works for this run anyway
			}
		}

		/// <summary>Runs on the UI thread. The user can go back to the previous book of the review.</summary>
		private void ManualPhase(List<IdentifyResult> deferred)
		{
			bool goingBack = false;
			int index = 0;
			while (index < deferred.Count)
			{
				IdentifyResult result = deferred[index];
				ComicBook book = result.Book;
				form.SetProgress(index, deferred.Count, Loc.T("batch.manualProgress", Describe(book)));
				if (!goingBack && (taggedInReview.Contains(result) || TryAutomatically(result)))
				{
					index++; // already tagged (the re-analysis after an earlier choice found it), or tagged now: the series is known and the cover agrees
					continue;
				}
				goingBack = false;
				using (ManualSearchForm manual = new ManualSearchForm(Describe(book), result, page => SafePage(book, page), book.PageCount, comicVine, settings))
				{
					manual.CanGoBack = index > 0;
					manual.IsUnwantedPublisher = IsUnwanted;
					manual.SetUnwantedPublisher = SetUnwanted;
					manual.SuggestedVolume = identifier.SuggestedVolumeFor(result.Hints);
					manual.Rescan = (page, progress, token) =>
					{
						statusSink = progress;
						try
						{
							comicVine.Context = Describe(book);
							return identifier.Identify(book, token, page);
						}
						finally
						{
							statusSink = null;
						}
					};
					DialogResult answer = manual.ShowDialog(form);
					if (answer == DialogResult.Retry)
					{
						index = Math.Max(0, index - 1); // back to the previous book, to change what was chosen for it
						goingBack = true; // and it is shown even if it was tagged
						continue;
					}
					if (answer == DialogResult.OK && manual.SelectedIssue != null)
					{
						try
						{
							IssueMatch match = new IssueMatch { Volume = manual.SelectedVolume, Issue = manual.SelectedIssue, Source = "manual" };
							comicVine.LoadDetails(match.Issue, CancellationToken.None);
							MetadataWriter.Write(book, match.Volume, match.Issue, options);
							identifier.Accept(result.Hints, match.Volume); // what this choice tells about the series is used for the books that are left
							if (taggedInReview.Add(result))
							{
								tagged++;
								review--;
							}
							Log(Loc.T("batch.taggedManually", Describe(book), match.Volume.Name, match.Issue.Number));
							form.EndBook(result.Index, Outcome.Tagged, Title(match), Loc.T("batch.taggedManuallyStatus"), -1, IssueCover(match, CancellationToken.None));
							ReanalyzeRemaining(deferred, index);
						}
						catch (Exception e)
						{
							Log(Loc.T("batch.couldNotTag", Describe(book), e.Message));
						}
					}
					else
					{
						Log(Loc.T("batch.skippedManual", Describe(book)));
					}
					Counters();
					if (manual.CancelAll)
					{
						break;
					}
				}
				index++;
			}
		}

		/// <summary>
		/// After a choice made by hand, the books that are still waiting are looked at again with what it taught: the series name it
		/// matched (and the related ones). The ones that now match, with the cover agreeing, are tagged and are not asked any more.
		/// </summary>
		private void ReanalyzeRemaining(List<IdentifyResult> deferred, int current)
		{
			int found = 0;
			for (int j = current + 1; j < deferred.Count; j++)
			{
				if (!taggedInReview.Contains(deferred[j]) && TryAutomatically(deferred[j]))
				{
					found++;
				}
			}
			if (found > 0)
			{
				Log(Loc.T("batch.reanalyzed", found));
			}
		}

		/// <summary>The fingerprint of the first page of a book of the review, read once (null when covers are not compared).</summary>
		private CoverFingerprint FingerprintOf(IdentifyResult result)
		{
			if (!settings.UseCoverComparison)
			{
				return null;
			}
			if (!fingerprints.TryGetValue(result, out CoverFingerprint fingerprint))
			{
				using (Bitmap cover = SafePage(result.Book, 0))
				{
					fingerprint = CoverFingerprint.From(cover);
				}
				fingerprints[result] = fingerprint;
			}
			return fingerprint;
		}

		/// <summary>
		/// A book that was left for the review while what is known about its series has changed: an earlier book with the same series
		/// name (or a related one) was matched by hand or by the search. It is tagged with that volume if the issue is there and the
		/// cover agrees. Costs no search. Runs on the UI thread.
		/// </summary>
		private bool TryAutomatically(IdentifyResult result)
		{
			try
			{
				ComicBook book = result.Book;
				if (identifier.SuggestedVolumeFor(result.Hints) == null)
				{
					return false; // nothing is known about this series (yet): nothing to look at, and no cover to read
				}
				comicVine.Context = Describe(book);
				IssueMatch match = identifier.Reconsider(result.Hints, FingerprintOf(result), CancellationToken.None);
				if (match == null || match.Confidence < settings.AcceptThreshold)
				{
					return false;
				}
				comicVine.LoadDetails(match.Issue, CancellationToken.None);
				MetadataWriter.Write(book, match.Volume, match.Issue, options);
				if (taggedInReview.Add(result))
				{
					tagged++;
					review--;
				}
				Log(Loc.T("batch.ok", Describe(book), match));
				form.EndBook(result.Index, Outcome.Tagged, Title(match), Loc.T("batch.tagged", match.Source, ""), match.Confidence, IssueCover(match, CancellationToken.None));
				Counters();
				return true;
			}
			catch (Exception)
			{
				return false; // anything that goes wrong here only means that the user is asked, as before
			}
		}

		/// <summary>Loads the details on the worker thread, writes on the UI thread.</summary>
		private void Apply(ComicBook book, IssueMatch match, CancellationToken token)
		{
			comicVine.LoadDetails(match.Issue, token);
			form.Invoke(new Action(() => MetadataWriter.Write(book, match.Volume, match.Issue, options)));
		}

		private byte[] IssueCover(IssueMatch match, CancellationToken token)
		{
			if (match?.Issue == null)
			{
				return null;
			}
			using (Bitmap image = comicVine.DownloadImage(match.Issue.ImageUrl, token)) // already cached from the comparison
			{
				return ImageUtil.Encode(image, CoverHeight);
			}
		}

		private static string Title(IssueMatch match) =>
			$"{match.Volume.Name} ({match.Volume.StartYear}) #{match.Issue.Number}" + (string.IsNullOrWhiteSpace(match.Volume.Publisher) ? "" : " - " + match.Volume.Publisher);

		private static string ClaudeNote(IdentifyResult result) =>
			result.Claude != null && result.Claude.IsUsable ? Loc.T("batch.claudeNote", result.Claude.Series, result.Claude.Number, result.Claude.Confidence.ToString("P0")) : "";

		private Bitmap GetCover(ComicBook book)
		{
			Bitmap cover = (Bitmap)form.Invoke(new Func<Bitmap>(() => SafeCover(book)));
			if (cover != null)
			{
				form.SetBookCover(currentIndex, ImageUtil.Encode(cover, CoverHeight));
			}
			return cover;
		}

		private Bitmap SafeCover(ComicBook book) => SafePage(book, 0);

		/// <summary>A page of the book (the first one without its back cover), or null if it cannot be read.</summary>
		private Bitmap SafePage(ComicBook book, int page)
		{
			try
			{
				if (!book.IsLinked)
				{
					return null;
				}
				Bitmap image = app.GetComicPage(book, page);
				return page == 0 ? CoverFingerprint.StripBackCover(image) : image;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string Describe(ComicBook book) =>
			string.IsNullOrEmpty(book.FileNameWithExtension) ? book.Series + " #" + book.Number : book.FileNameWithExtension;
	}
}
