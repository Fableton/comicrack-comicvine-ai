using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>
	/// Texts of the plugin in several languages. English is the reference: any text missing in another language is shown in
	/// English. The languages built in (English, Spanish, French) are embedded in the DLL as Languages\xx.json. Anyone can
	/// add or correct a language without compiling: a file <c>languages\xx.json</c> in the plugin folder (same format, only the
	/// texts that change are needed) overrides the built in one. A file has "language.name" with the name of the language.
	/// </summary>
	public static class Loc
	{
		private static Dictionary<string, string> english = new Dictionary<string, string>();
		private static Dictionary<string, string> current = new Dictionary<string, string>();

		/// <summary>The language in use: "en", "es", "fr"...</summary>
		public static string Code { get; private set; } = "en";

		/// <summary>The built in languages: code and name, names written in their own language.</summary>
		public static readonly string[][] BuiltIn =
		{
			new[] { "en", "English" },
			new[] { "es", "Español" },
			new[] { "fr", "Français" }
		};

		static Loc()
		{
			Initialize("auto", null);
		}

		/// <summary>The language ComicRack runs in (it sets the UI culture of its thread).</summary>
		public static string DetectCode() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

		/// <param name="setting">"auto" (the language of ComicRack) or a language code.</param>
		/// <param name="pluginFolder">Where the optional languages folder is; null for none.</param>
		public static void Initialize(string setting, string pluginFolder)
		{
			Dictionary<string, string> reference = LoadEmbedded("en") ?? new Dictionary<string, string>();
			string code = string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase) ? DetectCode() : setting.Trim().ToLowerInvariant();

			Dictionary<string, string> map = new Dictionary<string, string>(reference);
			bool found = code == "en";
			if (code != "en")
			{
				Dictionary<string, string> embedded = LoadEmbedded(code);
				if (embedded != null)
				{
					Overlay(map, embedded);
					found = true;
				}
			}
			Dictionary<string, string> external = LoadFile(pluginFolder, code);
			if (external != null)
			{
				Overlay(map, external);
				found = true;
			}
			english = reference;
			current = map;
			Code = found ? code : "en";
		}

		public static string T(string key)
		{
			if (current.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text))
			{
				return text;
			}
			return english.TryGetValue(key, out text) ? text : key;
		}

		public static string T(string key, params object[] args)
		{
			string format = T(key);
			try
			{
				return string.Format(CultureInfo.CurrentCulture, format, args);
			}
			catch (FormatException)
			{
				return format;
			}
		}

		/// <summary>The name of the language in use in English, for the prompts sent to Claude.</summary>
		public static string EnglishName
		{
			get
			{
				try
				{
					return new CultureInfo(Code).EnglishName.Split('(')[0].Trim();
				}
				catch (CultureNotFoundException)
				{
					return "English";
				}
			}
		}

		/// <summary>The languages to choose from: the built in ones and the files found in the languages folder.</summary>
		public static List<string[]> Available(string pluginFolder)
		{
			List<string[]> list = BuiltIn.Select(l => new[] { l[0], l[1] }).ToList();
			try
			{
				string folder = pluginFolder == null ? null : Path.Combine(pluginFolder, "languages");
				if (folder != null && Directory.Exists(folder))
				{
					foreach (string file in Directory.GetFiles(folder, "*.json"))
					{
						string code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
						if (list.All(l => l[0] != code))
						{
							Dictionary<string, string> content = LoadFile(pluginFolder, code);
							list.Add(new[] { code, content != null && content.TryGetValue("language.name", out string name) ? name : code });
						}
					}
				}
			}
			catch (IOException)
			{
			}
			return list;
		}

		private static void Overlay(Dictionary<string, string> map, Dictionary<string, string> translated)
		{
			foreach (KeyValuePair<string, string> pair in translated)
			{
				if (!string.IsNullOrEmpty(pair.Value))
				{
					map[pair.Key] = pair.Value;
				}
			}
		}

		private static Dictionary<string, string> LoadEmbedded(string code)
		{
			try
			{
				Assembly assembly = typeof(Loc).Assembly;
				using (Stream stream = assembly.GetManifestResourceStream("ComicVineAI.lang." + code + ".json"))
				{
					if (stream == null)
					{
						return null;
					}
					using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
					{
						return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
					}
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Dictionary<string, string> LoadFile(string pluginFolder, string code)
		{
			try
			{
				string file = pluginFolder == null ? null : Path.Combine(pluginFolder, "languages", code + ".json");
				if (file != null && File.Exists(file))
				{
					return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(file, Encoding.UTF8));
				}
			}
			catch (Exception)
			{
				// a broken file of the user must not stop the plugin: the built in text is used
			}
			return null;
		}
	}
}
