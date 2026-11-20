// GR panels in Inventor: the panels planned in the openings are made with the panel tool
// (Inv.ApplyPanel, unchanged) into Einzelteile\ next to the GR, placed in the GR with the -Au
// side out, and mated to the profiles through the named faces (iLogic "Assign Name"):
//   panel Top     flush with the profile / divider Out face (the outside of the GR)
//   panel Edge_W* mate to the Seat of the support on that side, offset = clearance
//   panel Edge_L* mate to the Seat of the support on that side, offset = clearance
// Kept apart from the panel code (FP / TP / RP): it only calls ApplyPanel.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.Core;

namespace InventorSizer
{
    public static partial class Inv
    {
        const int kNoInference = 24833;

        public class PanelJob
        {
            public Func<string, string, string> MakePanel;   // (panel name with prefix, folder) -> made there; returns the .iam path
            public string Prefix = "", Folder = "";
        }

        // Named faces of a part, by name.
        static List<dynamic> NamedFaces(dynamic partDoc, string name)
        {
            var list = new List<dynamic>();
            try { foreach (var o in Items(partDoc.AttributeManager.FindObjects("iLogicEntityNameSet", "iLogicEntityName", name))) list.Add(o); } catch { }
            return list;
        }

        // The entity (a face of the part) as seen in the GR: ComponentOccurrence.CreateGeometryProxy.
        // Its result is an out argument of type IDispatch**, which a late-bound (dynamic) call cannot
        // pass: it is called through IDispatch with the second argument as a by-reference object
        // (DispatchWrapper = VT_BYREF | VT_DISPATCH), and as a by-reference VARIANT if Inventor wants that.
        static dynamic Proxy(dynamic occ, dynamic entity)
        {
            object o = occ;
            Exception last = null;
            foreach (var slot in new Func<object>[] { () => new System.Runtime.InteropServices.DispatchWrapper(null), () => null })
            {
                var args = new object[] { (object)entity, slot() };
                var byRef = new System.Reflection.ParameterModifier(2);
                byRef[1] = true;
                try
                {
                    o.GetType().InvokeMember("CreateGeometryProxy", System.Reflection.BindingFlags.InvokeMethod, null, o, args, new[] { byRef }, null, null);
                    object r = args[1] is System.Runtime.InteropServices.DispatchWrapper w ? w.WrappedObject : args[1];
                    if (r != null) return r;
                }
                catch (System.Reflection.TargetInvocationException e) { last = e.InnerException ?? e; }
                catch (Exception e) { last = e; }
            }
            throw new SizerException("Inventor gave no face in the GR for a named face" + (last != null ? $" ({last.Message})" : "") + ".");
        }

        // A planar face's outward normal and a point on it (assembly cm). A class, not a tuple: the
        // face is a late-bound COM object, so the call is late-bound too, and tuple names are lost then.
        public class FacePlane { public double[] N, P; }

        static FacePlane PlaneOf(object faceObj)
        {
            dynamic face = faceObj;
            dynamic g = face.Geometry;
            var n = new[] { (double)g.Normal.X, (double)g.Normal.Y, (double)g.Normal.Z };
            bool reversed = false;
            try { reversed = (bool)face.IsParamReversed; } catch { }
            if (reversed) n = n.Select(x => -x).ToArray();
            return new FacePlane { N = n, P = new[] { (double)g.RootPoint.X, (double)g.RootPoint.Y, (double)g.RootPoint.Z } };
        }

        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static double[] Scale(double[] a, double k) => a.Select(x => x * k).ToArray();
        static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };

        // Unit vector of a GR axis (0 L, 1 W, 2 H) in assembly axes.
        static double[] AxisVec(GrModel m, int grAxis, double sign = 1) { var v = new double[3]; v[m.RealMap[grAxis]] = sign; return v; }

        // A layout point (GR mm) in assembly coordinates (cm).
        static double[] ToAssembly(GrModel m, GrFrame f, double[] layout)
        {
            var p = new double[3];
            for (int g = 0; g < 3; g++) p[m.RealMap[g]] = (layout[g] - f.Pos[g] + f.RealOrigin[g]) / 10;
            return p;
        }

