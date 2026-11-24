using System.Reflection;

namespace ComicVineAI
{
	/// <summary>The version of the plugin, shown in the windows and in the log so a problem can be reported with it.</summary>
	public static class AppInfo
	{
		/// <summary>The number set in ComicVineAI.csproj (Version), plus " build MMdd-HHmm" in the builds that are not a release.</summary>
		public static string Version { get; } = ReadVersion();

		/// <summary>"Comic Vine AI 0.2.0 build 1120-1130".</summary>
		public static string NameAndVersion => "Comic Vine AI " + Version;

		private static string ReadVersion()
		{
			string text = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
			int plus = text.IndexOf('+'); // the build adds its stamp after a plus sign (BuildStamp in the csproj)
			return plus >= 0 ? text.Substring(0, plus) + " build " + text.Substring(plus + 1) : text;
		}
	}
}
