using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ComicVineAI
{
	/// <summary>Key/value store on disk. Entries older than the time to live are ignored.</summary>
	public sealed class DiskCache
	{
		private readonly string folder;
		private readonly TimeSpan timeToLive;

		public DiskCache(string folder, int days)
		{
			this.folder = folder;
			timeToLive = TimeSpan.FromDays(Math.Max(1, days));
			Directory.CreateDirectory(folder);
		}

		private string PathFor(string key, string extension)
		{
			using (SHA1 sha = SHA1.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
				return Path.Combine(folder, BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + extension);
			}
		}

		private bool IsFresh(string file) => File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < timeToLive;

		public bool TryGetText(string key, out string text)
		{
			string file = PathFor(key, ".json");
			text = null;
			try
			{
				if (IsFresh(file))
				{
					text = File.ReadAllText(file, Encoding.UTF8);
					return true;
				}
			}
			catch (IOException)
			{
			}
			return false;
		}

		public void PutText(string key, string text)
		{
			try
			{
				File.WriteAllText(PathFor(key, ".json"), text, Encoding.UTF8);
			}
			catch (IOException)
			{
			}
		}

		public bool TryGetBytes(string key, out byte[] bytes)
		{
			string file = PathFor(key, ".img");
			bytes = null;
			try
			{
				// images never change for a URL, so they do not expire
				if (File.Exists(file))
				{
					bytes = File.ReadAllBytes(file);
					return true;
				}
			}
			catch (IOException)
			{
			}
			return false;
		}

		public void PutBytes(string key, byte[] bytes)
		{
			try
			{
				File.WriteAllBytes(PathFor(key, ".img"), bytes);
			}
			catch (IOException)
			{
			}
		}

		/// <summary>The folder of the cache (other caches of the plugin keep their files next to these).</summary>
		public string Folder => folder;

		/// <summary>True when the image of this url is already on disk (without reading it).</summary>
		public bool HasImage(string key) => File.Exists(PathFor(key, ".img"));

		public bool HasBlob(string key, string extension) => File.Exists(PathFor(key, extension));

		/// <summary>A binary value that never expires (the fingerprint of a cover).</summary>
		public bool TryGetBlob(string key, string extension, out byte[] bytes)
		{
			bytes = null;
			try
			{
				string file = PathFor(key, extension);
				if (File.Exists(file))
				{
					bytes = File.ReadAllBytes(file);
					return true;
				}
			}
			catch (IOException)
			{
			}
			return false;
		}

		public void PutBlob(string key, string extension, byte[] bytes)
		{
			try
			{
				File.WriteAllBytes(PathFor(key, extension), bytes);
			}
			catch (IOException)
			{
			}
		}
	}
}
