using System;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicVineAI
{
	/// <summary>
	/// Identifies the selected comics on Comic Vine and tags them. Like the Python scraper it shows up as:
	/// a button in the toolbar of the book list (with the Configure action in its drop down), an entry in the Automation
	/// menu of the list, and a script button in the book information dialog (Editor hook).
	/// The drop down of the toolbar button only has Configure (it is fixed in ComicRack), so the manual and the automatic start
	/// are two more entries of the Automation menu (<see cref="ComicVineAIManualPlugin"/>, <see cref="ComicVineAIAutoPlugin"/>).
	/// </summary>
	public class ComicVineAIPlugin : ComicVineAIBase, IBooks, IEditor, IConfig
	{
		public override string Key { get; set; } = "ComicVineAI";

		public ComicVineAIPlugin()
		{
			Name = "Comic Vine AI";
			Description = Loc.T("plugin.description") + " (v" + AppInfo.Version + ")";
			Image = "comicvineai.png"; // embedded in the DLL: this is what makes ComicRack show the toolbar button
		}

		// IBooks: the selected books of the library. Manual or automatic as the settings say.
		public void Execute(ComicBook[] books) => Run(books, fromEditor: false, manual: null);

		// IEditor: the book of the information dialog. The user asked for this book explicitly, so it is not skipped
		// when it was scraped before.
		void IEditor.Execute(ComicBook[] books) => Run(books, fromEditor: true, manual: null);

		// IConfig: the Configure action of this plugin.
		void IConfig.Execute()
		{
			UiState.Init(ScriptPath);
			PluginSettings settings = PluginSettings.Load(ScriptPath);
			Ui.FontScale = (float)Math.Max(0.8, Math.Min(2.5, settings.FontScale));
			Loc.Initialize(settings.Language, ScriptPath);
			Configure(settings);
		}
	}

	/// <summary>An entry of the Automation menu of the book list: every book goes to the manual review, whatever the settings say.</summary>
	public class ComicVineAIManualPlugin : ComicVineAIBase, IBooks
	{
		public override string Key { get; set; } = "ComicVineAIManual";

		public ComicVineAIManualPlugin()
		{
			Name = Loc.T("plugin.manualName");
			Description = Loc.T("plugin.manualDescription");
		}

		public void Execute(ComicBook[] books) => Run(books, fromEditor: false, manual: true);
	}

	/// <summary>An entry of the Automation menu of the book list: the sure books are tagged by themselves, whatever the settings say.</summary>
	public class ComicVineAIAutoPlugin : ComicVineAIBase, IBooks
	{
		public override string Key { get; set; } = "ComicVineAIAuto";

		public ComicVineAIAutoPlugin()
		{
			Name = Loc.T("plugin.autoName");
			Description = Loc.T("plugin.autoDescription");
		}

		public void Execute(ComicBook[] books) => Run(books, fromEditor: false, manual: false);
	}

	/// <summary>What the commands of the plugin share; they only differ in how the run starts.</summary>
	public abstract class ComicVineAIBase : DotNetPlugin
	{
		protected override void OnLoad()
		{
			Log($"{AppInfo.NameAndVersion} loaded from {ScriptPath}");
		}

		/// <param name="manual">true = manual review for every book, false = automatic, null = what the settings say (this run only).</param>
		protected void Run(ComicBook[] books, bool fromEditor, bool? manual)
		{
			if (books == null || books.Length == 0)
			{
				return;
			}
			UiState.Init(ScriptPath);
			PluginSettings settings = PluginSettings.Load(ScriptPath);
			Ui.FontScale = (float)Math.Max(0.8, Math.Min(2.5, settings.FontScale));
			Loc.Initialize(settings.Language, ScriptPath);
			if (string.IsNullOrWhiteSpace(settings.ComicVineApiKey) && !Configure(settings))
			{
				return;
			}
			if (fromEditor)
			{
				settings.SkipAlreadyScraped = false; // this run only, it is not saved
			}
			if (manual == null && settings.AskBeforeStart)
			{
				// the plain button: ask which way to start (the Manual and Automatic entries of the Automation menu never ask)
				using (StartModeForm ask = new StartModeForm(books.Length, settings.ManualOnly))
				{
					if (ask.ShowDialog(ComicRack.MainWindow) != DialogResult.OK)
					{
						return;
					}
					manual = ask.Manual;
				}
			}
			new BatchRunner(ComicRack.MainWindow, ComicRack.App, settings, ScriptPath, message => Log(message), manual).Run(books);
		}

		protected bool Configure(PluginSettings settings)
		{
			using (SettingsForm form = new SettingsForm(settings, ScriptPath))
			{
				if (form.ShowDialog(ComicRack.MainWindow) != DialogResult.OK)
				{
					return false;
				}
			}
			settings.Save(ScriptPath);
			Loc.Initialize(settings.Language, ScriptPath); // the language that was just chosen applies from now on
			return !string.IsNullOrWhiteSpace(settings.ComicVineApiKey);
		}
	}
}
