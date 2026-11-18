using System.Drawing;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>A bar for a 0..1 confidence with a marker at the acceptance threshold. Green when it passes.</summary>
	public sealed class ConfidenceBar : Control
	{
		private double value = -1;

		/// <summary>Confidence 0..1, or a negative number for "none".</summary>
		public double Value
		{
			get => value;
			set
			{
				this.value = value;
				Invalidate();
			}
		}

		public double Threshold { get; set; } = 0.8;

		public ConfidenceBar()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw, true);
			FitHeight();
		}

		private void FitHeight() => Height = Font.Height + Ui.Px(10);

		protected override void OnFontChanged(System.EventArgs e)
		{
			base.OnFontChanged(e);
			FitHeight();
		}

		protected override void OnParentChanged(System.EventArgs e)
		{
			base.OnParentChanged(e);
			FitHeight(); // the font is inherited from the parent
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
			using (Brush back = new SolidBrush(Color.FromArgb(235, 235, 235)))
			{
				g.FillRectangle(back, box);
			}
			string text = Loc.T("bar.none");
			if (value >= 0)
			{
				Color color = value >= Threshold ? Color.FromArgb(76, 175, 80) : value >= Threshold * 0.8 ? Color.FromArgb(255, 179, 0) : Color.FromArgb(229, 115, 115);
				using (Brush fill = new SolidBrush(color))
				{
					g.FillRectangle(fill, 0, 0, (int)(box.Width * System.Math.Min(1, value)), box.Height);
				}
				int x = (int)(box.Width * Threshold);
				g.DrawLine(Pens.Black, x, 0, x, box.Height);
				text = Loc.T("bar.value", value.ToString("P0"), Threshold.ToString("P0"));
			}
			g.DrawRectangle(Pens.Gray, box);
			TextRenderer.DrawText(g, text, Font, box, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		}
	}
}
