// A new P350 across a face in Inventor (a span): a copy of a P350 that hangs on two joints, with
// its joints, turned and moved to where the model has it (horizontal between a P150 and a P350, or
// vertical on an end face), then held by the named faces:
//   joint Base     mate  to the seat of the support at each end (P150 Seat_1 / Seat_2, P350
//                         Seat_Side_A / Seat_Side_B: the one facing the new P350)
//   P350 Out       flush with the outside of the support (P150 Out_1 / Out_2, P350 Out)
//   P350 Seat_Side flush with the start end of the support (End_A / End_B), offset = its position
// The copy keeps the Mate / Flush between the P350 and its joints. The joint at the far end is
// mated after the P350 has its new length (Write new GR's second pass over the lengths).
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using InventorSizer.Core;

namespace InventorSizer
{
    public static partial class Inv
    {
        // the far-end joints, mated once the new P350 have their length
        static readonly List<(string What, Action Do)> spanFinish = new List<(string, Action)>();

        static double Coord(dynamic point, int i) => i == 0 ? (double)point.X : i == 1 ? (double)point.Y : (double)point.Z;

        static string[] NamesOf(string kind, string startsWith) =>
            (Gr.FaceNames.TryGetValue(kind, out var fn) ? fn : new string[0]).Where(n => n.StartsWith(startsWith)).ToArray();

