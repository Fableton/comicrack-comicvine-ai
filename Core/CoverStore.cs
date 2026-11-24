using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ComicVineAI
{
	/// <summary>
	/// What was already worked out about covers, kept on disk so no cover is analysed twice:
	/// the fingerprint of every Comic Vine cover (by its url) and the similarity between the cover of a book and a Comic Vine cover
	/// (by the fingerprint of the book and the url). Both belong to a version of the comparison (CoverFingerprint.Version).
	/// Comic Vine images never change for a url, so nothing expires.
	/// </summary>
	public sealed class CoverStore
	{
		private readonly DiskCache cache;
		private readonly string scoreFile;
		private readonly Dictionary<string, float> scores = new Dictionary<string, float>();
		private readonly object gate = new object();

		public CoverStore(DiskCache cache)
		{
			this.cache = cache;
			scoreFile = Path.Combine(cache.Folder, "cover-scores-v" + CoverFingerprint.Version + ".txt");
			Load();
		}

		/// <summary>How many book/cover similarities are known (for the log and the tests).</summary>
		public int ScoreCount
		{
			get
			{
				lock (gate)
				{
					return scores.Count;
				}
			}
		}

		private static string UrlKey(string url)
		{
			using (SHA1 sha = SHA1.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url)), 0, 8).Replace("-", "").ToLowerInvariant();
			}
		}

		private static string FingerprintKey(string url) => "fp" + CoverFingerprint.Version + ":" + url;

		private void Load()
		{
			try
			{
				if (!File.Exists(scoreFile))
				{
					return;
				}
				foreach (string line in File.ReadAllLines(scoreFile, Encoding.ASCII))
				{
					string[] p = line.Split(' ');
					if (p.Length == 3 && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float score))
					{
						scores[p[0] + p[1]] = score;
					}
				}
			}
			catch (Exception)
			{
				// a damaged file only means the scores are worked out again
			}
		}

		public bool TryGetScore(CoverFingerprint book, string url, out double score)
		{
			score = 0;
			if (book == null || string.IsNullOrEmpty(url))
			{
				return false;
			}
			lock (gate)
			{
				if (scores.TryGetValue(book.Key + UrlKey(url), out float value))
				{
					score = value;
					return true;
				}
			}
			return false;
		}

		public void PutScore(CoverFingerprint book, string url, double score)
		{
			if (book == null || string.IsNullOrEmpty(url) || score < 0)
			{
				return;
			}
			string b = book.Key;
			string u = UrlKey(url);
			lock (gate)
			{
				if (scores.ContainsKey(b + u))
				{
					return;
				}
				scores[b + u] = (float)score;
				try
				{
					File.AppendAllText(scoreFile, b + " " + u + " " + score.ToString("0.000", CultureInfo.InvariantCulture) + "\n", Encoding.ASCII);
				}
				catch (IOException)
				{
				}
			}
		}

		public bool HasFingerprint(string url) => cache.HasBlob(FingerprintKey(url), ".fp");

		/// <summary>The stored fingerprint of the Comic Vine cover at this url, or null.</summary>
		public CoverFingerprint GetFingerprint(string url)
		{
			return cache.TryGetBlob(FingerprintKey(url), ".fp", out byte[] bytes) ? CoverFingerprint.DecodeFull(bytes) : null;
		}

		public void PutFingerprint(string url, CoverFingerprint fingerprint)
		{
			byte[] bytes = fingerprint?.EncodeFull();
			if (bytes != null)
			{
				cache.PutBlob(FingerprintKey(url), ".fp", bytes);
			}
		}
	}
}