        // The named face of a support occurrence whose outward normal points along dir.
        // The named face of a support that points along dir; with an expected position (assembly cm along
        // the assembly axis `along`), the one nearest to it, so a profile's two seats cannot be mixed up.
        static dynamic SupportFace(object occObj, string[] names, double[] dir, string what, double? expected = null, int along = -1)
        {
            dynamic occ = occObj;
            dynamic part = occ.Definition.Document;
            dynamic best = null; string bestName = ""; double bestDiff = double.MaxValue;
            foreach (var n in names)
                foreach (var f in NamedFaces(part, n))
                {
                    dynamic p = Proxy(occ, f);
                    var pl = PlaneOf(p);
                    if (Dot(pl.N, dir) <= 0.9) continue;
                    double diff = expected == null || along < 0 ? 0 : Math.Abs(pl.P[along] - expected.Value);
                    if (diff < bestDiff) { best = p; bestName = n; bestDiff = diff; }
                }
            if (best == null) throw new SizerException($"{occ.Name}: no face named {string.Join(" / ", names)} faces the {what}. Check its named faces (Files > Check named faces).");
            if (expected != null && bestDiff > 1.0) Log.Write($"  note: {occ.Name} {bestName} is {U.F(Math.Round(bestDiff * 10))} mm from where the {what} expects it - check that face's name");
            Log.Dev($"  {occ.Name}: {bestName} for the {what}");
            return best;
        }

        // The -Au part occurrence inside a panel occurrence (a proxy in the GR) and its named faces.
        static dynamic AuOccurrence(dynamic panelOcc)
        {
            foreach (var s in Items(panelOcc.SubOccurrences))
            {
                string file = "";
                try { file = s.ReferencedDocumentDescriptor.FullDocumentName; } catch { }
                if (Path.GetFileNameWithoutExtension(file).EndsWith("-Au")) return s;
            }
            throw new SizerException($"{panelOcc.Name}: no -Au part found inside the panel.");
        }

        static dynamic PanelFace(dynamic auOcc, string name)
        {
            var faces = NamedFaces(auOcc.Definition.Document, name);
            if (faces.Count == 0) throw new SizerException($"The -Au part of the panel has no face named {name}. Name it in the panel master (see the Named faces setup).");
            return Proxy(auOcc, faces[0]);
        }

        // Panel axes in its own assembly: W, L, H directions (right-handed), its centre (cm), and which
        // named edges lie across the width (short side) and across the length. Checked against the panel
        // size: Edge_W0 / W1 must be W apart and Edge_L0 / L1 L apart (if the master has them the other
        // way round, that is said and the names are used the other way round).
        public class PanelAxes { public double[] W, L, H, C; public string[] WEdges = { "Edge_W0", "Edge_W1" }, LEdges = { "Edge_L0", "Edge_L1" }; }

