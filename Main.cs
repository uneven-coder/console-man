using System;
using System.Collections.Generic;
using HarmonyLib;
using ModLoader;
using SFS.IO;
using UITools;
using UnityEngine;
using SFSConsole = ModLoader.IO.Console;

namespace consoleMan
{
    public class Main : Mod
    {
        public static Main main;
        public static FolderPath modFolder;

        public override string ModNameID => "console-man";
        public override string DisplayName => "Console Manager";
        public override string Author => "Cratior";
        public override string MinimumGameVersionNecessary => "1.5.10";
        public override string ModVersion => "2.0.0";
        public override string Description => "Enhanced console: filter by log level and source, persistent config.";

        public override Dictionary<string, string> Dependencies => new Dictionary<string, string>
        {
            { "UITools", "1.1.5" }
        };

        public override void Early_Load()
        {
            main = this;
            modFolder = new FolderPath(ModFolder);
            Config.Setup();
        }

        public override void Load()
        {
            LogInterceptor.RegisterMods(Loader.main.GetLoadedMods());
            new Harmony("com.consoleman").PatchAll(typeof(Main).Assembly);

            if (SFSConsole.main != null)
            {
                LogInterceptor.Init(SFSConsole.main);
                ConsolePanel.Build(SFSConsole.main);
            }
        }
    }

    [HarmonyPatch(typeof(SFSConsole), "Awake")]
    static class Patch_Console_Awake
    {
        [HarmonyPostfix]
        static void Postfix(SFSConsole __instance)
        {
            LogInterceptor.Init(__instance);
            ConsolePanel.Build(__instance);
        }
    }

    [HarmonyPatch(typeof(SFSConsole), "HandleLog")]
    static class Patch_Console_HandleLog
    {
        [HarmonyPrefix]
        static bool Prefix(SFSConsole __instance, string message, string stackTrace, LogType type)
            => LogInterceptor.HandleLog(__instance, message, stackTrace, type);
    }

    [HarmonyPatch(typeof(SFSConsole), "Copy")]
    static class Patch_Console_Copy
    {
        [HarmonyPrefix]
        static bool Prefix(SFSConsole __instance)
        {
            GUIUtility.systemCopyBuffer = LogInterceptor.GetFilteredText();
            __instance.WriteText("Copied filtered log to clipboard!");
            return false;
        }
    }

    public class Config : ModSettings<Config.Data>
    {
        public static Config main;

        protected override FilePath SettingsFile =>
            Main.modFolder.ExtendToFile("ConsoleMan.Config.txt");

        static Action saveTrigger;

        protected override void RegisterOnVariableChange(Action onChange)
        {
            Application.quitting += onChange;
            saveTrigger = onChange;
        }

        public static void Setup() { main = new Config(); main.Initialize(); }
        public static void Save() => saveTrigger?.Invoke();
        public static Data S => settings;

        public class SourceFilter
        {
            public bool Log = true;
            public bool Warning = true;
            public bool Error = true;
            public bool Exception = true;

            public bool GetLevel(LogType t) => t switch
            {
                LogType.Log => Log,
                LogType.Warning => Warning,
                LogType.Error => Error,
                LogType.Exception => Exception,
                _ => true,
            };

            public void SetLevel(LogType t, bool v)
            {
                switch (t)
                {
                    case LogType.Log: Log = v; break;
                    case LogType.Warning: Warning = v; break;
                    case LogType.Error: Error = v; break;
                    case LogType.Exception: Exception = v; break;
                }
            }
        }

        public class Data
        {
            public Dictionary<string, SourceFilter> Sources =
                new Dictionary<string, SourceFilter>(StringComparer.OrdinalIgnoreCase);

            public SourceFilter GetOrAdd(string source)
            {
                if (!Sources.TryGetValue(source, out var f))
                    Sources[source] = f = new SourceFilter();
                return f;
            }
        }
    }
}
