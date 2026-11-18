using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ComicVineAI
{
	/// <summary>
	/// Asks the Claude CLI (claude -p) to identify a comic from its cover. The cover is written to a temporary
	/// folder and Claude reads it with its Read tool; the prompt goes through stdin.
	/// </summary>
	public static class ClaudeCli
	{
		private const int TimeoutMs = 180000;

		public static string FindExecutable(PluginSettings settings)
		{
			if (!string.IsNullOrWhiteSpace(settings.ClaudePath) && File.Exists(settings.ClaudePath))
			{
				return settings.ClaudePath;
			}
			string path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (string folder in path.Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p)))
			{
				foreach (string name in new[] { "claude.exe", "claude.cmd", "claude.bat" })
				{
					try
					{
						string candidate = Path.Combine(folder.Trim('"'), name);
						if (File.Exists(candidate))
						{
							return candidate;
						}
					}
					catch (ArgumentException)
					{
					}
				}
			}
			return null;
		}

		public static ClaudeSuggestion Identify(Bitmap cover, BookHints hints, PluginSettings settings, Action<string> log, CancellationToken token)
		{
			string exe = FindExecutable(settings);
			if (exe == null)
			{
				throw new InvalidOperationException(Loc.T("claude.notFound"));
			}

			string work = Path.Combine(Path.GetTempPath(), "ComicVineAI-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(work);
			try
			{
				SaveJpeg(cover, Path.Combine(work, "cover.jpg"));
				string output = Run(exe, settings, BuildPrompt(hints, settings), work, token);
				return Parse(output, log);
			}
			finally
			{
				try
				{
					Directory.Delete(work, true);
				}
				catch (IOException)
				{
				}
			}
		}

		private static string BuildPrompt(BookHints hints, PluginSettings settings)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("You are helping to identify a comic book from its cover.");
			sb.AppendLine("The cover image is the file cover.jpg in the current directory: view it with the Read tool.");
			sb.AppendLine("What the user's library currently says about it (it may be wrong, translated or localized):");
			sb.AppendLine($"- series: {hints.Series}");
			sb.AppendLine($"- number: {hints.Number}");
			sb.AppendLine($"- year: {(hints.Year > 0 ? hints.Year.ToString() : "unknown")}");
			sb.AppendLine($"- publisher: {hints.Publisher}");
			sb.AppendLine($"- file name: {hints.FileName}");
			sb.AppendLine();
			if (settings.PreferOriginal)
			{
				sb.AppendLine("This may be a reprint published in another language or by a local publisher (Panini, Planeta, Televisa, Norma, Urban...).");
				sb.AppendLine("Identify the ORIGINAL first publication that this cover art comes from (usually a US issue from Marvel, DC, Image, Dark Horse...):");
				sb.AppendLine("give the series title as it is listed on comicvine.gamespot.com, the issue number of the ORIGINAL issue, its original publisher and the year the original volume started.");
			}
			else
			{
				sb.AppendLine("Identify this exact issue: the series title as it is listed on comicvine.gamespot.com, its issue number, publisher and the year the volume started.");
			}
			sb.AppendLine("Identify the INDIVIDUAL issue (a single monthly comic), not a collected edition (trade paperback, hardcover, omnibus), unless the cover clearly belongs to a collected edition.");
			sb.AppendLine();
			sb.AppendLine("Write the value of \"notes\" in " + Loc.EnglishName + ".");
			sb.AppendLine("Answer with ONLY one JSON object and no other text, with these keys:");
			sb.AppendLine("{\"series\":\"\",\"number\":\"\",\"volume_year\":0,\"year\":0,\"publisher\":\"\",\"is_reprint\":false,\"language\":\"\",\"confidence\":0.0,\"notes\":\"\"}");
			sb.AppendLine("confidence is between 0 and 1 and says how sure you are about series AND number. If you cannot identify the comic, use confidence 0 and empty strings. Do not guess wildly.");
			return sb.ToString();
		}

		private static string Run(string exe, PluginSettings settings, string prompt, string workDir, CancellationToken token)
		{
			string args = "-p --output-format json --allowedTools Read";
			if (!string.IsNullOrWhiteSpace(settings.ClaudeModel))
			{
				args += " --model " + settings.ClaudeModel.Trim();
			}
			bool isScript = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
			ProcessStartInfo info = new ProcessStartInfo
			{
				FileName = isScript ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : exe,
				Arguments = isScript ? "/c \"\"" + exe + "\" " + args + "\"" : args,
				WorkingDirectory = workDir,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};

			using (Process process = Process.Start(info))
			{
				StringBuilder stdout = new StringBuilder();
				StringBuilder stderr = new StringBuilder();
				process.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (stdout) { stdout.AppendLine(e.Data); } } };
				process.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (stderr) { stderr.AppendLine(e.Data); } } };
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				using (StreamWriter writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
				{
					writer.Write(prompt);
				}

				using (token.Register(() => Kill(process)))
				{
					if (!process.WaitForExit(TimeoutMs))
					{
						Kill(process);
						throw new TimeoutException(Loc.T("claude.timeout"));
					}
					process.WaitForExit(); // flush the async output
				}
				token.ThrowIfCancellationRequested();
				if (process.ExitCode != 0)
				{
					throw new InvalidOperationException(Loc.T("claude.failed", process.ExitCode, (stderr.ToString().Trim() + " " + stdout.ToString().Trim()).Trim()));
				}
				return stdout.ToString();
			}
		}

		private static void Kill(Process process)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch (Exception)
			{
			}
		}

		internal static ClaudeSuggestion Parse(string cliOutput, Action<string> log)
		{
			ClaudeSuggestion suggestion = new ClaudeSuggestion();
			string text = cliOutput;
			try
			{
				Dictionary<string, object> envelope = Json.ParseObject(cliOutput.Trim());
				if (envelope != null && envelope.ContainsKey("result"))
				{
					text = Json.Str(envelope, "result");
				}
			}
			catch (Exception)
			{
				// not the JSON envelope: look for the object in the raw text
			}

			int start = text.IndexOf('{');
			int end = text.LastIndexOf('}');
			if (start < 0 || end <= start)
			{
				log(Loc.T("claude.noJson", Shorten(text)));
				return suggestion;
			}
			try
			{
				Dictionary<string, object> d = Json.ParseObject(text.Substring(start, end - start + 1));
				suggestion.Series = Json.Str(d, "series").Trim();
				suggestion.Number = Json.Str(d, "number").Trim().TrimStart('#');
				suggestion.VolumeYear = Json.Int(d, "volume_year");
				suggestion.Year = Json.Int(d, "year");
				suggestion.Publisher = Json.Str(d, "publisher").Trim();
				suggestion.IsReprint = string.Equals(Json.Str(d, "is_reprint"), "true", StringComparison.OrdinalIgnoreCase);
				suggestion.Language = Json.Str(d, "language");
				suggestion.Notes = Json.Str(d, "notes");
				double.TryParse(Json.Str(d, "confidence"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double confidence);
				suggestion.Confidence = Math.Max(0, Math.Min(1, confidence));
			}
			catch (Exception e)
			{
				log(Loc.T("claude.parseError", e.Message, Shorten(text)));
			}
			return suggestion;
		}

		private static string Shorten(string text) => text.Length > 200 ? text.Substring(0, 200) + "..." : text;

		private static void SaveJpeg(Bitmap cover, string file)
		{
			int maxHeight = 1100;
			double scale = cover.Height > maxHeight ? (double)maxHeight / cover.Height : 1.0;
			int width = Math.Max(1, (int)(cover.Width * scale));
			int height = Math.Max(1, (int)(cover.Height * scale));
			using (Bitmap scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(scaled))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.DrawImage(cover, new Rectangle(0, 0, width, height));
				}
				ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
				using (EncoderParameters parameters = new EncoderParameters(1))
				{
					parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
					scaled.Save(file, jpeg, parameters);
				}
			}
		}
	}
}
