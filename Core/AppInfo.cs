using System.Reflection;

namespace ComicVineAI
{
	/// <summary>The version of the plugin, shown in the windows and in the log so a problem can be reported with it.</summary>
	public static class AppInfo
	{
		/// <summary>The number set in ComicVineAI.csproj (Version).</summary>
		public static string Version { get; } = ReadVersion();

		/// <summary>"Comic Vine AI 0.1.0".</summary>
		public static string NameAndVersion => "Comic Vine AI " + Version;

		private static string ReadVersion()
		{
			string text = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
			int plus = text.IndexOf('+'); // the build adds the commit after a plus sign
			return plus >= 0 ? text.Substring(0, plus) : text;
		}
	}
}
