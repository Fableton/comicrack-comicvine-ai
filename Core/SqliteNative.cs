using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ComicVineAI
{
	/// <summary>
	/// A minimal SQLite binding. The plugin is a single DLL: the native SQLite library (the official build, public domain, with
	/// FTS5) is embedded in it as a resource for 32 and 64 bits, written once to
	/// %LOCALAPPDATA%\ComicVineAI\native\ and loaded from there, so nothing has to be installed next to the plugin.
	/// </summary>
	internal static class SqliteNative
	{
		private const string Lib = "e_sqlite3";

		public const int Ok = 0, Row = 100, Done = 101;
		public const int TypeInteger = 1, TypeFloat = 2, TypeText = 3, TypeBlob = 4, TypeNull = 5;
		public const int OpenReadOnly = 0x1, OpenUri = 0x40, OpenFullMutex = 0x10000;

		private static readonly object loadLock = new object();
		private static bool loaded;

		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr LoadLibrary(string path);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_close_v2(IntPtr db);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr stmt);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr stmt);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int length, IntPtr destructor);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_null(IntPtr stmt, int index);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_count(IntPtr stmt);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_type(IntPtr stmt, int column);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long sqlite3_column_int64(IntPtr stmt, int column);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern double sqlite3_column_double(IntPtr stmt, int column);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_bytes(IntPtr stmt, int column);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_libversion();

		public static readonly IntPtr Transient = new IntPtr(-1); // SQLITE_TRANSIENT: SQLite copies the text

		/// <summary>Extracts the embedded library (once) and loads it. Throws if it cannot be loaded.</summary>
		public static void EnsureLoaded()
		{
			lock (loadLock)
			{
				if (loaded)
				{
					return;
				}
				string arch = IntPtr.Size == 8 ? "x64" : "x86";
				Assembly assembly = typeof(SqliteNative).Assembly;
				byte[] bytes;
				using (Stream stream = assembly.GetManifestResourceStream("ComicVineAI.native.e_sqlite3." + arch + ".dll"))
				{
					if (stream == null)
					{
						throw new InvalidOperationException(Loc.T("sqlite.notEmbedded", arch));
					}
					using (MemoryStream ms = new MemoryStream())
					{
						stream.CopyTo(ms);
						bytes = ms.ToArray();
					}
				}

				// the folder name carries a hash of the library: a newer plugin with a newer SQLite never meets an old file
				string hash;
				using (SHA1 sha = SHA1.Create())
				{
					hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 12).ToLowerInvariant();
				}
				string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicVineAI", "native", arch + "-" + hash);
				string file = Path.Combine(folder, "e_sqlite3.dll");
				if (!File.Exists(file) || new FileInfo(file).Length != bytes.Length)
				{
					Directory.CreateDirectory(folder);
					string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
					File.WriteAllBytes(temp, bytes);
					try
					{
						if (File.Exists(file))
						{
							File.Delete(file);
						}
						File.Move(temp, file);
					}
					catch (IOException)
					{
						File.Delete(temp); // another ComicRack window won the race: its copy is the same file
					}
				}
				if (LoadLibrary(file) == IntPtr.Zero)
				{
					throw new InvalidOperationException(Loc.T("sqlite.cannotLoad", file, Marshal.GetLastWin32Error()));
				}
				loaded = true;
			}
		}

		public static byte[] Utf8(string text)
		{
			byte[] raw = Encoding.UTF8.GetBytes(text ?? "");
			byte[] withZero = new byte[raw.Length + 1];
			Buffer.BlockCopy(raw, 0, withZero, 0, raw.Length);
			return withZero;
		}

		public static string FromUtf8(IntPtr pointer, int length)
		{
			if (pointer == IntPtr.Zero)
			{
				return null;
			}
			byte[] bytes = new byte[length];
			Marshal.Copy(pointer, bytes, 0, length);
			return Encoding.UTF8.GetString(bytes);
		}

		public static string Message(IntPtr db)
		{
			IntPtr p = sqlite3_errmsg(db);
			return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
		}

		public static string Version() => Marshal.PtrToStringAnsi(sqlite3_libversion());
	}
}
