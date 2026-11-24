using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>
	/// The values that are going to be written to a book, to be checked and changed before they are sent: the data of the Comic Vine issue
	/// that was chosen (for example to write the number "1-2" when the file holds two issues), or what the research with AI found. The
	/// research window (extended) also has creators, dates, the summary and the lists; a value left empty adds nothing.
	/// </summary>
	public sealed class EditMetadataForm : Form
	{
		private static readonly Regex dateFormat = new Regex(@"^\d{4}(-\d{1,2}(-\d{1,2})?)?$");

		private readonly TextBox series = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox number = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox title = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox year = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox publisher = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox writer = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox penciller = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox inker = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox colorist = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox letterer = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox coverArtist = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox editor = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox coverDate = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox storeDate = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox summary = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Height = Ui.Px(110) };
		private readonly TextBox characters = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox teams = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox locations = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox storyArcs = new TextBox { Dock = DockStyle.Fill };
		private readonly TextBox web = new TextBox { Dock = DockStyle.Fill };

		private readonly bool extended;

		/// <summary>What the user typed. Valid when the window closed with OK.</summary>
		public ManualEdit Result { get; private set; }

		/// <param name="heading">Where the values come from and what happens when the window is accepted.</param>
		/// <param name="notes">Extra text to read (what Claude said about its answer), or empty.</param>
		/// <param name="extended">Also the creators, dates, summary and lists (the research with AI).</param>
		public EditMetadataForm(string windowTitle, string heading, string notes, ManualEdit start, bool extended = false)
		{
			this.extended = extended;
			Font = Ui.CreateFont();
			Text = windowTitle;
			StartPosition = FormStartPosition.CenterParent;
			ShowInTaskbar = false;
			MinimizeBox = false;
			MaximizeBox = extended;
			FormBorderStyle = extended ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
			ClientSize = Ui.Px(extended ? 720 : 600, extended ? 780 : 360);
			MinimumSize = Ui.Px(560, extended ? 520 : 340);

			series.Text = start.Series;
			number.Text = start.Number;
			title.Text = start.Title;
			year.Text = start.Year > 0 ? start.Year.ToString() : "";
			publisher.Text = start.Publisher;
			writer.Text = start.Writer;
			penciller.Text = start.Penciller;
			inker.Text = start.Inker;
			colorist.Text = start.Colorist;
			letterer.Text = start.Letterer;
			coverArtist.Text = start.CoverArtist;
			editor.Text = start.Editor;
			coverDate.Text = start.CoverDate;
			storeDate.Text = start.StoreDate;
			summary.Text = (start.Summary ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
			characters.Text = start.Characters;
			teams.Text = start.Teams;
			locations.Text = start.Locations;
			storyArcs.Text = start.StoryArcs;
			web.Text = start.Web;

			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(Ui.Px(12)) };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Panel scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
			scroller.Controls.Add(table);
			scroller.Resize += (s, e) => table.Width = Math.Max(Ui.Px(300), scroller.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);

			int row = 0;
			AddNote(table, ref row, heading, Margin: new Padding(0, 0, 0, Ui.Px(8)));
			AddField(table, ref row, Loc.T("edit.series"), series);
			AddField(table, ref row, Loc.T("edit.number"), number);
			Label hint = new Label { Text = Loc.T("edit.numberHint"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(Ui.Px(430), 0) };
			table.Controls.Add(hint, 1, row++);
			AddField(table, ref row, Loc.T("edit.title"), title);
			AddField(table, ref row, Loc.T("edit.year"), year);
			AddField(table, ref row, Loc.T("edit.publisher"), publisher);
			if (extended)
			{
				AddField(table, ref row, Loc.T("edit.writer"), writer);
				AddField(table, ref row, Loc.T("edit.penciller"), penciller);
				AddField(table, ref row, Loc.T("edit.inker"), inker);
				AddField(table, ref row, Loc.T("edit.colorist"), colorist);
				AddField(table, ref row, Loc.T("edit.letterer"), letterer);
				AddField(table, ref row, Loc.T("edit.coverArtist"), coverArtist);
				AddField(table, ref row, Loc.T("edit.editor"), editor);
				AddField(table, ref row, Loc.T("edit.coverDate"), coverDate);
				AddField(table, ref row, Loc.T("edit.storeDate"), storeDate);
				Label dateHint = new Label { Text = Loc.T("edit.dateHint"), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(Ui.Px(430), 0) };
				table.Controls.Add(dateHint, 1, row++);
				AddField(table, ref row, Loc.T("edit.summary"), summary);
				AddField(table, ref row, Loc.T("edit.characters"), characters);
				AddField(table, ref row, Loc.T("edit.teams"), teams);
				AddField(table, ref row, Loc.T("edit.locations"), locations);
				AddField(table, ref row, Loc.T("edit.storyArcs"), storyArcs);
				AddField(table, ref row, Loc.T("edit.web"), web);
			}
			if (!string.IsNullOrWhiteSpace(notes))
			{
				AddNote(table, ref row, Loc.T("edit.notes", notes), Margin: new Padding(0, Ui.Px(8), 0, 0));
			}

			Button ok = new Button { Text = Loc.T("edit.send"), AutoSize = true, DialogResult = DialogResult.OK };
			Button cancel = new Button { Text = Loc.T("edit.cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(Ui.Px(12), Ui.Px(8), Ui.Px(12), Ui.Px(10)) };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);

			AcceptButton = extended ? null : ok; // in the research window Enter is for the summary
			CancelButton = cancel;
			Controls.Add(scroller);
			Controls.Add(buttons);

			FormClosing += (s, e) =>
			{
				if (DialogResult != DialogResult.OK)
				{
					return;
				}
				string problem = FindProblem();
				if (problem != null)
				{
					MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
					e.Cancel = true;
					return;
				}
				int.TryParse(year.Text.Trim(), out int parsedYear);
				Result = new ManualEdit
				{
					Series = series.Text.Trim(),
					Number = number.Text.Trim().TrimStart('#'),
					Title = title.Text.Trim(),
					Year = Math.Max(0, parsedYear),
					Publisher = publisher.Text.Trim(),
					Writer = writer.Text.Trim(),
					Penciller = penciller.Text.Trim(),
					Inker = inker.Text.Trim(),
					Colorist = colorist.Text.Trim(),
					Letterer = letterer.Text.Trim(),
					CoverArtist = coverArtist.Text.Trim(),
					Editor = editor.Text.Trim(),
					CoverDate = coverDate.Text.Trim(),
					StoreDate = storeDate.Text.Trim(),
					Summary = summary.Text.Trim(),
					Characters = characters.Text.Trim(),
					Teams = teams.Text.Trim(),
					Locations = locations.Text.Trim(),
					StoryArcs = storyArcs.Text.Trim(),
					Web = web.Text.Trim()
				};
			};
			Shown += (s, e) => { number.Focus(); number.SelectAll(); };
		}

		/// <summary>What is wrong with the values, or null.</summary>
		private string FindProblem()
		{
			if (series.Text.Trim().Length == 0 || number.Text.Trim().Length == 0)
			{
				return Loc.T("edit.required"); // a one-shot is number 1
			}
			if (extended)
			{
				foreach (string date in new[] { coverDate.Text.Trim(), storeDate.Text.Trim() })
				{
					if (date.Length > 0 && !dateFormat.IsMatch(date))
					{
						return Loc.T("edit.dateInvalid", date);
					}
				}
				string address = web.Text.Trim();
				if (address.Length > 0 && !(Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
				{
					return Loc.T("edit.webInvalid");
				}
			}
			return null;
		}

		private static void AddNote(TableLayoutPanel table, ref int row, string text, Padding Margin)
		{
			Label label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Ui.Px(640), 0), Margin = Margin };
			table.Controls.Add(label, 0, row++);
			table.SetColumnSpan(label, 2);
		}

		private static void AddField(TableLayoutPanel table, ref int row, string label, Control box)
		{
			table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(0, Ui.Px(6), Ui.Px(10), 0) }, 0, row);
			table.Controls.Add(box, 1, row);
			row++;
		}
	}
}
