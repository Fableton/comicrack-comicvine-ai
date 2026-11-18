using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicVineAI
{
	/// <summary>
	/// A local copy of the Comic Vine data (the SQLite database that other tools extract from it): volumes, issues with their credits,
	/// publishers, and a full text index of the volume names. It answers the same questions the plugin asks the API, with no calls and
	/// no hourly limit. Read only. It is a snapshot: what Comic Vine got after its last sync date is not in it, which is why the
	/// plugin asks the API for what is not found here (see <see cref="MayBeNewerThanSnapshot"/>).
	/// </summary>
	public sealed class LocalDb : IDisposable
	{
		private readonly object sync = new object();
		private IntPtr db;

		public string Path { get; private set; }
		public int Volumes { get; private set; }
		public int Issues { get; private set; }

		/// <summary>Date of the last synchronization with Comic Vine, "yyyy-MM-dd", or "".</summary>
		public string SyncDate { get; private set; } = "";

		private LocalDb()
		{
		}

		/// <summary>Opens the database. Returns null and an explanation if the file cannot be used.</summary>
		public static LocalDb TryOpen(string path, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(path))
			{
				return null;
			}
			if (!File.Exists(path))
			{
				error = Loc.T("localdb.missing");
				return null;
			}
			try
			{
				SqliteNative.EnsureLoaded();
				LocalDb result = new LocalDb { Path = path };
				if (!result.Open(path, out error))
				{
					return null;
				}
				return result;
			}
			catch (Exception e)
			{
				error = e.Message;
				return null;
			}
		}

		private bool Open(string path, out string error)
		{
			error = null;
			// first as a plain read only file; if SQLite cannot create the -shm file of a WAL database next to it, as an immutable one
			int rc = SqliteNative.sqlite3_open_v2(SqliteNative.Utf8(path), out db, SqliteNative.OpenReadOnly | SqliteNative.OpenFullMutex, IntPtr.Zero);
			if (rc == SqliteNative.Ok && !CheckSchema(out error))
			{
				Close();
				rc = OpenImmutable(path);
				if (rc == SqliteNative.Ok && !CheckSchema(out error))
				{
					Close();
					return false;
				}
				if (rc == SqliteNative.Ok)
				{
					error = null;
				}
			}
			else if (rc != SqliteNative.Ok)
			{
				Close();
				rc = OpenImmutable(path);
				if (rc != SqliteNative.Ok || !CheckSchema(out error))
				{
					error = error ?? "SQLite error " + rc;
					Close();
					return false;
				}
			}
			SqliteNative.sqlite3_busy_timeout(db, 5000);
			try
			{
				Volumes = (int)Scalar("select count(*) from cv_volume");
				Issues = (int)Scalar("select count(*) from cv_issue");
				SyncDate = (Query("select max(last_sync_date) from cv_sync_metadata").FirstOrDefault()?[0] as string) ?? "";
			}
			catch (Exception e)
			{
				error = e.Message;
				Close();
				return false;
			}
			return true;
		}

		private int OpenImmutable(string path)
		{
			string uri = "file:///" + Uri.EscapeUriString(path.Replace('\\', '/')).Replace("?", "%3f").Replace("#", "%23") + "?immutable=1";
			return SqliteNative.sqlite3_open_v2(SqliteNative.Utf8(uri), out db, SqliteNative.OpenReadOnly | SqliteNative.OpenUri | SqliteNative.OpenFullMutex, IntPtr.Zero);
		}

		/// <summary>The tables the plugin needs are there (and the file is a database at all).</summary>
		private bool CheckSchema(out string error)
		{
			error = null;
			try
			{
				List<object[]> tables = Query("select name from sqlite_master where type in ('table','view')");
				HashSet<string> names = new HashSet<string>(tables.Select(r => Convert.ToString(r[0])), StringComparer.OrdinalIgnoreCase);
				foreach (string needed in new[] { "cv_volume", "cv_issue", "cv_publisher", "volume_fts" })
				{
					if (!names.Contains(needed))
					{
						error = Loc.T("localdb.notComicVine", needed);
						return false;
					}
				}
				return true;
			}
			catch (Exception e)
			{
				error = e.Message;
				return false;
			}
		}

		private void Close()
		{
			if (db != IntPtr.Zero)
			{
				SqliteNative.sqlite3_close_v2(db);
				db = IntPtr.Zero;
			}
		}

		public void Dispose()
		{
			lock (sync)
			{
				Close();
			}
		}

		// ---- queries ------------------------------------------------------------------------------------------

		/// <summary>Runs a query; every row is an array of long, double, string or null.</summary>
		private List<object[]> Query(string sql, params object[] args)
		{
			lock (sync)
			{
				if (db == IntPtr.Zero)
				{
					throw new ObjectDisposedException(nameof(LocalDb));
				}
				if (SqliteNative.sqlite3_prepare_v2(db, SqliteNative.Utf8(sql), -1, out IntPtr statement, IntPtr.Zero) != SqliteNative.Ok)
				{
					throw new InvalidOperationException(SqliteNative.Message(db));
				}
				try
				{
					for (int i = 0; i < args.Length; i++)
					{
						object a = args[i];
						if (a == null)
						{
							SqliteNative.sqlite3_bind_null(statement, i + 1);
						}
						else if (a is string text)
						{
							byte[] raw = Encoding.UTF8.GetBytes(text);
							SqliteNative.sqlite3_bind_text(statement, i + 1, raw, raw.Length, SqliteNative.Transient);
						}
						else
						{
							SqliteNative.sqlite3_bind_int64(statement, i + 1, Convert.ToInt64(a, CultureInfo.InvariantCulture));
						}
					}
					List<object[]> rows = new List<object[]>();
					int columns = SqliteNative.sqlite3_column_count(statement);
					int rc;
					while ((rc = SqliteNative.sqlite3_step(statement)) == SqliteNative.Row)
					{
						object[] row = new object[columns];
						for (int c = 0; c < columns; c++)
						{
							switch (SqliteNative.sqlite3_column_type(statement, c))
							{
								case SqliteNative.TypeInteger: row[c] = SqliteNative.sqlite3_column_int64(statement, c); break;
								case SqliteNative.TypeFloat: row[c] = SqliteNative.sqlite3_column_double(statement, c); break;
								case SqliteNative.TypeNull: row[c] = null; break;
								default:
									IntPtr p = SqliteNative.sqlite3_column_text(statement, c);
									row[c] = SqliteNative.FromUtf8(p, SqliteNative.sqlite3_column_bytes(statement, c));
									break;
							}
						}
						rows.Add(row);
					}
					if (rc != SqliteNative.Done)
					{
						throw new InvalidOperationException(SqliteNative.Message(db));
					}
					return rows;
				}
				finally
				{
					SqliteNative.sqlite3_finalize(statement);
				}
			}
		}

		private long Scalar(string sql, params object[] args)
		{
			List<object[]> rows = Query(sql, args);
			return rows.Count == 0 || rows[0][0] == null ? 0 : Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture);
		}

		private static string Text(object value) => value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);

		private static int Int(object value) => value == null ? 0 : (int)Convert.ToInt64(value, CultureInfo.InvariantCulture);

		// ---- volumes ------------------------------------------------------------------------------------------

		private const string VolumeSelect =
			"select v.id, v.name, v.start_year, p.name, v.count_of_issues, v.image_url from {0} left join cv_publisher p on p.id = v.publisher_id ";

		/// <summary>
		/// Volumes whose name (or alias) matches the words of the name. All the words first; if that finds little, any of them,
		/// ranked by how many match, which is what the search of the API does with a name that has extra words in it.
		/// </summary>
		public List<VolumeInfo> SearchVolumes(string name, int limit = 40)
		{
			List<string> words = ExpandCompounds(Words(name));
			if (words.Count == 0)
			{
				return new List<VolumeInfo>();
			}
			List<VolumeInfo> found = new List<VolumeInfo>();
			// The words next to each other, in order, first: "amazing spider man" finds "The Amazing Spider-Man" before anything else.
			// A name like that matches hundreds of volumes, so they are sorted before the list is cut: the ones named exactly like the
			// search first, then the biggest (the series that has most issues is rarely the one that is not wanted).
			string joined = string.Join(" ", words);
			IEnumerable<VolumeInfo> Ranked(List<VolumeInfo> list) => list.OrderByDescending(v => TextMatch.Normalize(v.Name) == joined).ThenByDescending(v => v.IssueCount);
			Add(found, Ranked(MatchVolumes("\"" + joined + "\"", 500)), limit);
			Add(found, Ranked(MatchVolumes(string.Join(" ", words.Select(w => "\"" + w + "\"")), 500)), limit);
			if (found.Count < 10 && words.Count > 1)
			{
				Add(found, MatchVolumes(string.Join(" ", words.Take(words.Count - 1).Select(w => "\"" + w + "\"")) + " \"" + words.Last() + "\"*", limit), limit);
			}
			if (found.Count < 10 && words.Count > 1)
			{
				Add(found, MatchVolumes(string.Join(" OR ", words.Select(w => "\"" + w + "\"")), limit), limit);
			}
			FillNumbering(found);
			return found;
		}

		private static void Add(List<VolumeInfo> list, IEnumerable<VolumeInfo> more, int limit)
		{
			foreach (VolumeInfo v in more)
			{
				if (list.Count < limit && list.All(x => x.Id != v.Id))
				{
					list.Add(v);
				}
			}
		}

		private List<VolumeInfo> MatchVolumes(string match, int limit)
		{
			try
			{
				return Query(string.Format(VolumeSelect, "volume_fts f join cv_volume v on v.id = f.rowid") + "where volume_fts match ?1 order by f.rank limit ?2", match, limit)
					.Select(ToVolume).ToList();
			}
			catch (InvalidOperationException)
			{
				return new List<VolumeInfo>(); // a query that FTS5 does not accept finds nothing
			}
		}

		/// <summary>True if the volume is in the database (cheap: one lookup by key).</summary>
		public bool HasVolume(int id) => Scalar("select count(*) from cv_volume where id = ?1", id) > 0;

		public VolumeInfo GetVolume(int id)
		{
			VolumeInfo volume = Query(string.Format(VolumeSelect, "cv_volume v") + "where v.id = ?1", id).Select(ToVolume).FirstOrDefault();
			if (volume != null)
			{
				FillNumbering(new List<VolumeInfo> { volume });
			}
			return volume;
		}

		private static VolumeInfo ToVolume(object[] r)
		{
			int.TryParse(Regex.Match(Text(r[2]), @"\d{4}").Value, out int year);
			return new VolumeInfo { Id = Int(r[0]), Name = Text(r[1]), StartYear = year, Publisher = Text(r[3]), IssueCount = Int(r[4]), ImageUrl = Medium(Text(r[5])), Local = true };
		}

		/// <summary>The first and last issue number of each volume, from its issues (the dump has no such field).</summary>
		private void FillNumbering(List<VolumeInfo> volumes)
		{
			if (volumes.Count == 0)
			{
				return;
			}
			string ids = string.Join(",", volumes.Select(v => v.Id));
			Dictionary<int, List<double>> numbers = new Dictionary<int, List<double>>();
			foreach (object[] r in Query("select volume_id, cast(issue_number as real) from cv_issue where volume_id in (" + ids + ") and issue_number glob '[0-9]*'"))
			{
				int id = Int(r[0]);
				if (!numbers.TryGetValue(id, out List<double> list))
				{
					numbers[id] = list = new List<double>();
				}
				list.Add(Convert.ToDouble(r[1], CultureInfo.InvariantCulture));
			}
			foreach (VolumeInfo v in volumes)
			{
				if (numbers.TryGetValue(v.Id, out List<double> list) && list.Count > 0)
				{
					// a volume can have a freak number (Catwoman has a real issue #1000000, DC One Million): the range ignores what is
					// far from the middle of the numbering, as the first and last issue of Comic Vine do
					list.Sort();
					double median = list[list.Count / 2];
					double margin = 2 * Math.Max(v.IssueCount, list.Count) + 20;
					List<double> usual = list.Where(n => Math.Abs(n - median) <= margin).ToList();
					v.FirstNumber = usual.Min();
					v.LastNumber = usual.Max();
				}
			}
		}

		private readonly Dictionary<string, string[]> compounds = new Dictionary<string, string[]>();

		/// <summary>How many volumes have the term (or the phrase) in their name.</summary>
		private long TermCount(string term)
		{
			try
			{
				return Scalar("select count(*) from volume_fts where volume_fts match ?1", "\"" + term + "\"");
			}
			catch (InvalidOperationException)
			{
				return 0;
			}
		}

		/// <summary>
		/// Two words stuck together ("spiderman" for "Spider-Man", "wonderwoman") are split when the parts are words of the index and the
		/// two together, in order, are much more common than the stuck word (a few volumes really are called "Spiderman", but "Spider-Man"
		/// is what is meant). A word like "catwoman", where the split is rare, stays.
		/// </summary>
		private List<string> ExpandCompounds(List<string> words)
		{
			List<string> result = new List<string>();
			foreach (string word in words)
			{
				if (word.Length < 7 || word.Any(char.IsDigit))
				{
					result.Add(word);
					continue;
				}
				if (!compounds.TryGetValue(word, out string[] parts))
				{
					parts = null;
					long whole = TermCount(word);
					for (int i = 3; i <= word.Length - 3 && parts == null; i++)
					{
						string a = word.Substring(0, i), b = word.Substring(i);
						long together = TermCount(a + " " + b);
						if (together > 0 && (whole == 0 || together > 3 * whole))
						{
							parts = new[] { a, b };
						}
					}
					compounds[word] = parts;
				}
				if (parts != null)
				{
					result.AddRange(parts);
				}
				else
				{
					result.Add(word);
				}
			}
			return result.Distinct().ToList();
		}

		/// <summary>The words of a name, lower case, as the full text index sees them.</summary>
		private static List<string> Words(string name) =>
			Regex.Matches(TextMatch.Normalize(name), @"[\p{L}\p{N}]+").Cast<Match>().Select(m => m.Value).Where(w => w.Length > 0).Distinct().Take(12).ToList();

		// ---- issues -------------------------------------------------------------------------------------------

		/// <summary>All the issues of a volume.</summary>
		public List<IssueInfo> ListIssues(VolumeInfo volume)
		{
			return Query("select id, name, issue_number, cover_date, store_date, image_url, site_detail_url from cv_issue where volume_id = ?1", volume.Id)
				.Select(r => new IssueInfo
				{
					Id = Int(r[0]), VolumeId = volume.Id, VolumeName = volume.Name, Name = Text(r[1]), Number = Text(r[2]), CoverDate = Text(r[3]), StoreDate = Text(r[4]),
					ImageUrl = Medium(Text(r[5])), SiteUrl = Text(r[6]), Local = true
				})
				.OrderBy(i => double.TryParse(TextMatch.NormalizeNumber(i.Number), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : double.MaxValue)
				.ThenBy(i => i.Number, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>Credits, description and alternative covers, which are in the same row. False if the issue is not here.</summary>
		public bool LoadDetails(IssueInfo issue)
		{
			object[] r = Query("select name, cover_date, store_date, description, site_detail_url, image_url, character_credits, person_credits, team_credits, location_credits, story_arc_credits, associated_images " +
				"from cv_issue where id = ?1", issue.Id).FirstOrDefault();
			if (r == null)
			{
				return false;
			}
			issue.Name = Text(r[0]);
			issue.CoverDate = Text(r[1]);
			issue.StoreDate = Text(r[2]);
			issue.Description = Text(r[3]); // raw HTML, MetadataWriter cleans it like the scraper
			issue.SiteUrl = Text(r[4]);
			issue.ImageUrl = Medium(Text(r[5]));
			issue.Characters = Names(Text(r[6]));
			issue.Persons = Json.Arr(ParseArray(Text(r[7]))).Select(o => new Credit { Name = Json.Str(Json.Obj(o), "name"), Role = Json.Str(Json.Obj(o), "role") }).Where(c => c.Name.Length > 0).ToList();
			issue.Teams = Names(Text(r[8]));
			issue.Locations = Names(Text(r[9]));
			issue.StoryArcs = Names(Text(r[10]));
			issue.AltImageUrls = Json.Arr(ParseArray(Text(r[11]))).Select(o => Json.Str(Json.Obj(o), "original_url")).Where(u => u.Length > 0).ToList();
			issue.HasDetails = true;
			return true;
		}

		/// <summary>
		/// True when an issue that is not in the snapshot may exist at Comic Vine: the number is beyond the last one the snapshot
		/// has for a volume that was still being published when it was taken. A number inside the range of the volume that is
		/// missing really is missing, and so is one in a volume that had ended: there the API is not asked.
		/// </summary>
		public bool MayBeNewerThanSnapshot(VolumeInfo volume, List<IssueInfo> issues, string number)
		{
			if (!double.TryParse(TextMatch.NormalizeNumber(number), NumberStyles.Float, CultureInfo.InvariantCulture, out double wanted))
			{
				return false;
			}
			double last = issues.Select(i => double.TryParse(TextMatch.NormalizeNumber(i.Number), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : double.NaN).Where(n => !double.IsNaN(n)).DefaultIfEmpty(0).Max();
			if (wanted <= last)
			{
				return false;
			}
			if (!DateTime.TryParse(SyncDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime sync))
			{
				return true;
			}
			string newest = issues.Select(i => i.CoverDate).Where(d => !string.IsNullOrEmpty(d) && string.CompareOrdinal(d, "2100") < 0).DefaultIfEmpty("").Max();
			return string.CompareOrdinal(newest, sync.AddMonths(-9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0; // still being published
		}

		/// <summary>The dump keeps the original image; the comparison needs a medium one, which is much smaller to download.</summary>
		private static string Medium(string url) => url.Replace("/uploads/original/", "/uploads/scale_medium/");

		private static object ParseArray(string json)
		{
			if (string.IsNullOrWhiteSpace(json) || json[0] != '[')
			{
				return null;
			}
			try
			{
				return Json.ParseArrayText(json);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static List<string> Names(string json) =>
			Json.Arr(ParseArray(json)).Select(o => Json.Str(Json.Obj(o), "name")).Where(n => n.Length > 0).ToList();
	}
}
