using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SystemLibraryPreload
    {
        private static readonly string[] LibraryNames = new string[]
        {
            "version.dll",
            "winmm.dll",
            "winhttp.dll"
        };

        public static void PreloadSystemLibraries()
        {
            bool isWindows =
                Environment.OSVersion.Platform == PlatformID.Win32NT;
            string[] paths = GetAbsolutePaths(
                Environment.SystemDirectory,
                isWindows);
            for (int index = 0; index < paths.Length; index++)
                LoadLibraryExW(paths[index], IntPtr.Zero, 0u);
        }

        public static string[] GetAbsolutePaths(
            string systemDirectory,
            bool isWindows)
        {
            if (!isWindows)
                return new string[0];
            if (String.IsNullOrEmpty(systemDirectory))
                throw new InvalidOperationException(
                    "The Windows system directory is unavailable.");

            string[] paths = new string[LibraryNames.Length];
            for (int index = 0; index < paths.Length; index++)
                paths[index] = Path.Combine(
                    systemDirectory,
                    LibraryNames[index]);
            return paths;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "LoadLibraryExW",
            ExactSpelling = true)]
        private static extern IntPtr LoadLibraryExW(
            string fileName,
            IntPtr file,
            uint flags);
    }
}
