using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using ModLoader;
using UnityEngine;
using SFSConsole = ModLoader.IO.Console;

namespace consoleMan
{
    public static class LogInterceptor
    {
        public struct Entry
        {
            public string formatted;
            public LogType type;
            public string source;
            public string message;
            public string stack;
        }

        static readonly List<Entry> allEntries = new(512);
        static readonly object lockObj = new();
        static readonly Dictionary<string, string> asmToSource = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> nsToSource = new(StringComparer.OrdinalIgnoreCase);

        struct Counts { public int Log, Warning, Error, Exception; }
        static readonly Dictionary<string, Counts> counts = new(StringComparer.OrdinalIgnoreCase);

        public static volatile bool pendingQueueUpdate = false;
        public static volatile bool pendingSourceRefresh = false;
        public static volatile bool pendingCountRefresh = false;

        static FieldInfo queueField;
        static FieldInfo lastLogField;
        static Queue<string> logQueue;
        public static MethodInfo updateTextMethod;

        public static void Init(SFSConsole console)
        {
            var t = typeof(SFSConsole);
            queueField = t.GetField("queue", BindingFlags.Instance | BindingFlags.NonPublic);
            lastLogField = t.GetField("lastLog", BindingFlags.Instance | BindingFlags.NonPublic);
            updateTextMethod = t.GetMethod("UpdateText", BindingFlags.Instance | BindingFlags.NonPublic);
            logQueue = queueField?.GetValue(console) as Queue<string>;
        }

        public static void RegisterMods(Mod[] mods)
        {
            asmToSource.Clear();
            nsToSource.Clear();

            asmToSource["Assembly-CSharp"] = "Game";
            asmToSource["ModLoader"] = "ModLoader";
            asmToSource["0Harmony"] = "Harmony";
            nsToSource["SFS"] = "Game";
            nsToSource["ModLoader"] = "ModLoader";
            nsToSource["HarmonyLib"] = "Harmony";
            nsToSource["UnityEngine"] = "Unity";

            foreach (string s in new[] { "Game", "ModLoader", "Harmony", "Other" })
                Config.S.GetOrAdd(s);

            foreach (Mod mod in mods)
            {
                string display = mod.DisplayName;
                string asmName = mod.GetType().Assembly.GetName().Name;
                string ns = mod.GetType().Namespace ?? mod.ModNameID;

                asmToSource[asmName] = display;
                if (!string.IsNullOrEmpty(ns))
                {
                    nsToSource[ns] = display;
                    int dot = ns.IndexOf('.');
                    if (dot > 0)
                    {
                        string root = ns.Substring(0, dot);
                        if (!nsToSource.ContainsKey(root))
                            nsToSource[root] = display;
                    }
                }

                Config.S.GetOrAdd(display);
            }

            ReclassifyAllEntries();
            Config.Save();
        }

        public static bool HandleLog(SFSConsole console, string message, string stackTrace, LogType type)
        {
            string lastLog = lastLogField?.GetValue(console) as string ?? string.Empty;
            if (message == lastLog) return false;
            lastLogField?.SetValue(console, message);

            string source = DetectSource(stackTrace);
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
            string formatted = type switch
            {
                LogType.Error => $"[{timestamp}] ERROR: {message}",
                LogType.Warning => $"[{timestamp}] WARNING: {message}\n{stackTrace}",
                LogType.Exception => $"[{timestamp}] EXCEPTION: {message}\n{stackTrace}",
                _ => $"[{timestamp}] LOG: {message}",
            };

            var entry = new Entry { formatted = formatted, type = type, source = source, message = message, stack = stackTrace };
            bool newSource = false;

            lock (lockObj)
            {
                allEntries.Add(entry);
                if (!Config.S.Sources.ContainsKey(source))
                {
                    Config.S.Sources[source] = new Config.SourceFilter();
                    newSource = true;
                }

                counts.TryGetValue(source, out Counts ct);
                switch (type)
                {
                    case LogType.Log: ct.Log++; break;
                    case LogType.Warning: ct.Warning++; break;
                    case LogType.Error: ct.Error++; break;
                    case LogType.Exception: ct.Exception++; break;
                }
                counts[source] = ct;
            }

            if (newSource) pendingSourceRefresh = true;
            pendingCountRefresh = true;

            if (IsVisible(entry) && logQueue != null)
            {
                lock (lockObj)
                {
                    if (logQueue.Count >= 150) logQueue.Dequeue();
                    logQueue.Enqueue(entry.formatted);
                }
                pendingQueueUpdate = true;
            }

            return false;
        }

        static string DetectSource(string unityTrace)
        {
            if (!string.IsNullOrEmpty(unityTrace))
            {
                string fromTrace = ParseUnityTrace(unityTrace);
                if (fromTrace != null) return fromTrace;
            }
            return DetectSourceFromStack();
        }

