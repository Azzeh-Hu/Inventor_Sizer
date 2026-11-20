// GR (unit frame) in Inventor: reading the active GR and writing an edited GR as a new
// assembly. Kept apart from the panel code (FP / TP / RP) so the two do not affect each other.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using InventorSizer.Core;

namespace InventorSizer
{
    public static partial class Inv
    {
        // Components (with file and range box) and constraints of the active GR, in one pass.
        public static GrData ReadGr()
        {
            dynamic asm = ActiveAssembly();
            var d = new GrData { Source = "Inventor", SourcePath = asm.FullFileName };
            d.Name = U.LeafName(d.SourcePath);
            int skipped = 0;
            // Quiet (no redraw, no input) while reading: with the GR drawn in a window each call
            // waited for Inventor (about 50 ms), many times faster quiet.
            // on the loading bar: one item per part and per constraint (joins Open GR's bar)
            using (Progress.Run("Read GR from Inventor", true, ("gr.read.component", 40), ("gr.read.constraint", 120)))
            Quietly((object)asm.Parent, () => {
            Log.Measure("Read components from Inventor", () =>
            {
                List<dynamic> occList = Items(asm.ComponentDefinition.Occurrences);
                Progress.Expect("gr.read.component", occList.Count);
                int at = 0;
                foreach (var occ in occList)
                {
                    Progress.Next("gr.read.component", $"Reading part {++at} of {occList.Count}");
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
                List<dynamic> conList = Items(asm.ComponentDefinition.Constraints);
                Progress.Expect("gr.read.constraint", conList.Count);
                int at = 0;
                foreach (var c in conList)
                {
                    Progress.Next("gr.read.constraint", $"Reading constraint {++at} of {conList.Count}");
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
            });
            Log.Dev($"  {d.Components.Count} components, {d.Constraints.Count} constraints, {skipped} skipped");
            if (skipped > 0) Log.Write($"  note: {skipped} item(s) Inventor could not return were skipped");
            return d;
        }

        // Named faces (iLogic "Assign Name") in every part of the active GR, or in the active part:
        // one line per part file with the names it is missing.
        public static List<string> CheckNamedFaces()
        {
            dynamic doc = App().ActiveDocument;
            if (doc == null) throw new SizerException("Open a GR assembly, or one library part, in Inventor first.");
            var docs = new List<dynamic>();
            if ((int)doc.DocumentType == kAssemblyDocumentObject) { foreach (var d in Items(doc.AllReferencedDocuments)) docs.Add(d); }
            else docs.Add(doc);
            var lines = new List<string>(); int bad = 0, checkedParts = 0;
            foreach (var d in docs)
            {
                string leaf = U.LeafName((string)d.FullFileName);
                var names = new List<string>();
                try { foreach (var a in Items(d.AttributeManager.FindAttributes("iLogicEntityNameSet", "iLogicEntityName"))) names.Add(Convert.ToString(a.Value)); }
                catch (Exception e) { Log.Dev($"  {leaf}: names could not be read ({e.Message})"); }
                var line = Gr.NameReport(leaf, names, out bool ok);
                if (line == null) continue;
                checkedParts++;
                if (!ok) bad++;
                lines.Add((ok ? "  ok   " : "  MISSING ") + line);
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            lines.Insert(0, $"Named faces: {checkedParts} part file(s) checked, {bad} with missing names.");
            return lines;
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
        // every new profile length becomes "<prefix> P150 1700mm.ipt" in Einzelteile\ next to the
        // new GR, the occurrences are replaced (the model takes the names Inventor gives them), the
        // divider offsets set, the planned panels made and placed (job), the new GR saved and opened.
        // On the loading bar: one item per step below and per panel made and placed; the time of
        // each kind of item is learned from earlier writes (a TP panel takes longer than an FP).
        public static void ApplyGr(GrModel m, string newName, string prefix, PanelJob job = null)
        {
            var pending = Gr.Pending(m);
            var openings = job == null ? new List<GrOpening>() : GrOpenings.Planned(GrOpenings.Find(m));
            var plan = new List<(string Key, int Count)>
            {
                ("gr.start", 1), ("gr.remove", m.Removed.Count > 0 ? 1 : 0), ("gr.box", m.Frames.Count(f => f.CloneOf != 0)),
                ("gr.profile", pending.Profiles.Count), ("gr.ringremove", m.RingRemoves.Count > 0 ? 1 : 0), ("gr.ring", m.RingAdds.Count),
                ("gr.update", 1), ("gr.save", 1),
            };
            plan.AddRange(PanelPlan(openings));
            using (Progress.Run("Write new GR to Inventor", plan.ToArray()))
                ApplyGrSteps(m, newName, prefix, job);
        }

        // The panel part of the plan: each panel file made once (per type), each opening placed.
        static List<(string Key, int Count)> PanelPlan(List<GrOpening> planned)
        {
            var plan = planned.GroupBy(o => o.Panel.Name).Select(g => g.First().Panel.Type).GroupBy(t => t)
                .Select(g => ("gr.panel.make." + g.Key, g.Count())).ToList();
            if (planned.Count > 0) plan.AddRange(new[] { ("gr.panel.place", planned.Count), ("gr.panel.check", 1), ("gr.save", 1) });
            return plan;
        }

        static void ApplyGrSteps(GrModel m, string newName, string prefix, PanelJob job)
        {
            newName = (newName ?? "").Trim(); prefix = (prefix ?? "").Trim();
            if (newName == "") throw new SizerException("Enter the name of the new GR assembly (e.g. 5794 GR).");
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new SizerException($"'{newName}' is not a valid file name.");
            if (newName == m.Name) throw new SizerException("The new GR needs a new name; the original stays as it is.");
            var todo = Gr.Pending(m);
            if (todo.Blocked.Count > 0) throw new SizerException("Not written:\n" + string.Join("\n", todo.Blocked) + "\nUndo those (load the GR again) or make them in Inventor.");
            Progress.Next("gr.start", $"Saving the GR as {newName}");
            var src = OpenGrSource(m);
            dynamic asm = src.Doc;
            string name = Path.GetFileNameWithoutExtension((string)asm.FullFileName);
            string newPath = Path.Combine(Path.GetDirectoryName((string)asm.FullFileName), newName + Path.GetExtension((string)asm.FullFileName));
            if (File.Exists(newPath)) throw new SizerException($"{newPath} already exists.");
            dynamic inv = asm.Parent;
            var planned = job == null ? new List<GrOpening>() : GrOpenings.Planned(GrOpenings.Find(m));
            var bad = planned.Where(o => o.Panel.Problem != null).ToList();
            if (bad.Count > 0) throw new SizerException("Not written - fix these openings first (or set them to -):\n" + string.Join("\n", bad.Select(o => $"{o.Label}: {o.Panel.Problem}")));
            if (planned.Count > 0 && m.Frames.Any(f => f.RealOrigin == null)) throw new SizerException("Placing panels needs the GR read from Inventor (real part positions): read it again, then write.");
            string parts = Path.Combine(Path.GetDirectoryName(newPath), "Einzelteile");
            CheckRemovable(m, (object)asm);
            Log.Measure($"Save the GR as {newName}", () => { asm.SaveAs(newPath, false); });
            Log.Write($"  saved {newPath} (the new GR; {name} stays as it was)");
            dynamic occs = asm.ComponentDefinition.Occurrences;
            var renamed = new Dictionary<string, string>();
            // boxes first: a new box is a copy at the old size, its profiles get their own size below
            // Quiet (no redraw, no input) while parts are removed, copied and moved; not around the
            // Save As / Save calls (an assembly's save writes its view and fails with the screen off).
            if (m.Removed.Count > 0) Progress.Next("gr.remove", "Removing boxes");
            Quietly((object)inv, () =>
            {
                RemoveBoxes(m, (object)asm);
                if (m.Frames.Any(f => f.CloneOf != 0)) { AddBoxes(m, (object)asm); todo = Gr.Pending(m); }
            });
            Progress.Expect("gr.profile", todo.Profiles.Count);   // new boxes bring their own lengths
            foreach (var g in todo.Profiles)
            {
                Progress.Next("gr.profile", $"Profile {g.From} -> {g.To} ({g.Names.Count} x)");
                Log.Measure($"Profile {g.From} -> {g.To} ({g.Names.Count} x)", () =>
                {
                    dynamic srcPart = occs.ItemByName(g.Names[0]).Definition.Document;
                    string ext = Path.GetExtension((string)srcPart.FullFileName);
                    string partName = prefix != "" ? $"{prefix} {g.To}" : g.To;
                    Directory.CreateDirectory(parts);
                    string partPath = Path.Combine(parts, partName + ext);
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
                    foreach (var n in g.Names)
                    {
                        dynamic occ = occs.ItemByName(n);
                        occ.Replace(partPath, false);
                        m.Components[n].OrigBase = g.To;
                        string now = n;
                        try { now = occ.Name; } catch { }
                        if (now != n) { Gr.RenameComponent(m, n, now); renamed[n] = now; }
                    }
                    Log.Write($"  {g.Names.Count} x {g.From} -> {partName}");
                });
            }
            // P350 rings: removed, then new ones copied from rings that already have their final length
            Quietly((object)inv, () =>
            {
                if (m.RingRemoves.Count > 0) { Progress.Next("gr.ringremove", "Removing P350 rings"); RemoveRings(m, (object)asm); }
                if (m.RingAdds.Count > 0) { AddRings(m, (object)asm); todo = Gr.Pending(m); }
                Progress.Next("gr.update", "Moving dividers and updating the GR");
                if (todo.Dividers.Count > 0)
                {
                    dynamic cons = asm.ComponentDefinition.Constraints;
                    foreach (var d in todo.Dividers)
                    {
                        string dn = renamed.TryGetValue(d.Name, out var r) ? r : d.Name;
                        try { cons.Item(d.Constraint).Offset.Expression = $"{U.F(d.To)} mm"; Log.Write($"  {dn}: {U.F(d.From)} -> {U.F(d.To)} mm"); m.Components[dn].OrigPosition = d.To; }
                        catch (Exception e) { Log.Write($"  could not move {d.Name} (constraint {d.Constraint}: {e.Message})"); }
                    }
                }
                Log.Measure("Update the new GR", () => { asm.Update(); });
            });
            Progress.Next("gr.save", "Saving the new GR");
            Log.Measure("Save the new GR", () => { asm.Save2(true); });
            if (planned.Count > 0)
            {
                var now = GrOpenings.Find(m);   // again: sizes, rings and names may have changed above
                GrOpenings.AutoDefaults(m, now);   // openings made by the edits get their default type
                planned = GrOpenings.Planned(now);
                foreach (var k in new[] { "gr.panel.make.FP", "gr.panel.make.TP", "gr.panel.make.RP", "gr.panel.place", "gr.panel.check" }) Progress.Expect(k, 0);
                foreach (var (key, count) in PanelPlan(planned)) Progress.Expect(key, count);   // the panels as they are now
                job.Folder = parts;
                Log.Write(PlacePanels(m, asm, planned, job));
                Progress.Next("gr.save", "Saving the new GR with its panels");
                Log.Measure("Save the new GR with its panels", () => { asm.Update(); asm.Save2(true); });
            }
            foreach (var p in todo.Panels.Where(p => !planned.Any(o => o.Existing == p.Name))) Log.Write($"  panel {p.Name}: needs size {p.To} (now {p.From}) - set its opening to a panel type to replace it");
            m.Name = newName; m.Source = "Inventor"; m.SourcePath = newPath; m.Edited = false;
            Show(src);
            Log.Write($"New GR {newName} saved and opened.");
        }
    }
}
