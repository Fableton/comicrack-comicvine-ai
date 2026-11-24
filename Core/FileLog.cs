using System;
using System.IO;
using System.Reflection;

namespace ComicVineAI
{
	/// <summary>
	/// The log file of the plugin (plugin.log in the plugin folder, 1 MB x 3 files): every line of the progress window goes to it, so it can
	/// be read after the window is closed. It never throws: a log that cannot be written must not stop the identification.
	/// log4net is embedded in the DLL (the plugin stays a single file) and is only touched through Log4NetSink, so the resolver below is
	/// registered before the runtime looks for the assembly.
	/// </summary>
	public static class FileLog
	{
		private static readonly object gate = new object();
		private static bool started;
		private static bool failed;

		public static string FilePath { get; private set; }

		public static void Start(string folder)
		{
			lock (gate)
			{
				if (started || failed)
				{
					return;
				}
				try
				{
					AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
					Directory.CreateDirectory(folder);
					FilePath = Path.Combine(folder, "plugin.log");
					Log4NetSink.Start(FilePath);
					started = true;
				}
				catch (Exception)
				{
					failed = true;
				}
			}
		}

		public static void Write(string message)
		{
			if (!started || string.IsNullOrEmpty(message))
			{
				return;
			}
			try
			{
				Log4NetSink.Write(message);
			}
			catch (Exception)
			{
				// see the summary
			}
		}

		private static Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
		{
			if (!new AssemblyName(args.Name).Name.Equals("log4net", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			using (Stream stream = typeof(FileLog).Assembly.GetManifestResourceStream("ComicVineAI.lib.log4net.dll"))
			{
				if (stream == null)
				{
					return null;
				}
				byte[] bytes = new byte[stream.Length];
				int read = 0;
				while (read < bytes.Length)
				{
					int n = stream.Read(bytes, read, bytes.Length - read);
					if (n <= 0)
					{
						break;
					}
					read += n;
				}
				return Assembly.Load(bytes);
			}
		}
	}
}
