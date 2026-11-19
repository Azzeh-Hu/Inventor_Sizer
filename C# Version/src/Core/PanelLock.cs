// Locked panel files (FP / TP / RP). panels.lock, next to the app, lists every file the panel
// process depends on with its SHA-256: the masters (masters\FP, TP, RP), the type files
// (standards\FP.json, TP.json, RP.json) and the hinge / handle parts in MODELS\.
//
// - At start the app compares the files with the list. A changed, missing or new file blocks
//   Apply for its type (MODELS\ for every type) until it is restored or the change is accepted.
// - The app never writes a locked file (Set up master, Learn hardware, saving a master).
// - Accepting a change on purpose: lock-panels.cmd (InventorSizer.exe /lock) writes the list again.
// - tools\check_panel_lock.py does the same check on GitHub (and can write the list).
//
// Text files (.json .xml .txt .md) are hashed without carriage returns, so a Windows checkout
// (CRLF) and the repository (LF) give the same hash. Inventor leftovers are skipped: OldVersions\,
// *.lck, ~*, Thumbs.db, desktop.ini.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace InventorSizer.Core
{
    public static class PanelLock
    {
        public const string FileName = "panels.lock";
        public static readonly string[] Types = { "FP", "TP", "RP" };
        public const string AllTypes = "*";   // MODELS\: used by every panel type

        public class Change { public string Path, Type, What; public override string ToString() => $"{What}: {Path}"; }

        static string root;
        static Dictionary<string, string> expected;   // relative path (forward slashes) -> SHA-256
        public static List<Change> Changes = new List<Change>();

        public static bool Active => expected != null;

        // Reads panels.lock and compares the files. Without panels.lock nothing is locked.
        public static void Init(string appRoot)
        {
            root = appRoot.TrimEnd('\\', '/');
            expected = Read(Path.Combine(root, FileName));
            Changes = expected == null ? new List<Change>() : Verify(root, expected);
        }

        // Every locked file under root, relative with forward slashes, sorted.
        public static List<string> Files(string appRoot)
        {
            var list = new List<string>();
            foreach (var t in Types)
            {
                string json = Path.Combine(appRoot, "standards", t + ".json");
                if (File.Exists(json)) list.Add(Rel(appRoot, json));
                string dir = Path.Combine(appRoot, "masters", t);
                if (Directory.Exists(dir)) list.AddRange(Walk(appRoot, dir));
            }
            string models = Path.Combine(appRoot, "MODELS");
            if (Directory.Exists(models)) list.AddRange(Walk(appRoot, models));
            return list.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        static IEnumerable<string> Walk(string appRoot, string dir)
        {
            foreach (var f in Directory.GetFiles(dir))
                if (!Skip(Path.GetFileName(f))) yield return Rel(appRoot, f);
            foreach (var d in Directory.GetDirectories(dir))
                if (!string.Equals(Path.GetFileName(d), "OldVersions", StringComparison.OrdinalIgnoreCase))
                    foreach (var f in Walk(appRoot, d)) yield return f;
        }

        static bool Skip(string name) =>
            name.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) || name.StartsWith("~") ||
            string.Equals(name, "Thumbs.db", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase);

        static string Rel(string appRoot, string full) => full.Substring(appRoot.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');

        static readonly string[] TextExt = { ".json", ".xml", ".txt", ".md" };

        public static string Hash(string full)
        {
            byte[] data = File.ReadAllBytes(full);
            if (TextExt.Contains(Path.GetExtension(full).ToLowerInvariant())) data = data.Where(b => b != 13).ToArray();
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        public static Dictionary<string, string> Read(string file)
        {
            if (!File.Exists(file)) return null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line == "" || line.StartsWith("#")) continue;
                int sp = line.IndexOf("  ", StringComparison.Ordinal);
                if (sp != 64) continue;
                d[line.Substring(sp + 2)] = line.Substring(0, 64);
            }
            return d;
        }

        public static List<Change> Verify(string appRoot, Dictionary<string, string> want)
        {
            var o = new List<Change>();
            var have = Files(appRoot);
            foreach (var kv in want.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string full = Path.Combine(appRoot, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) o.Add(new Change { Path = kv.Key, Type = TypeOf(kv.Key), What = "missing" });
                else if (Hash(full) != kv.Value) o.Add(new Change { Path = kv.Key, Type = TypeOf(kv.Key), What = "changed" });
            }
            foreach (var f in have.Where(f => !want.ContainsKey(f))) o.Add(new Change { Path = f, Type = TypeOf(f), What = "new" });
            return o;
        }

        // Writes panels.lock for the files as they are now. Returns the number of files.
        public static int Write(string appRoot)
        {
            var files = Files(appRoot);
            var sb = new StringBuilder();
            sb.Append("# Locked panel files (FP / TP / RP): SHA-256 and path. Written by lock-panels.cmd.\n");
            sb.Append("# The app blocks Apply for a type whose files differ from this list, and never writes them.\n");
            sb.Append("# Text files are hashed without carriage returns. Do not edit by hand.\n");
            foreach (var f in files) sb.Append(Hash(Path.Combine(appRoot, f.Replace('/', Path.DirectorySeparatorChar)))).Append("  ").Append(f).Append('\n');
            File.WriteAllText(Path.Combine(appRoot, FileName), sb.ToString(), new UTF8Encoding(false));
            return files.Count;
        }

        public static string TypeOf(string rel)
        {
            var parts = rel.Split('/');
            if (parts.Length >= 2 && parts[0] == "masters") return parts[1];
            if (parts.Length == 2 && parts[0] == "standards") return Path.GetFileNameWithoutExtension(parts[1]);
            return AllTypes;
        }

        // The changes that concern a type (its own files and MODELS\).
        public static List<Change> ChangesFor(string type) =>
            Changes.Where(c => c.Type == AllTypes || string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();

        // Apply refuses a type whose locked files changed.
        public static void RequireUnchanged(string type)
        {
            var c = ChangesFor(type);
            if (c.Count == 0) return;
            throw new SizerException($"The locked {type} panel files differ from {FileName}, so nothing was applied:\n" +
                string.Join("\n", c.Take(12).Select(x => "  " + x)) + (c.Count > 12 ? $"\n  ... and {c.Count - 12} more" : "") +
                "\n\nRestore them (git checkout -- <file>, or pull again), or accept the change on purpose with lock-panels.cmd.");
        }

        // True for a path the lock protects: a type file, anything under masters\FP|TP|RP or MODELS\.
        public static bool IsLocked(string full)
        {
            if (!Active || string.IsNullOrEmpty(full)) return false;
            string f;
            try { f = Path.GetFullPath(full); } catch { return false; }
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
            var rel = f.Substring(r.Length).Replace('\\', '/');
            var p = rel.Split('/');
            if (p.Length == 2 && p[0].Equals("standards", StringComparison.OrdinalIgnoreCase))
                return Types.Any(t => p[1].Equals(t + ".json", StringComparison.OrdinalIgnoreCase));
            if (p.Length >= 2 && p[0].Equals("masters", StringComparison.OrdinalIgnoreCase))
                return Types.Any(t => p[1].Equals(t, StringComparison.OrdinalIgnoreCase));
            return p.Length >= 1 && p[0].Equals("MODELS", StringComparison.OrdinalIgnoreCase);
        }

        // The app's own writes stop here for locked files (also a folder inside the locked area).
        public static void EnsureWritable(string full, string what)
        {
            if (!IsLocked(full) && !IsLocked(Path.Combine(full ?? "", "x"))) return;
            throw new SizerException($"{what}: {full} is a locked panel file ({FileName}), so nothing was written.\n\n" +
                "To change panel files on purpose: run lock-panels.cmd /unlock, make the change, then run lock-panels.cmd to lock them again.");
        }
    }
}
