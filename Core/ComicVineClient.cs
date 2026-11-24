using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ComicVineAI
{
	public class ComicVineException : Exception
	{
		public int StatusCode { get; }

		public ComicVineException(string message, int statusCode = 0) : base(message)
		{
			StatusCode = statusCode;
		}
	}

	/// <summary>
	/// Comic Vine API client. Comic Vine allows 200 requests per hour for each resource, so every answer goes through
	/// a memory and a disk cache, only the needed fields are requested, a whole volume is listed with one call instead
	/// of one call per issue number, and the calls are counted (see <see cref="UsageTracker"/>).
	/// </summary>
	public sealed class ComicVineClient
	{
		/// <summary>The address of the API. Only the tests change it.</summary>
		public static string BaseUrl { get; set; } = "https://comicvine.gamespot.com/api/";
		private const int MinimumRequestSpacingMs = 1100;
		private const int PageSize = 100; // the most that Comic Vine returns in one call
		private const int MaxPages = 10;
		private const string IssueFields = "id,name,issue_number,cover_date,store_date,image,volume,site_detail_url";

		private static readonly object throttle = new object();
		private static DateTime lastRequest = DateTime.MinValue;
		private static readonly int[] serverLimitWaits = { 60, 120, 300, 300, 300, 300, 600, 600 };

		private readonly string apiKey;
		private readonly DiskCache cache;
		private readonly UsageTracker usage;
		private readonly LocalDb localDb;
		private int localQueries;
		private readonly Dictionary<string, string> memory = new Dictionary<string, string>();
		private readonly Dictionary<int, List<IssueInfo>> volumeIssues = new Dictionary<int, List<IssueInfo>>();
		private readonly Dictionary<int, int> lookups = new Dictionary<int, int>();
		private readonly Dictionary<string, int> runCalls = new Dictionary<string, int>();
		private readonly Action<string> log;

		/// <summary>Calls that really went to the Comic Vine API (not the cache).</summary>
		public int ApiRequests { get; private set; }

		/// <summary>Calls answered by the cache.</summary>
		public int CacheHits { get; private set; }

		/// <summary>The book that is being worked on, for calls.log.</summary>
		public string Context { get; set; } = "";

		/// <summary>The stage (metadata, claude...) that is making the calls, for calls.log.</summary>
		public string Stage { get; set; } = "";

		public UsageTracker Usage => usage;

		/// <summary>True when the local database is used: its queries are free, so the search can be much more thorough.</summary>
		public bool UsesLocalDb => localDb != null;

		public ComicVineClient(string apiKey, DiskCache cache, UsageTracker usage, Action<string> log, LocalDb localDb = null)
		{
			this.localDb = localDb;
			this.apiKey = apiKey;
			this.cache = cache;
			this.usage = usage;
			this.log = log ?? (_ => { });
			ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
		}

		/// <summary>The calls of this run per resource, for example "search 2, issues 9, issue 6".</summary>
		public string RunSummary()
		{
			lock (runCalls)
			{
				string api = runCalls.Count == 0 ? "none" : string.Join(", ", runCalls.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value));
				return localDb == null ? api : "local db " + localQueries + ", " + (runCalls.Count == 0 ? "api none" : "api " + api);
			}
		}

		// ---- API ---------------------------------------------------------------------------------------------

		private void CountLocal()
		{
			lock (runCalls)
			{
				localQueries++;
			}
		}

		public List<VolumeInfo> SearchVolumes(string name, CancellationToken token)
		{
			if (localDb != null)
			{
				List<VolumeInfo> local = localDb.SearchVolumes(name, 120);
				CountLocal();
				if (local.Count > 0 || string.IsNullOrWhiteSpace(apiKey) || usage.SecondsUntilBudget("search") > 0)
				{
					return local;
				}
				// nothing like it in the snapshot: it may be a volume that Comic Vine got after it, so the API is asked
			}
			string json = Get("search/", "resources=volume&limit=30&field_list=id,name,start_year,publisher,count_of_issues,image,first_issue,last_issue&query=" + Uri.EscapeDataString(name), token);
			return Json.Arr(Json.Get(Json.ParseObject(json), "results")).Select(o => ParseVolume(Json.Obj(o))).Where(v => v != null).ToList();
		}

		public VolumeInfo GetVolume(int id, CancellationToken token)
		{
			if (localDb != null)
			{
				VolumeInfo local = localDb.GetVolume(id);
				CountLocal();
				if (local != null)
				{
					return local;
				}
			}
			string json = Get("volume/4050-" + id + "/", "field_list=id,name,start_year,publisher,count_of_issues,image,first_issue,last_issue", token);
			return ParseVolume(Json.Obj(Json.Get(Json.ParseObject(json), "results")));
		}

		/// <summary>
		/// Finds an issue of a volume by number; null when the volume has no such issue.
		/// A volume of up to 100 issues is listed once with a single call (kept in the cache), which answers this and
		/// every later question about the volume: other numbers, other books of the series and numbers that do not
		/// exist cost nothing. A bigger volume is asked one number at a time, until the same volume is asked often
		/// enough for the whole list to be cheaper.
		/// </summary>
		/// <summary>
		/// A volume that was remembered from an earlier run (or from before the database was chosen) does not say where it came from:
		/// if the database has it, it is the database that answers, not the API.
		/// </summary>
		private void MarkLocal(VolumeInfo volume)
		{
			if (localDb != null && !volume.Local && localDb.HasVolume(volume.Id))
			{
				volume.Local = true;
			}
		}

		public IssueInfo FindIssue(VolumeInfo volume, string number, CancellationToken token)
		{
			MarkLocal(volume);
			string wanted = TextMatch.NormalizeNumber(number);
			if (volume.Local && localDb != null)
			{
				List<IssueInfo> issues = ListIssues(volume, token);
				IssueInfo local = issues.FirstOrDefault(i => TextMatch.NormalizeNumber(i.Number) == wanted);
				if (local != null || !localDb.MayBeNewerThanSnapshot(volume, issues, number))
				{
					return local; // found, or really not in this volume
				}
				// beyond what the snapshot has of a volume that was still going: the API is asked, but only if it can answer at once; if it
				// cannot (no key, limit reached) the local answer stands
				try
				{
					return usage.SecondsUntilBudget("issues") > 0 || string.IsNullOrWhiteSpace(apiKey) ? null : FindIssueByNumber(volume.Id, wanted, token);
				}
				catch (ComicVineException)
				{
					return null;
				}
			}
			if (!volumeIssues.TryGetValue(volume.Id, out List<IssueInfo> all))
			{
				int pages = (volume.IssueCount + PageSize - 1) / PageSize;
				lookups.TryGetValue(volume.Id, out int asked);
				bool list = volume.IssueCount > 0 && (pages <= 1 || (asked >= 3 && pages <= 8));
				if (!list)
				{
					lookups[volume.Id] = asked + 1;
					return FindIssueByNumber(volume.Id, wanted, token);
				}
				all = GetVolumeIssues(volume.Id, token);
				volumeIssues[volume.Id] = all;
			}
			return all.FirstOrDefault(i => TextMatch.NormalizeNumber(i.Number) == wanted);
		}

		/// <summary>All the issues of a volume: from the local database, or from the API (one call per 100 issues).</summary>
		public List<IssueInfo> ListIssues(VolumeInfo volume, CancellationToken token)
		{
			MarkLocal(volume);
			lock (volumeIssues)
			{
				if (volumeIssues.TryGetValue(volume.Id, out List<IssueInfo> cached))
				{
					return cached;
				}
			}
			List<IssueInfo> all;
			if (volume.Local && localDb != null)
			{
				all = localDb.ListIssues(volume);
				CountLocal();
			}
			else
			{
				all = GetVolumeIssues(volume.Id, token);
			}
			lock (volumeIssues)
			{
				volumeIssues[volume.Id] = all;
			}
			return all;
		}

		private List<IssueInfo> GetVolumeIssues(int volumeId, CancellationToken token)
		{
			List<IssueInfo> all = new List<IssueInfo>();
			int offset = 0;
			int total = int.MaxValue;
			for (int page = 0; offset < total && page < MaxPages; page++)
			{
				string json = Get("issues/", $"field_list={IssueFields}&filter=volume:{volumeId}&limit={PageSize}&offset={offset}", token);
				Dictionary<string, object> root = Json.ParseObject(json);
				total = Json.Int(root, "number_of_total_results");
				List<IssueInfo> issues = Json.Arr(Json.Get(root, "results")).Select(o => ParseIssue(Json.Obj(o))).ToList();
				if (issues.Count == 0)
				{
					break;
				}
				all.AddRange(issues);
				offset += issues.Count;
			}
			return all;
		}

		private IssueInfo FindIssueByNumber(int volumeId, string wanted, CancellationToken token)
		{
			string json = Get("issues/", $"field_list={IssueFields}&filter=volume:{volumeId},issue_number:{Uri.EscapeDataString(wanted)}", token);
			return Json.Arr(Json.Get(Json.ParseObject(json), "results")).Select(o => ParseIssue(Json.Obj(o))).FirstOrDefault(i => TextMatch.NormalizeNumber(i.Number) == wanted);
		}

		/// <summary>Loads the credits, description and alternative covers of an issue.</summary>
		public void LoadDetails(IssueInfo issue, CancellationToken token)
		{
			if (issue.HasDetails)
			{
				return;
			}
			if (issue.Local && localDb != null && localDb.LoadDetails(issue))
			{
				CountLocal();
				return;
			}
			string json = Get("issue/4000-" + issue.Id + "/", "field_list=id,name,issue_number,cover_date,store_date,description,volume,person_credits,character_credits,team_credits,location_credits,story_arc_credits,site_detail_url,image,associated_images", token);
			Dictionary<string, object> r = Json.Obj(Json.Get(Json.ParseObject(json), "results"));
			IssueInfo detail = ParseIssue(r);
			issue.Name = detail.Name;
			issue.CoverDate = detail.CoverDate;
			issue.StoreDate = detail.StoreDate;
			issue.SiteUrl = detail.SiteUrl;
			issue.ImageUrl = string.IsNullOrEmpty(detail.ImageUrl) ? issue.ImageUrl : detail.ImageUrl;
			issue.Description = Json.Str(r, "description"); // raw HTML, MetadataWriter cleans it like the scraper
			issue.AltImageUrls = Json.Arr(Json.Get(r, "associated_images")).Select(o => Json.Str(Json.Obj(o), "original_url")).Where(u => u.Length > 0).ToList();
			issue.Persons = Json.Arr(Json.Get(r, "person_credits")).Select(o => new Credit { Name = Json.Str(Json.Obj(o), "name"), Role = Json.Str(Json.Obj(o), "role") }).Where(c => c.Name.Length > 0).ToList();
			issue.Characters = Names(r, "character_credits");
			issue.Teams = Names(r, "team_credits");
			issue.Locations = Names(r, "location_credits");
			issue.StoryArcs = Names(r, "story_arc_credits");
			issue.HasDetails = true;
		}

		/// <summary>Fingerprints and similarities of covers already worked out (see CoverStore). Lives in the same folder as the images.</summary>
		public CoverStore Covers => covers ?? (covers = new CoverStore(cache));
		private CoverStore covers;

		/// <summary>
		/// The alternative covers of an issue when they cost nothing: the details are already loaded, or they come from the local database.
		/// False when only an API call would give them (the caller decides if it is worth it).
		/// </summary>
		public bool LoadAlternatesFree(IssueInfo issue)
		{
			if (issue.HasDetails)
			{
				return true;
			}
			if (issue.Local && localDb != null && localDb.LoadDetails(issue))
			{
				CountLocal();
				return true;
			}
			return false;
		}

		/// <summary>Puts the image on disk without decoding it, so that several can be fetched at once. Failures are ignored here: DownloadImage reports them if the image is really needed.</summary>
		public void PrefetchImage(string url, CancellationToken token)
		{
			if (string.IsNullOrEmpty(url) || cache.HasImage(url))
			{
				return;
			}
			try
			{
				token.ThrowIfCancellationRequested();
				HttpWebRequest request = CreateRequest(url);
				request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 6); // the default is 2 per host
				using (WebResponse response = request.GetResponse())
				using (Stream stream = response.GetResponseStream())
				using (MemoryStream copy = new MemoryStream())
				{
					stream.CopyTo(copy);
					cache.PutBytes(url, copy.ToArray());
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
			}
		}

		/// <summary>Downloads an image (cached on disk forever, they do not change). Null on failure. Images are not API calls.</summary>
		public Bitmap DownloadImage(string url, CancellationToken token)
		{
			if (string.IsNullOrEmpty(url))
			{
				return null;
			}
			try
			{
				if (!cache.TryGetBytes(url, out byte[] bytes))
				{
					token.ThrowIfCancellationRequested();
					HttpWebRequest request = CreateRequest(url);
					using (WebResponse response = request.GetResponse())
					using (Stream stream = response.GetResponseStream())
					using (MemoryStream copy = new MemoryStream())
					{
						stream.CopyTo(copy);
						bytes = copy.ToArray();
					}
					cache.PutBytes(url, bytes);
				}
				using (MemoryStream ms = new MemoryStream(bytes))
				using (Image image = Image.FromStream(ms))
				{
					return new Bitmap(image);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				if (url.Contains("/uploads/scale_medium/"))
				{
					return DownloadImage(url.Replace("/uploads/scale_medium/", "/uploads/original/"), token); // the dump only knows the original
				}
				log(Loc.T("cv.downloadFailed", url, e.Message));
				return null;
			}
		}

		// ---- Transport ---------------------------------------------------------------------------------------

		private string Get(string path, string query, CancellationToken token)
		{
			string cacheKey = path + "?" + query;
			lock (memory)
			{
				if (memory.TryGetValue(cacheKey, out string cached))
				{
					CacheHits++;
					return cached;
				}
			}
			if (cache.TryGetText(cacheKey, out string fromDisk))
			{
				CacheHits++;
				lock (memory)
				{
					memory[cacheKey] = fromDisk;
				}
				return fromDisk;
			}
			if (string.IsNullOrWhiteSpace(apiKey))
			{
				throw new ComicVineException(Loc.T("cv.noKey"), 100);
			}

			string resource = path.Split('/')[0]; // search, issues, issue, volume: each has its own limit of 200 per hour
			string url = BaseUrl + path + "?api_key=" + Uri.EscapeDataString(apiKey) + "&format=json&" + query;
			int serverRefusals = 0;
			int networkRetries = 0;
			while (true)
			{
				token.ThrowIfCancellationRequested();
				WaitForBudget(resource, token);
				WaitForTurn(token);
				string body;
				try
				{
					HttpWebRequest request = CreateRequest(url);
					using (WebResponse response = request.GetResponse())
					using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
					{
						body = reader.ReadToEnd();
					}
				}
				catch (WebException e) when (e.Response is HttpWebResponse http && ((int)http.StatusCode == 420 || (int)http.StatusCode == 429))
				{
					// the server counts more than we do (another tool with the same key, an earlier session): wait for the window
					if (serverRefusals >= serverLimitWaits.Length)
					{
						throw new ComicVineException(Loc.T("cv.refusing", resource, UsageTracker.Limit), 420);
					}
					int wait = serverLimitWaits[serverRefusals++];
					log(Loc.T("cv.refused", resource, usage.Summary(), wait / 60));
					Sleep(wait * 1000, token);
					continue;
				}
				catch (WebException e) when (networkRetries < 2 && e.Response == null)
				{
					if (e.Status == WebExceptionStatus.Timeout)
					{
						CountCall(resource, path, query); // the server may have answered after we gave up: it counts the call
					}
					Sleep(2000 * ++networkRetries, token);
					continue;
				}
				catch (WebException e) when (e.Response != null)
				{
					CountCall(resource, path, query); // an HTTP error (500...) was a call too
					throw;
				}

				CountCall(resource, path, query);

				Dictionary<string, object> root = Json.ParseObject(body);
				int status = Json.Int(root, "status_code");
				if (status != 1)
				{
					throw new ComicVineException(Loc.T("cv.error", Json.Str(root, "error")), status);
				}
				cache.PutText(cacheKey, body);
				lock (memory)
				{
					memory[cacheKey] = body;
				}
				return body;
			}
		}

		private void CountCall(string resource, string path, string query)
		{
			ApiRequests++;
			lock (runCalls)
			{
				runCalls[resource] = (runCalls.TryGetValue(resource, out int n) ? n : 0) + 1;
			}
			usage.Record(resource, path + "?" + Regex.Replace(query, @"field_list=[^&]*&?", ""), $"{Context} / {Stage}");
		}

		/// <summary>Waits (cancellable) while the last hour is at the limit of the resource, instead of getting errors.</summary>
		private void WaitForBudget(string resource, CancellationToken token)
		{
			DateTime lastNotice = DateTime.MinValue;
			int wait;
			while ((wait = usage.SecondsUntilBudget(resource)) > 0)
			{
				if (DateTime.UtcNow - lastNotice > TimeSpan.FromSeconds(60))
				{
					lastNotice = DateTime.UtcNow;
					log(Loc.T("cv.waitBudget", UsageTracker.Limit, resource, usage.Count(resource), (wait / 60) + ":" + (wait % 60).ToString("00")));
				}
				Sleep(Math.Min(wait, 10) * 1000, token);
			}
		}

		private static void WaitForTurn(CancellationToken token)
		{
			lock (throttle)
			{
				double wait = MinimumRequestSpacingMs - (DateTime.UtcNow - lastRequest).TotalMilliseconds;
				if (wait > 0)
				{
					Sleep((int)wait, token);
				}
				lastRequest = DateTime.UtcNow;
			}
		}

		private static void Sleep(int milliseconds, CancellationToken token)
		{
			if (token.WaitHandle.WaitOne(milliseconds))
			{
				token.ThrowIfCancellationRequested();
			}
		}

		private static HttpWebRequest CreateRequest(string url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.UserAgent = "ComicVineAI-ComicRackPlugin/0.1";
			request.Timeout = 30000;
			request.ReadWriteTimeout = 30000;
			return request;
		}

		// ---- Parsing -----------------------------------------------------------------------------------------

		private static VolumeInfo ParseVolume(Dictionary<string, object> d)
		{
			if (d == null)
			{
				return null;
			}
			int.TryParse(Regex.Match(Json.Str(d, "start_year"), @"\d{4}").Value, out int year);
			VolumeInfo volume = new VolumeInfo
			{
				Id = Json.Int(d, "id"),
				Name = Json.Str(d, "name"),
				StartYear = year,
				Publisher = Json.Str(Json.Obj(Json.Get(d, "publisher")), "name"),
				IssueCount = Json.Int(d, "count_of_issues"),
				ImageUrl = FirstNonEmpty(Json.Str(Json.Obj(Json.Get(d, "image")), "medium_url"), Json.Str(Json.Obj(Json.Get(d, "image")), "thumb_url"))
			};
			// the range of numbers of the volume, free with the search: it tells where the numbering of a volume really is
			if (double.TryParse(Json.Str(Json.Obj(Json.Get(d, "first_issue")), "issue_number"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double first))
			{
				volume.FirstNumber = first;
			}
			if (double.TryParse(Json.Str(Json.Obj(Json.Get(d, "last_issue")), "issue_number"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double last))
			{
				volume.LastNumber = last;
			}
			return volume;
		}

		private static IssueInfo ParseIssue(Dictionary<string, object> d)
		{
			Dictionary<string, object> image = Json.Obj(Json.Get(d, "image"));
			Dictionary<string, object> volume = Json.Obj(Json.Get(d, "volume"));
			string imageUrl = Json.Str(image, "medium_url");
			if (imageUrl.Length == 0)
			{
				imageUrl = Json.Str(image, "small_url");
			}
			return new IssueInfo
			{
				Id = Json.Int(d, "id"),
				VolumeId = Json.Int(volume, "id"),
				VolumeName = Json.Str(volume, "name"),
				Number = Json.Str(d, "issue_number"),
				Name = Json.Str(d, "name"),
				CoverDate = Json.Str(d, "cover_date"),
				StoreDate = Json.Str(d, "store_date"),
				SiteUrl = Json.Str(d, "site_detail_url"),
				ImageUrl = imageUrl
			};
		}

		private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

		private static List<string> Names(Dictionary<string, object> d, string key) =>
			Json.Arr(Json.Get(d, key)).Select(o => Json.Str(Json.Obj(o), "name")).Where(n => n.Length > 0).ToList();
	}
}
