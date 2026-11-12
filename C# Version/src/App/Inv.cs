// Everything that talks to Autodesk Inventor (COM, late bound with dynamic: no Inventor
// references needed to build, works with any Inventor version).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using InventorSizer.Core;

namespace InventorSizer
{
    public class OpenedDoc { public dynamic Doc; public bool Opened; public dynamic App; }

    public static class Inv
    {
        const int kPartDocumentObject = 12290, kAssemblyDocumentObject = 12291;
        const string DesignTrackingProps = "{32853F0F-3444-11D1-9E93-0060B03C1CA6}";
        const int PartNumberPropId = 5;

        // ------------------------------------------------------------------ application, documents

        public static dynamic App(bool start = false)
        {
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

        // Opens a file in the background (no window, faster), or uses it when it is open already.
        public static OpenedDoc Open(string path)
        {
            dynamic inv = App(true);
            foreach (var d in Items(inv.Documents))
                if (string.Equals((string)d.FullFileName, path, StringComparison.OrdinalIgnoreCase)) { Log.Dev($"{path} is already open in Inventor - used as it is"); return new OpenedDoc { Doc = d, Opened = false, App = inv }; }
            dynamic doc = Log.Measure($"Open {Path.GetFileName(path)} in the background", () => (object)inv.Documents.Open(path, false));
            return new OpenedDoc { Doc = doc, Opened = true, App = inv };
        }

        // After a Save As the background document is the new file: give it a window, bring it up.
        public static void Show(OpenedDoc d)
        {
            try { if (d.Opened && (int)d.Doc.Views.Count == 0) d.Doc.Views.Add(); d.Doc.Activate(); }
            catch (Exception e) { Log.Write($"  note: open the new file yourself ({e.Message})"); }
        }

        public static void OpenVisible(string path)
        {
            if (!File.Exists(path)) throw new SizerException($"File not found: {path}");
            dynamic inv = App(true);
            dynamic doc = inv.Documents.Open(path, true);
            doc.Activate();
            Log.Write($"Opened {path}");
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

        public static void PushParameters(dynamic asm, IEnumerable<Param> ps)
        {
            var byName = new Dictionary<string, Param>();
            foreach (var p in ps) byName[p.Name] = p;
            var used = new HashSet<string>();
            int changed = 0;
            var docs = ModelDocuments(asm);
            foreach (var doc in docs)
            {
                if (!(bool)doc.IsModifiable) continue;
                string shortName = Path.GetFileName((string)doc.FullFileName);
                dynamic plist = doc.ComponentDefinition.Parameters;
                int n = (int)plist.Count;
                for (int i = 1; i <= n; i++)
                {
                    dynamic ip = plist.Item(i);
                    string name = ip.Name;
                    if (!byName.TryGetValue(name, out var src)) continue;
                    used.Add(name);
                    if (UnitKind(src.Unit) != UnitKind((string)ip.Units)) { Log.Write($"  {shortName} : {name} is '{ip.Units}' in Inventor but '{src.Unit}' in the app - skipped"); continue; }
                    double target = ToDb(src.Value, src.Unit);
                    if (Math.Abs((double)ip.Value - target) < 1e-9) continue;
                    try { string old = ip.Expression; ip.Value = target; changed++; Log.Write($"  {shortName} : {name}  {old} -> {src.FormatValue()}"); }
                    catch (Exception e) { Log.Write($"  {shortName} : could not set {name} ({e.Message})"); }
                }
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
            inv.SilentOperation = true;
            try
            {
                foreach (var item in plan)
                {
                    UpdatePartNumber(item.Doc, oldN, newN, item.Name);
                    item.Doc.SaveAs(item.Path, false);
                    Log.Write($"  saved {item.Path}");
                }
                asm.Save2(true);
            }
            finally { inv.SilentOperation = false; }
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
            Log.Measure("Push parameters and rebuild", () => { PushParameters(asm, all); });
            Log.Measure("Inner-panel split", () => { if (!InnerPanelsPatterned(asm, s, lay.Split)) UpdateInnerSplit(asm, s, lay.Split, lay.InnerWidth); });
            asm.Update2(true);
            Log.Measure("Save the new files", () => { SaveAsNewSize(asm, oldN, newN, newName, outDir, s, lay.Split, overwrite, confirm); });
        }

        // A new panel from the type's master: the master is opened in the background, changed,
        // saved under the new name into outDir, and the new panel is opened. The master on disk
        // is never changed; what is open in Inventor is not used. An existing panel of that name
        // is only replaced with overwrite; confirm is asked when only part files exist already.
        public static string ApplyPanel(string name, List<Param> ps, Settings set, string paramPath, List<Standard> standards, Standard s, bool overwrite = false, Func<List<string>, bool> confirm = null)
        {
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
            if (string.IsNullOrEmpty(s.Master) || !File.Exists(s.Master)) throw new SizerException($"There is no master for {s.Name}. Click 'Set master...' and pick its assembly (or put it in masters\\{s.Id}\\ next to the app).");
            if (newName == Path.GetFileNameWithoutExtension(s.Master)) throw new SizerException("The new panel needs another name than the master; the master is never changed.");
            if (string.IsNullOrEmpty(set.OutputFolder)) throw new SizerException("Choose the folder for the new panel files first.");
            string target = Path.Combine(set.OutputFolder, newName + ".iam");
            if (File.Exists(target) && !overwrite) throw new SizerException($"{target} already exists.");

            ParamFile.Save(paramPath, ps);
            Log.Write($"Saved values to {paramPath}");
            foreach (var p in ps) Log.Write($"  {p}");
            Log.Write($"  holes: length wall {lay.Length.Holes} (pitch {U.F(lay.Length.Pitch)} mm), width wall {lay.Width.Holes}{(lay.Width.MidOn == 1 ? " incl. centre pair" : "")}");
            Log.Write($"  inner panel: {lay.Split} x {U.F(lay.InnerWidth)}");
            foreach (var n in lay.Notes) Log.Write("  note: " + n);

            var src = Open(s.Master);
            try
            {
                TestMasterSetup(src.Doc, s);
                UpdatePanelAssembly(src.Doc, standards, s, newName, ps, lay, set.OutputFolder, overwrite, confirm);
            }
            catch { if (src.Opened) { try { src.Doc.Close(true); } catch { } } throw; }
            Show(src);
            Log.Write($"New panel: {target} (the master is unchanged)");
            return newName;
        }

        // ------------------------------------------------------------------ master setup

        // The marker "Set up master" leaves in the master assembly: a custom iProperty with the
        // type and the setup version, so Apply can tell a master that was never set up, or was
        // set up for an older version of the standard.
        const string CustomProps = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";
        const string SetupMarker = "InventorSizerSetup";

        static string SetupMark(Standard s) => $"{s.Id} {s.SetupVersion}";

        static string ReadSetupMark(dynamic asm)
        {
            try { return Convert.ToString(asm.PropertySets.Item(CustomProps).Item(SetupMarker).Value) ?? ""; } catch { return ""; }
        }

        static void WriteSetupMark(dynamic asm, Standard s)
        {
            dynamic set = asm.PropertySets.Item(CustomProps);
            try { set.Item(SetupMarker).Value = SetupMark(s); }
            catch { set.Add(SetupMark(s), SetupMarker); }
        }

        // Types whose setup has a version need a master that "Set up master" has marked with it.
        static void TestMasterSetup(dynamic asm, Standard s)
        {
            if (s.SetupVersion <= 0) return;
            string mark = ReadSetupMark(asm);
            if (mark == SetupMark(s)) return;
            string has = mark == "" ? "it has never been set up with this version of the app" : $"it was set up as \"{mark}\"";
            throw new SizerException($"The master {Path.GetFileName((string)asm.FullFileName)} is not set up for the {s.Id} standard (setup version {s.SetupVersion}): {has}.\n\nOpen the master in Inventor, click \"Set up master...\", check that nothing moved, save the master, then Apply again.");
        }

        // Applies the type's "setup" (standards\<type>.json) to the open master: per role the
        // user parameters are created or set, then the model dimensions get their formulas.
        public static void MasterSetup(Standard s)
        {
            if (s.Setup == null) throw new SizerException($"The type {s.Name} has no setup section in {s.File}.");
            dynamic asm = ActiveAssembly();
            int done = 0;
            foreach (var doc in ModelDocuments(asm))
            {
                string b = Path.GetFileNameWithoutExtension((string)doc.FullFileName);
                string role = (string)doc.FullFileName == (string)asm.FullFileName ? "assembly"
                            : s.PartSuffixes("inner").Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "inner"
                            : s.PartSuffixes("outer").Any(x => b.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "outer" : "";
                var setup = role == "" ? null : s.Setup.Obj(role);
                if (setup == null) continue;
                if (!(bool)doc.IsModifiable) { Log.Write($"  {b} : read-only - skipped"); continue; }
                Log.Measure($"Setup {role} : {b}", () =>
                {
                    dynamic ps = doc.ComponentDefinition.Parameters;
                    foreach (var u in setup.Arr("user").OfType<JsonObject>())
                    {
                        dynamic existing = null;
                        try { existing = ps.Item(u.Str("name")); } catch { }
                        if (existing != null) existing.Expression = u.Str("value");
                        else ps.UserParameters.AddByExpression(u.Str("name"), u.Str("value"), u.Str("unit"));
                    }
                    foreach (var f in setup.Arr("formulas").OfType<JsonObject>())
                    {
                        try { ps.Item(f.Str("name")).Expression = f.Str("formula"); }
                        catch (Exception e) { Log.Write($"  {b} : {f.Str("name")} = {f.Str("formula")} failed ({e.Message})"); }
                    }
                    doc.Update2(true);
                    Log.Write($"  {b} ({role}): {setup.Arr("user").Count} user parameters, {setup.Arr("formulas").Count} formulas");
                });
                done++;
            }
            if (done == 0) throw new SizerException("No part of the open assembly matched the type (outer / inner suffix) - nothing was set up.");
            asm.Update2(true);
            if (s.SetupVersion > 0)
            {
                try { WriteSetupMark(asm, s); Log.Write($"  marked the master: {SetupMarker} = {SetupMark(s)} (custom iProperty)"); }
                catch (Exception e) { Log.Write($"  warning: could not mark the master ({e.Message}) - Apply will refuse it"); }
            }
            Log.Write("Setup done. Check that nothing moved, then save the master.");
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

        // ------------------------------------------------------------------ GR

        // Components (with file and range box) and constraints of the active GR, in one pass.
        public static GrData ReadGr()
        {
            dynamic asm = ActiveAssembly();
            var d = new GrData { Source = "Inventor", SourcePath = asm.FullFileName };
            d.Name = U.LeafName(d.SourcePath);
            int skipped = 0;
            Log.Measure("Read components from Inventor", () =>
            {
                foreach (var occ in Items(asm.ComponentDefinition.Occurrences))
                {
                    string name, file = "";
                    try { name = occ.Name; } catch { skipped++; continue; }
                    try { file = occ.ReferencedDocumentDescriptor.FullDocumentName; } catch { }
                    var c = Gr.NewComponent(name, file);
                    try
                    {
                        dynamic rb = occ.RangeBox; dynamic mn = rb.MinPoint; dynamic mx = rb.MaxPoint;
                        c.Box = new[] { (double)mn.X * 10, (double)mn.Y * 10, (double)mn.Z * 10, (double)mx.X * 10, (double)mx.Y * 10, (double)mx.Z * 10 };
                    }
                    catch { }
                    d.Components[name] = c;
                }
            });
            Log.Measure("Read constraints from Inventor", () =>
            {
                foreach (var c in Items(asm.ComponentDefinition.Constraints))
                {
                    var k = new GrConstraint();
                    try { k.Name = c.Name; } catch { skipped++; continue; }
                    k.Type = Microsoft.VisualBasic.Information.TypeName(c) ?? "";
                    if (k.Type.EndsWith("Constraint")) k.Type = k.Type.Substring(0, k.Type.Length - "Constraint".Length);
                    try { k.A = c.OccurrenceOne.Name; } catch { }
                    try { k.B = c.OccurrenceTwo.Name; } catch { }
                    try { k.Suppressed = c.Suppressed; } catch { }
                    try { k.Value = c.Offset.Expression; } catch { try { k.Value = c.Angle.Expression; } catch { } }
                    d.Constraints.Add(k);
                }
            });
            Log.Dev($"  {d.Components.Count} components, {d.Constraints.Count} constraints, {skipped} skipped");
            if (skipped > 0) Log.Write($"  note: {skipped} item(s) Inventor could not return were skipped");
            return d;
        }

        static dynamic FindLengthParameter(dynamic doc, string kind, double oldLength)
        {
            dynamic ps = doc.ComponentDefinition.Parameters;
            double want = oldLength / 10;
            if (Gr.LengthParams.TryGetValue(kind, out var name))
                try { dynamic p = ps.Item(name); if (Math.Abs((double)p.Value - want) < 1e-6) return p; } catch { }
            List<dynamic> mps = Items(ps.ModelParameters);
            var hits = mps.Where(p => Math.Abs((double)p.Value - want) < 1e-6).ToList();
            if (hits.Count == 1) return hits[0];
            throw new SizerException($"{Path.GetFileName((string)doc.FullFileName)}: no single parameter holds the length {U.F(oldLength)} mm (looked for '{name}').");
        }

        static OpenedDoc OpenGrSource(GrModel m)
        {
            if (!string.IsNullOrEmpty(m.SourcePath) && File.Exists(m.SourcePath)) return Open(m.SourcePath);
            dynamic inv = App(true);
            dynamic a = inv.ActiveDocument;
            if (a != null && Path.GetFileNameWithoutExtension((string)a.FullFileName) == m.Name) return new OpenedDoc { Doc = a, Opened = false, App = inv };
            throw new SizerException($"The source GR '{m.Name}' was not found{(string.IsNullOrEmpty(m.SourcePath) ? "" : " at " + m.SourcePath)}. Load the GR again from its file, or open it in Inventor.");
        }

        // The edited GR as a NEW assembly: the source GR is saved under newName (same folder),
        // every new profile length becomes "<prefix> P150 1700mm.ipt" next to the original
        // profile, the occurrences are replaced, the divider offsets set, the new GR saved and opened.
        public static void ApplyGr(GrModel m, string newName, string prefix)
        {
            newName = (newName ?? "").Trim(); prefix = (prefix ?? "").Trim();
            if (newName == "") throw new SizerException("Enter the name of the new GR assembly (e.g. 5794 GR).");
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new SizerException($"'{newName}' is not a valid file name.");
            if (newName == m.Name) throw new SizerException("The new GR needs a new name; the original stays as it is.");
            var todo = Gr.Pending(m);
            if (todo.Blocked.Count > 0) throw new SizerException("Not written:\n" + string.Join("\n", todo.Blocked) + "\nUndo those (load the GR again) or make them in Inventor.");
            var src = OpenGrSource(m);
            dynamic asm = src.Doc;
            string name = Path.GetFileNameWithoutExtension((string)asm.FullFileName);
            string newPath = Path.Combine(Path.GetDirectoryName((string)asm.FullFileName), newName + Path.GetExtension((string)asm.FullFileName));
            if (File.Exists(newPath)) throw new SizerException($"{newPath} already exists.");
            dynamic inv = asm.Parent;
            Log.Measure($"Save the GR as {newName}", () => { asm.SaveAs(newPath, false); });
            Log.Write($"  saved {newPath} (the new GR; {name} stays as it was)");
            dynamic occs = asm.ComponentDefinition.Occurrences;
            foreach (var g in todo.Profiles)
            {
                Log.Measure($"Profile {g.From} -> {g.To} ({g.Names.Count} x)", () =>
                {
                    dynamic srcPart = occs.ItemByName(g.Names[0]).Definition.Document;
                    string ext = Path.GetExtension((string)srcPart.FullFileName);
                    string partName = prefix != "" ? $"{prefix} {g.To}" : g.To;
                    string partPath = Path.Combine(Path.GetDirectoryName((string)srcPart.FullFileName), partName + ext);
                    if (File.Exists(partPath)) Log.Write($"  {partName}{ext} exists - used as it is");
                    else
                    {
                        dynamic p = FindLengthParameter(srcPart, g.Kind, Gr.LengthOf(g.From));
                        string old = p.Expression;
                        inv.SilentOperation = true;
                        try { p.Value = g.Length / 10; srcPart.Update(); srcPart.SaveAs(partPath, true); }
                        finally { p.Expression = old; srcPart.Update(); inv.SilentOperation = false; }
                        Log.Write($"  created {partPath} ({p.Name} = {U.F(g.Length)} mm)");
                    }
                    foreach (var n in g.Names) occs.ItemByName(n).Replace(partPath, false);
                    Log.Write($"  {g.Names.Count} x {g.From} -> {partName}");
                    foreach (var n in g.Names) m.Components[n].OrigBase = g.To;
                });
            }
            if (todo.Dividers.Count > 0)
            {
                dynamic cons = asm.ComponentDefinition.Constraints;
                foreach (var d in todo.Dividers)
                {
                    try { cons.Item(d.Constraint).Offset.Expression = $"{U.F(d.To)} mm"; Log.Write($"  {d.Name}: {U.F(d.From)} -> {U.F(d.To)} mm"); m.Components[d.Name].OrigPosition = d.To; }
                    catch (Exception e) { Log.Write($"  could not move {d.Name} (constraint {d.Constraint}: {e.Message})"); }
                }
            }
            Log.Measure("Update and save the new GR", () => { asm.Update(); asm.Save2(true); });
            foreach (var p in todo.Panels) Log.Write($"  panel {p.Name}: needs size {p.To} (now {p.From}) - make it with the panel tool and replace it");
            m.Name = newName; m.Source = "Inventor"; m.SourcePath = newPath; m.Edited = false;
            Show(src);
            Log.Write($"New GR {newName} saved and opened.");
        }
    }
}
