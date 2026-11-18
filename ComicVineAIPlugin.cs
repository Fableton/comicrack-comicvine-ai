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
	/// </summary>
	public class ComicVineAIPlugin : DotNetPlugin, IBooks, IEditor, IConfig
	{
		public override string Key { get; set; } = "ComicVineAI";

		public ComicVineAIPlugin()
		{
			Name = "Comic Vine AI";
			Description = Loc.T("plugin.description") + " (v" + AppInfo.Version + ")";
			Image = "comicvineai.png"; // embedded in the DLL: this is what makes ComicRack show the toolbar button
		}

		protected override void OnLoad()
		{
			Log($"{AppInfo.NameAndVersion} loaded from {ScriptPath}");
		}

		// IBooks: the selected books of the library.
		public void Execute(ComicBook[] books) => Run(books, fromEditor: false);

		// IEditor: the book of the information dialog. The user asked for this book explicitly, so it is not skipped
		// when it was scraped before.
		void IEditor.Execute(ComicBook[] books) => Run(books, fromEditor: true);

		// IConfig: the Configure action of this plugin.
		void IConfig.Execute()
		{
			UiState.Init(ScriptPath);
			PluginSettings settings = PluginSettings.Load(ScriptPath);
			Ui.FontScale = (float)Math.Max(0.8, Math.Min(2.5, settings.FontScale));
			Loc.Initialize(settings.Language, ScriptPath);
			Configure(settings);
		}

		private void Run(ComicBook[] books, bool fromEditor)
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
			new BatchRunner(ComicRack.MainWindow, ComicRack.App, settings, ScriptPath, message => Log(message)).Run(books);
		}

		private bool Configure(PluginSettings settings)
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
