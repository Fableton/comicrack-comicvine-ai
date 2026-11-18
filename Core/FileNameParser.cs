using System.IO;
using System.Text.RegularExpressions;

namespace ComicVineAI
{
	public class ParsedName
	{
		public string Series = "";
		public string Number = "";
		public int Year;
		public int Volume;
	}

	/// <summary>Extracts series, number, year and volume from a comic file name.</summary>
	public static class FileNameParser
	{
		private static readonly Regex yearInBrackets = new Regex(@"[\(\[]\s*((?:19|20)\d{2})\s*[\)\]]", RegexOptions.Compiled);
		private static readonly Regex yearAlone = new Regex(@"(?<![\d#.])((?:19|20)\d{2})(?![\d])", RegexOptions.Compiled);
		private static readonly Regex brackets = new Regex(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);
		private static readonly Regex volume = new Regex(@"\b(?:vol(?:ume)?|tomo|tome|v)\.?\s*(\d{1,4})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex numberOf = new Regex(@"(?<![\d.])(\d{1,4}(?:\.\d+)?)\s+(?:of|de)\s+\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex numberMarked = new Regex(@"(?:#|\bno\.?|\bn[º°o]\.?|\bnum\.?|\bissue\.?|\bnumero\b)\s*(\d{1,4}(?:\.\d+)?[a-z]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex numberTrailing = new Regex(@"(?<![\d.])(\d{1,4}(?:\.\d+)?)\s*$", RegexOptions.Compiled);
		private static readonly Regex numberAnywhere = new Regex(@"\s(\d{1,4}(?:\.\d+)?)(?=\s|$)", RegexOptions.Compiled);

		public static ParsedName Parse(string fileName)
		{
			ParsedName result = new ParsedName();
			if (string.IsNullOrWhiteSpace(fileName))
			{
				return result;
			}
			string text = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');

			Match year = yearInBrackets.Match(text);
			if (year.Success)
			{
				result.Year = int.Parse(year.Groups[1].Value);
			}
			text = brackets.Replace(text, " ");
			if (result.Year == 0)
			{
				MatchCollection years = yearAlone.Matches(text);
				if (years.Count > 0)
				{
					// the last 4 digit number that looks like a year; "Batman 2016" alone stays the name
					Match last = years[years.Count - 1];
					string without = text.Remove(last.Index, last.Length).Trim();
					if (without.Length > 0 && (years.Count > 1 || numberMarked.IsMatch(without) || numberTrailing.IsMatch(without) || last.Index > 0))
					{
						result.Year = int.Parse(last.Groups[1].Value);
						text = without;
					}
				}
			}

			Match vol = volume.Match(text);
			if (vol.Success)
			{
				result.Volume = int.Parse(vol.Groups[1].Value);
				text = text.Remove(vol.Index, vol.Length);
			}

			text = text.Trim();
			Match number = numberOf.Match(text);
			if (!number.Success)
			{
				number = numberMarked.Match(text);
			}
			if (!number.Success)
			{
				number = numberTrailing.Match(text);
			}
			if (!number.Success)
			{
				number = numberAnywhere.Match(text);
			}
			if (number.Success)
			{
				result.Number = number.Groups[1].Value;
				text = text.Remove(number.Index, number.Length);
			}

			result.Series = Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '_', ',', ':', '.', '#');
			return result;
		}
	}
}
