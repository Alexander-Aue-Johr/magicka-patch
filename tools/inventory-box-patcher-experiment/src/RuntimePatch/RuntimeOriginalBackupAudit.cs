using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimeOriginalBackupAudit
    {
        private const string AuditSchema = "1";
        private const string OriginalFileCatalog = "steam_build_4143032";
        private const long OriginalMagickaSize = 3524096L;
        private const string OriginalMagickaSha256 =
            "a896e05a3cff65cf9bab4e67e13ae72cb428d99aa93098cf6a8dd8cbc3112ee7";
        private const long OriginalPolygonHeadSize = 560128L;
        private const string OriginalPolygonHeadSha256 =
            "b43450b31ba5865db85b9589d7d9ac679d9c1d365b54c6521198b431603cc514";

        public static void AddTelemetryProperties(
            Dictionary<string, string> properties)
        {
            AddTelemetryProperties(properties, GetGameDirectory());
        }

        public static void AddTelemetryProperties(
            Dictionary<string, string> properties,
            string gameDirectory)
        {
            if (properties == null)
                return;
            properties["original_backup_audit_schema"] = AuditSchema;
            properties["original_backup_catalog"] = OriginalFileCatalog;
            try
            {
                bool magickaVerified;
                bool polygonHeadVerified;
                bool magickaCandidate;
                bool polygonHeadCandidate;
                Audit(
                    gameDirectory,
                    out magickaVerified,
                    out polygonHeadVerified,
                    out magickaCandidate,
                    out polygonHeadCandidate);
                properties["original_magicka_backup_status"] =
                    FileStatus(magickaVerified, magickaCandidate);
                properties["original_polygonhead_backup_status"] =
                    FileStatus(polygonHeadVerified, polygonHeadCandidate);
                properties["original_backup_status"] = CombinedStatus(
                    magickaVerified,
                    polygonHeadVerified);
            }
            catch
            {
                properties["original_magicka_backup_status"] = "audit_failed";
                properties["original_polygonhead_backup_status"] = "audit_failed";
                properties["original_backup_status"] = "audit_failed";
            }
        }

        private static void Audit(
            string gameDirectory,
            out bool magickaVerified,
            out bool polygonHeadVerified,
            out bool magickaCandidate,
            out bool polygonHeadCandidate)
        {
            magickaVerified = false;
            polygonHeadVerified = false;
            magickaCandidate = false;
            polygonHeadCandidate = false;
            Dictionary<string, int> candidates =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string patchDirectory = Path.Combine(gameDirectory, "CommunityPatch");
            AddManifestCandidates(
                Path.Combine(patchDirectory, "install-manifest.ini"),
                gameDirectory,
                candidates);
            AddBackupDirectoryCandidates(
                Path.Combine(patchDirectory, "backup"),
                candidates);
            AddGameDirectoryCandidates(gameDirectory, candidates);
            foreach (KeyValuePair<string, int> candidate in candidates)
            {
                try
                {
                    FileInfo file = new FileInfo(candidate.Key);
                    if (!file.Exists)
                        continue;
                    int hint = candidate.Value | FileNameHint(file.Name);
                    if (file.Length == OriginalMagickaSize)
                        hint |= 1;
                    if (file.Length == OriginalPolygonHeadSize)
                        hint |= 2;
                    magickaCandidate |= (hint & 1) != 0;
                    polygonHeadCandidate |= (hint & 2) != 0;
                    if (!magickaVerified && file.Length == OriginalMagickaSize)
                        magickaVerified = HashMatches(file.FullName, OriginalMagickaSha256);
                    if (!polygonHeadVerified &&
                        file.Length == OriginalPolygonHeadSize)
                        polygonHeadVerified = HashMatches(
                            file.FullName,
                            OriginalPolygonHeadSha256);
                }
                catch
                {
                }
            }
        }

        private static void AddManifestCandidates(
            string manifestPath,
            string gameDirectory,
            Dictionary<string, int> candidates)
        {
            try
            {
                if (!File.Exists(manifestPath))
                    return;
                string[] lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
                for (int index = 0; index < lines.Length; index++)
                {
                    int separator = lines[index].IndexOf('=');
                    if (separator <= 0)
                        continue;
                    string key = lines[index].Substring(0, separator).Trim();
                    string path = lines[index].Substring(separator + 1).Trim();
                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(gameDirectory, path);
                    if (key.Equals(
                        "original_magicka_backup",
                        StringComparison.OrdinalIgnoreCase))
                        AddCandidate(candidates, path, 1);
                    else if (key.Equals(
                        "original_polygonhead_backup",
                        StringComparison.OrdinalIgnoreCase))
                        AddCandidate(candidates, path, 2);
                }
            }
            catch
            {
            }
        }

        private static void AddBackupDirectoryCandidates(
            string backupDirectory,
            Dictionary<string, int> candidates)
        {
            try
            {
                if (!Directory.Exists(backupDirectory))
                    return;
                Queue<string> pending = new Queue<string>();
                pending.Enqueue(backupDirectory);
                int directoryCount = 0;
                int fileCount = 0;
                while (pending.Count != 0 &&
                    directoryCount < 256 && fileCount < 4096)
                {
                    string directory = pending.Dequeue();
                    directoryCount++;
                    try
                    {
                        string[] files = Directory.GetFiles(directory);
                        for (int index = 0;
                            index < files.Length && fileCount < 4096;
                            index++)
                        {
                            AddCandidate(
                                candidates,
                                files[index],
                                FileNameHint(Path.GetFileName(files[index])));
                            fileCount++;
                        }
                        string[] directories = Directory.GetDirectories(directory);
                        for (int index = 0; index < directories.Length; index++)
                        {
                            try
                            {
                                if ((File.GetAttributes(directories[index]) &
                                    FileAttributes.ReparsePoint) == 0)
                                    pending.Enqueue(directories[index]);
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

        private static void AddGameDirectoryCandidates(
            string gameDirectory,
            Dictionary<string, int> candidates)
        {
            try
            {
                string[] files = Directory.GetFiles(gameDirectory);
                for (int index = 0; index < files.Length; index++)
                {
                    string name = Path.GetFileName(files[index]);
                    if (!name.Equals("Magicka.exe", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("PolygonHead.dll", StringComparison.OrdinalIgnoreCase) &&
                        IsLikelyManualBackup(name))
                        AddCandidate(candidates, files[index], FileNameHint(name));
                }
            }
            catch
            {
            }
        }

        private static void AddCandidate(
            Dictionary<string, int> candidates,
            string path,
            int hint)
        {
            if (String.IsNullOrEmpty(path))
                return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                int existing;
                if (candidates.TryGetValue(fullPath, out existing))
                    candidates[fullPath] = existing | hint;
                else
                    candidates.Add(fullPath, hint);
            }
            catch
            {
            }
        }

        private static int FileNameHint(string fileName)
        {
            if (String.IsNullOrEmpty(fileName))
                return 0;
            if (fileName.IndexOf(
                "polygonhead",
                StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            if (fileName.IndexOf("magicka", StringComparison.OrdinalIgnoreCase) >= 0 &&
                fileName.IndexOf("patch", StringComparison.OrdinalIgnoreCase) < 0)
                return 1;
            return 0;
        }

        private static bool IsLikelyManualBackup(string fileName)
        {
            return fileName.IndexOf("original", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fileName.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fileName.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                fileName.IndexOf(".bak.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fileName.EndsWith(".old", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HashMatches(string path, string expectedHash)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder text = new StringBuilder(hash.Length * 2);
                    for (int index = 0; index < hash.Length; index++)
                        text.Append(hash[index].ToString("x2"));
                    return text.ToString().Equals(
                        expectedHash,
                        StringComparison.OrdinalIgnoreCase);
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
                string path = AppDomain.CurrentDomain.BaseDirectory;
                if (!String.IsNullOrEmpty(path))
                    return path.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
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
                return "verified";
            return candidate ? "unverified" : "missing";
        }

        private static string CombinedStatus(
            bool magickaVerified,
            bool polygonHeadVerified)
        {
            if (magickaVerified && polygonHeadVerified)
                return "verified_both";
            if (magickaVerified)
                return "verified_magicka_only";
            if (polygonHeadVerified)
                return "verified_polygonhead_only";
            return "none_verified";
        }
    }
}
