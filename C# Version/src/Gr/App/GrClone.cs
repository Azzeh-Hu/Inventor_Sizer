// GR parts added and removed in Inventor by copying what is there: a new P350 ring is a copy of
// an existing ring (its P350 and joints), a new box a copy of an existing box (8 corners, 12 P150).
// The copies keep every Mate / Flush the originals have among themselves and (for a ring) to the
// frame profiles, so no face has to be guessed; only the ring's position offset is new, and a new
// box is joined to its neighbour corner to corner through the corners' named Out faces.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.Core;

namespace InventorSizer
{
    public static partial class Inv
    {
        static string ConstraintType(dynamic c)
        {
            string t = Microsoft.VisualBasic.Information.TypeName(c) ?? "";
            return t.EndsWith("Constraint") ? t.Substring(0, t.Length - "Constraint".Length) : t;
        }

        static string OccName(Func<dynamic> get) { try { return (string)get().Name; } catch { return ""; } }

        // A class, not a tuple: called with late-bound Inventor objects, tuple names are lost at run time.
        public class CloneResult
        {
            public Dictionary<string, dynamic> Occs = new Dictionary<string, dynamic>();
            public Dictionary<string, string> Cons = new Dictionary<string, string>();
            public Dictionary<string, int> Constrained = new Dictionary<string, int>();   // old name -> constraints its copy got
        }

        // Copies the occurrences `names` (same files) moved by `shift` (assembly cm; shiftOf gives one per
        // occurrence instead), and every Mate / Flush they have among themselves; one to a part outside
        // the set is copied when keep(constraint name, other occurrence) says so, to partnerFor(other)
        // when that gives another occurrence (the matching profile of another box). offset(constraint
        // name) may give a new offset (cm). Returns old name -> new occurrence, and old constraint name ->
        // new constraint name.
        static CloneResult CloneOccurrences(object asmObj, IEnumerable<string> names, double[] shift, Func<string, string, bool> keep, Func<string, double?> offset,
            Func<string, double[]> shiftOf = null, Func<string, string> partnerFor = null)
        {
            dynamic asm = asmObj;
            dynamic occs = asm.ComponentDefinition.Occurrences;
            dynamic tg = asm.Parent.TransientGeometry;
            var result = new CloneResult();
            var map = result.Occs;
            foreach (var n in names.Distinct())
            {
                var sh = shiftOf?.Invoke(n) ?? shift;
                dynamic o = occs.ItemByName(n);
                dynamic mtx = o.Transformation;
                dynamic tr = mtx.Translation;
                mtx.SetTranslation(tg.CreateVector((double)tr.X + sh[0], (double)tr.Y + sh[1], (double)tr.Z + sh[2]));
                map[n] = occs.Add((string)o.ReferencedDocumentDescriptor.FullDocumentName, mtx);
                result.Constrained[n] = 0;
            }
            var newCons = result.Cons;
            dynamic cons = asm.ComponentDefinition.Constraints;
            List<dynamic> all = Items(cons);   // typed: LINQ does not bind on a late-bound result
            foreach (var c in all)
            {
                string type = ConstraintType(c);
                if (type != "Mate" && type != "Flush") continue;
                try { if ((bool)c.Suppressed) continue; } catch { }
                string a = OccName(() => c.OccurrenceOne), b = OccName(() => c.OccurrenceTwo), name = OccName(() => c);
                bool inA = map.ContainsKey(a), inB = map.ContainsKey(b);
                if (!inA && !inB) continue;
                if (inA != inB && !keep(name, inA ? b : a)) continue;
                try
                {
                    dynamic e1 = c.EntityOne, e2 = c.EntityTwo;
                    if (inA) e1 = Proxy(map[a], e1.NativeObject);
                    if (inB) e2 = Proxy(map[b], e2.NativeObject);
                    // a partner outside the set: in another box, its matching part there
                    string other = inA ? b : a, there = inA != inB && partnerFor != null ? partnerFor(other) : other;
                    if (there == null) { Log.Write($"  {name} ({type} {a} - {b}) not copied: no matching part of {other} in that box"); continue; }
                    if (there != other)
                    {
                        dynamic target = occs.ItemByName(there), source = occs.ItemByName(other);
                        bool sameFile = string.Equals((string)target.ReferencedDocumentDescriptor.FullDocumentName, (string)source.ReferencedDocumentDescriptor.FullDocumentName, StringComparison.OrdinalIgnoreCase);
                        dynamic moved = MatchEntity(inA ? e2 : e1, target, sameFile) ?? throw new SizerException($"no face of {there} matches the one of {other}");
                        if (inA) e2 = moved; else e1 = moved;
                    }
                    double off = offset(name) ?? (double)c.Offset.Value;
                    dynamic nc = type == "Mate" ? cons.AddMateConstraint(e1, e2, off, kNoInference, kNoInference) : cons.AddFlushConstraint(e1, e2, off);
                    newCons[name] = (string)nc.Name;
                    if (inA) result.Constrained[a]++;
                    if (inB) result.Constrained[b]++;
                }
                catch (Exception e) { Log.Write($"  could not copy {name} ({type} {a} - {b}): {e.Message}"); }
            }
            return result;
        }