        static PanelAxes PanelFrame(object panelDocObj, double wMm, double lMm)
        {
            dynamic panelDoc = panelDocObj;
            string file = Path.GetFileName((string)panelDoc.FullFileName);
            dynamic au = null;
            foreach (var o in Items(panelDoc.ComponentDefinition.Occurrences))
            {
                string f = "";
                try { f = o.ReferencedDocumentDescriptor.FullDocumentName; } catch { }
                if (Path.GetFileNameWithoutExtension(f).EndsWith("-Au")) { au = o; break; }
            }
            if (au == null) throw new SizerException($"{file}: no -Au part found.");
            FacePlane F(string name)
            {
                var faces = NamedFaces(au.Definition.Document, name);
                if (faces.Count == 0) throw new SizerException($"{file}: the -Au part has no face named {name}. Name it in the panel master (see the Named faces setup).");
                return PlaneOf(Proxy(au, faces[0]));
            }
            FacePlane w0 = F("Edge_W0"), w1 = F("Edge_W1"), l0 = F("Edge_L0"), l1 = F("Edge_L1"), top = F("Top");
            double Apart(FacePlane a, FacePlane b) => Math.Abs(Dot(new[] { b.P[0] - a.P[0], b.P[1] - a.P[1], b.P[2] - a.P[2] }, b.N)) * 10;
            double dw = Apart(w0, w1), dl = Apart(l0, l1);
            var ax = new PanelAxes();
            if (Dot(w0.N, w1.N) > -0.9 || Dot(l0.N, l1.N) > -0.9)
                throw new SizerException($"{file}: Edge_W0 / W1 or Edge_L0 / L1 are not opposite faces of the tray (check the names in the -Au master).");
            if (Math.Abs(Dot(top.N, w1.N)) > 0.1 || Math.Abs(Dot(top.N, l1.N)) > 0.1)
                throw new SizerException($"{file}: Top is not square to the edge faces (Top must be the outer face of the tray).");
            if (Math.Abs(dw - wMm) < 3 && Math.Abs(dl - lMm) < 3) { ax.W = w1.N; ax.L = l1.N; }
            else if (Math.Abs(dw - lMm) < 3 && Math.Abs(dl - wMm) < 3)
            {
                Log.Write($"  note: in {file} Edge_W0 / W1 are {U.F(Math.Round(dw))} mm apart, the length: the W and L names are swapped in its master - used the other way round");
                ax.W = l1.N; ax.L = w1.N; ax.WEdges = new[] { "Edge_L0", "Edge_L1" }; ax.LEdges = new[] { "Edge_W0", "Edge_W1" };
            }
            else throw new SizerException($"{file}: Edge_W0 / W1 are {U.F(Math.Round(dw))} mm apart and Edge_L0 / L1 {U.F(Math.Round(dl))} mm, but the panel is {U.F(wMm)} x {U.F(lMm)}: the edge names sit on the wrong faces (they must be the outer faces of the tray walls).");
            ax.H = top.N;
            if (Dot(Cross(ax.W, ax.L), ax.H) < 0) ax.W = Scale(ax.W, -1);
            dynamic rb = panelDoc.ComponentDefinition.RangeBox;
            ax.C = new[] { ((double)rb.MinPoint.X + (double)rb.MaxPoint.X) / 2, ((double)rb.MinPoint.Y + (double)rb.MaxPoint.Y) / 2, ((double)rb.MinPoint.Z + (double)rb.MaxPoint.Z) / 2 };
            Log.Dev($"  {file}: W {string.Join(",", ax.W.Select(U.F))}  L {string.Join(",", ax.L.Select(U.F))}  H {string.Join(",", ax.H.Select(U.F))}");
            return ax;
        }

