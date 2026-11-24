using System;
using System.Drawing;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>Asked before a run starts: automatic (the sure books are tagged by themselves) or manual (every book goes to the review).</summary>
	public sealed class StartModeForm : Form
	{
		/// <summary>True = manual, false = automatic. Valid when the window closed with OK.</summary>
		public bool Manual { get; private set; }

		/// <param name="manualByDefault">The mode of the settings: its button is the one that Enter presses.</param>
		public StartModeForm(int bookCount, bool manualByDefault)
		{
			Font = Ui.CreateFont();
			Text = Loc.T("start.title") + " (v" + AppInfo.Version + ")";
			StartPosition = FormStartPosition.CenterParent;
			ShowInTaskbar = false;
			MinimizeBox = false;
			MaximizeBox = false;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			Label question = new Label { Text = Loc.T("start.question", bookCount), AutoSize = true, MaximumSize = new Size(Ui.Px(460), 0) };
			Button automatic = new Button { Text = Loc.T("start.auto"), AutoSize = true };
			Button manual = new Button { Text = Loc.T("start.manual"), AutoSize = true };
			Button cancel = new Button { Text = Loc.T("start.cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
			automatic.Click += (s, e) => { Manual = false; DialogResult = DialogResult.OK; };
			manual.Click += (s, e) => { Manual = true; DialogResult = DialogResult.OK; };

			FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.Px(12), 0, 0) };
			buttons.Controls.Add(automatic);
			buttons.Controls.Add(manual);
			buttons.Controls.Add(cancel);

			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(Ui.Px(14)) };
			table.Controls.Add(question, 0, 0);
			table.Controls.Add(buttons, 0, 1);
			Controls.Add(table);

			AcceptButton = manualByDefault ? manual : automatic;
			CancelButton = cancel;
			Shown += (s, e) => (manualByDefault ? manual : automatic).Focus();
		}
	}
}
