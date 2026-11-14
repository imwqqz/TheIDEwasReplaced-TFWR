using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TFWRFolders
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "tfwr.folders";
        public const string PluginName = "TFWR Folders";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigFile ConfigFile;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<float> TreeWidth;
        internal static ConfigEntry<float> ConsoleHeight;
        internal static ConfigEntry<string> ExplorerSide;
        internal static ConfigEntry<bool> DebugLogs;
        internal static ConfigEntry<float> WindowX;
        internal static ConfigEntry<float> WindowY;
        internal static ConfigEntry<float> WindowWidth;
        internal static ConfigEntry<float> WindowHeight;

        private void Awake()
        {
            Log = Logger;
            ConfigFile = Config;

            ToggleKey = Config.Bind("General", "Toggle key", new KeyboardShortcut(KeyCode.F10), "Shows or hides the IDE.");
            TreeWidth = Config.Bind("General", "Tree width", 210f, "Width of the file tree column in UI pixels.");
            ConsoleHeight = Config.Bind("General", "Console height", 170f, "Height of the terminal panel in UI pixels.");
            ExplorerSide = Config.Bind("General", "Explorer side", "Right", "Where the Project explorer sits: Right or Left.");
            DebugLogs = Config.Bind("General", "Debug logs", false, "Log editor clicks and drag steps.");
            WindowX = Config.Bind("Window", "X", 0f, "Saved window X position (canvas units, 0 = centered).");
            WindowY = Config.Bind("Window", "Y", 0f, "Saved window Y position (canvas units, 0 = centered).");
            WindowWidth = Config.Bind("Window", "Width", 900f, "Saved window width in UI pixels.");
            WindowHeight = Config.Bind("Window", "Height", 600f, "Saved window height in UI pixels.");

            LogBuildInfo();

            try
            {
                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("[Folders] Harmony patches applied.");
            }
            catch (Exception e)
            {
                Log.LogError("[Folders] Patching failed (mod inactive): " + e);
            }

            var go = new GameObject("TFWRFolders_Root");
            DontDestroyOnLoad(go);
            go.AddComponent<IdePanel>();
        }

        private void LogBuildInfo()
        {
            try
            {
                string exeVersion = "";
                var exe = Process.GetCurrentProcess().MainModule;
                if (exe != null && !string.IsNullOrEmpty(exe.FileName) && File.Exists(exe.FileName))
                    exeVersion = FileVersionInfo.GetVersionInfo(exe.FileName).FileVersion;

                string coreHash = "";
                var location = typeof(Saver).Assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    using (var sha = SHA256.Create())
                    {
                        var hash = sha.ComputeHash(File.ReadAllBytes(location));
                        coreHash = BitConverter.ToString(hash).Replace("-", "");
                    }
                }

                Log.LogInfo("[Folders] game=" + Application.version + " engine=" + Application.unityVersion + " exe=" + exeVersion + " core=" + coreHash);
                try
                {
                    var self = typeof(Plugin).Assembly.Location;
                    if (!string.IsNullOrEmpty(self) && File.Exists(self))
                        Log.LogInfo("[Folders] plugin " + self + " " + File.GetLastWriteTimeUtc(self).ToString("yyyy-MM-dd HH:mm:ss") + "Z");
                }
                catch { }
                Log.LogInfo("[Folders] tested against game 0.1 / Unity 6000.0.43f1 / Steam build 26462009 / Core.dll F0EFDA7C138DE8028B1769C121A4B5C17B6BB538AEAE5A67D1881E7C4EBD7D25");
                if (!string.IsNullOrEmpty(exeVersion) && exeVersion != "6000.0.43.9905963")
                    Log.LogWarning("[Folders] game build differs from the tested one; patches are guarded but verify saving.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[Folders] build info failed: " + e.Message);
            }
        }
    }
}
