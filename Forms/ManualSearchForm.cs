using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineAI
{
	/// <summary>
	/// Manual rescue for a book that was not identified with enough certainty. It shows what the AI and the search found, lets the
	/// user look through the pages of the comic (to find the real cover, or the page where the publisher prints the data) with zoom,
	/// and search Comic Vine by series, number, publisher and year.
	/// The search works in two steps: a list of series, where the one that is selected shows its cover, and (double click or the
	/// button) the list of the issues of that series, where the right one is chosen.
	/// </summary>
	public sealed class ManualSearchForm : Form
	{
		private readonly ComicVineClient comicVine;
		private readonly PluginSettings settings;
		private readonly Func<int, Bitmap> getPage;
		private readonly int pageCount;
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

		private readonly TextBox series = new TextBox();
		private readonly TextBox number = new TextBox();
		private readonly TextBox year = new TextBox();
		private readonly TextBox publisher = new TextBox();
		private readonly Button searchButton = new Button { Text = Loc.T("manual.search"), AutoSize = true };
		private readonly ListView volumeList = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, Dock = DockStyle.Fill };
		private readonly ListView issueList = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
		private readonly TableLayoutPanel issuesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Visible = false };
		private readonly Label issuesHeader = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = Ui.CreateFont(FontStyle.Bold) };
		private readonly Button backButton = new Button { Text = Loc.T("manual.backToSeries"), AutoSize = true };
		private readonly ZoomPictureBox bookView = new ZoomPictureBox();
		private readonly ZoomPictureBox issueView = new ZoomPictureBox();
		private readonly NumericUpDown pageBox = new NumericUpDown { Minimum = 1 };
		private readonly Label pageTotal = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
		private readonly Button firstPage = new Button { Text = "|◀", AutoSize = true };
		private readonly Button lastPage = new Button { Text = "▶|", AutoSize = true };
		private readonly Button previousPage = new Button { Text = "◀", AutoSize = true };
		private readonly Button nextPage = new Button { Text = "▶", AutoSize = true };
		private readonly Label issueInfo = new Label { Dock = DockStyle.Fill, AutoSize = true };
		private readonly LinkLabel issueLink = new LinkLabel { AutoSize = true, Visible = false, Text = Loc.T("manual.openIssue"), Margin = new Padding(Ui.Px(3), 0, Ui.Px(3), Ui.Px(4)) };
		private readonly Label status = new Label { Dock = DockStyle.Fill, AutoSize = true };
		private readonly Button navButton = new Button { Text = Loc.T("manual.showIssues"), AutoSize = true, Enabled = false };
		private readonly Button useButton = new Button { Text = Loc.T("manual.use"), AutoSize = true, Enabled = false };
		private readonly Button useRestButton = new Button { AutoSize = true, Enabled = false, Visible = false };
		private readonly Button aiButton = new Button { Text = Loc.T("manual.ai"), AutoSize = true };
		private readonly Button editButton = new Button { Text = Loc.T("manual.edit"), AutoSize = true, Enabled = false };
		private List<IssueMatch> candidates = new List<IssueMatch>(); // what the search found for this book, with its match percentage

		private readonly CheckBox hideUnwanted = new CheckBox { Text = Loc.T("manual.hideUnwanted"), Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
		private readonly Button previousBookButton = new Button { Text = Loc.T("manual.previousBook"), AutoSize = true };
		private readonly Button coverButton = new Button { Text = Loc.T("manual.otherCovers"), AutoSize = true, Visible = false, Anchor = AnchorStyles.Left };
		private readonly ContextMenuStrip volumeMenu = new ContextMenuStrip();
		private readonly ContextMenuStrip pageMenu = new ContextMenuStrip();
		private readonly CheckBox isCoverBox = new CheckBox { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = Ui.Px(24), Margin = new Padding(Ui.Px(3), Ui.Px(3), Ui.Px(3), Ui.Px(3)) };
		private readonly HashSet<int> autoCoverPages = new HashSet<int>(); // pages marked as the cover because they look like the Comic Vine cover
		private bool showingCoverBox;
		private readonly ToolTip coverTip = new ToolTip();
		private const double CoverAgrees = 0.62; // the similarity from which the page on screen is taken for the cover (the engine's cover floor)
		private readonly Label pageTypeLabel = new Label { AutoSize = true, Visible = false, ForeColor = Color.DarkBlue, Margin = new Padding(Ui.Px(8), Ui.Px(7), Ui.Px(3), 0) };
		private List<VolumeInfo> allVolumes = new List<VolumeInfo>();
		private List<string> coverUrls; // the main cover and the alternatives of the selected issue, once they are known
		private int coverIndex;

		private readonly Label knownLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(Ui.Px(1150), 0) };
		private readonly Button useCoverButton = new Button { Text = Loc.T("manual.usePageAsCover"), AutoSize = true };
		private readonly Label coverPageLabel = new Label { AutoSize = true, Visible = false, ForeColor = Color.Firebrick, Margin = new Padding(Ui.Px(8), Ui.Px(7), Ui.Px(3), 0) };
		private BookHints bookHints;

		private CoverFingerprint bookFingerprint;
		private int lookupVersion;
		private int currentPage;
		private IssueInfo foundIssue;
		private VolumeInfo foundVolume;
		private VolumeInfo browsing; // the series whose issues are listed
		private bool inIssues;
		private double similarity = -1;
		private bool loadingPage;

		/// <summary>The volume remembered for the series name of the book: it is shown selected, ready to confirm.</summary>
		public VolumeInfo SuggestedVolume { get; set; }

		/// <summary>
		/// The suggested volume is the same one the previous book was just chosen with by hand: the issue list opens right away
		/// (once), so the user only has to pick the number.
		/// </summary>
		public bool OpenIssuesForSuggested { get; set; }

		/// <summary>Says if a publisher is in the list of unwanted ones.</summary>
		public Func<string, bool> IsUnwantedPublisher { get; set; } = publisher => false;

		/// <summary>Adds a publisher to the list of unwanted ones (true) or removes it (false).</summary>
		public Action<string, bool> SetUnwantedPublisher { get; set; } = (publisher, add) => { };

		/// <summary>
		/// Searches the book again with a page as its cover (the page, a way to tell about progress, the cancellation) and says what was
		/// found. Set by the caller; without it the button is not there. Runs on a worker thread.
		/// </summary>
		public Func<Bitmap, Action<string>, CancellationToken, IdentifyResult> Rescan { get; set; }

		/// <summary>There is a previous book in the review to go back to.</summary>
		public bool CanGoBack { get; set; }

		public bool CancelAll { get; private set; }

		/// <summary>The types of page that the user chose with the menu of the page (page index, 0 based). They are applied to the book when it is tagged.</summary>
		public Dictionary<int, ComicPageType> PageTypeChanges { get; } = new Dictionary<int, ComicPageType>();

		/// <summary>How many of the books that are left in the review are of the same series as this one. Set by the caller; 0 hides the button.</summary>
		public Func<int> CountRemaining { get; set; }

		/// <summary>The user chose "use for the rest": after this book, the books of the same series are tagged with the number of each one.</summary>
		public bool ApplyToRemaining { get; private set; }

		/// <summary>
		/// Asks Claude about a cover (the image, the context that the user wrote, the cancellation) and gives its answer. Set by the caller; without it
		/// the "research with AI" button is disabled. Runs on a worker thread.
		/// </summary>
		public Func<Bitmap, string, CancellationToken, ClaudeSuggestion> AskClaude { get; set; }

		/// <summary>Why Claude cannot be used (shown as the tooltip of the disabled AI button), when <see cref="AskClaude"/> is not set.</summary>
		public string AiUnavailableReason { get; set; } = "";

		/// <summary>
		/// What the user changed or typed in the edit window. When the form closes with OK and this is set, it replaces what Comic Vine says
		/// for the selected issue; when there is no selected issue (the AI path) it is the only data and the book is not linked to Comic Vine.
		/// </summary>
		public ManualEdit Edit { get; private set; }
		public VolumeInfo SelectedVolume => foundVolume;
		public IssueInfo SelectedIssue => foundIssue;

		/// <param name="getPage">Gives the page of the comic (0 based), or null. Called on the UI thread.</param>
		/// <param name="pageCount">Number of pages of the comic, 0 when unknown.</param>
		public ManualSearchForm(string title, IdentifyResult result, Func<int, Bitmap> getPage, int pageCount, ComicVineClient comicVine, PluginSettings settings)
		{
			this.comicVine = comicVine;
			this.settings = settings;
			this.getPage = getPage;
			this.pageCount = Math.Max(0, pageCount);

			Font = Ui.CreateFont();
			Text = Loc.T("manual.title", title) + " (v" + AppInfo.Version + ")";
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = Ui.Px(900, 600);
			ShowInTaskbar = false;
			KeyPreview = true;
			UiState.Attach(this, "manual", new Size(1300, 880));

			// the search starts from the best idea available: Claude, then the book itself
			BookHints hints = result.Hints ?? new BookHints();
			bookHints = hints;
			candidates = result.Candidates ?? new List<IssueMatch>();
			ClaudeSuggestion claude = result.Claude;
			if (claude != null && claude.IsUsable)
			{
				series.Text = claude.Series;
				number.Text = NumberToStart(bookHints, claude);
				year.Text = ""; // the year Claude gives is often wrong, and the list is ordered by it before the name
				publisher.Text = claude.Publisher;
			}
			else
			{
				series.Text = hints.Series;
				number.Text = hints.Number;
				year.Text = settings.PreferOriginal || hints.Year <= 0 ? "" : hints.Year.ToString();
				publisher.Text = settings.PreferOriginal ? "" : hints.Publisher;
			}

			BuildLayout(hints, result);

			volumeList.Columns.Add(Loc.T("manual.col.series"), Ui.Px(200));
			volumeList.Columns.Add(Loc.T("manual.col.year"), Ui.Px(55));
			volumeList.Columns.Add(Loc.T("manual.col.publisher"), Ui.Px(120));
			volumeList.Columns.Add(Loc.T("manual.col.issues"), Ui.Px(55));
			volumeList.Columns.Add(Loc.T("manual.col.match"), Ui.Px(60));
			issueList.Columns.Add(Loc.T("manual.col.number"), Ui.Px(60));
			issueList.Columns.Add(Loc.T("manual.col.title"), Ui.Px(220));
			issueList.Columns.Add(Loc.T("manual.col.coverDate"), Ui.Px(100));
			volumeList.Resize += (s, e) => FitColumns(volumeList, 0);
			issueList.Resize += (s, e) => FitColumns(issueList, 1);
			MakeSortable(volumeList, 1, 3, 4);
			MakeSortable(issueList, 0);

			volumeList.SelectedIndexChanged += (s, e) => OnVolumeSelected();
			volumeList.DoubleClick += (s, e) => EnterIssues();
			issueList.SelectedIndexChanged += (s, e) => OnIssueSelected();
			issueList.DoubleClick += (s, e) => UseIssue();
			navButton.Click += (s, e) => { if (inIssues) { LeaveIssues(); } else { EnterIssues(); } };
			previousBookButton.Click += (s, e) => DialogResult = DialogResult.Retry; // the review goes back one book
			useCoverButton.Click += (s, e) => UsePageAsCover();
			coverButton.Click += (s, e) => NextCover();
			volumeList.ContextMenuStrip = volumeMenu;
			volumeMenu.Opening += (s, e) => PrepareMenu(e);
			BuildPageMenu();
			hideUnwanted.CheckedChanged += (s, e) => ShowVolumes(allVolumes);
			backButton.Click += (s, e) => LeaveIssues();
			searchButton.Click += (s, e) => Search();
			useButton.Click += (s, e) => UseIssue();
			useRestButton.Click += (s, e) => UseForRemaining();
			aiButton.Click += (s, e) => ResearchWithAi();
			editButton.Click += (s, e) => EditSelected();
			AcceptButton = searchButton;
			number.Leave += (s, e) =>
			{
				if (inIssues)
				{
					SelectIssueByNumber();
				}
				else
				{
					OnVolumeSelected();
				}
			};

			firstPage.Click += (s, e) => GoToPage(0);
			lastPage.Click += (s, e) => GoToPage(pageCount - 1);
			previousPage.Click += (s, e) => GoToPage(currentPage - 1);
			nextPage.Click += (s, e) => GoToPage(currentPage + 1);
			pageBox.ValueChanged += (s, e) =>
			{
				if (!loadingPage)
				{
					GoToPage((int)pageBox.Value - 1);
				}
			};

			FormClosed += (s, e) => cancellation.Cancel();
			Shown += (s, e) =>
			{
				useCoverButton.Visible = Rescan != null; // set by the caller after the window is built
				aiButton.Enabled = AskClaude != null; // without Claude the button is there, disabled, and says why
				if (AskClaude == null && AiUnavailableReason.Length > 0 && aiButton.Parent != null)
				{
					// a disabled button shows no tooltip of its own: its container shows the reason while the mouse is over it
					Control host = aiButton.Parent;
					ToolTip why = new ToolTip();
					bool shown = false;
					host.MouseMove += (s2, e2) =>
					{
						bool over = aiButton.Bounds.Contains(e2.Location);
						if (over && !shown)
						{
							why.Show(AiUnavailableReason, host, aiButton.Left, aiButton.Bottom + Ui.Px(4), 8000);
						}
						else if (!over && shown)
						{
							why.Hide(host);
						}
						shown = over;
					};
					host.MouseLeave += (s2, e2) =>
					{
						why.Hide(host);
						shown = false;
					};
				}
				int remaining = CountRemaining?.Invoke() ?? 0;
				useRestButton.Text = Loc.T("manual.useRest", remaining);
				useRestButton.Visible = remaining > 0;
				previousBookButton.Enabled = CanGoBack;
				GoToPage(0);
				if (SuggestedVolume != null)
				{
					series.Text = SuggestedVolume.Name;
					year.Text = SuggestedVolume.StartYear > 0 ? SuggestedVolume.StartYear.ToString() : "";
					publisher.Text = "";
				}
				Search();
			};
		}

		private void BuildLayout(BookHints hints, IdentifyResult result)
		{
			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.Px(12)) };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			// what we know
			knownLabel.Text = KnownText(hints, result);
			root.Controls.Add(knownLabel, 0, 0);

			// search fields
			TableLayoutPanel search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, RowCount = 1, AutoSize = true };
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.Px(80)));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.Px(80)));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			foreach (TextBox box in new[] { series, number, year, publisher })
			{
				box.Dock = DockStyle.Fill;
			}
			AddField(search, 0, Loc.T("manual.series"), series);
			AddField(search, 2, Loc.T("manual.issue"), number);
			AddField(search, 4, Loc.T("manual.year"), year);
			AddField(search, 6, Loc.T("manual.publisher"), publisher);
			search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			search.Controls.Add(searchButton, 8, 0);
			search.Controls.Add(hideUnwanted, 9, 0);
			root.Controls.Add(search, 0, 1);

			// your comic (pages, zoom) | series, then the issues of one | cover and data of what is selected
			bookView.Dock = DockStyle.Fill;
			issueView.Dock = DockStyle.Fill;

			TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			isCoverBox.Text = Loc.T("manual.isCover");
			isCoverBox.CheckedChanged += (s, e) =>
			{
				if (showingCoverBox)
				{
					return;
				}
				// the user decides: from now on this page keeps what was chosen, whatever the covers say
				autoCoverPages.Remove(currentPage);
				PageTypeChanges[currentPage] = isCoverBox.Checked ? ComicPageType.FrontCover : ComicPageType.Story;
				ShowPageType();
			};
			left.Controls.Add(isCoverBox, 0, 0);
			left.Controls.Add(bookView, 0, 1);
			left.Controls.Add(BuildPageBar(), 0, 2);
			new ToolTip().SetToolTip(bookView, Loc.T("manual.tooltip"));

			issuesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			issuesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			issuesPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			issuesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			issuesPanel.Controls.Add(backButton, 0, 0);
			issuesPanel.Controls.Add(issuesHeader, 1, 0);
			issuesPanel.Controls.Add(issueList, 0, 1);
			issuesPanel.SetColumnSpan(issueList, 2);
			Panel listHost = new Panel { Dock = DockStyle.Fill };
			listHost.Controls.Add(volumeList);
			listHost.Controls.Add(issuesPanel);

			TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
			right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			right.Controls.Add(issueView, 0, 0);
			right.Controls.Add(coverButton, 0, 1);
			right.Controls.Add(issueInfo, 0, 2);
			right.Controls.Add(issueLink, 0, 3);
			issueLink.LinkClicked += (s, e) => OpenIssuePage();
			right.Resize += (s, e) => issueInfo.MaximumSize = new Size(Math.Max(Ui.Px(120), right.ClientSize.Width - Ui.Px(8)), 0); // long names wrap instead of being cut

			SplitContainer inner = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
			inner.Panel1.Controls.Add(listHost);
			inner.Panel2.Controls.Add(right);
			SplitContainer outer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
			outer.Panel1.Controls.Add(left);
			outer.Panel2.Controls.Add(inner);
			root.Controls.Add(outer, 0, 2);
			UiState.AttachSplitter(this, outer, "manual.books", 520, 260, 380);
			UiState.AttachSplitter(this, inner, "manual.volumes", 460, 240, 170);

			// buttons
			TableLayoutPanel bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 9, RowCount = 1, AutoSize = true };
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			for (int i = 0; i < 8; i++)
			{
				bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			}
			Button skip = new Button { Text = Loc.T("manual.skip"), AutoSize = true, DialogResult = DialogResult.Ignore };
			Button stop = new Button { Text = Loc.T("manual.stop"), AutoSize = true };
			stop.Click += (s, e) => { CancelAll = true; DialogResult = DialogResult.Cancel; };
			bottom.Controls.Add(status, 0, 0);
			bottom.Controls.Add(previousBookButton, 1, 0);
			bottom.Controls.Add(aiButton, 2, 0);
			bottom.Controls.Add(editButton, 3, 0);
			bottom.Controls.Add(navButton, 4, 0);
			bottom.Controls.Add(useButton, 5, 0);
			bottom.Controls.Add(useRestButton, 6, 0);
			bottom.Controls.Add(skip, 7, 0);
			bottom.Controls.Add(stop, 8, 0);
			root.Controls.Add(bottom, 0, 3);

			Controls.Add(root);
		}

		private static string KnownText(BookHints hints, IdentifyResult result)
		{
			string known = Loc.T("manual.file", hints.FileName) + "\r\n" + Loc.T("manual.current", hints);
			if (result.Claude != null)
			{
				known += "\r\n" + Loc.T("manual.claude", result.Claude);
			}
			if (result.Best != null)
			{
				known += "\r\n" + Loc.T("manual.best", result.Best);
			}
			return known;
		}

		/// <summary>Previous / next page, the page number and the zoom buttons of the comic.</summary>
		private Control BuildPageBar()
		{
			FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
			pageBox.Width = Ui.Px(62);
			pageBox.Maximum = Math.Max(1, pageCount);
			pageBox.Anchor = AnchorStyles.Left;
			pageTotal.Text = pageCount > 0 ? "/ " + pageCount : "";
			pageTotal.Margin = new Padding(Ui.Px(3), Ui.Px(7), Ui.Px(12), 0);

			Button fit = new Button { Text = Loc.T("manual.fit"), AutoSize = true };
			Button actual = new Button { Text = "100%", AutoSize = true };
			Button zoomIn = new Button { Text = "+", AutoSize = true };
			Button zoomOut = new Button { Text = "−", AutoSize = true };
			fit.Click += (s, e) => bookView.FitToWindow();
			actual.Click += (s, e) => bookView.ActualSize();
			zoomIn.Click += (s, e) => bookView.ZoomBy(1.4f);
			zoomOut.Click += (s, e) => bookView.ZoomBy(1f / 1.4f);

			ToolTip tips = new ToolTip();
			tips.SetToolTip(firstPage, Loc.T("manual.first"));
			tips.SetToolTip(lastPage, Loc.T("manual.last"));
			bar.Controls.Add(new Label { Text = Loc.T("manual.page"), AutoSize = true, Margin = new Padding(Ui.Px(3), Ui.Px(7), Ui.Px(3), 0) });
			bar.Controls.Add(firstPage);
			bar.Controls.Add(previousPage);
			bar.Controls.Add(pageBox);
			bar.Controls.Add(nextPage);
			bar.Controls.Add(lastPage);
			bar.Controls.Add(pageTotal);
			bar.Controls.Add(zoomOut);
			bar.Controls.Add(zoomIn);
			bar.Controls.Add(fit);
			bar.Controls.Add(actual);
			bar.Controls.Add(useCoverButton);
			bar.Controls.Add(coverPageLabel);
			bar.Controls.Add(pageTypeLabel);
			return bar;
		}

		private static void AddField(TableLayoutPanel table, int column, string label, Control box)
		{
			table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Ui.Px(8), Ui.Px(6), Ui.Px(3), Ui.Px(3)) }, column, 0);
			table.Controls.Add(box, column + 1, 0);
		}

		// ---- lists: width and sorting --------------------------------------------------------------------------

		/// <summary>The column with the names takes what the others leave, so the names are not cut.</summary>
		private static void FitColumns(ListView list, int stretchColumn)
		{
			if (list.Columns.Count == 0 || list.ClientSize.Width <= 0)
			{
				return;
			}
			int others = list.Columns.Cast<ColumnHeader>().Where((c, i) => i != stretchColumn).Sum(c => c.Width);
			list.Columns[stretchColumn].Width = Math.Max(Ui.Px(120), list.ClientSize.Width - others);
		}

		/// <summary>Click on a column header to sort by it, again to reverse. Numeric columns sort as numbers.</summary>
		private static void MakeSortable(ListView list, params int[] numericColumns)
		{
			ColumnSorter sorter = new ColumnSorter(numericColumns);
			list.ColumnClick += (s, e) =>
			{
				sorter.Descending = sorter.Column == e.Column && !sorter.Descending;
				sorter.Column = e.Column;
				list.ListViewItemSorter = sorter;
				list.Sort();
			};
		}

		private sealed class ColumnSorter : IComparer
		{
			private readonly HashSet<int> numeric;
			public int Column = -1;
			public bool Descending;

			public ColumnSorter(int[] numericColumns)
			{
				numeric = new HashSet<int>(numericColumns);
			}

			public int Compare(object a, object b)
			{
				if (Column < 0)
				{
					return 0;
				}
				string x = ((ListViewItem)a).SubItems[Column].Text.TrimEnd('%', ' ');
				string y = ((ListViewItem)b).SubItems[Column].Text.TrimEnd('%', ' ');
				int result;
				if (numeric.Contains(Column) && double.TryParse(TextMatch.NormalizeNumber(x), NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) && double.TryParse(TextMatch.NormalizeNumber(y), NumberStyles.Float, CultureInfo.InvariantCulture, out double dy))
				{
					result = dx.CompareTo(dy);
				}
				else
				{
					result = string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
				}
				return Descending ? -result : result;
			}
		}

		// ---- Pages of the comic ------------------------------------------------------------------------------

		/// <summary>Shows a page. The covers are compared with the page that is on screen, so browsing to the real cover improves the match.</summary>
		private void GoToPage(int index)
		{
			if (loadingPage || index < 0 || (pageCount > 0 && index >= pageCount))
			{
				return;
			}
			loadingPage = true;
			try
			{
				using (new WaitCursor(this))
				{
					Bitmap page = getPage?.Invoke(index);
					if (page == null)
					{
						status.Text = Loc.T("manual.pageUnreadable", index + 1);
						return;
					}
					currentPage = index;
					bookView.Image = page;
					bookFingerprint = CoverFingerprint.From(page);
				}
				pageBox.Value = Math.Min(pageBox.Maximum, index + 1);
				ShowPageType();
				firstPage.Enabled = previousPage.Enabled = index > 0;
				nextPage.Enabled = pageCount == 0 || index < pageCount - 1;
				lastPage.Enabled = pageCount > 0 && index < pageCount - 1; // the last page is only known when the comic says how many it has
				UpdateInfo();
			}
			finally
			{
				loadingPage = false;
			}
		}

		/// <summary>The menu of the image of the comic: the type of the page on screen, and copying the image.</summary>
		private void BuildPageMenu()
		{
			ToolStripMenuItem types = new ToolStripMenuItem(Loc.T("manual.pageTypeMenu"));
			foreach (ComicPageType type in PageTypes.Choices)
			{
				ComicPageType chosen = type;
				ToolStripMenuItem item = new ToolStripMenuItem(PageTypeName(type)) { Tag = type };
				item.Click += (s, e) =>
				{
					autoCoverPages.Remove(currentPage);
					PageTypeChanges[currentPage] = chosen;
					ShowPageType();
				};
				types.DropDownItems.Add(item);
			}
			ToolStripMenuItem copy = new ToolStripMenuItem(Loc.T("manual.copyImage"));
			copy.Click += (s, e) => CopyPageImage();
			pageMenu.Items.Add(types);
			pageMenu.Items.Add(copy);
			pageMenu.Opening += (s, e) =>
			{
				bool hasPage = bookView.Image != null;
				types.Enabled = hasPage;
				copy.Enabled = hasPage;
				ComicPageType now = CurrentPageType();
				foreach (ToolStripMenuItem item in types.DropDownItems)
				{
					item.Checked = (ComicPageType)item.Tag == now;
				}
			};
			bookView.ContextMenuStrip = pageMenu;
		}

		/// <summary>What the user chose for the page on screen; a story page when nothing was chosen (that is what the book gets).</summary>
		private ComicPageType CurrentPageType() =>
			PageTypeChanges.TryGetValue(currentPage, out ComicPageType type) ? type : ComicPageType.Story;

		/// <summary>Says next to the page buttons that the page has a type other than a story page.</summary>
		private void ShowPageType()
		{
			ComicPageType type = CurrentPageType();
			pageTypeLabel.Text = Loc.T("manual.pageType", PageTypeName(type));
			pageTypeLabel.Visible = type != ComicPageType.Story && type != ComicPageType.FrontCover; // the cover is shown by the check box
			showingCoverBox = true;
			isCoverBox.Checked = type == ComicPageType.FrontCover;
			isCoverBox.Text = autoCoverPages.Contains(currentPage) && similarity >= 0
				? Loc.T("manual.isCoverMatched", similarity.ToString("P0"))
				: Loc.T("manual.isCover");
			coverTip.SetToolTip(isCoverBox, isCoverBox.Text); // the whole text, when the panel is too narrow for it
			showingCoverBox = false;
		}

		/// <summary>
		/// The page on screen is marked as the cover while it looks like the cover of the issue that is shown, unless the user chose its type.
		/// </summary>
		private void MarkCoverByComparison()
		{
			bool userChose = PageTypeChanges.ContainsKey(currentPage) && !autoCoverPages.Contains(currentPage);
			if (!userChose)
			{
				if (similarity >= CoverAgrees)
				{
					PageTypeChanges[currentPage] = ComicPageType.FrontCover;
					autoCoverPages.Add(currentPage);
				}
				else if (autoCoverPages.Remove(currentPage))
				{
					PageTypeChanges.Remove(currentPage);
				}
			}
			ShowPageType();
		}

		private static string PageTypeName(ComicPageType type)
		{
			switch (type)
			{
				case ComicPageType.FrontCover: return Loc.T("pagetype.frontCover");
				case ComicPageType.InnerCover: return Loc.T("pagetype.innerCover");
				case ComicPageType.Roundup: return Loc.T("pagetype.roundup");
				case ComicPageType.Advertisement: return Loc.T("pagetype.advertisement");
				case ComicPageType.Editorial: return Loc.T("pagetype.editorial");
				case ComicPageType.Letters: return Loc.T("pagetype.letters");
				case ComicPageType.Preview: return Loc.T("pagetype.preview");
				case ComicPageType.BackCover: return Loc.T("pagetype.backCover");
				case ComicPageType.Other: return Loc.T("pagetype.other");
				case ComicPageType.Deleted: return Loc.T("pagetype.deleted");
				default: return Loc.T("pagetype.story");
			}
		}

		/// <summary>Puts the page on screen in the clipboard as an image.</summary>
		private void CopyPageImage()
		{
			Bitmap page = bookView.Image;
			if (page == null)
			{
				return;
			}
			try
			{
				using (Bitmap copy = new Bitmap(page))
				{
					Clipboard.SetImage(copy);
				}
				status.Text = Loc.T("manual.imageCopied", currentPage + 1);
			}
			catch (Exception e)
			{
				status.Text = Loc.T("manual.copyFailed", e.Message); // the clipboard can be locked by another program
			}
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (!(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown))
			{
				if (keyData == Keys.Home) { GoToPage(0); return true; }
				if (keyData == Keys.End && pageCount > 0) { GoToPage(pageCount - 1); return true; }
				if (keyData == Keys.PageDown) { GoToPage(currentPage + 1); return true; }
				if (keyData == Keys.PageUp) { GoToPage(currentPage - 1); return true; }
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		// ---- The cover is not the first page ---------------------------------------------------------------------

		/// <summary>
		/// The page on screen is the cover of this book: the whole identification is done again with it (the name, the database or the API,
		/// the comparison of covers and Claude if it is on), and what it finds fills the search fields and preselects the series.
		/// </summary>
		private void UsePageAsCover()
		{
			if (Rescan == null)
			{
				return;
			}
			int page = currentPage;
			Bitmap cover = getPage?.Invoke(page); // the page is read here, on the UI thread
			if (cover == null)
			{
				status.Text = Loc.T("manual.pageUnreadable", page + 1);
				return;
			}
			coverPageLabel.Text = Loc.T("manual.coverIsPage", page + 1);
			coverPageLabel.Visible = true;
			useCoverButton.Enabled = false;
			searchButton.Enabled = false;
			status.Text = Loc.T("manual.rescanning", page + 1);
			CancellationToken token = cancellation.Token;

			Task.Run(() =>
			{
				try
				{
					IdentifyResult found = Rescan(cover, message => OnUi(() => status.Text = message), token);
					OnUi(() => ApplyRescan(found));
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() =>
					{
						status.Text = Loc.T("manual.lookupFailed", e.Message);
						useCoverButton.Enabled = true;
						searchButton.Enabled = true;
					});
				}
			});
		}

		/// <summary>
		/// The number to start the search with. The number of the book (file name or library data) wins over the one of Claude when there
		/// is one: Claude reads a cover that can be cropped and has mixed up "Chapter Two" with "Chapter One"; the name of the file is
		/// what the user wrote. Claude's note about it stays on screen.
		/// </summary>
		private static string NumberToStart(BookHints hints, ClaudeSuggestion claude)
		{
			string own = (hints?.Number ?? "").Trim().TrimStart('#').Trim();
			return own.Length > 0 ? own : claude.Number;
		}

		private void ApplyRescan(IdentifyResult found)
		{
			useCoverButton.Enabled = true;
			searchButton.Enabled = true;
			knownLabel.Text = KnownText(bookHints, found);
			candidates = found.Candidates ?? new List<IssueMatch>();
			ClaudeSuggestion claude = found.Claude;
			if (claude != null && claude.IsUsable)
			{
				series.Text = claude.Series;
				number.Text = NumberToStart(bookHints, claude);
				year.Text = ""; // the year Claude gives is often wrong
				publisher.Text = claude.Publisher;
			}
			else if (found.Best != null)
			{
				series.Text = found.Best.Volume.Name;
				number.Text = found.Best.Issue.Number;
				year.Text = found.Best.Volume.StartYear > 0 ? found.Best.Volume.StartYear.ToString() : "";
				publisher.Text = "";
			}
			if (found.Best != null)
			{
				SuggestedVolume = found.Best.Volume; // preselected in the list, with its issue shown
			}
			Search();
		}

		// ---- Step 1: the series ------------------------------------------------------------------------------

		private void Search()
		{
			string name = series.Text.Trim();
			if (name.Length == 0)
			{
				return;
			}
			if (inIssues)
			{
				LeaveIssues();
			}
			int.TryParse(year.Text.Trim(), out int wantedYear);
			string wantedPublisher = publisher.Text.Trim();
			volumeList.Items.Clear();
			ClearSelection();
			status.Text = Loc.T("manual.searching");
			searchButton.Enabled = false;
			CancellationToken token = cancellation.Token;

			Task.Run(() =>
			{
				try
				{
					List<VolumeInfo> volumes = comicVine.SearchVolumes(name, token)
						.Where(v => wantedPublisher.Length == 0 || v.Publisher.IndexOf(wantedPublisher, StringComparison.OrdinalIgnoreCase) >= 0)
						.OrderBy(v => wantedYear > 0 ? Math.Abs(v.StartYear - wantedYear) : 0)
						.ThenByDescending(v => TextMatch.Similarity(name, v.Name))
						.ThenByDescending(v => v.IssueCount)
						.ToList();
					OnUi(() =>
					{
						allVolumes = volumes;
						ShowVolumes(volumes);
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() =>
					{
						status.Text = Loc.T("manual.searchFailed", e.Message);
						searchButton.Enabled = true;
					});
				}
			});
		}

		private ListViewItem VolumeItem(VolumeInfo v)
		{
			ListViewItem item = new ListViewItem(v.Name) { Tag = v, ToolTipText = $"{v.Name} ({v.StartYear})\r\n{v.Publisher}\r\n{v.IssueCount}" };
			item.SubItems.Add(v.StartYear > 0 ? v.StartYear.ToString() : "");
			item.SubItems.Add(v.Publisher);
			item.SubItems.Add(v.IssueCount.ToString());
			IssueMatch best = candidates.Where(c => c.Volume != null && c.Volume.Id == v.Id).OrderByDescending(c => c.Confidence).FirstOrDefault();
			item.SubItems.Add(best == null ? "" : best.Confidence.ToString("P0"));
			return item;
		}

		/// <summary>Shows the series, without the ones of unwanted publishers unless the box says otherwise.</summary>
		private void ShowVolumes(List<VolumeInfo> all)
		{
			List<VolumeInfo> volumes = hideUnwanted.Checked ? all.Where(v => !IsUnwantedPublisher(v.Publisher ?? "")).ToList() : all;
			volumeList.ListViewItemSorter = null;
			volumeList.Items.Clear();
			volumeList.BeginUpdate();
			foreach (VolumeInfo v in volumes)
			{
				volumeList.Items.Add(VolumeItem(v));
			}
			if (SuggestedVolume != null && !volumes.Any(v => v.Id == SuggestedVolume.Id))
			{
				volumeList.Items.Insert(0, VolumeItem(SuggestedVolume));
			}
			volumeList.EndUpdate();
			FitColumns(volumeList, 0);
			searchButton.Enabled = true;
			status.Text = Loc.T("manual.found", volumes.Count);
			if (volumeList.Items.Count > 0)
			{
				ListViewItem suggested = SuggestedVolume == null ? null : volumeList.Items.Cast<ListViewItem>().FirstOrDefault(i => ((VolumeInfo)i.Tag).Id == SuggestedVolume.Id);
				(suggested ?? volumeList.Items[0]).Selected = true;
				if (suggested != null && OpenIssuesForSuggested && !inIssues)
				{
					OpenIssuesForSuggested = false; // only the first time: "back to series" must stay in the series list
					EnterIssues();
				}
			}
		}

		/// <summary>Right click on a series: add its publisher to the unwanted ones, or take it out.</summary>
		private void PrepareMenu(System.ComponentModel.CancelEventArgs e)
		{
			volumeMenu.Items.Clear();
			Point point = volumeList.PointToClient(Cursor.Position);
			ListViewItem item = volumeList.GetItemAt(point.X, point.Y);
			string publisherName = item == null ? "" : ((VolumeInfo)item.Tag).Publisher;
			if (string.IsNullOrWhiteSpace(publisherName))
			{
				e.Cancel = true;
				return;
			}
			item.Selected = true;
			bool unwanted = IsUnwantedPublisher(publisherName);
			ToolStripMenuItem entry = new ToolStripMenuItem(Loc.T(unwanted ? "manual.menu.removeUnwanted" : "manual.menu.addUnwanted", publisherName));
			entry.Click += (s, a) =>
			{
				SetUnwantedPublisher(publisherName, !unwanted);
				ShowVolumes(allVolumes);
				status.Text = Loc.T(unwanted ? "manual.unwantedRemoved" : "manual.unwantedAdded", publisherName);
			};
			volumeMenu.Items.Add(entry);
		}

		private VolumeInfo SelectedVolumeItem => volumeList.SelectedItems.Count == 0 ? null : (VolumeInfo)volumeList.SelectedItems[0].Tag;

		/// <summary>The series that was clicked: its cover and data are shown. If the number box has a number that the series has, that issue is shown.</summary>
		private void OnVolumeSelected()
		{
			VolumeInfo volume = SelectedVolumeItem;
			if (volume == null || inIssues)
			{
				return;
			}
			string wantedNumber = number.Text.Trim();
			int version = ++lookupVersion;
			CancellationToken token = cancellation.Token;
			navButton.Enabled = true;
			ClearSelection();
			issueInfo.Text = VolumeText(volume);
			status.Text = wantedNumber.Length == 0 ? "" : Loc.T("manual.looking", wantedNumber);

			Task.Run(() =>
			{
				try
				{
					IssueInfo issue = wantedNumber.Length == 0 ? null : comicVine.FindIssue(volume, wantedNumber, token);
					Bitmap image = comicVine.DownloadImage(issue != null ? issue.ImageUrl : volume.ImageUrl, token);
					OnUi(() =>
					{
						if (version != lookupVersion || inIssues)
						{
							image?.Dispose();
							return;
						}
						if (issue != null)
						{
							ShowIssue(volume, issue, image);
							ProbeCovers(issue);
							status.Text = "";
						}
						else
						{
							issueView.Image = image; // the cover of the series
							status.Text = wantedNumber.Length == 0 ? Loc.T("manual.found", volumeList.Items.Count) : Loc.T("manual.noIssue", wantedNumber);
						}
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() => status.Text = Loc.T("manual.lookupFailed", e.Message));
				}
			});
		}

		private static string VolumeText(VolumeInfo v) => Loc.T("manual.volumeInfo", v.Name, v.StartYear > 0 ? v.StartYear.ToString() : "?", v.Publisher, v.IssueCount).Replace("\r\n", "\n").Replace("\n", "\r\n");

		// ---- Step 2: the issues of one series ----------------------------------------------------------------

		private void EnterIssues()
		{
			VolumeInfo volume = SelectedVolumeItem;
			if (volume == null)
			{
				return;
			}
			browsing = volume;
			inIssues = true;
			volumeList.Visible = false;
			issuesPanel.Visible = true;
			navButton.Text = Loc.T("manual.backToSeries");
			issuesHeader.Text = Loc.T("manual.issuesOf", volume.Name, volume.StartYear > 0 ? volume.StartYear.ToString() : "?", volume.Publisher);
			issueList.ListViewItemSorter = null;
			issueList.Items.Clear();
			ClearSelection();
			status.Text = Loc.T("manual.loadingIssues");
			int version = ++lookupVersion;
			CancellationToken token = cancellation.Token;

			Task.Run(() =>
			{
				try
				{
					List<IssueInfo> issues = comicVine.ListIssues(volume, token);
					OnUi(() =>
					{
						if (version != lookupVersion || !inIssues || browsing != volume)
						{
							return;
						}
						issueList.BeginUpdate();
						foreach (IssueInfo issue in issues)
						{
							ListViewItem item = new ListViewItem(issue.Number) { Tag = issue };
							item.SubItems.Add(issue.Name);
							item.SubItems.Add(issue.CoverDate);
							issueList.Items.Add(item);
						}
						issueList.EndUpdate();
						FitColumns(issueList, 1);
						status.Text = Loc.T("manual.issuesFound", issues.Count);
						if (!SelectIssueByNumber() && issueList.Items.Count > 0)
						{
							issueList.Items[0].Selected = true;
						}
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() => status.Text = Loc.T("manual.lookupFailed", e.Message));
				}
			});
		}

		private void LeaveIssues()
		{
			inIssues = false;
			issuesPanel.Visible = false;
			volumeList.Visible = true;
			navButton.Text = Loc.T("manual.showIssues");
			++lookupVersion;
			ClearSelection();
			OnVolumeSelected();
		}

		/// <summary>Selects the issue of the number box in the list. False if there is none.</summary>
		private bool SelectIssueByNumber()
		{
			string wanted = TextMatch.NormalizeNumber(number.Text);
			if (wanted.Length == 0)
			{
				return false;
			}
			foreach (ListViewItem item in issueList.Items)
			{
				if (TextMatch.NormalizeNumber(((IssueInfo)item.Tag).Number) == wanted)
				{
					item.Selected = true;
					item.EnsureVisible();
					return true;
				}
			}
			return false;
		}

		private void OnIssueSelected()
		{
			if (!inIssues || issueList.SelectedItems.Count == 0 || browsing == null)
			{
				return;
			}
			IssueInfo issue = (IssueInfo)issueList.SelectedItems[0].Tag;
			VolumeInfo volume = browsing;
			int version = ++lookupVersion;
			CancellationToken token = cancellation.Token;
			ShowIssue(volume, issue, null); // the data now, the cover when it arrives
			ProbeCovers(issue);

			Task.Run(() =>
			{
				try
				{
					Bitmap image = comicVine.DownloadImage(issue.ImageUrl, token);
					OnUi(() =>
					{
						if (version != lookupVersion || foundIssue != issue)
						{
							image?.Dispose();
							return;
						}
						issueView.Image = image;
						UpdateInfo();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() => status.Text = Loc.T("manual.lookupFailed", e.Message));
				}
			});
		}

		// ---- what is selected ----------------------------------------------------------------------------------

		private void ClearSelection()
		{
			foundVolume = null;
			foundIssue = null;
			coverUrls = null;
			coverIndex = 0;
			coverButton.Visible = false;
			useButton.Enabled = false;
			useRestButton.Enabled = false;
			editButton.Enabled = false;
			issueView.Image = null;
		}

		private void ShowIssue(VolumeInfo volume, IssueInfo issue, Bitmap image)
		{
			foundVolume = volume;
			foundIssue = issue;
			coverUrls = null;
			coverIndex = 0;
			coverButton.Visible = false;
			useButton.Enabled = issue != null;
			useRestButton.Enabled = issue != null;
			editButton.Enabled = issue != null;
			issueView.Image = image; // the view owns the image
			UpdateInfo();
		}

		// ---- the other covers of an issue ----------------------------------------------------------------------

		private static string ImageName(string url)
		{
			int cut = url.IndexOf('?');
			return System.IO.Path.GetFileName(cut >= 0 ? url.Substring(0, cut) : url);
		}

		/// <summary>
		/// With the local database the alternative covers are in the row of the issue, so they are known at once and the button only
		/// appears if there are some. With the API they cost a call, so the button is there and the call is made when it is pressed.
		/// Either way no cover image is downloaded until the user asks for it.
		/// </summary>
		private void ProbeCovers(IssueInfo issue)
		{
			if (issue.HasDetails)
			{
				BuildCoverList(issue, false);
				return;
			}
			if (comicVine.UsesLocalDb && issue.Local)
			{
				CancellationToken token = cancellation.Token;
				Task.Run(() =>
				{
					try
					{
						comicVine.LoadDetails(issue, token);
						OnUi(() =>
						{
							if (foundIssue == issue)
							{
								BuildCoverList(issue, false);
							}
						});
					}
					catch (Exception)
					{
						// no alternative covers shown: the main one is still there
					}
				});
				return;
			}
			coverButton.Text = Loc.T("manual.otherCovers");
			coverButton.Visible = true; // unknown until asked
		}

		/// <summary>The main cover and the alternatives that are not the same image. Returns how many covers there are.</summary>
		private int BuildCoverList(IssueInfo issue, bool asked)
		{
			List<string> urls = new List<string> { issue.ImageUrl };
			foreach (string url in issue.AltImageUrls)
			{
				if (!urls.Any(u => string.Equals(ImageName(u), ImageName(url), StringComparison.OrdinalIgnoreCase)))
				{
					urls.Add(url);
				}
			}
			if (urls.Count > 1)
			{
				coverUrls = urls;
				coverButton.Text = Loc.T("manual.otherCoversN", urls.Count - 1);
				coverButton.Visible = true;
			}
			else
			{
				coverUrls = null;
				coverButton.Visible = false;
				if (asked)
				{
					status.Text = Loc.T("manual.noOtherCovers");
				}
			}
			UpdateInfo();
			return urls.Count;
		}

		/// <summary>Shows the next cover of the issue; the first time (with the API) it finds out which ones there are.</summary>
		private void NextCover()
		{
			IssueInfo issue = foundIssue;
			if (issue == null)
			{
				return;
			}
			if (coverUrls == null)
			{
				CancellationToken token = cancellation.Token;
				coverButton.Enabled = false;
				Task.Run(() =>
				{
					try
					{
						comicVine.LoadDetails(issue, token);
						OnUi(() =>
						{
							coverButton.Enabled = true;
							if (foundIssue == issue && BuildCoverList(issue, true) > 1)
							{
								ShowCover(1);
							}
						});
					}
					catch (OperationCanceledException)
					{
					}
					catch (Exception e)
					{
						OnUi(() =>
						{
							coverButton.Enabled = true;
							status.Text = Loc.T("manual.lookupFailed", e.Message);
						});
					}
				});
				return;
			}
			ShowCover((coverIndex + 1) % coverUrls.Count);
		}

		private void ShowCover(int index)
		{
			if (coverUrls == null || index < 0 || index >= coverUrls.Count)
			{
				return;
			}
			IssueInfo issue = foundIssue;
			string url = coverUrls[index];
			int version = ++lookupVersion;
			CancellationToken token = cancellation.Token;
			coverIndex = index;
			status.Text = "";
			Task.Run(() =>
			{
				try
				{
					Bitmap image = comicVine.DownloadImage(url, token); // only now: nothing was downloaded for a cover nobody asked to see
					OnUi(() =>
					{
						if (version != lookupVersion || foundIssue != issue)
						{
							image?.Dispose();
							return;
						}
						issueView.Image = image;
						UpdateInfo();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() => status.Text = Loc.T("manual.lookupFailed", e.Message));
				}
			});
		}

		/// <summary>The data of the issue, with the similarity between the page on screen and its cover.</summary>
		private void UpdateInfo()
		{
			similarity = -1;
			issueLink.Visible = foundIssue != null && IsWebAddress(foundIssue.SiteUrl);
			if (foundIssue != null && issueView.Image != null && bookFingerprint != null)
			{
				similarity = bookFingerprint.Similarity(CoverFingerprint.From(issueView.Image));
			}
			MarkCoverByComparison();
			if (foundIssue == null)
			{
				VolumeInfo shown = inIssues ? browsing : SelectedVolumeItem;
				issueInfo.Text = shown == null ? "" : VolumeText(shown);
				return;
			}
			IssueMatch candidate = candidates.Where(c => c.Issue != null && c.Issue.Id == foundIssue.Id).OrderByDescending(c => c.Confidence).FirstOrDefault();
			issueInfo.Text = $"{foundVolume.Name} ({foundVolume.StartYear}) #{foundIssue.Number}\r\n{foundIssue.Name}\r\n{Loc.T("manual.coverDate", foundIssue.CoverDate)}\r\n{foundVolume.Publisher}\r\n" +
				(candidate == null ? Loc.T("manual.matchNone") : Loc.T("manual.match", candidate.Confidence.ToString("P0"), candidate.NameScore.ToString("P0"), candidate.CoverScore < 0 ? Loc.T("common.na") : candidate.CoverScore.ToString("P0"))) + "\r\n" +
				(similarity >= 0 ? Loc.T("manual.similarity", currentPage + 1, similarity.ToString("P0")) : Loc.T("manual.similarityNA")) +
				(coverUrls != null && coverUrls.Count > 1 ? "\r\n" + Loc.T("manual.coverNumber", coverIndex + 1, coverUrls.Count) : "");
		}

		/// <summary>
		/// "Research with AI": Claude looks at the page on screen (the user can go to the real cover first) and its answer is shown in the edit
		/// window, to be corrected before it is sent. The book is written with those values only; it is not linked to a Comic Vine issue.
		/// </summary>
		private void ResearchWithAi()
		{
			if (AskClaude == null)
			{
				return;
			}
			string context;
			using (AiContextForm ask = new AiContextForm(settings.AiChromeFallback))
			{
				if (ask.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				context = ask.Context;
			}
			Bitmap cover = getPage?.Invoke(currentPage); // read here, on the UI thread
			if (cover == null)
			{
				status.Text = Loc.T("manual.pageUnreadable", currentPage + 1);
				return;
			}
			aiButton.Enabled = false;
			status.Text = Loc.T("manual.aiWorking", currentPage + 1);
			CancellationToken token = cancellation.Token;
			Task.Run(() =>
			{
				try
				{
					ClaudeSuggestion suggestion = AskClaude(cover, context, token);
					OnUi(() => ShowAiSuggestion(suggestion));
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception e)
				{
					OnUi(() =>
					{
						aiButton.Enabled = true;
						status.Text = Loc.T("manual.aiFailed", e.Message);
					});
				}
				finally
				{
					cover.Dispose();
				}
			});
		}

		private void ShowAiSuggestion(ClaudeSuggestion suggestion)
		{
			aiButton.Enabled = true;
			if (suggestion == null || !suggestion.IsUsable)
			{
				status.Text = Loc.T("manual.aiNone");
				return;
			}
			status.Text = "";
			ManualEdit start = new ManualEdit
			{
				Series = suggestion.Series,
				Number = suggestion.Number,
				Title = suggestion.Title,
				Year = suggestion.VolumeYear > 0 ? suggestion.VolumeYear : suggestion.Year,
				Publisher = suggestion.Publisher,
				Writer = suggestion.Writer, Penciller = suggestion.Penciller, Inker = suggestion.Inker, Colorist = suggestion.Colorist,
				Letterer = suggestion.Letterer, CoverArtist = suggestion.CoverArtist, Editor = suggestion.Editor,
				CoverDate = suggestion.CoverDate, StoreDate = suggestion.StoreDate, Summary = suggestion.Summary,
				Characters = suggestion.Characters, Teams = suggestion.Teams, Locations = suggestion.Locations, StoryArcs = suggestion.StoryArcs,
				Web = suggestion.SourceUrl
			};
			string heading = Loc.T("edit.fromAi", suggestion.Confidence.ToString("P0"));
			using (EditMetadataForm form = new EditMetadataForm(Loc.T("edit.titleAi"), heading, suggestion.Notes, start, extended: true))
			{
				if (form.ShowDialog(this) == DialogResult.OK && form.Result != null)
				{
					Edit = form.Result;
					foundVolume = null; // nothing of Comic Vine goes with this: the values are the ones of the user
					foundIssue = null;
					DialogResult = DialogResult.OK;
				}
			}
		}

		/// <summary>"Edit data": the data of the selected Comic Vine issue, to change what is needed before it is sent (the number "1-2", for example).</summary>
		private void EditSelected()
		{
			if (foundIssue == null || foundVolume == null)
			{
				return;
			}
			IssueInfo issue = foundIssue;
			VolumeInfo volume = foundVolume;
			if (issue.HasDetails)
			{
				ShowEditWindow(volume, issue);
				return;
			}
			// the creators, summary and lists are in the details of the issue (one call, which the write needs anyway)
			CancellationToken token = cancellation.Token;
			editButton.Enabled = false;
			status.Text = Loc.T("manual.loadingDetails");
			Task.Run(() =>
			{
				string error = null;
				try
				{
					comicVine.LoadDetails(issue, token);
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (Exception e)
				{
					error = e.Message;
				}
				OnUi(() =>
				{
					status.Text = "";
					editButton.Enabled = foundIssue != null;
					if (error != null)
					{
						status.Text = Loc.T("manual.detailsFailed", error);
					}
					if (foundIssue == issue)
					{
						ShowEditWindow(volume, issue); // without details the fields come empty, but the number can still be corrected
					}
				});
			});
		}

		private void ShowEditWindow(VolumeInfo volume, IssueInfo issue)
		{
			Dictionary<string, List<string>> roles = MetadataWriter.ParseRoles(issue);
			Func<string, string> names = key => string.Join(", ", roles[key].Distinct());
			ManualEdit start = new ManualEdit
			{
				FromComicVine = true,
				Series = volume.Name,
				Number = issue.Number,
				Title = issue.Name ?? "",
				Year = volume.StartYear,
				Publisher = volume.Publisher ?? "",
				Writer = names("writers"), Penciller = names("pencillers"), Inker = names("inkers"), Colorist = names("colorists"),
				Letterer = names("letterers"), CoverArtist = names("cover_artists"), Editor = names("editors"),
				CoverDate = issue.CoverDate ?? "", StoreDate = issue.StoreDate ?? "",
				Summary = MetadataWriter.ParseSummary(issue.Description),
				Characters = string.Join(", ", issue.Characters), Teams = string.Join(", ", issue.Teams),
				Locations = string.Join(", ", issue.Locations), StoryArcs = string.Join(", ", issue.StoryArcs),
				Web = issue.SiteUrl ?? ""
			};
			using (EditMetadataForm form = new EditMetadataForm(Loc.T("edit.titleCv"), Loc.T("edit.fromCv", volume.Name, issue.Number), "", start, extended: true))
			{
				if (form.ShowDialog(this) == DialogResult.OK && form.Result != null)
				{
					form.Result.FromComicVine = true;
					Edit = form.Result;
					DialogResult = DialogResult.OK;
				}
			}
		}

		private static bool IsWebAddress(string url) =>
			Uri.TryCreate(url ?? "", UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

		/// <summary>Opens the page of the selected issue on Comic Vine in the browser.</summary>
		private void OpenIssuePage()
		{
			if (foundIssue == null || !IsWebAddress(foundIssue.SiteUrl))
			{
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo(foundIssue.SiteUrl) { UseShellExecute = true });
			}
			catch (Exception e)
			{
				status.Text = Loc.T("manual.openIssueFailed", e.Message);
			}
		}

		/// <summary>
		/// "Use for the rest": this issue is used for this book and its series for the other books of the review that are of the same series,
		/// each with its own number. Saves clicking through them one by one when the series is right.
		/// </summary>
		private void UseForRemaining()
		{
			if (foundIssue == null || foundVolume == null)
			{
				return;
			}
			int remaining = CountRemaining?.Invoke() ?? 0;
			if (MessageBox.Show(this, Loc.T("manual.useRestConfirm", remaining, foundVolume.Name, foundVolume.StartYear), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			{
				return;
			}
			ApplyToRemaining = true;
			DialogResult = DialogResult.OK;
		}

		private void UseIssue()
		{
			if (foundIssue != null)
			{
				DialogResult = DialogResult.OK;
			}
		}

		private void OnUi(Action action)
		{
			if (IsHandleCreated && !IsDisposed)
			{
				BeginInvoke(action);
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
	}
}