        // Makes, places and mates the planned panels in the GR assembly asm (part of Write new GR,
        // after the sizes and dividers are in Inventor). Returns a short summary.
        static string PlacePanels(GrModel m, dynamic asm, List<GrOpening> planned, PanelJob job)
        {
            dynamic inv = asm.Parent;
            dynamic tg = inv.TransientGeometry;
            dynamic occs = asm.ComponentDefinition.Occurrences;
            dynamic cons = asm.ComponentDefinition.Constraints;
            Directory.CreateDirectory(job.Folder);

            // 1. the panel files, one per size
            var files = new Dictionary<string, string>(); var failed = new Dictionary<string, string>();
            foreach (var name in planned.Select(o => (job.Prefix != "" ? job.Prefix + " " : "") + o.Panel.Name).Distinct())
                {
                    try { Log.Measure($"Panel {name}", () => { files[name] = job.MakePanel(name, job.Folder); }); }
                    catch (Exception e) { failed[name] = e.Message; Log.Write($"  {name} could not be made: {e.Message}"); }
                }

            int placed = 0, mated = 0; var problems = new List<string>();
            var placedOccs = new List<(GrOpening O, string Occ)>();
            var axesOf = new Dictionary<string, PanelAxes>(StringComparer.OrdinalIgnoreCase);
            Quietly((object)inv, (Action)(() => {   // no redraw while placing: Inventor draws once at the end
            foreach (var o in planned)
            {
                string step = "";   // the step that failed goes into the problem line
                string name = (job.Prefix != "" ? job.Prefix + " " : "") + o.Panel.Name;
                if (!files.ContainsKey(name)) { problems.Add($"{o.Label} ({name}): not placed, the panel could not be made ({(failed.TryGetValue(name, out var why) ? why : "?")})"); continue; }
                try
                {
                    Log.Measure($"Place {name} in {o.Label}", () =>
                    {
                        if (o.State == "filled")
                        {
                            try { occs.ItemByName(o.Existing).Delete(); Log.Write($"  removed {o.Existing}"); }
                            catch (Exception e) { throw new SizerException($"could not remove {o.Existing}: {e.Message}"); }
                        }
                        var f = o.Frame;
                        // target axes: H out of the face, L along the longer side, W across
                        var hOut = AxisVec(m, o.Axis, o.Side == 0 ? -1 : 1);
                        int longAx = o.ClearU >= o.ClearV ? o.U : o.V, shortAx = longAx == o.U ? o.V : o.U;
                        var lT = AxisVec(m, longAx); var wT = Cross(lT, hOut);
                        // panel axes from its named faces (the file is opened in the background)
                        step = "panel axes from its named faces (Top, Edge_W1, Edge_L1)";
                        if (!axesOf.TryGetValue(files[name], out var pf))
                        {
                            var pd = Open(files[name]);
                            try { pf = PanelFrame((object)pd.Doc, o.Panel.W, o.Panel.L); } finally { if (pd.Opened) try { pd.Doc.Close(true); } catch { } }
                            axesOf[files[name]] = pf;   // read once per panel file
                        }
                        double[] R(double[] x) => Add(Add(Scale(wT, Dot(pf.W, x)), Scale(lT, Dot(pf.L, x))), Scale(hOut, Dot(pf.H, x)));
                        // centre of the opening on the face, the panel's height inside
                        step = "placing the panel";
                        var mid = new double[3]; mid[o.Axis] = o.Plane; mid[o.U] = (o.U0 + o.U1) / 2; mid[o.V] = (o.V0 + o.V1) / 2;
                        var target = Add(ToAssembly(m, f, mid), Scale(hOut, -o.Panel.H / 20));
                        var t = Add(target, Scale(R(pf.C), -1));
                        dynamic mtx = tg.CreateMatrix();
                        mtx.SetCoordinateSystem(tg.CreatePoint(t[0], t[1], t[2]), Vec(tg, R(new[] { 1.0, 0, 0 })), Vec(tg, R(new[] { 0, 1.0, 0 })), Vec(tg, R(new[] { 0, 0, 1.0 })));
                        dynamic occ = occs.Add(files[name], mtx);
                        placed++;
                        placedOccs.Add((o, (string)occ.Name));
                        Log.Write($"  placed {occ.Name}");

                        // constraints through the named faces
                        step = "finding the -Au part in the placed panel";
                        dynamic au = AuOccurrence(occ);
                        var gr = Gr.PanelRules[o.Panel.Type];
                        string gap = $"{U.F(gr.Clearance)} mm";
                        int Idx(int axis, int end) => (axis == o.U ? 0 : 2) + end;   // Support index of a side
                        // flush the outside with the Out face of the support at the short side's min end
                        step = $"flush Top with the Out face of {o.Support[Idx(shortAx, 0)]}";
                        dynamic sup0 = occs.ItemByName(o.Support[Idx(shortAx, 0)]);
                        var onPlane = new double[3]; onPlane[o.Axis] = o.Plane; onPlane[o.U] = o.U0; onPlane[o.V] = o.V0;
                        dynamic outFace = SupportFace((object)sup0, new[] { "Out", "Out_1", "Out_2" }, hOut, "outside", ToAssembly(m, f, onPlane)[m.RealMap[o.Axis]], m.RealMap[o.Axis]);
                        cons.AddFlushConstraint(PanelFace(au, "Top"), outFace, 0); mated++;
                        foreach (var (axis, edges) in new[] { (shortAx, pf.WEdges), (longAx, pf.LEdges) })
                        {
                            step = $"mate {edges[0]} / {edges[1]} to the seat of {o.Support[Idx(axis, 0)]}";
                            var toMin = AxisVec(m, axis, -1);
                            dynamic edge = edges.Select(e => PanelFace(au, e)).FirstOrDefault(p => Dot(PlaneOf(p).N, toMin) > 0.9)
                                ?? throw new SizerException($"neither {edges[0]} nor {edges[1]} of the panel faces the {(axis == shortAx ? "short" : "long")} side - check the panel's named faces");
                            string supName = o.Support[Idx(axis, 0)];
                            if (supName == null) throw new SizerException("no profile or divider found on that side");
                            var at = new double[3]; at[o.Axis] = o.Plane; at[o.U] = o.U0; at[o.V] = o.V0;
                            at[axis] = (axis == o.U ? o.U0 : o.V0) - Gr.SeatDepth;   // the seat wall: 20 mm into the support
                            dynamic seat = SupportFace((object)occs.ItemByName(supName), new[] { "Seat_1", "Seat_2", "Seat_Side_A", "Seat_Side_B", "Seat_A", "Seat_B" }, AxisVec(m, axis, 1), "opening", ToAssembly(m, f, at)[m.RealMap[axis]], m.RealMap[axis]);
                            cons.AddMateConstraint(edge, seat, gap, kNoInference, kNoInference); mated++;
                        }
                    });
                }
                catch (Exception e) { problems.Add($"{o.Label} ({name}), {step}: {e.Message}"); Log.Write($"  problem at {step}: {e.Message}"); }
            }
            }));
            // after the constraints are solved: is every panel in its opening?
            try { asm.Update(); } catch { }
            foreach (var (o, occName) in placedOccs)
            {
                try
                {
                    dynamic rb = occs.ItemByName(occName).RangeBox;
                    var c = new[] { ((double)rb.MinPoint.X + (double)rb.MaxPoint.X) / 2, ((double)rb.MinPoint.Y + (double)rb.MaxPoint.Y) / 2, ((double)rb.MinPoint.Z + (double)rb.MaxPoint.Z) / 2 };
                    var mid = new double[3]; mid[o.Axis] = o.Plane; mid[o.U] = (o.U0 + o.U1) / 2; mid[o.V] = (o.V0 + o.V1) / 2;
                    var want = ToAssembly(m, o.Frame, mid);
                    double off = Math.Sqrt(new[] { o.U, o.V }.Sum(g => Math.Pow(c[m.RealMap[g]] - want[m.RealMap[g]], 2))) * 10;
                    if (off > 30) problems.Add($"{o.Label} ({occName}): placed, but its centre is {U.F(Math.Round(off))} mm off the opening's centre - turned or flipped");
                }
                catch (Exception e) { Log.Dev($"  check of {occName}: {e.Message}"); }
            }
            foreach (var p in problems) Log.Write("  not finished: " + p);
            foreach (var o in planned) m.PanelChoice.Remove(o.Key);
            return $"  {placed} of {planned.Count} panel(s) placed, {mated} constraints" + (problems.Count > 0 ? $", {problems.Count} problem(s) - see the log" : "");
        }

        static dynamic Vec(dynamic tg, double[] v) => tg.CreateVector(v[0], v[1], v[2]);

        // Closes a saved document the app opened (e.g. a new panel), so a batch leaves no windows.
        public static void CloseIfOpen(string path)
        {
            try
            {
                dynamic d = FindOpen(App(), path);
                if (d != null && !(bool)d.Dirty) d.Close(true);
            }
            catch (Exception e) { Log.Dev($"  {Path.GetFileName(path)} stays open ({e.Message})"); }
        }
    }
}