        // The entity of another occurrence that matches `entity` (a face, work plane, axis or point seen
        // through its occurrence): the same one when both use the same file; otherwise a planar face with
        // the same normal at the same distance across the part (a profile of another length keeps its
        // cross-section; an end face is the only one with its normal), or a work feature of the same name.
        static dynamic MatchEntity(object entityObj, object occObj, bool sameFile)
        {
            dynamic entity = entityObj, occ = occObj;
            dynamic native = entity;
            try { native = entity.NativeObject; } catch { }
            if (sameFile) return Proxy(occ, native);
            dynamic def = occ.Definition;
            // work features by name
            string kind = Microsoft.VisualBasic.Information.TypeName(native) ?? "";
            dynamic work = null;
            try { work = kind.Contains("WorkPlane") ? def.WorkPlanes : kind.Contains("WorkAxis") ? def.WorkAxes : kind.Contains("WorkPoint") ? def.WorkPoints : null; } catch { }
            if (work != null)
            {
                try { return Proxy(occ, work.Item((string)native.Name)); } catch { return null; }
            }
            // a planar face: same normal, nearest plane offset
            double[] n0, p0;
            try { dynamic g = native.Geometry; n0 = new[] { (double)g.Normal.X, (double)g.Normal.Y, (double)g.Normal.Z }; p0 = new[] { (double)g.RootPoint.X, (double)g.RootPoint.Y, (double)g.RootPoint.Z }; }
            catch { return null; }
            double d0 = Dot(n0, p0);
            dynamic best = null; double bestDiff = double.MaxValue;
            foreach (var body in Items(def.SurfaceBodies))
                foreach (var f in Items(body.Faces))
                {
                    try
                    {
                        dynamic g = f.Geometry;
                        var n1 = new[] { (double)g.Normal.X, (double)g.Normal.Y, (double)g.Normal.Z };
                        if (Dot(n0, n1) < 0.999) continue;
                        double diff = Math.Abs(Dot(n1, new[] { (double)g.RootPoint.X, (double)g.RootPoint.Y, (double)g.RootPoint.Z }) - d0);
                        if (diff < bestDiff) { bestDiff = diff; best = f; }
                    }
                    catch { }   // not planar
                }
            return best == null ? null : Proxy(occ, best);
        }

        // GR axis vector (mm, GR axes) as an assembly vector in cm.
        static double[] ShiftOf(GrModel m, double[] gr)
        {
            var v = new double[3];
            for (int g = 0; g < 3; g++) v[m.RealMap[g]] = gr[g] / 10;
            return v;
        }

