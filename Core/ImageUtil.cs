using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ComicVineAI
{
	/// <summary>Covers are kept as small JPEG byte arrays so a long batch does not fill the memory.</summary>
	public static class ImageUtil
	{
		public static byte[] Encode(Image source, int maxHeight, long quality = 80)
		{
			if (source == null || source.Width == 0 || source.Height == 0)
			{
				return null;
			}
			double scale = source.Height > maxHeight ? (double)maxHeight / source.Height : 1.0;
			int width = Math.Max(1, (int)(source.Width * scale));
			int height = Math.Max(1, (int)(source.Height * scale));
			using (Bitmap scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(scaled))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.DrawImage(source, new Rectangle(0, 0, width, height));
				}
				ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
				using (EncoderParameters parameters = new EncoderParameters(1))
				using (MemoryStream ms = new MemoryStream())
				{
					parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
					scaled.Save(ms, jpeg, parameters);
					return ms.ToArray();
				}
			}
		}

		public static Bitmap Decode(byte[] bytes)
		{
			if (bytes == null)
			{
				return null;
			}
			try
			{
				using (MemoryStream ms = new MemoryStream(bytes))
				using (Image image = Image.FromStream(ms))
				{
					return new Bitmap(image);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