        static string ParseUnityTrace(string unityTrace)
        {
            string gameCandidate = null;
            foreach (string line in unityTrace.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.StartsWith("at ", StringComparison.Ordinal))
                    trimmed = trimmed.Substring(3).TrimStart();

                if (trimmed.StartsWith("UnityEngine.", StringComparison.Ordinal)
                 || trimmed.StartsWith("System.", StringComparison.Ordinal)
                 || trimmed.StartsWith("HarmonyLib.", StringComparison.Ordinal)
                 || trimmed.StartsWith("Mono.", StringComparison.Ordinal)
                 || trimmed.StartsWith("ModLoader.", StringComparison.Ordinal)
                 || trimmed.StartsWith("consoleMan.", StringComparison.OrdinalIgnoreCase))
                    continue;

                string found = MatchNamespace(trimmed);
                if (found != null)
                {
                    if (found == "Game" || found == "Harmony") { gameCandidate ??= found; continue; }
                    return found;
                }

                int sep = trimmed.IndexOfAny(new[] { '.', ':' });
                if (sep > 0 && gameCandidate == null)
                    return trimmed.Substring(0, sep);
            }
            return gameCandidate;
        }

        static string DetectSourceFromStack()
        {
            string modCandidate = null;
            string modloaderCandidate = null;
            string gameCandidate = null;
            try
            {
                var frames = new StackTrace(false).GetFrames();
                if (frames != null)
                {
                    foreach (var frame in frames)
                    {
                        var method = frame.GetMethod();
                        if (method?.DeclaringType == null) continue;
                        string asmName = method.DeclaringType.Assembly.GetName().Name;
                        if (IsInternalAssembly(asmName)) continue;
                        if (asmName == "ModLoader" && method.DeclaringType.Name == "Console") continue;

                        if (asmToSource.TryGetValue(asmName, out string src))
                        {
                            if (src == "Game" || src == "Harmony") gameCandidate ??= src;
                            else if (src == "ModLoader") modloaderCandidate ??= src;
                            else modCandidate ??= src;
                        }
                        else
                        {
                            modCandidate ??= asmName;
                        }
                    }
                }
            }
            catch { }
            return modCandidate ?? modloaderCandidate ?? gameCandidate ?? "Other";
        }

        static string MatchNamespace(string trimmed)
        {
            foreach (var kvp in nsToSource)
                if (trimmed.StartsWith(kvp.Key + ".", StringComparison.OrdinalIgnoreCase)
                 || trimmed.StartsWith(kvp.Key + ":", StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;

            int sep = trimmed.IndexOfAny(new[] { '.', ':' });
            if (sep > 0)
            {
                string root = trimmed.Substring(0, sep);
                foreach (var kvp in nsToSource)
                    if (root.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
            }
            return null;
        }

        static bool IsInternalAssembly(string name)
        {
            if (name == null) return true;
            return name == "mscorlib"
                || name == "netstandard"
                || name == "console-man"
                || name == "0Harmony"
                || name == "Newtonsoft.Json"
                || name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Unity", StringComparison.Ordinal)
                || name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name.StartsWith("HarmonyLib", StringComparison.Ordinal)
                || name.StartsWith("Mono.", StringComparison.Ordinal);
        }

        public struct LogCounts
        {
            public int Log, Warning, Error, Exception;
            public int Total => Log + Warning + Error + Exception;
        }

        public static LogCounts GetLogCounts(string source)
        {
            lock (lockObj)
            {
                counts.TryGetValue(source, out Counts c);
                return new LogCounts { Log = c.Log, Warning = c.Warning, Error = c.Error, Exception = c.Exception };
            }
        }

        public static bool IsVisible(Entry entry)
            => Config.S.GetOrAdd(entry.source).GetLevel(entry.type);

        public static void RebuildQueue(SFSConsole console)
        {
            if (console == null || logQueue == null) return;
            lock (lockObj)
            {
                logQueue.Clear();
                foreach (var entry in allEntries)
                {
                    if (!IsVisible(entry)) continue;
                    if (logQueue.Count >= 150) logQueue.Dequeue();
                    logQueue.Enqueue(entry.formatted);
                }
            }
            updateTextMethod?.Invoke(console, null);
            pendingQueueUpdate = true;
        }

        static void ReclassifyAllEntries()
        {
            lock (lockObj)
            {
                counts.Clear();
                for (int i = 0; i < allEntries.Count; i++)
                {
                    var e = allEntries[i];
                    string src = DetectSource(e.stack);
                    if (!string.Equals(src, e.source, StringComparison.OrdinalIgnoreCase))
                        e.source = src;
                    allEntries[i] = e;

                    counts.TryGetValue(e.source, out Counts ct);
                    switch (e.type)
                    {
                        case LogType.Log: ct.Log++; break;
                        case LogType.Warning: ct.Warning++; break;
                        case LogType.Error: ct.Error++; break;
                        case LogType.Exception: ct.Exception++; break;
                    }
                    counts[e.source] = ct;
                }
            }
            pendingCountRefresh = true;
            pendingSourceRefresh = true;
        }

        public static string GetFilteredText()
        {
            var sb = new StringBuilder();
            lock (lockObj)
                foreach (var entry in allEntries)
                    if (IsVisible(entry))
                        sb.AppendLine(entry.formatted);
            return sb.ToString();
        }
    }
}
