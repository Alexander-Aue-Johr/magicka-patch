using System;
using System.IO;
using System.Windows.Forms;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimeSteamApiPreflight
    {
        public static void EnsureAccessible()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string path = GetPath(directory);
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                }
                Directory.SetCurrentDirectory(directory);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "Magicka could not open steam_api.dll in its installation " +
                    "folder. Start Magicka through Steam, check that the folder " +
                    "path is valid and accessible, and use Steam's 'Verify " +
                    "integrity of game files'.",
                    "Magicka startup error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Hand);
                Environment.Exit(1);
            }
        }

        public static string GetPath(string directory)
        {
            if (directory == null)
                throw new ArgumentNullException("directory");
            return Path.Combine(directory, "steam_api.dll");
        }

        public static bool CanOpen(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
