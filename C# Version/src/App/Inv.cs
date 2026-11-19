// Everything that talks to Autodesk Inventor (COM, late bound with dynamic: no Inventor
// references needed to build, works with any Inventor version).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using InventorSizer.Core;

namespace InventorSizer
{
    public class OpenedDoc { public dynamic Doc; public bool Opened; public dynamic App; }

    public static partial class Inv
    {
        const int kPartDocumentObject = 12290, kAssemblyDocumentObject = 12291;
        const string DesignTrackingProps = "{32853F0F-3444-11D1-9E93-0060B03C1CA6}";
        const int PartNumberPropId = 5;

        // ------------------------------------------------------------------ application, documents

        public static dynamic App(bool start = false)
        {
            WaitForPreload();
            try { return Marshal.GetActiveObject("Inventor.Application"); }
            catch (COMException) { }
            if (!start) throw new SizerException("Autodesk Inventor is not running. If it is open, Inventor and this app must run at the same permission level (both normal, or both \"Run as administrator\").");
            Log.Write("Starting Inventor - this can take a minute...");
            var type = Type.GetTypeFromProgID("Inventor.Application") ?? throw new SizerException("Autodesk Inventor is not installed on this PC.");
            dynamic inv = Activator.CreateInstance(type);
            inv.Visible = true;
            return inv;
        }

        public static string Status()
        {
            if (PreloadRunning) return "Inventor: loading masters in the background...";
            try
            {
                dynamic inv = Marshal.GetActiveObject("Inventor.Application");
                dynamic doc = inv.ActiveDocument;
                if (doc == null) return "Inventor: no document open";
                return "Inventor: " + Path.GetFileNameWithoutExtension((string)doc.FullFileName);
            }
            catch { return "Inventor: not running"; }
        }

        public static dynamic ActiveAssembly()
        {
            dynamic doc = App().ActiveDocument;
            if (doc == null || (int)doc.DocumentType != kAssemblyDocumentObject) throw new SizerException("Open the assembly (.iam) in Inventor and make it the active window.");
            return doc;
        }

        // The items of a COM collection; enumerated directly (some collections report a Count
        // that Item(i) cannot reach), with Item(i) as a fallback.
        public static List<dynamic> Items(dynamic coll)
        {
            var list = new List<dynamic>();
            if (coll == null) return list;
            try { foreach (var o in coll) if (o != null) list.Add(o); return list; }
            catch { list.Clear(); }
            int n = (int)coll.Count;
            for (int i = 1; i <= n; i++) { try { var o = coll.Item(i); if (o != null) list.Add(o); } catch { } }
            return list;
        }

        static dynamic FindOpen(dynamic inv, string path)
        {
            foreach (var d in Items(inv.Documents))
            {
                string f; try { f = (string)d.FullFileName; } catch { continue; }
                if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) return d;
            }
            return null;
        }

        // Opens a file in the background (no window, faster), or uses it when it is open already.
        // A master the app pre-loaded (hidden, see PreloadMasters) counts as opened by the app:
        // it is the app's to change, and to close again when the change fails.
        public static OpenedDoc Open(string path)
        {
            dynamic inv = App(true);
            dynamic d = FindOpen(inv, path);
            if (d != null)
            {
                bool ours = TakePreloaded(path) && (int)d.Views.Count == 0;
                Log.Dev(ours ? $"{path} was pre-loaded in the background - used without opening" : $"{path} is already open in Inventor - used as it is");
                return new OpenedDoc { Doc = d, Opened = ours, App = inv };
            }
            dynamic doc = Log.Measure($"Open {Path.GetFileName(path)} in the background", () => (object)inv.Documents.Open(path, false));
            return new OpenedDoc { Doc = doc, Opened = true, App = inv };
        }

        // After a Save As the background document is the new file: give it a window, bring it up.
        public static void Show(OpenedDoc d)
        {
            try { if ((int)d.Doc.Views.Count == 0) d.Doc.Views.Add(); d.Doc.Activate(); }
            catch (Exception e) { Log.Write($"  note: open the new file yourself ({e.Message})"); }
        }

        // Opens a file in a window. A file that is open already (also hidden, pre-loaded) gets a
        // window instead of being opened again; from then on it is the user's, not the app's.
        public static void OpenVisible(string path)
        {
            if (!File.Exists(path)) throw new SizerException($"File not found: {path}");
            dynamic inv = App(true);
            dynamic doc = FindOpen(inv, path);
            TakePreloaded(path);
            if (doc == null) doc = inv.Documents.Open(path, true);
            else if ((int)doc.Views.Count == 0) doc.Views.Add();
            doc.Activate();
            Log.Write($"Opened {path}");
        }

        // ------------------------------------------------------------------ masters pre-loaded in the background

        // Opening a master cold takes 10-40 s. The app opens the masters hidden in Inventor ahead
        // of time, on a background thread, so "Open master" and Apply find them loaded. Only the
        // paths are kept (COM objects belong to the thread that got them). Inventor handles one
        // caller at a time: before the app itself talks to Inventor (App), the background thread
        // is asked to stop after the file it is opening, and later started again for the rest.
        static readonly object preloadLock = new object();
        static readonly List<string> preloadQueue = new List<string>();
        static readonly HashSet<string> preloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static Thread preloadThread;
        static volatile bool preloadStop;

        static bool PreloadRunning { get { var t = preloadThread; return t != null && t.IsAlive; } }

