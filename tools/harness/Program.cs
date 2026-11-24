using System;
using System.IO;
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
