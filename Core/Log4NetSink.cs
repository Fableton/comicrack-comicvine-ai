using System.Text;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace ComicVineAI
{
	/// <summary>The only place that uses log4net (see FileLog). Own repository, so it never touches the configuration of another log4net in the process.</summary>
	internal static class Log4NetSink
	{
		private static ILog logger;

		public static void Start(string file)
		{
			PatternLayout layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss}  %message%newline");
			layout.ActivateOptions();
			RollingFileAppender appender = new RollingFileAppender
			{
				File = file,
				AppendToFile = true,
				StaticLogFileName = true,
				RollingStyle = RollingFileAppender.RollingMode.Size,
				MaximumFileSize = "1MB",
				MaxSizeRollBackups = 3,
				LockingModel = new FileAppender.MinimalLock(), // two ComicRack windows can share the file
				Encoding = new UTF8Encoding(false),
				Layout = layout
			};
			appender.ActivateOptions();
			Hierarchy repository = (Hierarchy)LogManager.CreateRepository("ComicVineAI");
			repository.Root.AddAppender(appender);
			repository.Root.Level = Level.All;
			repository.Configured = true;
			logger = LogManager.GetLogger("ComicVineAI", "plugin");
		}

		public static void Write(string message)
		{
			logger?.Info(message);
		}
	}
}
