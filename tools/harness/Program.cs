using System;
using System.IO;
using System.Linq;
using System.Threading;
using ComicVineAI;
using cYo.Projects.ComicRack.Engine;

// Smoke test of the identification with the local Comic Vine database, no ComicRack and no API key needed.
// Usage: Harness <path to the local database> <series> <number>
// Copy this file as a starting point for any other experiment (a cover from disk, a fake server, a single method...).
internal static class Program
{
	private static void Main(string[] args)
	{
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		if (args.Length == 2 && args[0] == "--covers")
		{
			CoverScenarios.Run(args[1]); // cover scenarios, see CoverScenarios.cs
			return;
		}
		if (args.Length == 5 && args[0] == "--book-image")
		{
			CoverScenarios.RunImage(args[1], args[2], args[3], args[4]);
			return;
		}
		if (args.Length == 5 && args[0] == "--series-covers")
		{
			CoverScenarios.DumpSeries(args[1], args[2], int.Parse(args[3]), args[4]);
			return;
		}
		if (args.Length == 5 && args[0] == "--resolve")
		{
			// the search that runs with Claude's answer (no cover): Harness --resolve <db> "<series>" <number> <publisher>
			string tmp = Path.Combine(Path.GetTempPath(), "cvai-harness");
			Directory.CreateDirectory(tmp);
			LocalDb ldb = LocalDb.TryOpen(args[1], out string err);
			if (ldb == null)
			{
				Console.WriteLine("Database: " + err);
				return;
			}
			PluginSettings s = new PluginSettings { UseClaude = false, UseCoverComparison = false, ComicVineApiKey = "" };
			ComicVineClient c = new ComicVineClient("", new DiskCache(Path.Combine(tmp, "cache"), 1), new UsageTracker(tmp), x => { }, ldb);
			Identifier id = new Identifier(s, WriteOptions.Load(s), c, new SeriesChoices(Path.Combine(tmp, "choices-" + Guid.NewGuid().ToString("N"))), null, book => null, x => { });
			BookHints hints = new BookHints { Publisher = args[4] };
			hints.SeriesNames.Add(args[2]);
			hints.Numbers.Add(args[3]);
			foreach (IssueMatch m in id.Resolve(hints, null, 0, "claude", CancellationToken.None))
			{
				Console.WriteLine("   " + m);
			}
			return;
		}
		if ((args.Length == 4 || args.Length == 5) && args[0] == "--shots")
		{
			WindowShots.Run(args[1], args[2], args[3], args.Length == 5 ? args[4] : null); // every window in a language, see WindowShots.cs
			return;
		}
		if (args.Length == 6 && args[0] == "--manual-shot")
		{
			// the manual review window on an image, the issues of a volume open, saved as PNG: Harness --manual-shot <db> <page image> "<series>" <year> <out.png>
			string tmp = Path.Combine(Path.GetTempPath(), "cvai-harness");
			LocalDb ldb = LocalDb.TryOpen(args[1], out string err);
			PluginSettings s = new PluginSettings { UseClaude = false, ComicVineApiKey = "" };
			ComicVineClient c = new ComicVineClient("", new DiskCache(Path.Combine(tmp, "cache"), 1), new UsageTracker(tmp), x => { }, ldb);
			VolumeInfo v = ldb.SearchVolumes(args[3]).First(x => x.Name == args[3] && x.StartYear == int.Parse(args[4]));
			BookHints hints = new BookHints();
			hints.SeriesNames.Add(args[3]);
			hints.Numbers.Add("52");
			using (System.Drawing.Bitmap page = new System.Drawing.Bitmap(args[2]))
			using (ManualSearchForm form = new ManualSearchForm("test", new IdentifyResult { Hints = hints }, i => (System.Drawing.Bitmap)page.Clone(), 3, c, s))
			{
				form.SuggestedVolume = v;
				form.OpenIssuesForSuggested = true;
				form.Show();
				for (int i = 0; i < 80; i++)
				{
					System.Windows.Forms.Application.DoEvents();
					Thread.Sleep(100);
				}
				using (System.Drawing.Bitmap shot = new System.Drawing.Bitmap(form.Width, form.Height))
				{
					form.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
					shot.Save(args[5]);
				}
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--compare")
		{
			// the similarity (0..1) that the plugin gives to two cover files: Harness --compare <book cover> <Comic Vine cover>
			using (System.Drawing.Bitmap a = new System.Drawing.Bitmap(args[1]))
			using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(args[2]))
			{
				Console.WriteLine("similarity " + CoverFingerprint.From(a).Similarity(CoverFingerprint.From(b)).ToString("P1"));
			}
			return;
		}
		if (args.Length < 3)
		{
			Console.WriteLine("Usage: Harness <local database> <series> <number>");
			return;
		}
		string folder = Path.Combine(Path.GetTempPath(), "cvai-harness");
		Directory.CreateDirectory(folder);
		LocalDb db = LocalDb.TryOpen(args[0], out string error);
		if (db == null)
		{
			Console.WriteLine("Database: " + error);
			return;
		}
		PluginSettings settings = new PluginSettings { UseClaude = false, UseCoverComparison = false, ComicVineApiKey = "" };
		ComicVineClient client = new ComicVineClient("", new DiskCache(Path.Combine(folder, "cache"), 1), new UsageTracker(folder), Console.WriteLine, db);
		Identifier identifier = new Identifier(settings, WriteOptions.Load(settings), client, new SeriesChoices(folder), null, book => null, Console.WriteLine);
		IdentifyResult result = identifier.Identify(new ComicBook { Series = args[1], Number = args[2] }, CancellationToken.None);
		Console.WriteLine(result.Best == null ? "No match" : (result.Accepted ? "ACCEPTED " : "pending ") + result.Best);
	}
}
