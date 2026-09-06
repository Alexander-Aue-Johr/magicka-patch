using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Magicka.CommunityPatch
{
	internal static class TelemetryRuntimeContext
	{
		private const int MaxNavigationChars = 4096;
		private static readonly object sSync = new object();
		private static string sNavigationHistory = "";
		private static string sPlayStateCount = "0";
		private static string sSceneTransitionCount = "0";
		private static string sNavigationTruncated = "false";
		private static int sPlayStateCountValue;
		private static int sSceneTransitionCountValue;
		private static string sLanguage = "";
		private static string sGlyphFontSource = "";
		private static string sGlyphFileCount = "0";
		private static string sGlyphTotalBytes = "0";
		private static string sGlyphSha256 = "";
		private static string sGlyphFingerprintStatus = "not_recorded";
		private static string sResolutionWidth = "";
		private static string sResolutionHeight = "";
		private static string sUiScalePercent = "";

		internal static void RecordPlayState(string levelPath, string levelName)
		{
			try
			{
				string relativeLevel = NormalizeLevelPath(levelPath);
				string safeLevelName = SafeLabel(levelName);
				lock (sSync)
				{
					sPlayStateCountValue++;
					sPlayStateCount = sPlayStateCountValue.ToString(CultureInfo.InvariantCulture);
					AppendUnsafe((sNavigationHistory.Length == 0 ? "" : " | ") + relativeLevel + " -> " + safeLevelName);
				}
			}
			catch
			{
			}
		}

		internal static void RecordScene(string sceneName)
		{
			try
			{
				string safeSceneName = SafeLabel(sceneName);
				lock (sSync)
				{
					sSceneTransitionCountValue++;
					sSceneTransitionCount = sSceneTransitionCountValue.ToString(CultureInfo.InvariantCulture);
					AppendUnsafe(" -> " + safeSceneName);
				}
			}
			catch
			{
			}
		}

		internal static void RecordMenu()
		{
			try
			{
				lock (sSync)
				{
					if (sNavigationHistory.Length != 0 && !sNavigationHistory.EndsWith(" -> Menu", StringComparison.Ordinal))
					{
						AppendUnsafe(" -> Menu");
					}
				}
			}
			catch
			{
			}
		}

		internal static void RecordLanguage(string language)
		{
			string selectedLanguage = SafeLabel(language);
			string fontSource = selectedLanguage;
			string fileCount = "0";
			string totalBytes = "0";
			string fingerprint = "";
			string status = "error";
			try
			{
				string fontDirectory = Path.Combine(Path.Combine(Path.Combine("content", "Languages"), selectedLanguage), "font");
				if (!Directory.Exists(fontDirectory))
				{
					fontSource = "eng";
					fontDirectory = Path.Combine(Path.Combine(Path.Combine("content", "Languages"), fontSource), "font");
				}
				if (!Directory.Exists(fontDirectory))
				{
					status = "missing";
				}
				else
				{
					string[] files = Directory.GetFiles(fontDirectory, "*.xnb", SearchOption.TopDirectoryOnly);
					Array.Sort(files, StringComparer.OrdinalIgnoreCase);
					long byteCount = 0L;
					StringBuilder manifest = new StringBuilder(files.Length * 96);
					for (int i = 0; i < files.Length; i++)
					{
						FileInfo info = new FileInfo(files[i]);
						byteCount += info.Length;
						byte[] fileHash;
						using (SHA256 fileHasher = SHA256.Create())
						using (FileStream stream = File.OpenRead(files[i]))
						{
							fileHash = fileHasher.ComputeHash(stream);
						}
						manifest.Append(info.Name.ToLowerInvariant());
						manifest.Append(':');
						manifest.Append(info.Length.ToString(CultureInfo.InvariantCulture));
						manifest.Append(':');
						manifest.Append(Convert.ToBase64String(fileHash));
						manifest.Append(';');
					}
					byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToString());
					using (SHA256 manifestHasher = SHA256.Create())
					{
						fingerprint = ToHex(manifestHasher.ComputeHash(manifestBytes));
					}
					fileCount = files.Length.ToString(CultureInfo.InvariantCulture);
					totalBytes = byteCount.ToString(CultureInfo.InvariantCulture);
					status = "ok";
				}
			}
			catch
			{
			}
			try
			{
				lock (sSync)
				{
					sLanguage = selectedLanguage;
					sGlyphFontSource = fontSource;
					sGlyphFileCount = fileCount;
					sGlyphTotalBytes = totalBytes;
					sGlyphSha256 = fingerprint;
					sGlyphFingerprintStatus = status;
				}
			}
			catch
			{
			}
		}

		internal static void RecordResolution(int width, int height)
		{
			try
			{
				string widthText = width.ToString(CultureInfo.InvariantCulture);
				string heightText = height.ToString(CultureInfo.InvariantCulture);
				lock (sSync)
				{
					sResolutionWidth = widthText;
					sResolutionHeight = heightText;
				}
			}
			catch
			{
			}
		}

		internal static void RecordUiScale(float scale)
		{
			try
			{
				string scalePercent = ((int)(scale * 100f + 0.5f)).ToString(CultureInfo.InvariantCulture);
				lock (sSync)
				{
					sUiScalePercent = scalePercent;
				}
			}
			catch
			{
			}
		}

		internal static void AddProperties(Dictionary<string, string> properties)
		{
			try
			{
				lock (sSync)
				{
					properties["navigation_history"] = sNavigationHistory;
					properties["playstate_count"] = sPlayStateCount;
					properties["scene_transition_count"] = sSceneTransitionCount;
					properties["navigation_history_truncated"] = sNavigationTruncated;
					properties["language"] = sLanguage;
					properties["glyph_font_source"] = sGlyphFontSource;
					properties["glyph_file_count"] = sGlyphFileCount;
					properties["glyph_total_bytes"] = sGlyphTotalBytes;
					properties["glyph_sha256"] = sGlyphSha256;
					properties["glyph_fingerprint_status"] = sGlyphFingerprintStatus;
					properties["resolution_width"] = sResolutionWidth;
					properties["resolution_height"] = sResolutionHeight;
					properties["ui_scale_percent"] = sUiScalePercent;
				}
			}
			catch
			{
			}
		}

		private static string NormalizeLevelPath(string levelPath)
		{
			if (string.IsNullOrEmpty(levelPath))
			{
				return "(unknown level)";
			}
			string normalized = levelPath.Replace('/', '\\');
			const string marker = "content\\Levels\\";
			int markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (markerIndex >= 0)
			{
				normalized = normalized.Substring(markerIndex + marker.Length);
			}
			else
			{
				normalized = Path.GetFileName(normalized);
			}
			return SafeLabel(normalized);
		}

		private static string SafeLabel(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "(unknown)";
			}
			string result = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '_');
			if (result.Length > 160)
			{
				result = result.Substring(result.Length - 160);
			}
			return result;
		}

		private static string ToHex(byte[] bytes)
		{
			StringBuilder builder = new StringBuilder(bytes.Length * 2);
			for (int i = 0; i < bytes.Length; i++)
			{
				builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
			}
			return builder.ToString();
		}

		private static void AppendUnsafe(string value)
		{
			string combined = sNavigationHistory + value;
			if (combined.Length > MaxNavigationChars)
			{
				combined = "..." + combined.Substring(combined.Length - (MaxNavigationChars - 3));
				sNavigationTruncated = "true";
			}
			sNavigationHistory = combined;
		}
	}
}
