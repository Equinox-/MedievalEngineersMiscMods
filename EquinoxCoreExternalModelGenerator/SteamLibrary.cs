using System;
using System.Collections.Generic;
using System.IO;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public static class SteamLibrary
    {
        private const string InstallKey = @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam";
        private const string InstallValue = "SteamPath";

        public static string FindAppInstallDir(uint appId)
        {
            // ReSharper disable once UsePatternMatching
            var steamInstall = Microsoft.Win32.Registry.GetValue(InstallKey, InstallValue, null) as string;
            if (steamInstall == null || !Directory.Exists(steamInstall))
                throw new InvalidOperationException(
                    "Couldn't find Steam install directory.  Do you have steam installed?");

            var rootAppDir = Path.Combine(steamInstall, "steamapps");
            if (!Directory.Exists(rootAppDir))
                throw new InvalidOperationException("Couldn't find Steam apps directory.");
            var appDirectories = new Stack<string>();
            var exploredDirectories = new HashSet<string>();
            appDirectories.Push(rootAppDir);

            while (appDirectories.Count > 0)
            {
                var test = appDirectories.Pop();
                if (!exploredDirectories.Add(test) || !Directory.Exists(test))
                    continue;

                var libraryAux = Path.Combine(test, "libraryfolders.vdf");
                if (File.Exists(libraryAux))
                {
                    var prop = VdfConvert.Deserialize(File.ReadAllText(libraryAux));
                    if (prop.Key != "LibraryFolders")
                        throw new InvalidOperationException($"Root key on library folders was incorrect ({prop.Key})");
                    var child = (VObject) prop.Value;
                    foreach (var kv in child.Children())
                        if (int.TryParse(kv.Key, out var libraryId) && kv.Value is VValue value &&
                            value.Value is string childPath)
                            appDirectories.Push(childPath);
                }

                var appAux = Path.Combine(test, $"appmanifest_{appId}.acf");
                // ReSharper disable once InvertIf
                if (File.Exists(appAux))
                {
                    var prop = VdfConvert.Deserialize(File.ReadAllText(appAux));
                    if (prop.Key != "AppState")
                        throw new InvalidOperationException($"Root key on app manifest was incorrect ({prop.Key})");

                    var child = (VObject) prop.Value;
                    foreach (var kv in child.Children())
                        if (kv.Key == "installdir" && kv.Value is VValue value && value.Value is string installDir)
                        {
                            var installAbs = Path.Combine(test, "common", installDir);
                            if (!Directory.Exists(installAbs))
                                throw new InvalidOperationException($"Install directory doesn't exist {installAbs}");
                            return installAbs;
                        }

                    throw new InvalidOperationException($"Unable to find install directory key in app manifest.");
                }
            }

            return null;
        }
    }
}