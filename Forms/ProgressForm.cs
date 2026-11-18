using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace ComicVineAI
{
	public enum Outcome
	{
		Working,
		Tagged,
		Review,
		Skipped,
		Error
	}

	/// <summary>
	/// The window of a batch: the current book next to the Comic Vine cover it was matched with, a list with one tile
	/// per book (color coded), counters and the log. Every public method can be called from any thread.
	/// All the pixel sizes are in 96 DPI units and go through <see cref="Ui.Px(int)"/> so they follow the zoom of Windows.
	/// </summary>
	public sealed class ProgressForm : Form
	{
		private sealed class Card
		{
			public string Name = "";
			public string Title = "";
			public string Status = "";
			public double Confidence = -1;
			public Outcome Outcome = Outcome.Working;
			public byte[] BookJpeg;
			public byte[] IssueJpeg;
		}

		private static int TileImageWidth => Ui.Px(170);
		private static int TileImageHeight => Ui.Px(112);

		private readonly Label progressText = new Label { AutoSize = true, Dock = DockStyle.Fill };
		private readonly Label counters = new Label { AutoSize = true, Dock = DockStyle.Fill };
		private readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Fill };
		private readonly Label nameLabel = new Label { AutoSize = true, Dock = DockStyle.Fill };
		private readonly Label titleLabel = new Label { AutoSize = true, Dock = DockStyle.Fill };
		private readonly Label statusLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
		private readonly PictureBox bookPicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
		private readonly PictureBox issuePicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
		private readonly ConfidenceBar confidence = new ConfidenceBar { Dock = DockStyle.Fill };
		private readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Tile, MultiSelect = false, HideSelection = false, FullRowSelect = true };
		private readonly ImageList images = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
		private readonly RichTextBox logBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, HideSelection = false };
		private readonly CheckBox follow = new CheckBox { Text = Loc.T("progress.follow"), Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
		private readonly Button button = new Button { Text = Loc.T("progress.cancel"), AutoSize = true, Anchor = AnchorStyles.Right };
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
		private readonly Dictionary<int, ListViewItem> items = new Dictionary<int, ListViewItem>();
		private readonly double threshold;

		private Card selected;
		private bool programmaticSelection;
		private bool running = true;

		public CancellationToken Token => cancellation.Token;

		public ProgressForm(double threshold)
		{
			this.threshold = threshold;
			Font = Ui.CreateFont();
			Text = AppInfo.NameAndVersion;
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = Ui.Px(900, 600);
			ShowInTaskbar = false;
			UiState.Attach(this, "progress", new Size(1250, 800));

			confidence.Threshold = threshold;
			counters.Font = Ui.CreateFont(FontStyle.Bold);
			nameLabel.Font = Ui.CreateFont(FontStyle.Bold);
			logBox.Font = Ui.CreateMonoFont();
			images.ImageSize = new Size(TileImageWidth, TileImageHeight);
			list.LargeImageList = images;
			list.Columns.Add("Book", 200);
			list.Columns.Add("Match", 200);
			list.Columns.Add("Status", 200);
			list.Resize += (s, e) => FitTiles();
			list.ItemSelectionChanged += (s, e) =>
			{
				if (!e.IsSelected)
				{
					return;
				}
				if (!programmaticSelection)
				{
					follow.Checked = false; // the user is looking at an older book: stop jumping to the current one
				}
				ShowCard((Card)e.Item.Tag);
			};

			// current book: your cover next to the Comic Vine cover
			TableLayoutPanel current = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(Ui.Px(8)) };
			current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			current.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			current.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			current.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			current.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			current.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			current.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			current.Controls.Add(nameLabel, 0, 0);
			current.SetColumnSpan(nameLabel, 2);
			current.Controls.Add(new Label { Text = Loc.T("progress.yourComic"), AutoSize = true, ForeColor = Color.DimGray }, 0, 1);
			current.Controls.Add(new Label { Text = Loc.T("progress.cvMatch"), AutoSize = true, ForeColor = Color.DimGray }, 1, 1);
			current.Controls.Add(bookPicture, 0, 2);
			current.Controls.Add(issuePicture, 1, 2);
			current.Controls.Add(confidence, 0, 3);
			current.SetColumnSpan(confidence, 2);
			current.Controls.Add(titleLabel, 0, 4);
			current.SetColumnSpan(titleLabel, 2);
			current.Controls.Add(statusLabel, 0, 5);
			current.SetColumnSpan(statusLabel, 2);

			SplitContainer top = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
			top.Panel1.Controls.Add(current);
			top.Panel2.Controls.Add(list);
			SplitContainer main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
			main.Panel1.Controls.Add(top);
			main.Panel2.Controls.Add(logBox);

			TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
			header.Controls.Add(progressText, 0, 0);
			header.Controls.Add(counters, 0, 1);
			header.Controls.Add(bar, 0, 2);

			TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true };
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			footer.Controls.Add(follow, 0, 0);
			footer.Controls.Add(button, 1, 0);

			TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.Px(12)) };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.Controls.Add(header, 0, 0);
			root.Controls.Add(main, 0, 1);
			root.Controls.Add(footer, 0, 2);
			Controls.Add(root);

			// 96 DPI units: default distance, minimum of the first panel, minimum of the second panel
			UiState.AttachSplitter(this, top, "progress.current", 560, 320, 260);
			UiState.AttachSplitter(this, main, "progress.log", 600, 250, 70);

			button.Click += (s, e) =>
			{
				if (running)
				{
					cancellation.Cancel();
					button.Enabled = false;
					progressText.Text = Loc.T("progress.stopping");
				}
				else
				{
					Close();
				}
			};
			new ToolTip().SetToolTip(button, Loc.T("progress.cancelHint"));
			FormClosing += (s, e) =>
			{
				if (running)
				{
					cancellation.Cancel();
					e.Cancel = true; // wait for the worker to stop; Finish() enables Close
					progressText.Text = Loc.T("progress.stopping");
				}
			};
			FormClosed += (s, e) => ClearPictures();
		}

		/// <summary>Each tile takes the whole width of the list so the names are not cut.</summary>
		private void FitTiles()
		{
			int width = Math.Max(Ui.Px(260), list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Ui.Px(6));
			list.TileSize = new Size(width, TileImageHeight + Ui.Px(12));
		}

		// ---- Thread safe API ---------------------------------------------------------------------------------

		private void OnUi(Action action)
		{
			if (!IsHandleCreated || IsDisposed)
			{
				return;
			}
			if (InvokeRequired)
			{
				BeginInvoke(action);
			}
			else
			{
				action();
			}
		}

		public void BeginBook(int index, string name)
		{
			OnUi(() =>
			{
				Card card = new Card { Name = name, Status = Loc.T("progress.starting"), Outcome = Outcome.Working };
				ListViewItem item = new ListViewItem(name) { Tag = card };
				item.SubItems.Add("");
				item.SubItems.Add(card.Status);
				images.Images.Add(Compose(card));
				item.ImageIndex = images.Images.Count - 1;
				item.BackColor = BackFor(card.Outcome);
				items[index] = item;
				list.Items.Add(item);
				if (follow.Checked)
				{
					programmaticSelection = true;
					item.Selected = true;
					item.EnsureVisible();
					programmaticSelection = false;
					ShowCard(card);
				}
			});
		}

		/// <summary>The cover of the book, as soon as it has been read.</summary>
		public void SetBookCover(int index, byte[] jpeg)
		{
			OnUi(() =>
			{
				if (items.TryGetValue(index, out ListViewItem item))
				{
					Card card = (Card)item.Tag;
					card.BookJpeg = jpeg;
					UpdateItem(item);
				}
			});
		}

		/// <summary>A progress message about the book that is being worked on.</summary>
		public void SetStatus(int index, string text)
		{
			OnUi(() =>
			{
				if (items.TryGetValue(index, out ListViewItem item) && ((Card)item.Tag).Outcome == Outcome.Working)
				{
					((Card)item.Tag).Status = text;
					UpdateItem(item);
				}
			});
		}

		public void EndBook(int index, Outcome outcome, string title, string status, double confidenceValue, byte[] issueJpeg)
		{
			OnUi(() =>
			{
				if (items.TryGetValue(index, out ListViewItem item))
				{
					Card card = (Card)item.Tag;
					card.Outcome = outcome;
					card.Title = title ?? "";
					card.Status = status ?? "";
					card.Confidence = confidenceValue;
					card.IssueJpeg = issueJpeg ?? card.IssueJpeg;
					UpdateItem(item);
				}
			});
		}

		/// <summary>
		/// The best candidate found so far for a book that is still being searched: its cover shows in the preview and in the tile at once,
		/// and is replaced by a better one if it turns up, so the space next to the book is not empty while it works.
		/// </summary>
		public void SetCandidate(int index, string title, double confidenceValue, byte[] issueJpeg)
		{
			OnUi(() =>
			{
				if (items.TryGetValue(index, out ListViewItem item) && ((Card)item.Tag).Outcome == Outcome.Working)
				{
					Card card = (Card)item.Tag;
					card.Title = title ?? "";
					card.Confidence = confidenceValue;
					card.IssueJpeg = issueJpeg ?? card.IssueJpeg;
					UpdateItem(item);
				}
			});
		}

		public void SetProgress(int done, int total, string text)
		{
			OnUi(() =>
			{
				bar.Maximum = Math.Max(1, total);
				bar.Value = Math.Min(done, bar.Maximum);
				progressText.Text = $"{done} / {total}   {text}";
			});
		}

		public void SetCounters(string text)
		{
			OnUi(() => counters.Text = text);
		}

		public void Log(string message)
		{
			OnUi(() => logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine));
		}

		public void Finish(string summary)
		{
			OnUi(() =>
			{
				running = false;
				progressText.Text = summary;
				bar.Value = bar.Maximum;
				button.Enabled = true;
				button.Text = Loc.T("progress.close");
			});
		}

		// ---- Drawing -----------------------------------------------------------------------------------------

		private void UpdateItem(ListViewItem item)
		{
			Card card = (Card)item.Tag;
			item.BackColor = BackFor(card.Outcome);
			item.SubItems[1].Text = card.Title;
			item.SubItems[2].Text = card.Status;
			images.Images[item.ImageIndex] = Compose(card);
			list.Invalidate();
			if (card == selected)
			{
				ShowCard(card);
			}
		}

		private void ShowCard(Card card)
		{
			selected = card;
			ClearPictures();
			bookPicture.Image = ImageUtil.Decode(card.BookJpeg);
			issuePicture.Image = ImageUtil.Decode(card.IssueJpeg);
			nameLabel.Text = card.Name;
			titleLabel.Text = card.Title;
			statusLabel.Text = card.Status;
			confidence.Value = card.Confidence;
		}

		private void ClearPictures()
		{
			Image book = bookPicture.Image;
			Image issue = issuePicture.Image;
			bookPicture.Image = null;
			issuePicture.Image = null;
			book?.Dispose();
			issue?.Dispose();
		}

		private static Color BackFor(Outcome outcome)
		{
			switch (outcome)
			{
				case Outcome.Tagged: return Color.FromArgb(222, 244, 222);
				case Outcome.Review: return Color.FromArgb(255, 241, 204);
				case Outcome.Skipped: return Color.FromArgb(235, 235, 235);
				case Outcome.Error: return Color.FromArgb(250, 217, 217);
				default: return Color.FromArgb(225, 235, 250);
			}
		}

		/// <summary>The tile picture: your cover, an arrow and the Comic Vine cover.</summary>
		private static Bitmap Compose(Card card)
		{
			int width = TileImageWidth;
			int height = TileImageHeight;
			int box = (int)(width * 0.40);
			int gap = Ui.Px(3);
			Bitmap tile = new Bitmap(width, height);
			using (Graphics g = Graphics.FromImage(tile))
			{
				g.Clear(BackFor(card.Outcome));
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				DrawCover(g, card.BookJpeg, new Rectangle(gap, gap, box, height - 2 * gap));
				DrawCover(g, card.IssueJpeg, new Rectangle(width - box - gap, gap, box, height - 2 * gap));
				TextRenderer.DrawText(g, ">", SystemFonts.MessageBoxFont, new Rectangle(box + gap, 0, width - 2 * (box + gap), height), Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			}
			return tile;
		}

		private static void DrawCover(Graphics g, byte[] jpeg, Rectangle box)
		{
			using (Bitmap image = ImageUtil.Decode(jpeg))
			{
				if (image == null)
				{
					g.DrawRectangle(Pens.Silver, box);
					return;
				}
				double scale = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height);
				int w = (int)(image.Width * scale);
				int h = (int)(image.Height * scale);
				g.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
			}
		}
	}
}
