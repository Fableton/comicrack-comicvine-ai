using System;
using System.Drawing;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>Asks for the context of the research with AI: notes, links to a wiki or a publisher page, text copied from there.</summary>
	public sealed class AiContextForm : Form
	{
		private readonly TextBox context = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, WordWrap = true };

		/// <summary>What the user wrote (may be empty). Valid when the window closed with OK.</summary>
		public string Context => context.Text.Trim();

		public AiContextForm(bool chromeFallback)
		{
			Font = Ui.CreateFont();
			Text = Loc.T("ai.title") + " (v" + AppInfo.Version + ")";
			StartPosition = FormStartPosition.CenterParent;
			ShowInTaskbar = false;
			MinimizeBox = false;
			MaximizeBox = false;
			FormBorderStyle = FormBorderStyle.Sizable;
			ClientSize = Ui.Px(640, 400);
			MinimumSize = Ui.Px(480, 320);

			Label intro = new Label { Text = Loc.T(chromeFallback ? "ai.introChrome" : "ai.intro"), AutoSize = true, MaximumSize = new Size(Ui.Px(600), 0), Margin = new Padding(0, 0, 0, Ui.Px(8)) };
			Button research = new Button { Text = Loc.T("ai.research"), AutoSize = true, DialogResult = DialogResult.OK };
			Button cancel = new Button { Text = Loc.T("edit.cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
			FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.Px(8), 0, 0) };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(research);

			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.Px(12)) };
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(intro, 0, 0);
			table.Controls.Add(context, 0, 1);
			table.Controls.Add(buttons, 0, 2);
			Controls.Add(table);
			CancelButton = cancel;
			Shown += (s, e) => context.Focus();
		}
	}
}
