using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using ComicVineAI;
using cYo.Projects.ComicRack.Engine;

// Cover scenarios against the local database, no ComicRack, no API key, no Claude: the book "cover" is a real Comic Vine cover that is
// edited like a scan with a translators' banner (rescaled, recompressed, a strip and a logo added), and the whole identification runs on it.
// Comic Vine images are downloaded from their server (not API calls) into <temp>\cvai-harness\cache.
// Usage: Harness --covers <local database>
internal static class CoverScenarios
{
	private static readonly List<string> lines = new List<string>();

	public static void Run(string databasePath)
	{
		string folder = Path.Combine(Path.GetTempPath(), "cvai-harness");
		Directory.CreateDirectory(folder);
		LocalDb db = LocalDb.TryOpen(databasePath, out string error);
		if (db == null)
		{
			Console.WriteLine("Database: " + error);
			return;
		}
		PluginSettings settings = new PluginSettings { UseClaude = false, UseCoverComparison = true, ComicVineApiKey = "" };
		DiskCache cache = new DiskCache(Path.Combine(folder, "cache"), 1);
		ComicVineClient client = new ComicVineClient("", cache, new UsageTracker(folder), s => { }, db);
		Bitmap currentCover = null;
		Identifier identifier = new Identifier(settings, WriteOptions.Load(settings), client, new SeriesChoices(Path.Combine(folder, "choices-" + Guid.NewGuid().ToString("N"))), null, book => (Bitmap)currentCover.Clone(), s => lines.Add(s));
		CancellationToken token = CancellationToken.None;

		VolumeInfo powerPack = db.SearchVolumes("Power Pack").FirstOrDefault(v => v.StartYear == 1984 && v.Name == "Power Pack");
		if (powerPack == null)
		{
			Console.WriteLine("Power Pack (1984) is not in the database");
			return;
		}
		List<IssueInfo> issues = client.ListIssues(powerPack, token);
		Console.WriteLine($"Power Pack (1984): volume {powerPack.Id}, {issues.Count} issues in the database");

		IssueInfo Issue(string number) => issues.First(i => TextMatch.NormalizeNumber(i.Number) == number);

		// 1. the file name is right
		Scenario("1. #3 with a translators' banner, file says #3", identifier, client, ref currentCover, EditedCover(client, Issue("3").ImageUrl), "Power Pack", "3", "3");
		// 2. the file name has the number of a local edition: the cover is the one of #12
		Scenario("2. cover of #12, file says #5 (number of a local edition)", identifier, client, ref currentCover, EditedCover(client, Issue("12").ImageUrl), "Power Pack", "5", "12");
		// 3. no number in the file
		Scenario("3. cover of #14, the file has no number", identifier, client, ref currentCover, EditedCover(client, Issue("14").ImageUrl), "Power Pack", "", "14");
		// 4. a name that is not the series: only the words of it
		Scenario("4. cover of #20, file series 'Los Power Pack y sus amigos', #20", identifier, client, ref currentCover, EditedCover(client, Issue("20").ImageUrl), "Los Power Pack y sus amigos", "20", "20");

		// 5. a cover that is an alternative (variant) cover of an issue
		IssueInfo alternate = null;
		VolumeInfo alternateVolume = null;
		foreach (string name in new[] { "Runaways", "Wolverine", "Daredevil", "Thundercats", "Spider-Man" })
		{
			foreach (VolumeInfo v in db.SearchVolumes(name).Where(x => x.Name == name && x.StartYear >= 1995).Take(6))
			{
				foreach (IssueInfo i in client.ListIssues(v, token).Take(40))
				{
					if (db.LoadDetails(i) && i.AltImageUrls.Count(u => u != i.ImageUrl) > 0)
					{
						alternate = i;
						alternateVolume = v;
						break;
					}
				}
				if (alternate != null)
				{
					break;
				}
			}
			if (alternate != null)
			{
				break;
			}
		}
		if (alternate != null)
		{
			string alt = alternate.AltImageUrls.First(u => u != alternate.ImageUrl);
			Scenario($"5. alternative cover of {alternateVolume.Name} ({alternateVolume.StartYear}) #{alternate.Number}, file says #{alternate.Number}", identifier, client, ref currentCover, EditedCover(client, alt), alternateVolume.Name, alternate.Number, alternate.Number);
		}
		else
		{
			Console.WriteLine("5. (no issue with alternative covers found in the test series)");
		}

		// 7. the cover is of another series altogether (must NOT be accepted)
		if (alternate != null)
		{
			Scenario("7. cover of " + alternateVolume.Name + " #" + alternate.Number + " but the file says Power Pack #3 (must not be accepted)", identifier, client, ref currentCover, EditedCover(client, alternate.ImageUrl), "Power Pack", "3", "-");
		}

		// 6. everything again: nothing is downloaded or analysed any more
		Scenario("6. (again) #12 with number #5", identifier, client, ref currentCover, EditedCover(client, Issue("12").ImageUrl), "Power Pack", "5", "12");
		Console.WriteLine($"scores remembered: {client.Covers.ScoreCount}");
	}