        // The joints a divider hangs on: joints constrained to it in Inventor.
        static List<string> JointsOf(object asmObj, string divider)
        {
            dynamic asm = asmObj;
            var list = new List<string>();
            try
            {
                foreach (var c in Items(asm.ComponentDefinition.Occurrences.ItemByName(divider).Constraints))
                    foreach (var n in new[] { OccName(() => c.OccurrenceOne), OccName(() => c.OccurrenceTwo) })
                        if (n != divider && Gr.Kind(System.Text.RegularExpressions.Regex.Replace(n, @":\d+$", "")) == "joint" && !list.Contains(n)) list.Add(n);
            }
            catch (Exception e) { Log.Dev($"  joints of {divider}: {e.Message}"); }
            return list;
        }

        // Rings marked for removal: their P350 and joints deleted.
        static void RemoveRings(GrModel m, dynamic asm)
        {
            dynamic occs = asm.ComponentDefinition.Occurrences;
            foreach (var ring in m.RingRemoves)
            {
                var names = new List<string>(ring);
                foreach (var d in ring.Where(n => Gr.Kind(System.Text.RegularExpressions.Regex.Replace(n, @":\d+$", "")) == "divider").ToList())
                    foreach (var j in JointsOf((object)asm, d)) if (!names.Contains(j)) names.Add(j);
                foreach (var n in names)
                    try { occs.ItemByName(n).Delete(); } catch (Exception e) { Log.Write($"  could not delete {n}: {e.Message}"); }
                Log.Write($"  P350 ring removed: {string.Join(", ", names)}");
            }
            m.RingRemoves.Clear();
        }

        // A layout point (GR mm) of box f as a point of the assembly (GR axes, mm).
        static double[] RealOf(GrFrame f, double[] layout) => Enumerable.Range(0, 3).Select(g => (f.RealOrigin ?? f.Pos)[g] + layout[g] - f.Pos[g]).ToArray();

        // How far edits not yet written have moved a divider (its own offset and the ring it hangs on).
        static double[] EditShift(GrModel m, GrComponent c)
        {
            var v = new double[3]; var seen = new HashSet<string>();
            for (var x = c; x != null && seen.Add(x.Name);)
            {
                if (x.PosConstraint != "" && x.Position != null && x.OrigPosition != null && x.MoveAxis >= 0) v[x.MoveAxis] += (x.Position.Value - x.OrigPosition.Value) * x.MoveSign;
                string up = x.PosConstraint != "" ? x.PosPartner : x.Leader;
                x = up != "" && m.Components.TryGetValue(up, out var u) && u.Kind == "divider" ? u : null;
            }
            return v;
        }

