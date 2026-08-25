using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Magicka.CommunityPatch
{
	internal static class OriginalBackupAudit
	{
		internal static void AddTelemetryProperties(Dictionary<string, string> properties)
		{
			if (properties == null)
			{
				return;
			}
			properties["original_backup_audit_schema"] = OriginalBackupAudit.AuditSchema;
			properties["original_backup_catalog"] = OriginalBackupAudit.OriginalFileCatalog;
			try
			{
				bool magickaVerified;
				bool polygonHeadVerified;
				bool magickaCandidate;
				bool polygonHeadCandidate;
				OriginalBackupAudit.Audit(out magickaVerified, out polygonHeadVerified, out magickaCandidate, out polygonHeadCandidate);
				properties["original_magicka_backup_status"] = OriginalBackupAudit.FileStatus(magickaVerified, magickaCandidate);
				properties["original_polygonhead_backup_status"] = OriginalBackupAudit.FileStatus(polygonHeadVerified, polygonHeadCandidate);
				properties["original_backup_status"] = OriginalBackupAudit.CombinedStatus(magickaVerified, polygonHeadVerified);
			}
			catch
			{
				properties["original_magicka_backup_status"] = "audit_failed";
				properties["original_polygonhead_backup_status"] = "audit_failed";
				properties["original_backup_status"] = "audit_failed";
			}
		}

		private static void Audit(out bool magickaVerified, out bool polygonHeadVerified, out bool magickaCandidate, out bool polygonHeadCandidate)
		{
			magickaVerified = false;
			polygonHeadVerified = false;
			magickaCandidate = false;
			polygonHeadCandidate = false;
			string gameDirectory = OriginalBackupAudit.GetGameDirectory();
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			string communityPatchDirectory = Path.Combine(gameDirectory, "CommunityPatch");
			OriginalBackupAudit.AddManifestCandidates(Path.Combine(communityPatchDirectory, "install-manifest.ini"), gameDirectory, dictionary);
			OriginalBackupAudit.AddBackupDirectoryCandidates(Path.Combine(communityPatchDirectory, "backup"), dictionary);
			OriginalBackupAudit.AddGameDirectoryCandidates(gameDirectory, dictionary);
			foreach (KeyValuePair<string, int> keyValuePair in dictionary)
			{
				try
				{
					FileInfo fileInfo = new FileInfo(keyValuePair.Key);
					if (!fileInfo.Exists)
					{
						continue;
					}
					int num = keyValuePair.Value | OriginalBackupAudit.FileNameHint(fileInfo.Name);
					if (fileInfo.Length == OriginalBackupAudit.OriginalMagickaSize)
					{
						num |= 1;
					}
					if (fileInfo.Length == OriginalBackupAudit.OriginalPolygonHeadSize)
					{
						num |= 2;
					}
					if ((num & 1) != 0)
					{
						magickaCandidate = true;
					}
					if ((num & 2) != 0)
					{
						polygonHeadCandidate = true;
					}
					if (!magickaVerified && fileInfo.Length == OriginalBackupAudit.OriginalMagickaSize && OriginalBackupAudit.HashMatches(fileInfo.FullName, OriginalBackupAudit.OriginalMagickaSha256))
					{
						magickaVerified = true;
					}
					if (!polygonHeadVerified && fileInfo.Length == OriginalBackupAudit.OriginalPolygonHeadSize && OriginalBackupAudit.HashMatches(fileInfo.FullName, OriginalBackupAudit.OriginalPolygonHeadSha256))
					{
						polygonHeadVerified = true;
					}
				}
				catch
				{
				}
			}
		}

		private static void AddManifestCandidates(string manifestPath, string gameDirectory, Dictionary<string, int> candidates)
		{
			try
			{
				if (!File.Exists(manifestPath))
				{
					return;
				}
				string[] array = File.ReadAllLines(manifestPath, Encoding.UTF8);
				for (int i = 0; i < array.Length; i++)
				{
					int num = array[i].IndexOf('=');
					if (num <= 0)
					{
						continue;
					}
					string text = array[i].Substring(0, num).Trim();
					string text2 = array[i].Substring(num + 1).Trim();
					if (!Path.IsPathRooted(text2))
					{
						text2 = Path.Combine(gameDirectory, text2);
					}
					if (text.Equals("original_magicka_backup", StringComparison.OrdinalIgnoreCase))
					{
						OriginalBackupAudit.AddCandidate(candidates, text2, 1);
					}
					else if (text.Equals("original_polygonhead_backup", StringComparison.OrdinalIgnoreCase))
					{
						OriginalBackupAudit.AddCandidate(candidates, text2, 2);
					}
				}
			}
			catch
			{
			}
		}

		private static void AddBackupDirectoryCandidates(string backupDirectory, Dictionary<string, int> candidates)
		{
			try
			{
				if (!Directory.Exists(backupDirectory))
				{
					return;
				}
				Queue<string> queue = new Queue<string>();
				queue.Enqueue(backupDirectory);
				int num = 0;
				int num2 = 0;
				while (queue.Count != 0 && num < 256 && num2 < 4096)
				{
					string path = queue.Dequeue();
					num++;
					try
					{
						string[] files = Directory.GetFiles(path);
						for (int i = 0; i < files.Length && num2 < 4096; i++)
						{
							OriginalBackupAudit.AddCandidate(candidates, files[i], OriginalBackupAudit.FileNameHint(Path.GetFileName(files[i])));
							num2++;
						}
						string[] directories = Directory.GetDirectories(path);
						for (int j = 0; j < directories.Length; j++)
						{
							try
							{
								if ((File.GetAttributes(directories[j]) & FileAttributes.ReparsePoint) == 0)
								{
									queue.Enqueue(directories[j]);
								}
							}
							catch
							{
							}
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}

		private static void AddGameDirectoryCandidates(string gameDirectory, Dictionary<string, int> candidates)
		{
			try
			{
				string[] files = Directory.GetFiles(gameDirectory);
				for (int i = 0; i < files.Length; i++)
				{
					string fileName = Path.GetFileName(files[i]);
					if (!fileName.Equals("Magicka.exe", StringComparison.OrdinalIgnoreCase) && !fileName.Equals("PolygonHead.dll", StringComparison.OrdinalIgnoreCase) && OriginalBackupAudit.IsLikelyManualBackup(fileName))
					{
						OriginalBackupAudit.AddCandidate(candidates, files[i], OriginalBackupAudit.FileNameHint(fileName));
					}
				}
			}
			catch
			{
			}
		}

		private static void AddCandidate(Dictionary<string, int> candidates, string path, int hint)
		{
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			try
			{
				string fullPath = Path.GetFullPath(path);
				int num;
				if (candidates.TryGetValue(fullPath, out num))
				{
					candidates[fullPath] = (num | hint);
				}
				else
				{
					candidates.Add(fullPath, hint);
				}
			}
			catch
			{
			}
		}

		private static int FileNameHint(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
			{
				return 0;
			}
			if (fileName.IndexOf("polygonhead", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 2;
			}
			if (fileName.IndexOf("magicka", StringComparison.OrdinalIgnoreCase) >= 0 && fileName.IndexOf("patch", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return 1;
			}
			return 0;
		}

		private static bool IsLikelyManualBackup(string fileName)
		{
			return fileName.IndexOf("original", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || fileName.IndexOf(".bak.", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.EndsWith(".old", StringComparison.OrdinalIgnoreCase);
		}

		private static bool HashMatches(string path, string expectedHash)
		{
			try
			{
				using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					using (SHA256 sha = SHA256.Create())
					{
						byte[] array = sha.ComputeHash(fileStream);
						StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
						for (int i = 0; i < array.Length; i++)
						{
							stringBuilder.Append(array[i].ToString("x2"));
						}
						return stringBuilder.ToString().Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
					}
				}
			}
			catch
			{
				return false;
			}
		}

		private static string GetGameDirectory()
		{
			try
			{
				string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
				if (!string.IsNullOrEmpty(baseDirectory))
				{
					return baseDirectory.TrimEnd(new char[]
					{
						Path.DirectorySeparatorChar,
						Path.AltDirectorySeparatorChar
					});
				}
			}
			catch
			{
			}
			try
			{
				return Directory.GetCurrentDirectory();
			}
			catch
			{
				return ".";
			}
		}

		private static string FileStatus(bool verified, bool candidate)
		{
			if (verified)
			{
				return "verified";
			}
			return candidate ? "unverified" : "missing";
		}

		private static string CombinedStatus(bool magickaVerified, bool polygonHeadVerified)
		{
			if (magickaVerified && polygonHeadVerified)
			{
				return "verified_both";
			}
			if (magickaVerified)
			{
				return "verified_magicka_only";
			}
			if (polygonHeadVerified)
			{
				return "verified_polygonhead_only";
			}
			return "none_verified";
		}

		private const string AuditSchema = "1";

		// Steam app 42910, build 4143032, depot 42911, manifest 7751626926663409458.
		// SHA-256 values were captured from files restored by Steam validation.
		private const string OriginalFileCatalog = "steam_build_4143032";

		private const long OriginalMagickaSize = 3524096L;

		private const string OriginalMagickaSha256 = "a896e05a3cff65cf9bab4e67e13ae72cb428d99aa93098cf6a8dd8cbc3112ee7";

		private const long OriginalPolygonHeadSize = 560128L;

		private const string OriginalPolygonHeadSha256 = "b43450b31ba5865db85b9589d7d9ac679d9c1d365b54c6521198b431603cc514";
	}
}
