using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ComicVineAI
{
	/// <summary>
	/// Compact description of a cover image: a 64 bit difference hash plus a 16x16 grayscale thumbnail.
	/// Two scans of the same cover score close to 1, unrelated covers close to 0.
	/// </summary>
	public sealed class CoverFingerprint
	{
		private const int GraySize = 16;

		private const int GridWidth = 24;
		private const int GridHeight = 36;
		private const double TrimKept = 0.8; // the 20% of the cells that differ most are ignored (a translation group strip, a barcode box)
		private const double WorstDistance = 0.9; // the distance (z-scored grays) at which two covers have nothing in common
		private static readonly double[] Insets = { 0, 0.02, 0.04, 0.06, 0.08 };

		private readonly ulong dHash;
		private readonly double[] gray;
		private readonly float[][] grids; // z-scored gray grids of the cover with 0, 2, 4, 6 and 8% cut off every side; null when decoded from the cache

		private CoverFingerprint(ulong dHash, double[] gray, float[][] grids = null)
		{
			this.dHash = dHash;
			this.gray = gray;
			this.grids = grids;
		}

		public static CoverFingerprint From(Image image)
		{
			if (image == null || image.Width < 8 || image.Height < 8)
			{
				return null;
			}
			double[] d = Luma(image, 9, 8);
			ulong hash = 0;
			for (int y = 0; y < 8; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					hash <<= 1;
					if (d[y * 9 + x] > d[y * 9 + x + 1])
					{
						hash |= 1;
					}
				}
			}
			return new CoverFingerprint(hash, Luma(image, GraySize, GraySize), BuildGrids(image));
		}

		/// <summary>The fingerprint as text, to store it: the 64 bit hash and the 16x16 grayscale thumbnail.</summary>
		public string Encode()
		{
			byte[] bytes = gray.Select(g => (byte)Math.Max(0, Math.Min(255, Math.Round(g)))).ToArray();
			return dHash.ToString("x16") + ":" + Convert.ToBase64String(bytes);
		}

		/// <summary>Null when the text is not a fingerprint.</summary>
		public static CoverFingerprint Decode(string text)
		{
			try
			{
				string[] parts = (text ?? "").Split(':');
				byte[] bytes = Convert.FromBase64String(parts[1]);
				if (parts.Length != 2 || bytes.Length != GraySize * GraySize)
				{
					return null;
				}
				return new CoverFingerprint(Convert.ToUInt64(parts[0], 16), bytes.Select(b => (double)b).ToArray());
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Similarity 0..1 of two covers. The covers are compared cell by cell on a small grid of grays, after cutting a few percent off
		/// the edges of either one (scans and Comic Vine images are framed differently) and ignoring the cells that differ most
		/// (the logos a translation group adds, a barcode box). In a test with covers that were cropped, padded, rescaled, recompressed and
		/// marked, this puts the same cover at 85% on average where a global hash gives 65%, and the next issue of the same series
		/// stays far below. Fingerprints without grids (read from the cache of Claude answers) are compared with the small hash.
		/// </summary>
		public double Similarity(CoverFingerprint other)
		{
			if (other == null)
			{
				return 0;
			}
			if (grids == null || other.grids == null)
			{
				return LegacySimilarity(other);
			}
			double best = double.MaxValue;
			float[] diff = new float[GridWidth * GridHeight];
			foreach (float[] a in grids)
			{
				foreach (float[] b in other.grids)
				{
					for (int i = 0; i < diff.Length; i++)
					{
						float d = a[i] - b[i];
						diff[i] = d * d;
					}
					Array.Sort(diff);
					double sum = 0;
					int kept = (int)(diff.Length * TrimKept);
					for (int i = 0; i < kept; i++)
					{
						sum += diff[i];
					}
					best = Math.Min(best, sum / kept);
				}
			}
			return Math.Max(0, Math.Min(1, 1 - best / WorstDistance));
		}

		private static float[][] BuildGrids(Image image)
		{
			float[][] result = new float[Insets.Length][];
			for (int n = 0; n < Insets.Length; n++)
			{
				double inset = Insets[n];
				using (Bitmap small = new Bitmap(GridWidth, GridHeight, PixelFormat.Format24bppRgb))
				{
					using (Graphics g = Graphics.FromImage(small))
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.PixelOffsetMode = PixelOffsetMode.HighQuality;
						g.SmoothingMode = SmoothingMode.HighQuality;
						g.DrawImage(image, new Rectangle(0, 0, GridWidth, GridHeight), (float)(inset * image.Width), (float)(inset * image.Height),
							(float)(image.Width * (1 - 2 * inset)), (float)(image.Height * (1 - 2 * inset)), GraphicsUnit.Pixel);
					}
					double[] values = new double[GridWidth * GridHeight];
					for (int y = 0; y < GridHeight; y++)
					{
						for (int x = 0; x < GridWidth; x++)
						{
							Color c = small.GetPixel(x, y);
							values[y * GridWidth + x] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
						}
					}
					double mean = values.Average();
					double deviation = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
					if (deviation < 1e-6)
					{
						deviation = 1; // a flat image: all zeros
					}
					result[n] = values.Select(v => (float)((v - mean) / deviation)).ToArray();
				}
			}
			return result;
		}

		/// <summary>The small hash and thumbnail only (what is kept in the cache): about 0.93 for two scans of one cover.</summary>
		public double LegacySimilarity(CoverFingerprint other)
		{
			if (other == null)
			{
				return 0;
			}
			int distance = PopCount(dHash ^ other.dHash);
			double hashSimilarity = Math.Max(0, 1.0 - 2.0 * distance / 64.0);
			return 0.5 * hashSimilarity + 0.5 * Math.Max(0, Correlation(gray, other.gray));
		}

		private static int PopCount(ulong value)
		{
			int count = 0;
			while (value != 0)
			{
				value &= value - 1;
				count++;
			}
			return count;
		}

		private static double Correlation(double[] a, double[] b)
		{
			double meanA = 0, meanB = 0;
			for (int i = 0; i < a.Length; i++)
			{
				meanA += a[i];
				meanB += b[i];
			}
			meanA /= a.Length;
			meanB /= b.Length;
			double cov = 0, varA = 0, varB = 0;
			for (int i = 0; i < a.Length; i++)
			{
				double da = a[i] - meanA;
				double db = b[i] - meanB;
				cov += da * db;
				varA += da * da;
				varB += db * db;
			}
			if (varA <= 0 || varB <= 0)
			{
				return 0;
			}
			return cov / Math.Sqrt(varA * varB);
		}

		private static double[] Luma(Image image, int width, int height)
		{
			using (Bitmap small = new Bitmap(width, height, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(small))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.SmoothingMode = SmoothingMode.HighQuality;
					g.DrawImage(image, new Rectangle(0, 0, width, height));
				}
				double[] result = new double[width * height];
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						Color c = small.GetPixel(x, y);
						result[y * width + x] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
					}
				}
				return result;
			}
		}

		/// <summary>
		/// A double page spread (front and back cover side by side) keeps only the front cover, the right half.
		/// Same rule as the Python scraper. Returns a new image in that case and disposes the original.
		/// </summary>
		public static Bitmap StripBackCover(Bitmap image)
		{
			if (image == null || image.Height == 0)
			{
				return image;
			}
			double ratio = (double)image.Width / image.Height;
			if (ratio > 1.2 && ratio < 1.5)
			{
				Bitmap front = new Bitmap(image.Width / 2, image.Height);
				using (Graphics g = Graphics.FromImage(front))
				{
					g.DrawImage(image, -image.Width / 2, 0);
				}
				image.Dispose();
				return front;
			}
			return image;
		}
	}
}