        // New rings (and single P350): copies of their template, moved from where the template is in
        // Inventor to where the model has the new part (another box: its frame offset and size too).
        // A ring keeps its Mate / Flush among its parts and to the frame profiles (in another box: to
        // the matching profiles there); its position offset is the new one. A single P350, and any copy
        // that got no constraint, is grounded where it is placed. Lengths that differ from the template
        // follow in Write new GR's second pass over the profile lengths.
        static void AddRings(GrModel m, dynamic asm)
        {
            foreach (var add in m.RingAdds.ToList())
            try
            {
                if (!m.Components.TryGetValue(add.Template, out var lead)) { Log.Write($"  new P350 skipped: {add.Template} is no longer in the GR"); continue; }
                Progress.Next("gr.ring", add.Single ? $"Copying {add.Template} as a single P350" : $"Copying a P350 ring to {U.F(add.Position)} mm");
                var from = m.ById(lead.Frame); var to = add.Frame != 0 ? m.ById(add.Frame) : from;
                if (from == null || to == null) throw new SizerException("its box is no longer in the GR");
                if (to.RealOrigin == null) throw new SizerException($"{to.Label} has no real position in Inventor (it was not made)");
                var members = add.Names.Keys.ToList();
                var joints = add.Single ? new List<string>() : members.SelectMany(n => JointsOf((object)asm, n)).Distinct().Where(j => !members.Contains(j)).ToList();
                // per member: where it is in Inventor now -> where the model has the copy (GR mm)
                var shift = new Dictionary<string, double[]>(); var info = new Dictionary<string, ((double[] Min, double[] Max) Box, int K, double Grow)>();
                foreach (var n in members)
                {
                    var t = m.Components[n]; var nc = m.Components[add.Names[n]];
                    var tb = GrOpenings.LayoutBox(m, t) ?? throw new SizerException($"{n} has no position (read the GR from Inventor)");
                    var nb = GrOpenings.LayoutBox(m, nc) ?? throw new SizerException($"{nc.Name} has no position");
                    var es = EditShift(m, t);
                    var ext = Enumerable.Range(0, 3).Select(i => tb.Max[i] - tb.Min[i]).ToArray();
                    int k = Array.IndexOf(ext, ext.Max());   // its length: kept at its start, the far end grows with the length
                    var a = Enumerable.Range(0, 3).Select(g => (g == k ? tb.Min[g] : (tb.Min[g] + tb.Max[g]) / 2) - es[g]).ToArray();
                    var b = Enumerable.Range(0, 3).Select(g => g == k ? nb.Min[g] : (nb.Min[g] + nb.Max[g]) / 2).ToArray();
                    var ra = RealOf(from, a); var rb = RealOf(to, b);
                    shift[n] = Enumerable.Range(0, 3).Select(g => rb[g] - ra[g]).ToArray();
                    info[n] = (tb, k, (nb.Max[k] - nb.Min[k]) - ext[k]);
                }
                // joints go with the member they sit on (at its far end: also by its change in length)
                foreach (var j in joints)
                {
                    var jb = m.Components.TryGetValue(j, out var jc) ? GrOpenings.LayoutBox(m, jc) : null;
                    if (jb == null) { shift[j] = shift[members[0]]; continue; }
                    var jcen = Enumerable.Range(0, 3).Select(i => (jb.Value.Min[i] + jb.Value.Max[i]) / 2).ToArray();
                    string near = members.OrderBy(n => Enumerable.Range(0, 3).Sum(i => Math.Pow(Math.Max(0, Math.Max(info[n].Box.Min[i] - jcen[i], jcen[i] - info[n].Box.Max[i])), 2))).First();
                    var sh = (double[])shift[near].Clone(); var mi = info[near];
                    if (jcen[mi.K] > (mi.Box.Min[mi.K] + mi.Box.Max[mi.K]) / 2) sh[mi.K] += mi.Grow;
                    shift[j] = sh;
                }
                string KindOf(string occ) => m.Components.TryGetValue(occ, out var c) ? c.Kind : Gr.Kind(System.Text.RegularExpressions.Regex.Replace(occ, @":\d+$", ""));
                CloneResult r = Log.Measure(add.Single ? $"Copy {add.Template} as a single P350 into {to.Label}" : $"Copy the P350 ring of {add.Template} to {U.F(add.Position)} mm" + (to != from ? $" in {to.Label}" : ""),
                    () => CloneOccurrences((object)asm, members.Concat(joints), new double[3],
                        (con, other) => !add.Single && (con == lead.PosConstraint || KindOf(other) == "frameProfile" || KindOf(other) == "corner"),
                        con => con == lead.PosConstraint ? add.Position / 10 : (double?)null,
                        n => ShiftOf(m, shift[n]),
                        to == from ? (Func<string, string>)null : other => GrOpenings.Counterpart(m, other, from, to)));
                // the model takes the names Inventor gave the copies
                var grounded = new List<string>();
                foreach (var kv in add.Names.ToList())
                {
                    if (!r.Occs.TryGetValue(kv.Key, out var occ)) continue;
                    string now = occ.Name, tmp = add.Names[kv.Key];
                    Gr.RenameComponent(m, tmp, now);
                    var c = m.Components[now];
                    c.OrigBase = m.Components.TryGetValue(kv.Key, out var tc) ? tc.OrigBase : c.Base;   // the file it is a copy of; a new length follows
                    c.CloneOf = "";
                    if (kv.Key == add.Template && !add.Single)
                    {
                        c.PosConstraint = r.Cons.TryGetValue(lead.PosConstraint, out var pc) ? pc : "";
                        c.OrigPosition = c.Position;
                        if (c.PosConstraint == "") Log.Write($"  note: the position offset of {now} could not be copied - it is held where it was placed");
                    }
                    if (add.Single || r.Constrained[kv.Key] == 0) { try { occ.Grounded = true; grounded.Add(now); } catch { } }
                }
                m.RingAdds.Remove(add);
                Log.Write(add.Single ? $"  new P350 on {to.Label}: {string.Join(", ", r.Occs.Values.Select(o => (string)o.Name))} (grounded)"
                    : $"  new P350 ring at {U.F(add.Position)} mm{(to != from ? " in " + to.Label : "")}: {string.Join(", ", r.Occs.Values.Select(o => (string)o.Name))}" + (grounded.Count > 0 ? $"; grounded (nothing to mate to): {string.Join(", ", grounded)}" : ""));
            }
            catch (Exception e) { Log.Write($"  not finished: new P350 {(add.Single ? "" : "ring ")}like {add.Template}: {e.Message}"); }
        }

