using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>The settings of the plugin. Everything the plugin does is configured here; no other plugin is read.</summary>
	public sealed class SettingsForm : Form
	{
		private readonly PluginSettings settings;
		private readonly string pluginFolder;

		// general
		private readonly TextBox apiKey = new TextBox { Dock = DockStyle.Fill };
		private readonly NumericUpDown threshold = new NumericUpDown { Minimum = 50, Maximum = 100, Increment = 5 };
		private readonly CheckBox preferOriginal = new CheckBox { Text = Loc.T("settings.preferOriginal"), AutoSize = true };
		private readonly TextBox localDbPath = new TextBox { Dock = DockStyle.Fill };
		private readonly Label localDbStatus = new Label { AutoSize = true, MaximumSize = new Size(Ui.Px(700), 0) };
		private readonly TextBox localPublishers = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill };
		private readonly CheckBox preferIndividual = new CheckBox { Text = Loc.T("settings.preferIndividual"), AutoSize = true };
		private readonly CheckBox coverComparison = new CheckBox { Text = Loc.T("settings.coverComparison"), AutoSize = true };
		private readonly CheckBox skipScraped = new CheckBox { Text = Loc.T("settings.skipScraped"), AutoSize = true };
		private readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
		private readonly NumericUpDown textSize = new NumericUpDown { Minimum = 80, Maximum = 250, Increment = 10 };
		private readonly NumericUpDown maxVolumes = new NumericUpDown { Minimum = 1, Maximum = 10 };
		private readonly NumericUpDown cacheDays = new NumericUpDown { Minimum = 1, Maximum = 365 };

		// metadata
		private readonly Dictionary<string, CheckBox> fieldBoxes = new Dictionary<string, CheckBox>();
		private readonly CheckBox overwrite = new CheckBox { Text = Loc.T("settings.overwrite"), AutoSize = true };
		private readonly CheckBox ignoreBlanks = new CheckBox { Text = Loc.T("settings.ignoreBlanks"), AutoSize = true };
		private readonly CheckBox convertImprints = new CheckBox { Text = Loc.T("settings.convertImprints"), AutoSize = true };
		private readonly CheckBox keyInTags = new CheckBox { Text = Loc.T("settings.keyInTags"), AutoSize = true };
		private readonly CheckBox keyInNotes = new CheckBox { Text = Loc.T("settings.keyInNotes"), AutoSize = true };
		private readonly CheckBox noteDate = new CheckBox { Text = Loc.T("settings.noteDate"), AutoSize = true };
		private readonly TextBox aliases = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill };
		private readonly TextBox imprints = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill };

		// claude
		private readonly CheckBox useClaude = new CheckBox { Text = Loc.T("settings.useClaude"), AutoSize = true };
		private readonly TextBox claudePath = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox claudeModel = new TextBox { Dock = DockStyle.Fill };
		private readonly Label claudeStatus = new Label { AutoSize = true };

		public SettingsForm(PluginSettings settings, string pluginFolder = null)
		{
			this.settings = settings;
			this.pluginFolder = pluginFolder;
			Font = Ui.CreateFont();
			Text = Loc.T("settings.title") + " (v" + AppInfo.Version + ")";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = Ui.Px(760, 600);
			ShowInTaskbar = false;
			MinimizeBox = false;
			UiState.Attach(this, "settings", new Size(900, 700));

			TabControl tabs = new TabControl { Dock = DockStyle.Fill };
			tabs.TabPages.Add(BuildGeneral());
			tabs.TabPages.Add(BuildMetadata());
			tabs.TabPages.Add(BuildClaude());

			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
			Button cancel = new Button { Text = Loc.T("settings.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
			Button ok = new Button { Text = Loc.T("settings.save"), AutoSize = true };
			ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);

			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.Px(10)) };
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.Controls.Add(tabs, 0, 0);
			root.Controls.Add(buttons, 0, 1);
			Controls.Add(root);
			AcceptButton = ok;
			CancelButton = cancel;
			LoadValues();
		}

		// ---- tabs --------------------------------------------------------------------------------------------

		private TabPage BuildGeneral()
		{
			TableLayoutPanel table = NewTable();
			AddRow(table, Loc.T("settings.apiKey"), apiKey);

			Button browseDb = new Button { Text = "...", AutoSize = true };
			browseDb.Click += (s, e) =>
			{
				using (OpenFileDialog dialog = new OpenFileDialog { Filter = Loc.T("settings.localDbFilter") })
				{
					if (dialog.ShowDialog(this) == DialogResult.OK)
					{
						localDbPath.Text = dialog.FileName;
						UpdateLocalDbStatus();
					}
				}
			};
			localDbPath.Leave += (s, e) => UpdateLocalDbStatus();
			AddRow(table, Loc.T("settings.localDb"), localDbPath, 0, browseDb);
			AddWide(table, localDbStatus);
			AddRow(table, Loc.T("settings.threshold"), threshold);
			AddWide(table, preferOriginal);
			AddBlock(table, Loc.T("settings.localPublishers"), localPublishers, Ui.Px(110));
			AddWide(table, preferIndividual);
			AddWide(table, coverComparison);
			AddWide(table, skipScraped);
			AddRow(table, Loc.T("settings.textSize"), textSize);
			AddRow(table, Loc.T("settings.language"), language);
			AddRow(table, Loc.T("settings.maxVolumes"), maxVolumes);
			AddRow(table, Loc.T("settings.cacheDays"), cacheDays);
			return Page(Loc.T("settings.tab.general"), table);
		}

		private TabPage BuildMetadata()
		{
			TableLayoutPanel table = NewTable();

			Button all = new Button { Text = Loc.T("settings.selectAll"), AutoSize = true };
			Button none = new Button { Text = Loc.T("settings.selectNone"), AutoSize = true };
			all.Click += (s, e) => fieldBoxes.Values.ToList().ForEach(b => b.Checked = true);
			none.Click += (s, e) => fieldBoxes.Values.ToList().ForEach(b => b.Checked = false);
			FlowLayoutPanel head = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
			head.Controls.Add(new Label { Text = Loc.T("settings.fields"), AutoSize = true, Margin = new Padding(Ui.Px(3), Ui.Px(7), Ui.Px(12), 0), Font = Ui.CreateFont(FontStyle.Bold) });
			head.Controls.Add(all);
			head.Controls.Add(none);
			AddWide(table, head);

			TableLayoutPanel grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
			for (int c = 0; c < 3; c++)
			{
				grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
			}
			int index = 0;
			foreach (string field in WriteOptions.Fields)
			{
				CheckBox box = new CheckBox { Text = Loc.T("field." + field), AutoSize = true, Margin = new Padding(Ui.Px(3), Ui.Px(2), Ui.Px(12), Ui.Px(2)) };
				fieldBoxes[field] = box;
				grid.Controls.Add(box, index % 3, index / 3);
				index++;
			}
			AddWide(table, grid);

			AddWide(table, overwrite);
			AddWide(table, ignoreBlanks);
			AddWide(table, convertImprints);
			AddWide(table, keyInTags);
			AddWide(table, keyInNotes);
			AddWide(table, noteDate);
			AddBlock(table, Loc.T("settings.aliases"), aliases, Ui.Px(64));
			AddBlock(table, Loc.T("settings.imprints"), imprints, Ui.Px(64));
			return Page(Loc.T("settings.tab.metadata"), table);
		}

		private TabPage BuildClaude()
		{
			TableLayoutPanel table = NewTable();
			AddWide(table, useClaude);

			Button browse = new Button { Text = "...", AutoSize = true };
			browse.Click += (s, e) =>
			{
				using (OpenFileDialog dialog = new OpenFileDialog { Filter = "claude|claude.exe;claude.cmd|All files|*.*" })
				{
					if (dialog.ShowDialog(this) == DialogResult.OK)
					{
						claudePath.Text = dialog.FileName;
						UpdateClaudeStatus();
					}
				}
			};
			Button detect = new Button { Text = Loc.T("settings.detect"), AutoSize = true };
			detect.Click += (s, e) => UpdateClaudeStatus();
			AddRow(table, Loc.T("settings.claudePath"), claudePath, 0, browse);
			AddRow(table, Loc.T("settings.claudeModel"), claudeModel, 0, detect);
			AddWide(table, claudeStatus);
			return Page(Loc.T("settings.tab.claude"), table);
		}

		private static TabPage Page(string title, TableLayoutPanel table)
		{
			TabPage page = new TabPage(title) { AutoScroll = true, Padding = new Padding(Ui.Px(8)) };
			page.Controls.Add(table);
			// the table is as wide as the page, so the boxes use the whole width instead of shrinking to their label
			page.Resize += (s, e) => table.Width = Math.Max(Ui.Px(300), page.ClientSize.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
			return page;
		}

		private static TableLayoutPanel NewTable()
		{
			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			return table;
		}

		private static void AddRow(TableLayoutPanel table, string label, Control control, int height = 0, Control extra = null)
		{
			int row = table.RowCount++;
			table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = height > 0 ? AnchorStyles.Top | AnchorStyles.Left : AnchorStyles.Left, Margin = new Padding(Ui.Px(3), Ui.Px(6), Ui.Px(12), Ui.Px(3)) }, 0, row);
			if (height > 0)
			{
				control.Height = height;
				control.MinimumSize = new Size(0, height);
			}
			control.Margin = new Padding(Ui.Px(3), Ui.Px(3), Ui.Px(3), Ui.Px(3));
			table.Controls.Add(control, 1, row);
			if (extra != null)
			{
				table.Controls.Add(extra, 2, row);
			}
		}

		/// <summary>A multi line box: the label above it and the box as wide as the page.</summary>
		private static void AddBlock(TableLayoutPanel table, string label, Control control, int height)
		{
			AddWide(table, new Label { Text = label, AutoSize = true });
			control.Height = height;
			control.MinimumSize = new Size(0, height);
			AddWide(table, control);
		}

		private static void AddWide(TableLayoutPanel table, Control control)
		{
			int row = table.RowCount++;
			control.Margin = new Padding(Ui.Px(3), Ui.Px(5), Ui.Px(3), Ui.Px(3));
			table.Controls.Add(control, 0, row);
			table.SetColumnSpan(control, 3);
		}

		// ---- values ------------------------------------------------------------------------------------------

		private void LoadValues()
		{
			apiKey.Text = settings.ComicVineApiKey;
			localDbPath.Text = settings.LocalDbPath;
			UpdateLocalDbStatus();
			threshold.Value = (decimal)Math.Max(50, Math.Min(100, Math.Round(settings.AcceptThreshold * 100)));
			preferOriginal.Checked = settings.PreferOriginal;
			preferIndividual.Checked = settings.PreferIndividualIssues;
			localPublishers.Text = Crlf(settings.LocalPublishers);
			coverComparison.Checked = settings.UseCoverComparison;
			skipScraped.Checked = settings.SkipAlreadyScraped;

			language.Items.Add(new LanguageItem("auto", Loc.T("settings.languageAuto")));
			foreach (string[] item in Loc.Available(pluginFolder))
			{
				language.Items.Add(new LanguageItem(item[0], item[1]));
			}
			language.SelectedItem = language.Items.Cast<LanguageItem>().FirstOrDefault(l => l.Code == (settings.Language ?? "auto")) ?? language.Items[0];
			textSize.Value = (decimal)Math.Max(80, Math.Min(250, Math.Round(settings.FontScale * 100)));
			maxVolumes.Value = Math.Max(1, Math.Min(10, settings.MaxVolumeCandidates));
			cacheDays.Value = Math.Max(1, Math.Min(365, settings.CacheDays));

			foreach (KeyValuePair<string, CheckBox> pair in fieldBoxes)
			{
				pair.Value.Checked = !(settings.DisabledFields ?? new List<string>()).Contains(pair.Key);
			}
			overwrite.Checked = settings.OverwriteExisting;
			ignoreBlanks.Checked = settings.IgnoreBlanks;
			convertImprints.Checked = settings.ConvertImprints;
			keyInTags.Checked = settings.KeyInTags;
			keyInNotes.Checked = settings.KeyInNotes;
			noteDate.Checked = settings.NoteScrapeDate;
			aliases.Text = Crlf(settings.PublisherAliases);
			imprints.Text = Crlf(settings.CustomImprints);

			useClaude.Checked = settings.UseClaude;
			claudePath.Text = settings.ClaudePath;
			claudeModel.Text = settings.ClaudeModel;
			UpdateClaudeStatus();
		}

		/// <summary>Opens the database to say what is in it (or why it cannot be used).</summary>
		private void UpdateLocalDbStatus()
		{
			string path = localDbPath.Text.Trim();
			localDbStatus.ForeColor = SystemColors.ControlText;
			if (path.Length == 0)
			{
				localDbStatus.Text = Loc.T("settings.localDbNone");
				return;
			}
			using (new WaitCursor(this))
			using (LocalDb db = LocalDb.TryOpen(path, out string error))
			{
				if (db == null)
				{
					localDbStatus.Text = Loc.T("settings.localDbError", error);
					localDbStatus.ForeColor = Color.Firebrick;
				}
				else
				{
					localDbStatus.Text = Loc.T("settings.localDbOk", db.Volumes.ToString("N0"), db.Issues.ToString("N0"), db.SyncDate);
				}
			}
		}

		private sealed class WaitCursor : IDisposable
		{
			private readonly Control control;
			private readonly Cursor previous;

			public WaitCursor(Control control)
			{
				this.control = control;
				previous = control.Cursor;
				control.Cursor = Cursors.WaitCursor;
			}

			public void Dispose() => control.Cursor = previous;
		}

		private static string Crlf(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

		private void UpdateClaudeStatus()
		{
			string found = ClaudeCli.FindExecutable(new PluginSettings { ClaudePath = claudePath.Text });
			claudeStatus.Text = found == null ? Loc.T("settings.claudeNotFound") : Loc.T("settings.claudeFound", found);
			claudeStatus.ForeColor = found == null ? Color.Firebrick : SystemColors.ControlText;
		}

		private void Apply()
		{
			settings.ComicVineApiKey = apiKey.Text.Trim();
			settings.LocalDbPath = localDbPath.Text.Trim();
			settings.AcceptThreshold = (double)threshold.Value / 100.0;
			settings.PreferOriginal = preferOriginal.Checked;
			settings.PreferIndividualIssues = preferIndividual.Checked;
			settings.LocalPublishers = localPublishers.Text;
			settings.UseCoverComparison = coverComparison.Checked;
			settings.SkipAlreadyScraped = skipScraped.Checked;
			settings.Language = (language.SelectedItem as LanguageItem)?.Code ?? "auto";
			settings.FontScale = (double)textSize.Value / 100.0;
			Ui.FontScale = (float)settings.FontScale;
			settings.MaxVolumeCandidates = (int)maxVolumes.Value;
			settings.CacheDays = (int)cacheDays.Value;

			settings.DisabledFields = fieldBoxes.Where(p => !p.Value.Checked).Select(p => p.Key).ToList();
			settings.OverwriteExisting = overwrite.Checked;
			settings.IgnoreBlanks = ignoreBlanks.Checked;
			settings.ConvertImprints = convertImprints.Checked;
			settings.KeyInTags = keyInTags.Checked;
			settings.KeyInNotes = keyInNotes.Checked;
			settings.NoteScrapeDate = noteDate.Checked;
			settings.PublisherAliases = aliases.Text;
			settings.CustomImprints = imprints.Text;

			settings.UseClaude = useClaude.Checked;
			settings.ClaudePath = claudePath.Text.Trim();
			settings.ClaudeModel = claudeModel.Text.Trim();
		}

		private sealed class LanguageItem
		{
			public readonly string Code;
			private readonly string name;

			public LanguageItem(string code, string name)
			{
				Code = code;
				this.name = name;
			}

			public override string ToString() => name;
		}
	}
}