        // Queues the masters (first ones first) and starts the background thread. Does nothing
        // when Inventor is not running: the app never starts Inventor just to pre-load.
        // report is called on the background thread: a line for the log, or null when it ends.
        public static void PreloadMasters(IEnumerable<string> paths, Action<string> report)
        {
            lock (preloadLock)
            {
                foreach (var p in paths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).Reverse())
                {
                    preloadQueue.RemoveAll(q => string.Equals(q, p, StringComparison.OrdinalIgnoreCase));
                    preloadQueue.Insert(0, p);
                }
                if (preloadQueue.Count == 0 || PreloadRunning) return;
                preloadStop = false;
                preloadThread = new Thread(() => PreloadLoop(report)) { IsBackground = true, Name = "Pre-load masters" };
                preloadThread.SetApartmentState(ApartmentState.STA);
                preloadThread.Start();
            }
        }

        // report gets null when the thread ends.
        static void PreloadLoop(Action<string> report)
        {
            try { PreloadQueued(report); }
            finally { report?.Invoke(null); }
        }

        static void PreloadQueued(Action<string> report)
        {
            dynamic inv;
            try { inv = Marshal.GetActiveObject("Inventor.Application"); }
            catch { return; }   // Inventor is not running; queued paths wait for the next start
            while (!preloadStop)
            {
                string path;
                lock (preloadLock)
                {
                    if (preloadQueue.Count == 0) return;
                    path = preloadQueue[0]; preloadQueue.RemoveAt(0);
                }
                try
                {
                    if (FindOpen(inv, path) != null) continue;   // open already (the user's, or loaded before)
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    inv.Documents.Open(path, false);
                    lock (preloadLock) preloaded.Add(path);
                    report?.Invoke($"Pre-loaded {Path.GetFileName(path)} in the background ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
                }
                catch (Exception e) { report?.Invoke($"note: could not pre-load {Path.GetFileName(path)} ({e.Message})"); }
            }
        }

        // Called before the app's own Inventor calls: lets the background thread finish the file it
        // is opening and stop. The rest stays queued for the next PreloadMasters.
        static void WaitForPreload()
        {
            var t = preloadThread;
            if (t == null || !t.IsAlive || t == Thread.CurrentThread) return;
            preloadStop = true;
            Log.Write("  waiting for the master being pre-loaded in the background...");
            t.Join();
            preloadStop = false;
        }

        // True when the app pre-loaded the file; it is no longer counted as pre-loaded afterwards.
        static bool TakePreloaded(string path) { lock (preloadLock) return preloaded.Remove(path); }

        // On exit: closes the masters the app pre-loaded and nobody used (no changes, nothing saved).
        public static void ClosePreloaded()
        {
            List<string> paths;
            lock (preloadLock) { preloadQueue.Clear(); paths = preloaded.ToList(); preloaded.Clear(); }
            if (paths.Count == 0 && !PreloadRunning) return;
            WaitForPreload();
            lock (preloadLock) { paths.AddRange(preloaded); preloaded.Clear(); }
            dynamic inv;
            try { inv = Marshal.GetActiveObject("Inventor.Application"); } catch { return; }
            foreach (var p in paths)
            {
                try
                {
                    dynamic d = FindOpen(inv, p);
                    if (d != null && (int)d.Views.Count == 0 && !(bool)d.Dirty) d.Close(true);
                }
                catch { }
            }
        }

        public static void ActivateAssembly(string path)
        {
            foreach (var doc in Items(App().Documents))
                if ((int)doc.DocumentType == kAssemblyDocumentObject &&
                    string.Equals((string)doc.FullFileName, path, StringComparison.OrdinalIgnoreCase))
                {
                    doc.Activate();
                    return;
                }
            throw new SizerException($"The setup assembly is no longer open in Inventor: {path}");
        }

        // The assembly plus every part / sub-assembly it uses, at any depth.
        public static List<dynamic> ModelDocuments(dynamic asm)
        {
            var docs = new List<dynamic> { asm };
            foreach (var d in Items(asm.AllReferencedDocuments))
            {
                int t = (int)d.DocumentType;
                if (t == kPartDocumentObject || t == kAssemblyDocumentObject) docs.Add(d);
            }
            return docs;
        }

        static string UnitKind(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "mm": case "cm": case "m": case "in": case "ft": return "length";
                case "": case "ul": return "unitless";
                case "deg": case "rad": return "angle";
                default: return unit;
            }
        }

        // Inventor stores cm / rad internally; setting Value with these avoids decimal-comma issues.
        static double ToDb(double v, string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "mm": return v / 10;
                case "m": return v * 100;
                case "in": return v * 2.54;
                case "ft": return v * 30.48;
                case "deg": return v * Math.PI / 180;
                default: return v;
            }
        }

        // The panel's own files: the assembly and its sized parts (-Au, -In, -In-2T ...). Hinges,
        // handles and other library parts are left out: they hold none of the panel's parameters,
        // and reading or rebuilding them only costs time. Without parts in the type: all files.
        static List<dynamic> PanelDocuments(dynamic asm, Standard s)
        {
            var all = ModelDocuments(asm);
            var suffixes = s.Parts.SelectMany(p => new[] { p.Suffix, p.SplitSuffix }).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (suffixes.Count == 0) return all;
            var docs = new List<dynamic> { asm };
            for (int i = 1; i < all.Count; i++)
            {
                string b = Path.GetFileNameWithoutExtension((string)all[i].FullFileName);
                if (suffixes.Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase))) docs.Add(all[i]);
                else Log.Dev($"  not a panel file, parameters not touched: {Path.GetFileName((string)all[i].FullFileName)}");
            }
            return docs;
        }

        // An expression that uses other parameters (InWidth = ( Width - 3 mm ) / InSplit), not
        // a plain number with a unit.
        static readonly HashSet<string> UnitWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mm", "cm", "m", "in", "inch", "ft", "ul", "deg", "rad", "e" };

        public static bool IsFormula(string expression) =>
            System.Text.RegularExpressions.Regex.Matches(expression ?? "", @"[^\W\d][\w]*")
                .Cast<System.Text.RegularExpressions.Match>().Any(m => !UnitWords.Contains(m.Value));

        // Each value is looked up by name in each panel file (one call to Inventor), instead of
        // reading every parameter of every file: parts with patterns have hundreds (d0, d1 ...),
        // and every read is a separate request to Inventor.
        // A parameter that is a formula in the master is computed by Inventor and left alone: the
        // master's formula wins (e.g. HolePitchW of a -In-2T part is the inner panel's own pitch,
        // not the tray's). When its result differs from the app's value, the log says so.
        // The role of a panel file: "assembly", "outer" (-Au) or "inner" (-In, -In-2T).
        static string RoleOf(string path, string asmPath, Standard s)
        {
            if (string.Equals(path, asmPath, StringComparison.OrdinalIgnoreCase)) return "assembly";
            string b = Path.GetFileNameWithoutExtension(path);
            foreach (var role in new[] { "inner", "outer" })
                if (s.PartSuffixes(role).Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return role;
            return "";
        }

        // push (in the master's setup section): per role, parameters that get another of the
        // app's values, e.g. FP 2T { "inner": { "HolesW": "InHoleCountW" } }: the -In half
        // counts its own holes, so its HolesW is the per-half count.
        public static void PushParameters(dynamic asm, IEnumerable<Param> ps, Standard s, JsonObject push = null)
        {
            var byName = new Dictionary<string, Param>();
            foreach (var p in ps) byName[p.Name] = p;
            var used = new HashSet<string>();
            int changed = 0;
            var formulas = new List<(string File, Param Src, dynamic Ip, string Expr)>();
            var docs = PanelDocuments(asm, s);
            string asmPath = (string)asm.FullFileName;
            foreach (var doc in docs)
            {
                if (!(bool)doc.IsModifiable) continue;
                string shortName = Path.GetFileName((string)doc.FullFileName);
                dynamic plist = doc.ComponentDefinition.Parameters;
                var docParams = new Dictionary<string, Param>(byName);
                var map = push?.Obj(RoleOf((string)doc.FullFileName, asmPath, s));
                if (map != null)
                    foreach (var target in map.OrderedKeys)
                    {
                        string from = map.Str(target);
                        if (byName.TryGetValue(from, out var f)) { docParams[target] = new Param(target, f.Value, f.Unit); Log.Dev($"  {shortName} : {target} gets the app's {from} ({f.FormatValue()})"); }
                        else Log.Write($"  {shortName} : push {target} <- {from}: the app has no value {from} - not changed");
                    }
                foreach (var src in docParams.Values)
                {
                    string name = src.Name;
                    dynamic ip;
                    try { ip = plist.Item(name); } catch { continue; }   // not in this file
                    if (ip == null || (string)ip.Name != name) continue;  // same name, other case: not the same parameter
                    used.Add(name);
                    if (UnitKind(src.Unit) != UnitKind((string)ip.Units)) { Log.Write($"  {shortName} : {name} is '{ip.Units}' in Inventor but '{src.Unit}' in the app - skipped"); continue; }
                    string expr = ip.Expression;
                    if (IsFormula(expr)) { formulas.Add((shortName, src, ip, expr)); continue; }
                    double target = ToDb(src.Value, src.Unit);
                    if (Math.Abs((double)ip.Value - target) < 1e-9) continue;
                    try { ip.Value = target; changed++; Log.Write($"  {shortName} : {name}  {expr} -> {src.FormatValue()}"); }
                    catch (Exception e) { Log.Write($"  {shortName} : could not set {name} ({e.Message})"); }
                }
            }
            // After all values are in: the formulas' results, compared with the app's values.
            foreach (var f in formulas)
            {
                double now; try { now = (double)f.Ip.Value / ToDb(1, f.Src.Unit); } catch { continue; }
                if (Math.Abs(now - f.Src.Value) < 1e-6) Log.Dev($"  {f.File} : {f.Src.Name} = {f.Expr} (formula, kept) = {U.F(now)}");
                else Log.Write($"  {f.File} : {f.Src.Name} = {f.Expr} is a formula in the master - kept ({U.F(now)} {f.Src.Unit}; the app's {f.Src.Name} is {f.Src.FormatValue()})");
            }
            var missing = byName.Keys.Where(k => !used.Contains(k)).ToList();
            if (missing.Count > 0) Log.Write("  note: not used by this model (no parameter with that name): " + string.Join(", ", missing));
            if (changed == 0) Log.Write("  no parameter values changed");
            for (int i = docs.Count - 1; i >= 0; i--) if ((bool)docs[i].IsModifiable) docs[i].Update2(true);   // parts first, then the assembly
        }

        // ------------------------------------------------------------------ inner-panel split

        const string SplitConstraintName = "InSplit_Offset";

        static List<dynamic> OccurrencesBySuffix(dynamic asm, IEnumerable<string> suffixes)
        {
            var found = new List<dynamic>();
            var sx = suffixes.Where(s => !string.IsNullOrEmpty(s)).ToList();
            foreach (var occ in Items(asm.ComponentDefinition.Occurrences))
            {
                string file;
                try { file = Path.GetFileNameWithoutExtension((string)occ.Definition.Document.FullFileName); } catch { continue; }
                if (sx.Any(s => file.EndsWith(s, StringComparison.OrdinalIgnoreCase))) found.Add(occ);
            }
            return found;
        }

        static double[] BoxSize(dynamic box) => new[] { (double)box.MaxPoint.X - (double)box.MinPoint.X, (double)box.MaxPoint.Y - (double)box.MinPoint.Y, (double)box.MaxPoint.Z - (double)box.MinPoint.Z };
        static double[] BoxCenter(dynamic box) => new[] { ((double)box.MinPoint.X + (double)box.MaxPoint.X) / 2, ((double)box.MinPoint.Y + (double)box.MaxPoint.Y) / 2, ((double)box.MinPoint.Z + (double)box.MaxPoint.Z) / 2 };

        static int SplitAxis(dynamic partDef, double widthCm, string over)
        {
            int named = Array.IndexOf(new[] { "X", "Y", "Z" }, (over ?? "").ToUpperInvariant());
            if (named >= 0) return named;
            double[] size = BoxSize(partDef.RangeBox);
            var diff = size.Select(s => Math.Abs(s - widthCm)).ToArray();
            var order = Enumerable.Range(0, 3).OrderBy(i => diff[i]).ToArray();
            if (diff[order[0]] > 0.5 || diff[order[1]] - diff[order[0]] < 0.01)
                throw new SizerException($"Cannot tell which direction of the inner panel is its width (part is {string.Join(" x ", size.Select(s => U.F(s * 10)))} mm, expected width {U.F(widthCm * 10)} mm). Set \"splitAxis\" to X, Y or Z for this type in the standards file.");
            return order[0];
        }

        static dynamic FindConstraint(dynamic asm, string name)
        {
            foreach (var c in Items(asm.ComponentDefinition.Constraints)) if ((string)c.Name == name) return c;
            return null;
        }

        static double Distance(dynamic v, double[] t) => Math.Sqrt(Math.Pow((double)v.X - t[0], 2) + Math.Pow((double)v.Y - t[1], 2) + Math.Pow((double)v.Z - t[2], 2));

        // True when the master places the component with a component pattern (the pattern's
        // count and spacing then follow InSplit and InWidth by formula; see the type's setup).
        static bool InPattern(dynamic occ)
        {
            try { return (bool)occ.IsPatternElement; } catch { return false; }
        }

        static bool AnyInPattern(List<dynamic> occs)
        {
            foreach (object o in occs) if (InPattern(o)) return true;
            return false;
        }

        static void RemoveExtraInnerPanels(dynamic asm, Standard s, int split)
        {
            List<dynamic> inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            if (AnyInPattern(inner)) return;   // the pattern count (InSplit) removes it
            for (int i = inner.Count - 1; i >= Math.Max(split, 1); i--) { string n = inner[i].Name; inner[i].Delete(); Log.Write($"  removed inner panel {n} (not split at this width)"); }
        }

        // Inner panels placed by a component pattern: nothing to add or move, only a check.
        static bool InnerPanelsPatterned(dynamic asm, Standard s, int split)
        {
            List<dynamic> inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            if (!AnyInPattern(inner)) return false;
            int want = Math.Max(split, 1);
            if (inner.Count == want) Log.Write($"  inner panels: {inner.Count} (component pattern in the master)");
            else Log.Write($"  warning: the master's component pattern gives {inner.Count} inner panel(s), this width needs {want}. Run \"Set up master...\" on the master (pattern count = InSplit, spacing = InWidth).");
            return true;
        }

        static void TestInnerSplitReady(dynamic asm, Standard s, int split)
        {
            if (split < 2) return;
            var inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            if (inner.Count != 1) return;
            try { var _ = inner[0].Definition.Parameters.Item("InSplit"); }
            catch { throw new SizerException($"This width needs the inner panel split in 2, but {inner[0].Name} has no InSplit parameter. Run \"Set up master...\" for this type first (it adds InSplit to the -In part)."); }
        }

        // After the values are pushed and the parts rebuilt: add or re-position the second half,
        // one half width from the first towards the middle of the tray, tied with 3 flush
        // constraints on the origin planes (the one with the offset is InSplit_Offset).
        static void UpdateInnerSplit(dynamic asm, Standard s, int split, double innerWidth)
        {
            if (split < 2) return;
            var inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            if (inner.Count == 0) { Log.Write($"  note: no inner panel found (file name ending in {string.Join(" or ", s.PartSuffixes("inner"))}) - not split"); return; }
            dynamic first = inner[0];
            dynamic link = FindConstraint(asm, SplitConstraintName);
            if (inner.Count >= 2 && link == null && !(bool)inner[1].Grounded) { Log.Write("  second inner panel is already in the assembly - its own constraints position it"); return; }
            int axis = SplitAxis(first.Definition, innerWidth / 10, s.SplitAxis);
            double pitch = BoxSize(first.Definition.RangeBox)[axis];
            if (Math.Abs(pitch * 10 - innerWidth) > 0.01)
                Log.Write($"  warning: the inner panel is {U.F(pitch * 10)} mm wide in Inventor, the app expected {U.F(innerWidth)} mm. Check the d0 formula of the -In part (InSplit) and the sheet thickness in Settings.");
            dynamic m = first.Transformation;
            var dir = new[] { (double)m.Cell(1, axis + 1), (double)m.Cell(2, axis + 1), (double)m.Cell(3, axis + 1) };
            var outer = OccurrencesBySuffix(asm, s.PartSuffixes("outer"));
            dynamic refBox = outer.Count > 0 ? outer[0].RangeBox : asm.ComponentDefinition.RangeBox;
            var c0 = BoxCenter(refBox); var c1 = BoxCenter(first.RangeBox);
            double dot = (c0[0] - c1[0]) * dir[0] + (c0[1] - c1[1]) * dir[1] + (c0[2] - c1[2]) * dir[2];
            if (Math.Abs(dot) < 0.05) throw new SizerException("The inner panel stays in the middle of the tray after halving, so there is no room for the second half. Constrain the -In part to one wall of the -Au part (offset = Clearance), not to its middle.");
            int sign = dot > 0 ? 1 : -1;
            dynamic t = m.Translation;
            var target = new[] { (double)t.X + sign * pitch * dir[0], (double)t.Y + sign * pitch * dir[1], (double)t.Z + sign * pitch * dir[2] };
            m.SetTranslation(asm.Parent.TransientGeometry.CreateVector(target[0], target[1], target[2]), false);
            if (inner.Count >= 2)
            {
                if (link != null) { link.Offset.Value = (double)link.Offset.Value < 0 ? -pitch : pitch; Log.Write($"  second inner panel: offset set to {U.F(pitch * 10)} mm"); }
                else { inner[1].Transformation = m; Log.Write($"  second inner panel (grounded) moved to {U.F(pitch * 10)} mm from the first"); }
                return;
            }
            dynamic second = asm.ComponentDefinition.Occurrences.Add((string)first.Definition.Document.FullFileName, m);
            Log.Write($"  added inner panel {second.Name}, {U.F(pitch * 10)} mm from {first.Name}");
            try
            {
                dynamic planes = first.Definition.WorkPlanes;   // 1 = YZ, 2 = XZ, 3 = XY: plane n is normal to axis n - 1
                dynamic cons = asm.ComponentDefinition.Constraints;
                for (int j = 0; j < 3; j++)
                {
                    object p1, p2;
                    first.CreateGeometryProxy(planes.Item(j + 1), out p1);
                    second.CreateGeometryProxy(planes.Item(j + 1), out p2);
                    dynamic c = cons.AddFlushConstraint(p1, p2, j == axis ? pitch : 0.0);
                    if (j == axis) { c.Name = SplitConstraintName; link = c; }
                }
                asm.Update();
                if (Distance(second.Transformation.Translation, target) > 0.001) { link.Offset.Value = -pitch; asm.Update(); }
                if (Distance(second.Transformation.Translation, target) > 0.001) throw new Exception("the constraints did not put it in the expected place");
                Log.Write($"  mated {second.Name} to {first.Name} (3 flush constraints on the origin planes)");
            }
            catch (Exception e)
            {
                Log.Write($"  warning: could not mate the second inner panel ({e.Message}) - grounded in place instead");
                try { var l = FindConstraint(asm, SplitConstraintName); if (l != null) l.Delete(); } catch { }
                second.Transformation = m;
                second.Grounded = true;
            }
        }

        // ------------------------------------------------------------------ saving as a new size

        static void UpdatePartNumber(dynamic doc, PanelName oldN, PanelName newN, string fileName)
        {
            string shortName = Path.GetFileName((string)doc.FullFileName);
            try
            {
                dynamic prop = doc.PropertySets.Item(DesignTrackingProps).ItemByPropId(PartNumberPropId);
                string was = Convert.ToString(prop.Value);
                string now = !string.IsNullOrEmpty(fileName) && was == Path.GetFileNameWithoutExtension((string)doc.FullFileName) ? fileName : Standards.ConvertText(was, oldN, newN);
                // A Part Number with another size than the file's is left over from an older file
                // (the TP 2T master said 1112 TP-900x900x42): the new file's name replaces it.
                if (now == was && !string.IsNullOrEmpty(fileName) && U.SizeRegex.IsMatch(was) && !was.Contains(oldN.Token))
                {
                    prop.Value = fileName;
                    Log.Write($"  {shortName} : Part Number {was} -> {fileName} (it had another size than the file)");
                    return;
                }
                if (now != was) { prop.Value = now; Log.Write($"  {shortName} : Part Number {was} -> {now}"); }
                else Log.Write($"  {shortName} : Part Number '{was}' has no size in it - left as is");
            }
            catch (Exception e) { Log.Write($"  {shortName} : could not update the Part Number ({e.Message})"); }
        }

        // Every file whose name contains the old size gets the new size (and project lead); the
        // inner panel's file always matches the split state (-In / -In-2T). With an output
        // folder all new files go there.
        // Existing files are only replaced when the user said so: overwrite, or confirm (asked
        // with the list of files) returns true.
        static void SaveAsNewSize(dynamic asm, PanelName oldN, PanelName newN, string newAsmName, string outDir, Standard s, int split, bool overwrite = false, Func<List<string>, bool> confirm = null)
        {
            var plan = new List<(dynamic Doc, string Name, string Path)>();
            foreach (var doc in ModelDocuments(asm))
            {
                if ((string)doc.FullFileName == (string)asm.FullFileName || !(bool)doc.IsModifiable) continue;
                string b = Path.GetFileNameWithoutExtension((string)doc.FullFileName);
                if (!b.Contains(oldN.Token)) continue;
                string nb = Standards.ConvertSplitName(s, Standards.ConvertText(b, oldN, newN), split);
                if (nb == b && string.IsNullOrEmpty(outDir)) continue;
                string dir = !string.IsNullOrEmpty(outDir) ? outDir : Path.GetDirectoryName((string)doc.FullFileName);
                plan.Add((doc, nb, Path.Combine(dir, nb + Path.GetExtension((string)doc.FullFileName))));
            }
            string cur = Path.GetFileNameWithoutExtension((string)asm.FullFileName);
            if (newAsmName != cur || !string.IsNullOrEmpty(outDir))
            {
                string dir = !string.IsNullOrEmpty(outDir) ? outDir : Path.GetDirectoryName((string)asm.FullFileName);
                plan.Add((asm, newAsmName, Path.Combine(dir, newAsmName + Path.GetExtension((string)asm.FullFileName))));
            }
            var existing = plan.Where(p => File.Exists(p.Path)).Select(p => p.Path).ToList();
            if (existing.Count > 0 && !overwrite && (confirm == null || !confirm(existing)))
                throw new SizerException("These files already exist, nothing was saved:\n" + string.Join("\n", existing));
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) { Directory.CreateDirectory(outDir); Log.Write($"  created folder {outDir}"); }
            dynamic inv = asm.Parent;
            if (existing.Count > 0)
            {
                var open = new List<string>();
                foreach (var d in Items(inv.Documents))
                {
                    string f; try { f = (string)d.FullFileName; } catch { continue; }
                    if (existing.Any(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) open.Add(f);
                }
                if (open.Count > 0) throw new SizerException("These files are open in Inventor and cannot be overwritten. Close them, then Apply again:\n" + string.Join("\n", open));
                foreach (var f in existing) Log.Write($"  overwriting {f}");
            }
            bool wasSilent = (bool)inv.SilentOperation;
            inv.SilentOperation = true;
            try
            {
                foreach (var item in plan)
                {
                    UpdatePartNumber(item.Doc, oldN, newN, item.Name);
                    try { item.Doc.SaveAs(item.Path, false); }
                    catch (COMException e) { throw new SizerException($"Inventor could not save {item.Path} ({e.Message})."); }
                    Log.Write($"  saved {item.Path}");
                }
                asm.Save2(true);
            }
            finally { inv.SilentOperation = wasSilent; }
        }

        static void UpdatePanelAssembly(dynamic asm, List<Standard> standards, Standard s, string newName, List<Param> ps, HoleLayout lay, string outDir, bool overwrite = false, Func<List<string>, bool> confirm = null)
        {
            string cur = Path.GetFileNameWithoutExtension((string)asm.FullFileName);
            var oldN = Standards.Split(standards, cur);
            var newN = Standards.Split(standards, newName);
            if (newName != cur && oldN == null) throw new SizerException($"The current file name '{cur}' has no size in it, so the part files cannot be renamed.");
            TestInnerSplitReady(asm, s, lay.Split);
            Log.Write($"Updating {cur} ...");
            RemoveExtraInnerPanels(asm, s, lay.Split);
            var values = ps.ToDictionary(p => p.Name, p => p.Value);
            var all = new List<Param>(ps); all.AddRange(lay.Params);
            all.AddRange(Hardware.PitchParams(s, Standards.Get(values, "Length"), Hardware.Counts(s, values)));
            if (s.HandleRow != null)
            {
                var row = s.HandleRow.Layout(Standards.Get(values, "Width"), Standards.Get(values, "Length"), values);
                all.AddRange(row.Params);
                Log.Write($"  handles: {row.Count} along the {(row.Dir == 0 ? "length" : "width")}, pitch {U.F(row.Pitch)} mm, first {U.F(row.Offset)} mm from the centre, outer handle {U.F(row.Edge)} mm from the end");
            }
            // Quiet (no redraw, no input) while values change and the model rebuilds. Not while
            // saving: an assembly's Save As also writes its view (camera) and fails with the
            // screen switched off.
            Quietly((object)asm.Parent, () =>
            {
                var push = s.SetupFor(Standards.SubOf(lay.Split))?.Obj("push");
                Log.Measure("Push parameters and rebuild", () => { PushParameters(asm, all, s, push); });
                Log.Measure("Inner-panel split", () => { if (!InnerPanelsPatterned(asm, s, lay.Split)) UpdateInnerSplit(asm, s, lay.Split, lay.InnerWidth); });
                asm.Update2(true);
            });
            Log.Measure("Save the new files", () => { SaveAsNewSize(asm, oldN, newN, newName, outDir, s, lay.Split, overwrite, confirm); });
        }

        // A new panel from the type's master: the master is opened in the background, changed,
        // saved under the new name into outDir, and the new panel is opened. The master on disk
        // is never changed; what is open in Inventor is not used. An existing panel of that name
        // is only replaced with overwrite; confirm is asked when only part files exist already.
        public static string ApplyPanel(string name, List<Param> ps, Settings set, string paramPath, List<Standard> standards, Standard s, bool overwrite = false, Func<List<string>, bool> confirm = null)
        {
            PanelLock.RequireUnchanged(s.Id);
            string newName = name.Trim();
            var m = U.SizeRegex.Match(newName);
            if (!m.Success) throw new SizerException("The name must contain a size like 386x656x47.");
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new SizerException($"'{newName}' is not a valid file name.");
            var values = ps.ToDictionary(p => p.Name, p => p.Value);
            for (int i = 0; i < 3; i++)
            {
                double fromName = U.P(m.Groups[i + 1].Value);
                if (values[U.SizeParams[i]] != fromName) throw new SizerException($"The name says {U.SizeParams[i]} = {U.F(fromName)} but the value is {U.F(values[U.SizeParams[i]])}.");
            }
            var lay = HoleRules.Layout(values, set, s);
            if (lay.Errors.Count > 0) throw new SizerException("Not applied - fix these first:\n" + string.Join("\n", lay.Errors));
            var rowErrors = s.HandleRow?.Layout(values["Width"], values["Length"], values).Errors ?? new List<string>();
            if (rowErrors.Count > 0) throw new SizerException("Not applied - fix these first:\n" + string.Join("\n", rowErrors));
            string sub = Standards.SubOf(lay.Split), master = s.MasterFor(sub);
            if (!s.HasMaster(sub)) throw new SizerException($"There is no {sub} master for {s.Name} (masters\\{s.Id}\\{sub}\\ next to the app is empty). Start the dev run (Dev run.cmd), click 'Set up master...', choose a {s.Id} assembly with {(sub == "2T" ? "2 inner panels" : "1 inner panel")} and save it into Inventor Sizer.");
            if (newName == Path.GetFileNameWithoutExtension(master)) throw new SizerException("The new panel needs another name than the master; the master is never changed.");
            if (string.IsNullOrEmpty(set.OutputFolder)) throw new SizerException("Choose the folder for the new panel files first.");
            PanelLock.EnsureWritable(set.OutputFolder, "New panels cannot go into the masters");
            string target = Path.Combine(set.OutputFolder, newName + ".iam");
            if (File.Exists(target) && !overwrite) throw new SizerException($"{target} already exists.");

            ParamFile.Save(paramPath, ps);
            Log.Write($"Saved values to {paramPath}");
            foreach (var p in ps) Log.Write($"  {p}");
            Log.Write($"  holes: length wall {lay.Length.Holes} (pitch {U.F(lay.Length.Pitch)} mm), width wall {lay.Width.Holes}{(lay.Width.MidOn == 1 ? " incl. centre pair" : "")}");
            Log.Write($"  inner panel: {lay.Split} x {U.F(lay.InnerWidth)}");
            foreach (var n in lay.Notes) Log.Write("  note: " + n);

            Log.Write($"  master: {s.Id} {sub}  {master}");
            var src = Open(master);
            try
            {
                TestMasterSetup(src.Doc, s, sub);
                UpdatePanelAssembly(src.Doc, standards, s, newName, ps, lay, set.OutputFolder, overwrite, confirm);
            }
            catch
            {
                // The app's own (hidden) copy is closed unsaved. A master open in a window is the
                // user's: it now holds the changed values (and maybe new part files), so say so.
                if (src.Opened) { try { src.Doc.Close(true); } catch { } }
                else Log.Write($"  warning: the master open in Inventor ({Path.GetFileName(master)}) was changed by this Apply. Close it WITHOUT saving before the next Apply.");
                throw;
            }
            Show(src);
            Log.Write($"New panel: {target} (the master is unchanged)");
            return newName;
        }

        // While the app changes a model, Inventor does not redraw the screen, ignores clicks and
        // keys, and shows no dialogs of its own: it redraws once at the end. Each switch is set
        // back to what it was, also when the work fails.
        static void Quietly(dynamic inv, Action body)
        {
            bool silent = false, screen = true, blocked = false;
            dynamic ui = null;
            try { silent = (bool)inv.SilentOperation; } catch { }
            try { screen = (bool)inv.ScreenUpdating; } catch { }
            try { ui = inv.UserInterfaceManager; blocked = (bool)ui.UserInteractionDisabled; } catch { ui = null; }
            try
            {
                try { inv.SilentOperation = true; } catch { }
                try { inv.ScreenUpdating = false; } catch { }
                try { if (ui != null) ui.UserInteractionDisabled = true; } catch { }
                body();
            }
            finally
            {
                try { if (ui != null) ui.UserInteractionDisabled = blocked; } catch { }
                try { inv.ScreenUpdating = screen; } catch { }
                try { inv.SilentOperation = silent; } catch { }
            }
        }

        // ------------------------------------------------------------------ master setup

        // Saves the open master as copies into the app's masters\<type>\ folder: the assembly and
        // its sized parts (-Au, -In). Library parts (hinges, handles) stay where they are and
        // the copy keeps pointing at them. The opened files on disk are not changed; in Inventor
        // the open documents become the copies. Returns the copy's assembly path.
        public static string SaveMasterIntoApp(Standard s, string dir)
        {
            PanelLock.EnsureWritable(dir, "Save into Inventor Sizer");
            dynamic asm = ActiveAssembly();
            string asmPath = (string)asm.FullFileName;
            var suffixes = s.PartSuffixes("inner").Concat(s.PartSuffixes("outer")).ToList();
            var plan = new List<(dynamic Doc, string From, string To)>();
            foreach (var doc in ModelDocuments(asm))
            {
                string from = (string)doc.FullFileName;
                string b = Path.GetFileNameWithoutExtension(from);
                if (from == asmPath || !suffixes.Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                plan.Add((doc, from, Path.Combine(dir, Path.GetFileName(from))));
            }
            plan.Add((asm, asmPath, Path.Combine(dir, Path.GetFileName(asmPath))));
            plan = plan.Where(p => !string.Equals(p.From, p.To, StringComparison.OrdinalIgnoreCase)).ToList();   // already the app's copy
            dynamic inv = asm.Parent;
            var open = new List<string>();
            foreach (var d in Items(inv.Documents))
            {
                string f; try { f = (string)d.FullFileName; } catch { continue; }
                if (plan.Any(p => string.Equals(p.To, f, StringComparison.OrdinalIgnoreCase))) open.Add(f);
            }
            if (open.Count > 0) throw new SizerException("An older copy of the master is open in Inventor. Close these, then set up the master again:\n" + string.Join("\n", open));
            Directory.CreateDirectory(dir);
            inv.SilentOperation = true;
            try
            {
                foreach (var item in plan)
                {
                    if (File.Exists(item.To)) File.Delete(item.To);
                    item.Doc.SaveAs(item.To, false);
                    Log.Write($"  saved {item.To}");
                }
                asm.Save2(true);
            }
            finally { inv.SilentOperation = false; }
            Log.Write($"The master of {s.Name} is now stored in Inventor Sizer: {(string)asm.FullFileName}");
            return (string)asm.FullFileName;
        }

        // The sub type of the open master: 2T when it has 2 inner panels or a split inner part.
        public static string MasterSubType(Standard s)
        {
            dynamic asm = ActiveAssembly();
            List<dynamic> inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            var splitSuffixes = s.Parts.Where(p => p.Role == "inner" && !string.IsNullOrEmpty(p.SplitSuffix)).Select(p => p.SplitSuffix).ToList();
            bool splitFile = false;
            foreach (object o in inner)
            {
                string b; try { b = Path.GetFileNameWithoutExtension((string)((dynamic)o).Definition.Document.FullFileName); } catch { continue; }
                if (splitSuffixes.Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase))) splitFile = true;
            }
            return inner.Count >= 2 || splitFile ? "2T" : "1T";
        }

        public static void SaveActiveAssembly()
        {
            dynamic asm = ActiveAssembly();
            foreach (var d in ModelDocuments(asm)) PanelLock.EnsureWritable((string)d.FullFileName, "Save where it is");
            asm.Save2(true);
            Log.Write($"Saved {asm.FullFileName} and its changed parts");
        }

        // The marker "Set up master" leaves in the master assembly: a custom iProperty with the
        // type and the setup version, so Apply can tell a master that was never set up, or was
        // set up for an older version of the standard.
        const string CustomProps = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";
        const string SetupMarker = "InventorSizerSetup";

        // 2T keeps the mark of older versions ("TP 2"); a master set up with "setup1T" gets "TP 1T 1".
        static string SetupMark(Standard s, string sub) => s.SetupKey(sub) == "setup1T" ? $"{s.Id} 1T {s.SetupVersionFor(sub)}" : $"{s.Id} {s.SetupVersionFor(sub)}";

        static string ReadSetupMark(dynamic asm)
        {
            try { return Convert.ToString(asm.PropertySets.Item(CustomProps).Item(SetupMarker).Value) ?? ""; } catch { return ""; }
        }

        static void WriteSetupMark(dynamic asm, Standard s, string sub)
        {
            dynamic set = asm.PropertySets.Item(CustomProps);
            try { set.Item(SetupMarker).Value = SetupMark(s, sub); }
            catch { set.Add(SetupMark(s, sub), SetupMarker); }
        }

        // Types whose setup has a version need a master that "Set up master" has marked with it.
        static void TestMasterSetup(dynamic asm, Standard s, string sub)
        {
            if (s.SetupVersionFor(sub) <= 0) return;
            string mark = ReadSetupMark(asm);
            if (mark == SetupMark(s, sub)) return;
            string has = mark == "" ? "it has never been set up with this version of the app" : $"it was set up as \"{mark}\"";
            throw new SizerException($"The master {Path.GetFileName((string)asm.FullFileName)} is not set up as the {s.Id} {sub} master (expected \"{SetupMark(s, sub)}\"): {has}.\n\nStart the dev run (Dev run.cmd), click \"Set up master...\", choose this master as {sub}, check that nothing moved and save it, then Apply again.");
        }

        // Applies the type's setup one assignment at a time. When review is null, all
        // assignments run non-interactively; otherwise the callback can skip the next item or
        // cancel the remaining work after inspecting each result.
        public static MasterSetupReport MasterSetup(Standard s, string sub, Func<SetupAssignment, bool, SetupStepDecision> review = null)
        {
            var typeSetup = s.SetupFor(sub);
            if (typeSetup == null) throw new SizerException($"The type {s.Name} has no setup section in {s.File}.");
            Log.Write($"  using \"{s.SetupKey(sub)}\" from {s.File} ({sub} master)");
            dynamic asm = ActiveAssembly();
            var report = new MasterSetupReport();
            var work = new List<SetupWorkItem>();
            var rolesFound = new HashSet<string>();
            foreach (var doc in ModelDocuments(asm))
            {
                string path = (string)doc.FullFileName;
                string b = Path.GetFileNameWithoutExtension(path);
                string role = path == (string)asm.FullFileName ? "assembly"
                            : s.PartSuffixes("inner").Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "inner"
                            : s.PartSuffixes("outer").Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "outer" : "";
                var setup = role == "" ? null : typeSetup.Obj(role);
                if (setup == null) continue;
                rolesFound.Add(role);
                bool modifiable = (bool)doc.IsModifiable;
                dynamic ps = modifiable ? doc.ComponentDefinition.Parameters : null;
                foreach (var u in setup.Arr("user").OfType<JsonObject>())
                    work.Add(new SetupWorkItem { Doc = doc, Parameters = ps, Assignment = SetupAssignment.FromJson(role, path, u, false), Modifiable = modifiable });
                foreach (var f in setup.Arr("formulas").OfType<JsonObject>())
                    work.Add(new SetupWorkItem { Doc = doc, Parameters = ps, Assignment = SetupAssignment.FromJson(role, path, f, true), Modifiable = modifiable });
            }
            foreach (string role in new[] { "assembly", "outer", "inner" })
            {
                var setup = typeSetup.Obj(role);
                if (setup == null || rolesFound.Contains(role)) continue;
                string missing = $"No matching {role} document was found in the open assembly.";
                string file = $"(no matching {role} document)";
                foreach (var u in setup.Arr("user").OfType<JsonObject>())
                    work.Add(new SetupWorkItem { Assignment = SetupAssignment.FromJson(role, file, u, false), PreflightError = missing });
                foreach (var f in setup.Arr("formulas").OfType<JsonObject>())
                    work.Add(new SetupWorkItem { Assignment = SetupAssignment.FromJson(role, file, f, true), PreflightError = missing });
            }
            if (work.Count == 0) throw new SizerException("No part of the open assembly matched the type (assembly / outer / inner role) - nothing was set up.");

            bool skipNext = false;
            for (int i = 0; i < work.Count; i++)
            {
                var step = work[i];
                if (report.Cancelled)
                {
                    step.Assignment.Status = SetupAssignmentStatus.Skipped;
                    step.Assignment.Error = "Not attempted: remaining setup was cancelled.";
                    report.Assignments.Add(step.Assignment);
                    Log.Write(step.Assignment.ChecklistLine());
                    continue;
                }
                bool skip = skipNext;
                skipNext = false;
                ApplySetupAssignment(step, skip, report);
                if (review != null)
                {
                    var decision = ReviewSetupAssignment(step.Assignment, i + 1 < work.Count, review);
                    if (decision.Error != "")
                    {
                        step.Assignment.Status = SetupAssignmentStatus.Failed;
                        step.Assignment.Error = decision.Error;
                        report.FinalizationError = decision.Error;
                    }
                    else if (decision.Expression != null && decision.Expression != step.Assignment.Expression)
                        ApplySetupCorrection(step, decision.Expression);
                    if (decision.AddedAssignment != null)
                        work.Insert(i + 1, new SetupWorkItem
                        {
                            Doc = step.Doc, Parameters = step.Parameters, Modifiable = step.Modifiable,
                            Assignment = decision.AddedAssignment
                        });
                    skipNext = decision.Action == SetupStepAction.SkipNext;
                    if (decision.Action == SetupStepAction.CancelRemaining) report.Cancelled = true;
                }
            }

            if (report.Complete)
            {
                try
                {
                    if (!(bool)asm.Update2(true)) throw new SizerException("Inventor could not update the assembly after applying the assignments.");
                    if (s.SetupVersionFor(sub) > 0)
                    {
                        WriteSetupMark(asm, s, sub);
                        Log.Write($"  marked the master: {SetupMarker} = {SetupMark(s, sub)} (custom iProperty)");
                    }
                }
                catch (Exception e) { report.FinalizationError = e.Message; }
            }
            Log.Write(report.Complete ? "Setup assignments complete; changes are not saved yet." : "Setup incomplete; changes are not saved.");
            return report;
        }

        sealed class SetupWorkItem
        {
            public dynamic Doc, Parameters;
            public SetupAssignment Assignment;
            public bool Modifiable;
            public string PreflightError = "";
        }

        static void ApplySetupAssignment(SetupWorkItem step, bool skip, MasterSetupReport report)
        {
            var item = step.Assignment;
            if (skip)
            {
                item.Status = SetupAssignmentStatus.Skipped;
                item.Error = "Skipped at your request.";
                report.Assignments.Add(item);
                Log.Write(item.ChecklistLine());
                return;
            }
            if (step.PreflightError != "")
            {
                item.Status = SetupAssignmentStatus.Failed;
                item.Error = step.PreflightError;
                report.Assignments.Add(item);
                Log.Write(item.ChecklistLine());
                return;
            }
            if (!step.Modifiable)
            {
                item.Status = SetupAssignmentStatus.Failed;
                item.Error = "The document is read-only.";
                report.Assignments.Add(item);
                Log.Write(item.ChecklistLine());
                return;
            }
            try
            {
                Log.Measure($"Set {item.Role} {item.Target} in {Path.GetFileName(item.File)}", () =>
                {
                    if (item.IsFormula) step.Parameters.Item(item.Target).Expression = item.Expression;
                    else
                    {
                        object userParameters = step.Parameters.UserParameters;
                        dynamic existing = Items(userParameters)
                            .FirstOrDefault(p => string.Equals((string)p.Name, item.Target, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) existing.Expression = item.Expression;
                        else step.Parameters.UserParameters.AddByExpression(item.Target, item.Expression, item.Unit);
                    }
                    if (!(bool)step.Doc.Update2(true))
                        throw new SizerException($"Inventor could not update {Path.GetFileName(item.File)} after assigning {item.Target}.");
                });
                item.Status = SetupAssignmentStatus.Applied;
            }
            catch (Exception e)
            {
                item.Status = SetupAssignmentStatus.Failed;
                item.Error = e.Message;
            }
            report.Assignments.Add(item);
            Log.Write(item.ChecklistLine());
        }

        static void ApplySetupCorrection(SetupWorkItem step, string expression)
        {
            var item = step.Assignment;
            item.Expression = expression;
            try
            {
                Log.Measure($"Correct {item.Role} {item.Target} in {Path.GetFileName(item.File)}", () =>
                {
                    if (item.IsFormula) step.Parameters.Item(item.Target).Expression = expression;
                    else
                    {
                        object userParameters = step.Parameters.UserParameters;
                        dynamic existing = Items(userParameters)
                            .FirstOrDefault(p => string.Equals((string)p.Name, item.Target, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) existing.Expression = expression;
                        else step.Parameters.UserParameters.AddByExpression(item.Target, expression, item.Unit);
                    }
                    if (!(bool)step.Doc.Update2(true))
                        throw new SizerException($"Inventor could not update {Path.GetFileName(item.File)} after correcting {item.Target}.");
                });
                item.Status = SetupAssignmentStatus.Applied;
                item.Error = "";
            }
            catch (Exception e)
            {
                item.Status = SetupAssignmentStatus.Failed;
                item.Error = "Correction failed: " + e.Message;
            }
            Log.Write(item.ChecklistLine());
        }

        static SetupStepDecision ReviewSetupAssignment(SetupAssignment item, bool hasNext, Func<SetupAssignment, bool, SetupStepDecision> review)
        {
            return review(item, hasNext) ?? new SetupStepDecision();
        }

        // ------------------------------------------------------------------ hardware holes

        // Every full circle edge of the occurrences' bodies, in assembly space (mm).
        static List<Circle> Circles(IEnumerable<dynamic> occs)
        {
            var list = new List<Circle>();
            foreach (var occ in occs)
                foreach (var body in Items(occ.SurfaceBodies))
                    foreach (var edge in Items(body.Edges))
                    {
                        try
                        {
                            dynamic g = edge.Geometry;
                            double r = g.Radius; dynamic c = g.Center; dynamic n = g.Normal;
                            bool arc = true;
                            try { var _ = g.SweepAngle; } catch { arc = false; }
                            if (arc) continue;   // bends and rounded corners, not drilled holes
                            list.Add(new Circle { Center = new[] { (double)c.X * 10, (double)c.Y * 10, (double)c.Z * 10 }, Normal = new[] { (double)n.X, (double)n.Y, (double)n.Z }, Dia = r * 20 });
                        }
                        catch { }
                    }
            return list;
        }

        public static HoleTemplate LearnHardware(List<Standard> standards, Standard s, double ruleDia, double ruleZ)
        {
            dynamic asm = ActiveAssembly();
            string name = Path.GetFileNameWithoutExtension((string)asm.FullFileName);
            var m = U.SizeRegex.Match(name);
            if (!m.Success) throw new SizerException($"'{name}' has no size in its name.");
            double W = U.P(m.Groups[1].Value), L = U.P(m.Groups[2].Value), H = U.P(m.Groups[3].Value);
            var parsed = Standards.Split(standards, name);
            if (parsed?.Standard != null && parsed.Standard.Id != s.Id) throw new SizerException($"The active assembly is a {parsed.Standard.Name}, but the selected type is {s.Name}.");
            var outer = OccurrencesBySuffix(asm, s.PartSuffixes("outer"));
            if (outer.Count == 0) throw new SizerException($"No outer tray (file name ending in {string.Join(" or ", s.PartSuffixes("outer"))}) in {name}.");
            var inner = OccurrencesBySuffix(asm, s.PartSuffixes("inner"));
            dynamic box = outer[0].RangeBox;
            var min = new[] { (double)box.MinPoint.X * 10, (double)box.MinPoint.Y * 10, (double)box.MinPoint.Z * 10 };
            double[] boxSize = BoxSize(box);
            var size = boxSize.Select(v => v * 10).ToArray();
            List<dynamic> outerList = outer, innerList = inner;
            List<Circle> circles = Log.Measure("Read the circles", () => (List<Circle>)Circles(outerList.Concat(innerList)));
            var t = Hardware.FromCircles(circles, min, size, W, L, H, ruleDia, ruleZ);
            t.Source = (string)asm.FullFileName; t.Learned = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            t.Notes = "U / V: hole position. from = start, end or mid of the span: width (U) and length (V) on the face; along the wall (U) and height (V) on a wall.";
            int grouped = Hardware.KeepGroups(s.Hardware, t);
            if (s.HardwareCounts.Count > 0)
            {
                var without = s.HardwareCounts.Keys.Where(g => !t.Holes.Any(h => h.Group == g)).ToList();
                if (without.Count > 0) throw new SizerException($"The learned holes could not be sorted into {string.Join(" / ", without)} holes, so {string.Join(" / ", s.HardwareCounts.Values.Select(c => c.Param))} would stop working. Nothing was saved; the hardware holes in {s.File} stay as they are.");
            }
            Standards.UpdateVariant(s, v => v["hardware"] = t.ToJson());
            s.Hardware = t;
            Log.Write($"Learned {t.Holes.Count} hardware hole(s) from {name}; skipped {t.RuleHolesSkipped} wall hole(s) made by the hole rules; {grouped} sorted into hinge / handle groups. Saved in {s.File}.");
            foreach (var h in t.Holes) Log.Write($"  {h.Wall} {h.Side}: dia {U.F(h.Dia)}, {h.U.From} {U.F(h.U.Value)} / {h.V.From} {U.F(h.V.Value)}");
            return t;
        }
    }
}