        // The corner of box f nearest to a layout point (GR mm), from the corners' range boxes.
        static GrComponent CornerAt(GrModel m, GrFrame f, double[] layout)
        {
            GrComponent best = null; double bestD = double.MaxValue;
            foreach (var c in m.Components.Values.Where(c => c.Frame == f.Id && c.Kind == "corner" && c.Box != null))
            {
                double d = 0;
                for (int g = 0; g < 3; g++)
                {
                    double centre = (c.Box[m.RealMap[g]] + c.Box[m.RealMap[g] + 3]) / 2 - f.RealOrigin[g] + f.Pos[g];
                    d += (centre - layout[g]) * (centre - layout[g]);
                }
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // New boxes: copies of their template box, joined corner to corner to the box they sit against.
        static void AddBoxes(GrModel m, dynamic asm)
        {
            dynamic occs = asm.ComponentDefinition.Occurrences;
            dynamic cons = asm.ComponentDefinition.Constraints;
            var outNames = (Gr.FaceNames.TryGetValue("corner", out var cn) ? cn : new string[0]).Where(n => n.StartsWith("Out")).ToArray();
            foreach (var f in m.Order.Select(m.ById).Where(f => f != null && f.CloneOf != 0).ToList())
            try
            {
                var like = m.ById(f.CloneOf);
                Progress.Next("gr.box", $"Copying {like?.Label} as {f.Label}");
                if (like?.RealOrigin == null) throw new SizerException($"{f.Label}: the box it copies has no real positions (read the GR from Inventor first).");
                var tmpl = m.Components.Values.Where(c => c.Frame == like.Id && (c.Kind == "corner" || c.Kind == "frameProfile") && !string.IsNullOrEmpty(c.OrigBase)).ToList();
                var grShift = Enumerable.Range(0, 3).Select(g => f.Pos[g] - like.Pos[g]).ToArray();
                CloneResult r = Log.Measure($"Copy {like.Label} as {f.Label}", () => CloneOccurrences((object)asm, tmpl.Select(c => c.Name), ShiftOf(m, grShift), (con, other) => false, con => null));
                // the new box's model parts take the copies' names, one per template part of the same kind / direction
                var fresh = m.Components.Values.Where(c => c.Frame == f.Id && c.CloneOf == "box").ToList();
                foreach (var t in tmpl)
                {
                    if (!r.Occs.TryGetValue(t.Name, out var occ)) continue;
                    string dim = t.Follow.FirstOrDefault()?.Dim;
                    var n = fresh.FirstOrDefault(c => c.Kind == t.Kind && (t.Kind == "corner" || c.Follow.FirstOrDefault()?.Dim == dim));
                    if (n == null) continue;
                    fresh.Remove(n);
                    string now = occ.Name;
                    Gr.RenameComponent(m, n.Name, now);
                    var c2 = m.Components[now];
                    c2.OrigBase = t.OrigBase; c2.CloneOf = "";
                    c2.Box = t.Box == null ? null : Enumerable.Range(0, 6).Select(i => t.Box[i] + 10 * ShiftOf(m, grShift)[i % 3]).ToArray();
                }
                f.RealOrigin = Enumerable.Range(0, 3).Select(g => like.RealOrigin[g] + grShift[g]).ToArray();
                f.Size0 = like.Size0 ?? like.Size;
                f.CloneOf = 0;

                // join it to the box it sits against: one corner pair, Mate on the touching faces, Flush on the other two
                var p = m.ById(f.Parent);
                if (p == null || p.RealOrigin == null) { Log.Write($"  {f.Label} is not joined to a box (place it in Inventor)"); continue; }
                int axis = f.Place == "front" ? 1 : f.Place == "stack" ? 2 : 0, sign = f.Place == "stack" ? 1 : (f.Dir >= 0 ? 1 : -1);
                var nOuter = f.Size.Select(x => x + Gr.CornerAdd).ToArray(); var pOuter = p.Size.Select(x => x + Gr.CornerAdd).ToArray();
                var nAt = (double[])f.Pos.Clone(); nAt[axis] = sign > 0 ? f.Pos[axis] : f.Pos[axis] + nOuter[axis];
                var pAt = (double[])p.Pos.Clone(); pAt[axis] = sign > 0 ? p.Pos[axis] + pOuter[axis] : p.Pos[axis];
                var nc = CornerAt(m, f, nAt); var pc = CornerAt(m, p, pAt);
                if (nc == null || pc == null) { Log.Write($"  {f.Label}: no corner pair found to join it to {p.Label}"); continue; }
                dynamic no = occs.ItemByName(nc.Name), po = occs.ItemByName(pc.Name);
                var toP = AxisVec(m, axis, -sign);
                cons.AddMateConstraint(SupportFace(no, outNames, toP, $"{p.Label}"), SupportFace(po, outNames, Scale(toP, -1), $"{f.Label}"), 0, kNoInference, kNoInference);
                foreach (int g in Enumerable.Range(0, 3).Where(g => g != axis))
                    cons.AddFlushConstraint(SupportFace(no, outNames, AxisVec(m, g, -1), "outside"), SupportFace(po, outNames, AxisVec(m, g, -1), "outside"), 0);
                Log.Write($"  {f.Label} joined to {p.Label}: {nc.Name} to {pc.Name}");
            }
            catch (Exception e) { Log.Write($"  not finished: new {f.Label}: {e.Message}"); }
        }

        // Boxes marked for removal: their parts deleted.
        static void RemoveBoxes(GrModel m, dynamic asm)
        {
            if (m.Removed.Count == 0) return;
            dynamic occs = asm.ComponentDefinition.Occurrences;
            foreach (var n in m.Removed)
                try { occs.ItemByName(n).Delete(); } catch (Exception e) { Log.Write($"  could not delete {n}: {e.Message}"); }
            Log.Write($"  removed {m.Removed.Count} part(s) of removed boxes");
            m.Removed.Clear();
        }

        // A removed box must not hold the grounded part: the rest would float.
        static void CheckRemovable(GrModel m, dynamic asm)
        {
            dynamic occs = asm.ComponentDefinition.Occurrences;
            foreach (var n in m.Removed)
            {
                bool grounded = false;
                try { grounded = (bool)occs.ItemByName(n).Grounded; } catch { }
                if (grounded) throw new SizerException($"{n} is grounded: the box it belongs to holds the GR in place and cannot be removed. Remove the box at the other end instead.");
            }
        }
    }
}
