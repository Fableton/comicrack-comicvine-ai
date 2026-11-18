using System;
using System.Drawing;

namespace ComicVineAI
{
	/// <summary>
	/// Sizes that follow the zoom of the system. ComicRack is system DPI aware: fonts given in points scale by themselves,
	/// but pixel sizes do not. Every pixel size of the plugin goes through <see cref="Px(int)"/> (96 DPI units) and the
	/// text uses the system message font multiplied by <see cref="FontScale"/>.
	/// </summary>
	public static class Ui
	{
		private static float dpi;

		/// <summary>Text size multiplier (the "interface text size" setting).</summary>
		public static float FontScale { get; set; } = 1.25f;

		/// <summary>System zoom: 1.0 at 96 DPI (100%), 1.5 at 150%...</summary>
		public static float Dpi
		{
			get
			{
				if (dpi <= 0)
				{
					using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
					{
						dpi = g.DpiX / 96f;
					}
				}
				return dpi;
			}
		}

		/// <summary>A size given for 96 DPI converted to the real pixels of this system.</summary>
		public static int Px(int logical) => (int)Math.Round(logical * Dpi);

		/// <summary>The inverse of <see cref="Px(int)"/>, for the values that are stored.</summary>
		public static int Logical(int pixels) => (int)Math.Round(pixels / Dpi);

		public static Size Px(int width, int height) => new Size(Px(width), Px(height));

		/// <summary>The font of the windows: the system message font, a little larger.</summary>
		public static Font CreateFont(FontStyle style = FontStyle.Regular)
		{
			Font system = SystemFonts.MessageBoxFont;
			return new Font(system.FontFamily, system.SizeInPoints * FontScale, style, GraphicsUnit.Point);
		}

		public static Font CreateMonoFont() => new Font(FontFamily.GenericMonospace, SystemFonts.MessageBoxFont.SizeInPoints * FontScale, FontStyle.Regular, GraphicsUnit.Point);
	}
}
