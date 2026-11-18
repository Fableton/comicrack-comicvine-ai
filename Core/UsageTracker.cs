using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ComicVineAI
{
	/// <summary>
	/// Counts the calls made to Comic Vine, per resource (search, issues, issue, volume...), over the last hour.
	/// Comic Vine allows 200 requests per hour for each resource. The counts are saved so they survive restarts, and
	/// every real call is written to calls.log with the book and the stage that caused it, to see where the calls go.
	/// </summary>
	public sealed class UsageTracker
	{
		/// <summary>What Comic Vine allows for each resource every hour.</summary>
		public const int Limit = 200;

		/// <summary>The plugin stops here and waits, to leave room for other tools that share the API key.</summary>
		public const int SafeLimit = 190;

		private const long Window = 3600;
		private const long MaxLogBytes = 2 * 1024 * 1024;

		private readonly object sync = new object();
		private readonly Dictionary<string, List<long>> calls = new Dictionary<string, List<long>>();
		private readonly string usageFile;
		private readonly string callLog;

		public UsageTracker(string folder)
		{
			usageFile = Path.Combine(folder, "usage.json");
			callLog = Path.Combine(folder, "calls.log");
			try
			{
				if (File.Exists(usageFile))
				{
					Dictionary<string, List<long>> stored = new JavaScriptSerializer().Deserialize<Dictionary<string, List<long>>>(File.ReadAllText(usageFile));
					if (stored != null)
					{
						foreach (KeyValuePair<string, List<long>> pair in stored)
						{
							calls[pair.Key] = pair.Value ?? new List<long>();
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}

		private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		private List<long> Recent(string resource)
		{
			if (!calls.TryGetValue(resource, out List<long> list))
			{
				list = new List<long>();
				calls[resource] = list;
			}
			long cut = Now - Window;
			list.RemoveAll(t => t <= cut);
			return list;
		}

		/// <summary>Calls of the last hour for a resource.</summary>
		public int Count(string resource)
		{
			lock (sync)
			{
				return Recent(resource).Count;
			}
		}

		/// <summary>Seconds to wait before one more call stays under the safe limit; 0 when it can go now.</summary>
		public int SecondsUntilBudget(string resource)
		{
			lock (sync)
			{
				List<long> list = Recent(resource);
				if (list.Count < SafeLimit)
				{
					return 0;
				}
				// the call that is (count - SafeLimit) places from the oldest has to leave the one hour window
				long free = list.OrderBy(t => t).ElementAt(list.Count - SafeLimit) + Window + 2;
				return (int)Math.Max(1, free - Now);
			}
		}

		public void Record(string resource, string detail, string context)
		{
			lock (sync)
			{
				Recent(resource).Add(Now);
				try
				{
					File.WriteAllText(usageFile, new JavaScriptSerializer().Serialize(calls));
					if (File.Exists(callLog) && new FileInfo(callLog).Length > MaxLogBytes)
					{
						File.Delete(callLog); // it only has to cover the recent runs
					}
					File.AppendAllText(callLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {resource,-8} {Count(resource),3}/{Limit}  {detail}  [{context}]{Environment.NewLine}", new UTF8Encoding(false));
				}
				catch (IOException)
				{
				}
			}
		}

		/// <summary>For example "search 25/200, issues 87/200" with the resources used in the last hour.</summary>
		public string Summary()
		{
			lock (sync)
			{
				string text = string.Join(", ", calls.Keys.OrderBy(k => k).Select(k => new { k, n = Recent(k).Count }).Where(x => x.n > 0).Select(x => $"{x.k} {x.n}/{Limit}"));
				return text.Length == 0 ? "none" : text;
			}
		}
	}
}
