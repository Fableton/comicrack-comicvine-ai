using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ComicVineAI;

// Every window of the plugin in a language, saved as PNG, to check that the texts fit: Harness --shots <db> <language> <out folder> [page image]
internal static class WindowShots
{
	public static void Run(string databasePath, string language, string outDir, string pageImage)
	{
		Directory.CreateDirectory(outDir);
		string tmp = Path.Combine(Path.GetTempPath(), "cvai-harness");
		Directory.CreateDirectory(tmp);
		Loc.Initialize(language, tmp);
		PluginSettings settings = new PluginSettings { ComicVineApiKey = "", LocalDbPath = databasePath, UseClaude = true };

		Shot(new StartModeForm(12, false), outDir, "start");
		Shot(new AiContextForm(true), outDir, "aicontext");
		Shot(new EditMetadataForm("Comic Vine AI", "Silver Surfer #52", "", new ManualEdit { Series = "Silver Surfer", Number = "52" }, true), outDir, "edit-extended");
		Shot(new EditMetadataForm("Comic Vine AI", "Silver Surfer #52", "", new ManualEdit { Series = "Silver Surfer", Number = "52" }, false), outDir, "edit-basic");

		SettingsForm settingsForm = new SettingsForm(settings, tmp);
		TabControl tabs = All(settingsForm).OfType<TabControl>().FirstOrDefault();
		if (tabs == null)
		{
			Shot(settingsForm, outDir, "settings");
		}
		else
		{
			settingsForm.Show();
			for (int i = 0; i < tabs.TabPages.Count; i++)
			{
				tabs.SelectedIndex = i;
				Pump(5);
				Save(settingsForm, outDir, "settings-" + i);
			}
			settingsForm.Close();
			settingsForm.Dispose();
		}

		ProgressForm progress = new ProgressForm(0.8);
		progress.Show();
		Pump(5);
		progress.BeginBook(0, "Silver Surfer Vol.3 #52.cbr");
		progress.EndBook(0, Outcome.Tagged, "Silver Surfer (1987) #52", Loc.T("batch.tagged", "metadata", ""), 0.96, null);
		progress.BeginBook(1, "Spawn #48.cbr");
		progress.EndBook(1, Outcome.Review, Loc.T("batch.bestGuess", "Spawn (1992) #48"), Loc.T("batch.needsHelp", ""), 0.72, null);
		progress.BeginBook(2, "X-Factor 09.cbr");
		progress.SetProgress(2, 3, "X-Factor 09.cbr");
		progress.SetCounters(Loc.T("batch.counters", 1, 1, 0, "local db 52, api none", 0, ""));
		progress.Log(Loc.T("id.searching", "'Spawn' #48 (?)"));
		Pump(10);
		Save(progress, outDir, "progress");
		progress.Close();
		progress.Dispose();

		if (pageImage != null)
		{
			LocalDb db = LocalDb.TryOpen(databasePath, out string error);
			ComicVineClient client = new ComicVineClient("", new DiskCache(Path.Combine(tmp, "cache"), 1), new UsageTracker(tmp), x => { }, db);
			BookHints hints = new BookHints();
			hints.SeriesNames.Add("Silver Surfer");
			hints.Numbers.Add("52");
			using (Bitmap page = new Bitmap(pageImage))
			using (ManualSearchForm manual = new ManualSearchForm("Silver Surfer Vol.3 #52.cbr", new IdentifyResult { Hints = hints }, i => (Bitmap)page.Clone(), 3, client, settings))
			{
				manual.AiUnavailableReason = Loc.T("manual.aiNotFound");
				manual.Show();
				Pump(60);
				Save(manual, outDir, "manual");
			}
		}
		Console.WriteLine("saved to " + outDir);
	}

	private static IEnumerable<Control> All(Control root)
	{
		foreach (Control c in root.Controls)
		{
			yield return c;
			foreach (Control d in All(c))
			{
				yield return d;
			}
		}
	}

	private static void Shot(Form form, string outDir, string name)
	{
		form.Show();
		Pump(10);
		Save(form, outDir, name);
		form.Close();
		form.Dispose();
	}

	private static void Pump(int times)
	{
		for (int i = 0; i < times; i++)
		{
			Application.DoEvents();
			Thread.Sleep(50);
		}
	}

	private static void Save(Form form, string outDir, string name)
	{
		using (Bitmap shot = new Bitmap(form.Width, form.Height))
		{
			form.DrawToBitmap(shot, new Rectangle(0, 0, form.Width, form.Height));
			shot.Save(Path.Combine(outDir, name + ".png"));
		}
	}
}
