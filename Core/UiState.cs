using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ComicVineAI
{
	/// <summary>Remembers window sizes and splitter positions between runs (ui.json in the plugin folder).</summary>
	public static class UiState
	{
		private static readonly Dictionary<string, int> values = new Dictionary<string, int>();
		private static string file;

		public static void Init(string folder)
		{
			file = Path.Combine(folder, "ui.json");
			values.Clear();
			try
			{
				if (File.Exists(file))
				{
					Dictionary<string, int> stored = new JavaScriptSerializer().Deserialize<Dictionary<string, int>>(File.ReadAllText(file));
					if (stored != null)
					{
						foreach (KeyValuePair<string, int> pair in stored)
						{
							values[pair.Key] = pair.Value;
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}

		private static int Get(string key, int fallback) => values.TryGetValue(key, out int value) ? value : fallback;

		private static void Save()
		{
			if (file == null)
			{
				return;
			}
			try
			{
				File.WriteAllText(file, new JavaScriptSerializer().Serialize(values));
			}
			catch (Exception)
			{
			}
		}

		/// <summary>Restores the size (and maximized state) of the form and stores them again when it closes.</summary>
		public static void Attach(Form form, string key, Size defaultSize)
		{
			// sizes are stored for 96 DPI (logical units) so they stay right when the zoom of Windows changes
			Rectangle area = Screen.PrimaryScreen.WorkingArea;
			int width = Math.Max(form.MinimumSize.Width, Math.Min(Ui.Px(Get(key + ".lw", defaultSize.Width)), area.Width));
			int height = Math.Max(form.MinimumSize.Height, Math.Min(Ui.Px(Get(key + ".lh", defaultSize.Height)), area.Height));
			form.Size = new Size(width, height);
			if (Get(key + ".max", 0) == 1)
			{
				form.WindowState = FormWindowState.Maximized;
			}
			form.FormClosed += (s, e) =>
			{
				bool normal = form.WindowState == FormWindowState.Normal;
				Size size = normal ? form.Size : form.RestoreBounds.Size;
				values[key + ".lw"] = Ui.Logical(size.Width);
				values[key + ".lh"] = Ui.Logical(size.Height);
				values[key + ".max"] = form.WindowState == FormWindowState.Maximized ? 1 : 0;
				Save();
			};
		}

		/// <summary>Restores the position of a splitter when the form is shown and remembers where the user leaves it.</summary>
		public static void AttachSplitter(Form form, SplitContainer splitter, string key, int defaultDistance, int panel1Min, int panel2Min)
		{
			form.Shown += (s, e) =>
			{
				// the minimum sizes can only be set once the splitter has its real size
				// all the numbers are in 96 DPI units
				int min1 = Ui.Px(panel1Min);
				int min2 = Ui.Px(panel2Min);
				splitter.SplitterWidth = Ui.Px(7);
				int total = splitter.Orientation == Orientation.Vertical ? splitter.Width : splitter.Height;
				if (total > min1 + min2 + splitter.SplitterWidth)
				{
					splitter.Panel1MinSize = min1;
					splitter.Panel2MinSize = min2;
					int max = total - min2 - splitter.SplitterWidth;
					splitter.SplitterDistance = Math.Max(min1, Math.Min(Ui.Px(Get(key + ".l", defaultDistance)), max));
				}
				splitter.SplitterMoved += (a, b) => values[key + ".l"] = Ui.Logical(splitter.SplitterDistance);
			};
		}
	}
}