	/// <summary>Runs the identification on a real cover image: Harness --book-image db image.png "Silver Surfer" 52 [file name]</summary>
	public static void RunImage(string databasePath, string imagePath, string series, string number)
	{
		string folder = Path.Combine(Path.GetTempPath(), "cvai-harness");
		Directory.CreateDirectory(folder);
		LocalDb db = LocalDb.TryOpen(databasePath, out string error);
		if (db == null)
		{
			Console.WriteLine("Database: " + error);
			return;
		}
		PluginSettings settings = new PluginSettings { UseClaude = false, UseCoverComparison = true, ComicVineApiKey = "" };
		string settingsFolder = Environment.GetEnvironmentVariable("CVAI_SETTINGS_FOLDER"); // the plugin folder whose settings.json to use (read only)
		if (!string.IsNullOrEmpty(settingsFolder))
		{
			settings = PluginSettings.Load(settingsFolder);
			settings.UseClaude = false;
			settings.UseCoverComparison = true;
			settings.ComicVineApiKey = ""; // never the real key
		}
		ComicVineClient client = new ComicVineClient("", new DiskCache(Path.Combine(folder, "cache"), 1), new UsageTracker(folder), s => { }, db);
		using (Bitmap cover = new Bitmap(imagePath))
		{
			Identifier identifier = new Identifier(settings, WriteOptions.Load(settings), client, new SeriesChoices(Path.Combine(folder, "choices-" + Guid.NewGuid().ToString("N"))), null, book => (Bitmap)cover.Clone(), s => lines.Add(s));
			Stopwatch watch = Stopwatch.StartNew();
			IdentifyResult result = identifier.Identify(new ComicBook { Series = series, Number = number }, CancellationToken.None);
			Console.WriteLine($"{series} #{number}: {(result.Best == null ? "no match" : (result.Accepted ? "ACCEPTED " : "pending  ") + result.Best)}   ({watch.Elapsed.TotalSeconds:0.0}s)");
			foreach (string line in lines)
			{
				Console.WriteLine("   log: " + line);
			}
			foreach (IssueMatch m in result.Candidates.Take(4))
			{
				Console.WriteLine($"   candidate: {m}");
			}
		}
	}

