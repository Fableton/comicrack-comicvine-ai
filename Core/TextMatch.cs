using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ComicVineAI
{
	/// <summary>Fuzzy comparison of series names.</summary>
	public static class TextMatch
	{
		public static string Normalize(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return "";
			}
			string decomposed = text.Normalize(NormalizationForm.FormD);
			StringBuilder sb = new StringBuilder(decomposed.Length);
			foreach (char c in decomposed)
			{
				if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
				{
					continue;
				}
				if (c == '&')
				{
					sb.Append(" and ");
				}
				else if (char.IsLetterOrDigit(c))
				{
					sb.Append(char.ToLowerInvariant(c));
				}
				else
				{
					sb.Append(' ');
				}
			}
			string result = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
			if (result.StartsWith("the "))
			{
				result = result.Substring(4);
			}
			return result;
		}

		/// <summary>Similarity of two names, 0..1.</summary>
		public static double Similarity(string a, string b)
		{
			string na = Normalize(a);
			string nb = Normalize(b);
			if (na.Length == 0 || nb.Length == 0)
			{
				return 0;
			}
			if (na == nb)
			{
				return 1;
			}
			double edit = 1.0 - (double)Levenshtein(na, nb) / Math.Max(na.Length, nb.Length);
			return Math.Max(edit, TokenDice(na, nb));
		}

		private static double TokenDice(string a, string b)
		{
			string[] ta = a.Split(' ').Distinct().ToArray();
			string[] tb = b.Split(' ').Distinct().ToArray();
			return 2.0 * ta.Intersect(tb).Count() / (ta.Length + tb.Length);
		}

		private static int Levenshtein(string a, string b)
		{
			int[] previous = new int[b.Length + 1];
			int[] current = new int[b.Length + 1];
			for (int j = 0; j <= b.Length; j++)
			{
				previous[j] = j;
			}
			for (int i = 1; i <= a.Length; i++)
			{
				current[0] = i;
				for (int j = 1; j <= b.Length; j++)
				{
					int cost = a[i - 1] == b[j - 1] ? 0 : 1;
					current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
				}
				int[] swap = previous;
				previous = current;
				current = swap;
			}
			return previous[b.Length];
		}

		/// <summary>"003" and "3" are the same issue number.</summary>
		public static string NormalizeNumber(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
			{
				return "";
			}
			string trimmed = number.Trim().TrimStart('#').Trim().TrimStart('0');
			if (trimmed.Length == 0)
			{
				return "0";
			}
			if (trimmed[0] == '.')
			{
				trimmed = "0" + trimmed;
			}
			int dot = trimmed.IndexOf('.');
			if (dot > 0 && trimmed.Substring(dot + 1).All(c => c == '0'))
			{
				trimmed = trimmed.Substring(0, dot); // "12.0" is #12
			}
			return trimmed.ToLowerInvariant();
		}
	}
}
