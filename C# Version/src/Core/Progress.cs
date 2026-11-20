// The progress of a long step (Write new GR, Apply, Open / Read GR) and how long it has left.
// A step is a plan of items; each item has a kind (key, e.g. "gr.panel.make.TP") whose usual
// duration is learned from earlier runs and kept in a small file, so the estimate gets better
// with every run. No UI here: the progress window reads Snapshot() from its own thread, so
// everything is locked.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace InventorSizer.Core
{
    public sealed class ProgressSnapshot
    {
        public int Run;
        public string Title = "", Label = "";
        public double Elapsed, Remaining, Fraction;
        public bool Rough;     // some of the remaining items were never timed: the estimate is a guess
        public bool Overrun;   // the item in work takes clearly longer than it usually does
    }

    public static class Progress
    {
        // Where the learned durations are kept (set by the window; none: nothing is kept).
        public static string TimingsFile;

        // Raised on the thread that starts / ends the outer step (the window's thread).
        public static event Action<string> Started;
        public static event Action Ended;

        sealed class Item { public string Key, Label; }

        static readonly object gate = new object();
        static readonly Stopwatch total = new Stopwatch(), current = new Stopwatch();
        static readonly List<Item> pending = new List<Item>();
        static readonly Dictionary<string, double> learned = new Dictionary<string, double>();
        static readonly Dictionary<string, List<double>> measured = new Dictionary<string, List<double>>();
        static Item now;
        static string title = "";
        static double doneActual, doneExpected;
        static int depth, muted, runId;
        static bool loaded;

        // First guesses (seconds per item) for kinds that were never timed on this PC.
        static readonly Dictionary<string, double> Defaults = new Dictionary<string, double>
        {
            ["gr.open"] = 60, ["gr.start"] = 5, ["gr.remove"] = 2, ["gr.box"] = 15, ["gr.profile"] = 1.5,
            ["gr.ringremove"] = 2, ["gr.ring"] = 10, ["gr.update"] = 2, ["gr.save"] = 2,
            ["gr.read.component"] = 0.3, ["gr.read.constraint"] = 0.3,
            ["gr.panel.make"] = 10, ["gr.panel.make.TP"] = 20, ["gr.panel.place"] = 2.5, ["gr.panel.check"] = 5,
            ["panel.open"] = 5, ["panel.update"] = 3, ["panel.save"] = 3, ["panel.save.TP"] = 10,
        };

        public static bool Active { get { lock (gate) return depth > 0; } }

        // Starts a step with its plan (kind, number of items); dispose it when the step ends. Started
        // inside another step: with join its items become part of that step (Read GR inside Open GR),
        // without join it is not shown at all (the panel Apply inside Write new GR: the GR step
        // counts the whole panel as one item).
        public static IDisposable Run(string stepTitle, params (string Key, int Count)[] plan) => Run(stepTitle, false, plan);

        public static IDisposable Run(string stepTitle, bool join, params (string Key, int Count)[] plan)
        {
            bool outer;
            lock (gate)
            {
                outer = depth == 0;
                depth++;
                if (!outer && !join) { muted++; return new Scope(false, true); }
                if (!outer && muted > 0) return new Scope(false, false);   // joined into a muted step: still muted
                if (outer)
                {
                    Load();
                    pending.Clear(); measured.Clear(); now = null;
                    doneActual = doneExpected = 0;
                    title = stepTitle ?? "";
                    runId++;
                    total.Restart(); current.Reset();
                }
                foreach (var (key, count) in plan)
                    for (int i = 0; i < count; i++) pending.Add(new Item { Key = key });
            }
            if (outer) try { Started?.Invoke(stepTitle); } catch { }
            return new Scope(outer, false);
        }

        // The next item of that kind starts (the one in work is done); label says what it is.
        public static void Next(string key, string label = null)
        {
            lock (gate)
            {
                if (depth == 0 || muted > 0) return;
                Finish();
                int i = pending.FindIndex(p => p.Key == key);
                Item item;
                if (i >= 0) { item = pending[i]; pending.RemoveAt(i); }
                else item = new Item { Key = key };
                item.Label = label ?? "";
                now = item;
                current.Restart();
            }
        }

        // Exactly `remaining` items of that kind are still to come (the plan was a guess).
        public static void Expect(string key, int remaining)
        {
            lock (gate)
            {
                if (depth == 0 || muted > 0) return;
                int have = pending.Count(p => p.Key == key);
                for (; have > remaining; have--) pending.RemoveAt(pending.FindLastIndex(p => p.Key == key));
                for (; have < remaining; have++) pending.Add(new Item { Key = key });
            }
        }

        public static ProgressSnapshot Snapshot()
        {
            lock (gate)
            {
                var s = new ProgressSnapshot { Run = runId, Title = title, Label = now?.Label ?? "", Elapsed = total.Elapsed.TotalSeconds };
                if (depth == 0) { s.Fraction = 1; return s; }
                // how much slower or faster than learned this run is going, from the items done
                double k = doneExpected > 3 ? Math.Max(0.33, Math.Min(3, doneActual / doneExpected)) : 1;
                double left = 0;
                if (now != null)
                {
                    double usual = Expected(now.Key) * k, spent = current.Elapsed.TotalSeconds;
                    left = Math.Max(usual - spent, Math.Min(usual * 0.1, 2));
                    s.Overrun = spent > usual * 1.5 + 5;
                    s.Rough |= !learned.ContainsKey(now.Key);
                }
                foreach (var p in pending) { left += Expected(p.Key) * k; s.Rough |= !learned.ContainsKey(p.Key); }
                s.Remaining = left;
                s.Fraction = s.Elapsed / Math.Max(0.001, s.Elapsed + left);
                return s;
            }
        }

        // ------------------------------------------------------------------ internals

        sealed class Scope : IDisposable
        {
            readonly bool outer, mutes; bool done;
            public Scope(bool outer, bool mutes) { this.outer = outer; this.mutes = mutes; }
            public void Dispose()
            {
                if (done) return;
                done = true;
                bool ended = false;
                lock (gate)
                {
                    depth = Math.Max(0, depth - 1);
                    if (mutes) muted = Math.Max(0, muted - 1);
                    if (outer)
                    {
                        Finish();
                        pending.Clear();
                        total.Stop();
                        depth = 0; muted = 0;
                        ReportAndSave();
                        ended = true;
                    }
                }
                if (ended) try { Ended?.Invoke(); } catch { }
            }
        }

        static double Expected(string key)
        {
            if (learned.TryGetValue(key, out var v)) return v;
            if (Defaults.TryGetValue(key, out v)) return v;
            int dot = key.LastIndexOf('.');
            return dot > 0 ? Expected(key.Substring(0, dot)) : 3;
        }

        // The item in work is done: its time is measured and learned.
        static void Finish()
        {
            if (now == null) return;
            double spent = current.Elapsed.TotalSeconds, usual = Expected(now.Key);
            doneActual += spent; doneExpected += usual;
            if (!measured.TryGetValue(now.Key, out var list)) measured[now.Key] = list = new List<double>();
            list.Add(spent);
            // learned slowly, so one odd run (Inventor starting, a question waiting for an answer)
            // does not throw the estimate off; a kind timed for the first time is taken as it is
            if (!learned.TryGetValue(now.Key, out var old)) learned[now.Key] = spent;
            else learned[now.Key] = spent > old * 4 ? old * 0.85 + spent * 0.15 : old * 0.6 + spent * 0.4;
            now = null;
            current.Reset();
        }

        static void ReportAndSave()
        {
            foreach (var kv in measured.OrderBy(k => k.Key))
                Log.Dev($"progress: {kv.Key}  {kv.Value.Count} x {kv.Value.Average():0.0} s (now expected {Expected(kv.Key):0.0} s)");
            if (string.IsNullOrEmpty(TimingsFile)) return;
            try
            {
                var o = new JsonObject();
                foreach (var kv in learned.OrderBy(k => k.Key)) o[kv.Key] = Math.Round(kv.Value, 2);
                string dir = Path.GetDirectoryName(Path.GetFullPath(TimingsFile));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(TimingsFile, Json.Write(o), new UTF8Encoding(false));
            }
            catch (Exception e) { Log.Dev($"progress: timings not saved ({e.Message})"); }
        }

        static void Load()
        {
            if (loaded || string.IsNullOrEmpty(TimingsFile)) return;
            loaded = true;
            try
            {
                if (!File.Exists(TimingsFile)) return;
                if (Json.Parse(File.ReadAllText(TimingsFile)) is JsonObject o)
                    foreach (var k in o.OrderedKeys) { double v = o.Num(k, -1); if (v >= 0) learned[k] = v; }
            }
            catch (Exception e) { Log.Dev($"progress: timings not read ({e.Message})"); }
        }
    }
}