	/// <summary>Saves the covers of the issues of a volume as files (number.jpg) to try other comparisons on them: Harness --series-covers db "Silver Surfer" 1987 outDir</summary>
	public static void DumpSeries(string databasePath, string name, int year, string outDir)
	{
		string folder = Path.Combine(Path.GetTempPath(), "cvai-harness");
		Directory.CreateDirectory(folder);
		Directory.CreateDirectory(outDir);
		LocalDb db = LocalDb.TryOpen(databasePath, out string error);
		if (db == null)
		{
			Console.WriteLine("Database: " + error);
			return;
		}
		ComicVineClient client = new ComicVineClient("", new DiskCache(Path.Combine(folder, "cache"), 1), new UsageTracker(folder), s => { }, db);
		VolumeInfo volume = db.SearchVolumes(name).FirstOrDefault(v => v.Name == name && v.StartYear == year);
		if (volume == null)
		{
			Console.WriteLine("Volume not found");
			return;
		}
		foreach (IssueInfo issue in client.ListIssues(volume, CancellationToken.None).Take(80))
		{
			using (Bitmap image = client.DownloadImage(issue.ImageUrl, CancellationToken.None))
			{
				if (image != null)
				{
					image.Save(Path.Combine(outDir, issue.Number.Replace('/', '_') + ".png"), ImageFormat.Png);
				}
			}
		}
		Console.WriteLine("saved to " + outDir);
	}

	private static void Scenario(string title, Identifier identifier, ComicVineClient client, ref Bitmap currentCover, Bitmap cover, string series, string number, string expectedNumber)
	{
		currentCover?.Dispose();
		currentCover = cover;
		lines.Clear();
		Stopwatch watch = Stopwatch.StartNew();
		IdentifyResult result = identifier.Identify(new ComicBook { Series = series, Number = number }, CancellationToken.None);
		watch.Stop();
		IssueMatch best = result.Best;
		bool right = best != null && TextMatch.NormalizeNumber(best.Issue.Number) == expectedNumber;
		Console.WriteLine();
		Console.WriteLine(title);
		Console.WriteLine($"   {(best == null ? "no match" : (result.Accepted ? "ACCEPTED " : "pending  ") + best)}");
		Console.WriteLine($"   expected #{expectedNumber}: {(right ? "RIGHT" : "WRONG")}   time {watch.Elapsed.TotalSeconds:0.0}s   fromAlternate={best?.FromAlternate}  numberDiffers={best?.NumberDiffers}");
		foreach (string line in lines.Where(l => l.Contains("portadas") || l.Contains("covers") || l.Contains("alternativa") || l.Contains("alternative") || l.Contains("archivo dice") || l.Contains("file says")))
		{
			Console.WriteLine("   log: " + line);
		}
	}

	/// <summary>A real cover made to look like a scan with the marks of a translation group: smaller, a strip across the bottom, a logo, JPEG.</summary>
	private static Bitmap EditedCover(ComicVineClient client, string url)
	{
		using (Bitmap original = client.DownloadImage(url, CancellationToken.None))
		{
			if (original == null)
			{
				throw new InvalidOperationException("Cannot download " + url);
			}
			int width = (int)(original.Width * 0.75);
			int height = (int)(original.Height * 0.75);
			using (Bitmap scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(scaled))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.DrawImage(original, new Rectangle(0, 0, width, height));
					using (Brush strip = new SolidBrush(Color.FromArgb(235, 20, 20, 20)))
					{
						g.FillRectangle(strip, 0, (int)(height * 0.90), width, height - (int)(height * 0.90)); // 10% banner at the bottom
					}
					using (Font font = new Font("Arial", Math.Max(8, height / 40f), FontStyle.Bold))
					{
						g.DrawString("TRADUCIDO Y MAQUETADO POR LA COMUNIDAD", font, Brushes.White, 6, (int)(height * 0.915));
					}
					using (Brush logo = new SolidBrush(Color.FromArgb(230, 200, 30, 30)))
					{
						g.FillEllipse(logo, (int)(width * 0.02), (int)(height * 0.02), (int)(width * 0.17), (int)(width * 0.17)); // a round logo, top left
					}
				}
				using (MemoryStream ms = new MemoryStream())
				{
					ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
					using (EncoderParameters parameters = new EncoderParameters(1))
					{
						parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
						scaled.Save(ms, jpeg, parameters);
					}
					ms.Position = 0;
					using (Image decoded = Image.FromStream(ms))
					{
						return new Bitmap(decoded);
					}
				}
			}
		}
	}
}