        static void AddSpan(GrModel m, object asmObj, GrRingAdd add)
        {
            dynamic asm = asmObj;
            dynamic occs = asm.ComponentDefinition.Occurrences;
            dynamic cons = asm.ComponentDefinition.Constraints;
            dynamic tg = asm.Parent.TransientGeometry;
            string nn = add.Names[add.Template];
            var t = m.Components[add.Template];
            if (!m.Components.TryGetValue(nn, out var nc)) throw new SizerException($"{nn} is no longer in the GR");
            var f = m.ById(add.Frame); var tf = m.ById(t.Frame);
            string faceName = GrOpenings.FaceName(m, add.FaceAxis, add.FaceSide);
            Progress.Next("gr.ring", $"Adding a P350 across the {faceName} of {f?.Label}");
            if (f?.RealOrigin == null || tf?.RealOrigin == null) throw new SizerException("its box has no real position in Inventor");
            var sup = add.Supports.Select(s => !string.IsNullOrEmpty(s) && m.Components.ContainsKey(s) ? m.Components[s] : throw new SizerException($"the part it hangs on ({s}) is no longer in the GR")).ToArray();
            var joints = JointsOf(asmObj, add.Template);
            if (joints.Count < 2) throw new SizerException($"{add.Template} hangs on {joints.Count} joint(s) in Inventor, not 2");
            var tb = GrOpenings.LayoutBox(m, t) ?? throw new SizerException($"{add.Template} has no position (read the GR from Inventor)");
            var nb = GrOpenings.LayoutBox(m, nc) ?? throw new SizerException($"{nn} has no position");
            var es = EditShift(m, t);   // the template is moved in Inventor only after this
            var ext = Enumerable.Range(0, 3).Select(i => tb.Max[i] - tb.Min[i]).ToArray();
            int tk = Array.IndexOf(ext, ext.Max());
            var tface = Gr.FaceOf(m, t);
            int tn = tface.Axis >= 0 ? tface.Axis : Enumerable.Range(0, 3).First(i => i != tk);

            // turned: the template's length onto the new one's, its outside onto the outside of the face
            double[] Lt = AxisVec(m, tk), Nt = AxisVec(m, tn, tface.Side == 1 ? 1 : -1), La = AxisVec(m, add.Along), Na = AxisVec(m, add.FaceAxis, add.FaceSide == 1 ? 1 : -1);
            double[] Ct = Cross(Lt, Nt), Ca = Cross(La, Na);
            double[] Rot(double[] v) => Add(Add(Scale(La, Dot(Lt, v)), Scale(Na, Dot(Nt, v))), Scale(Ca, Dot(Ct, v)));
            // moved: the template's start (middle of its cross-section) onto the new one's start
            var a = Enumerable.Range(0, 3).Select(g => (g == tk ? tb.Min[g] : (tb.Min[g] + tb.Max[g]) / 2) - es[g]).ToArray();
            var b = Enumerable.Range(0, 3).Select(g => g == add.Along ? nb.Min[g] : (nb.Min[g] + nb.Max[g]) / 2).ToArray();
            var A = ToAssembly(m, tf, a); var B = ToAssembly(m, f, b);
            Func<string, object, object> place = (name, mtxObj) =>
            {
                dynamic mtx = mtxObj;
                dynamic tr = mtx.Translation;
                var t0 = new[] { (double)tr.X, (double)tr.Y, (double)tr.Z };
                var cols = new double[3][];
                for (int j = 0; j < 3; j++)
                {
                    dynamic v = tg.CreateVector(j == 0 ? 1.0 : 0.0, j == 1 ? 1.0 : 0.0, j == 2 ? 1.0 : 0.0);
                    v.TransformBy(mtx);
                    cols[j] = Rot(new[] { (double)v.X, (double)v.Y, (double)v.Z });
                }
                var t1 = Add(Rot(Add(t0, Scale(A, -1))), B);
                dynamic nm = tg.CreateMatrix();
                nm.SetCoordinateSystem(tg.CreatePoint(t1[0], t1[1], t1[2]), tg.CreateVector(cols[0][0], cols[0][1], cols[0][2]),
                                       tg.CreateVector(cols[1][0], cols[1][1], cols[1][2]), tg.CreateVector(cols[2][0], cols[2][1], cols[2][2]));
                return nm;
            };
            var members = new List<string> { add.Template }.Concat(joints).ToList();
            CloneResult r = Log.Measure($"Copy {add.Template} with its joints across the {faceName} of {f.Label}",
                () => CloneOccurrences(asmObj, members, new double[3], (con, other) => false, con => null, null, null, place));
            if (!r.Occs.TryGetValue(add.Template, out var barObj)) throw new SizerException($"{add.Template} could not be copied");
            dynamic bar = barObj;
            string barName = bar.Name;

            // the faces of the supports the new P350 sits between (layout mm along it) and its start
            int ax = m.RealMap[add.Along], runAx = m.RealMap[add.Axis];
            var list = Gr.Supports(m, f, add.FaceAxis, add.FaceSide, add.Along, (nb.Min[add.Axis] + nb.Max[add.Axis]) / 2);
            double? Cm(string name, bool high)
            {
                var s = list.FirstOrDefault(x => x.Name == name);
                return s == null ? (double?)null : ((high ? s.Lo : s.Hi) - f.Pos[add.Along] + f.RealOrigin[add.Along]) / 10;
            }
            // the joint at each end, by where its copy is along the new P350
            double At(dynamic occ) { dynamic rb = occ.RangeBox; return (Coord(rb.MinPoint, ax) + Coord(rb.MaxPoint, ax)) / 2; }
            var ends = joints.Where(r.Occs.ContainsKey).Select(j => r.Occs[j]).OrderBy(j => (double)At(j)).ToList();
            dynamic lowJ = ends.First(), highJ = ends.Last();
            dynamic lowS = occs.ItemByName(sup[0].Name), highS = occs.ItemByName(sup[1].Name);
            dynamic JointBase(dynamic j)
            {
                var named = NamedFaces(j.Definition.Document, "Base");
                if (named.Count == 0) throw new SizerException($"{j.Name}: no face named Base (Files > Check named faces)");
                return Proxy(j, named[0]);
            }
            // near end: joint Base to the seat of the first support
            cons.AddMateConstraint(JointBase(lowJ), SupportFace(lowS, NamesOf(sup[0].Kind, "Seat"), La, $"new P350 {barName}", Cm(sup[0].Name, false), ax), 0, kNoInference, kNoInference);
            // the outside: flush with the support's outside
            cons.AddFlushConstraint(SupportFace(bar, NamesOf("divider", "Out"), Na, "outside of the GR"), SupportFace(lowS, NamesOf(sup[0].Kind, "Out"), Na, "outside of the GR"), 0);
            // the position: its side flush with the start end of the support, as far from it as it is now
            var back = AxisVec(m, add.Axis, -1);
            dynamic supEnd = SupportFace(lowS, NamesOf(sup[0].Kind, "End"), back, "start of the GR");
            dynamic barSide = SupportFace(bar, NamesOf("divider", "Seat_Side"), back, "start of the GR");
            double want = PlaneOf(barSide).P[runAx], dist = Math.Abs(want - PlaneOf(supEnd).P[runAx]);
            dynamic pos = cons.AddFlushConstraint(barSide, supEnd, dist);
            int sign = 1;
            try
            {
                // Inventor measures a flush offset along the faces' normal: if the P350 went the other way, the offset is the other way round
                if (Math.Abs(PlaneOf(barSide).P[runAx] - want) > 0.01) { pos.Offset.Value = -dist; sign = -1; }
            }
            catch (Exception e) { Log.Dev($"  {barName}: position offset not checked ({e.Message})"); }
            spanFinish.Add(($"{barName}: joint {(string)highJ.Name} to {(string)highS.Name}", () =>
                cons.AddMateConstraint(JointBase(highJ), SupportFace(highS, NamesOf(sup[1].Kind, "Seat"), Scale(La, -1), $"new P350 {barName}", Cm(sup[1].Name, true), ax), 0, kNoInference, kNoInference)));

            // the model takes the names and the position constraint
            Gr.RenameComponent(m, nn, barName);
            var c = m.Components[barName];
            c.OrigBase = t.OrigBase; c.CloneOf = "";
            c.PosConstraint = (string)pos.Name; c.PosPartner = sup[0].Name; c.MoveSign = sign;
            c.Position = c.OrigPosition = Math.Round(sign * dist * 10, 2);
            if (c.Geo != null) c.Geo.Pos0 = c.Position;
            m.RingAdds.Remove(add);
            Log.Write($"  new P350 {barName} across the {faceName} of {f.Label}, between {sup[0].Name} and {sup[1].Name} (joints {string.Join(", ", ends.Select(j => (string)j.Name))}); position {pos.Name} = {U.F(c.Position.Value)} mm");
        }

        // After the lengths: the far joint of each new P350 to its support.
        static void FinishSpans()
        {
            foreach (var (what, act) in spanFinish)
                try { act(); Log.Write($"  mated {what}"); }
                catch (Exception e) { Log.Write($"  not finished: {what}: {e.Message} (mate it in Inventor)"); }
            spanFinish.Clear();
        }
    }
}
