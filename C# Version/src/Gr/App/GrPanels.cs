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
        static dynamic SupportFace(dynamic occ, string[] names, double[] dir, string what)
        {
            dynamic part = occ.Definition.Document;
            foreach (var n in names)
                foreach (var f in NamedFaces(part, n))
                {
                    dynamic p = Proxy(occ, f);
                    if (Dot(PlaneOf(p).N, dir) > 0.9) return p;
                }
            throw new SizerException($"{occ.Name}: no face named {string.Join(" / ", names)} faces the {what}. Check its named faces (Files > Check named faces).");
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

        // Panel axes in its own assembly: W, L, H directions (right-handed) and its centre (cm).
        static (double[] W, double[] L, double[] H, double[] C) PanelFrame(dynamic panelDoc)
        {
            dynamic au = null;
            foreach (var o in Items(panelDoc.ComponentDefinition.Occurrences))
            {
                string file = "";
                try { file = o.ReferencedDocumentDescriptor.FullDocumentName; } catch { }
                if (Path.GetFileNameWithoutExtension(file).EndsWith("-Au")) { au = o; break; }
            }
            if (au == null) throw new SizerException($"{Path.GetFileName((string)panelDoc.FullFileName)}: no -Au part found.");
            double[] N(string name)
            {
                var faces = NamedFaces(au.Definition.Document, name);
                if (faces.Count == 0) throw new SizerException($"{Path.GetFileName((string)panelDoc.FullFileName)}: the -Au part has no face named {name}. Name it in the panel master (see the Named faces setup).");
                return PlaneOf(Proxy(au, faces[0])).N;
            }
            var w = N("Edge_W1"); var l = N("Edge_L1"); var h = N("Top");
            if (Dot(Cross(w, l), h) < 0) w = Scale(w, -1);
            dynamic rb = panelDoc.ComponentDefinition.RangeBox;
            var c = new[] { ((double)rb.MinPoint.X + (double)rb.MaxPoint.X) / 2, ((double)rb.MinPoint.Y + (double)rb.MaxPoint.Y) / 2, ((double)rb.MinPoint.Z + (double)rb.MaxPoint.Z) / 2 };
            return (w, l, h, c);
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
                        var pd = Open(files[name]);
                        (double[] W, double[] L, double[] H, double[] C) pf;
                        try { pf = PanelFrame(pd.Doc); } finally { if (pd.Opened) try { pd.Doc.Close(true); } catch { } }
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
                        dynamic outFace = SupportFace(sup0, new[] { "Out", "Out_1", "Out_2" }, hOut, "outside");
                        cons.AddFlushConstraint(PanelFace(au, "Top"), outFace, 0); mated++;
                        foreach (var (axis, edges) in new[] { (shortAx, new[] { "Edge_W0", "Edge_W1" }), (longAx, new[] { "Edge_L0", "Edge_L1" }) })
                        {
                            step = $"mate {edges[0]} / {edges[1]} to the seat of {o.Support[Idx(axis, 0)]}";
                            var toMin = AxisVec(m, axis, -1);
                            dynamic edge = edges.Select(e => PanelFace(au, e)).FirstOrDefault(p => Dot(PlaneOf(p).N, toMin) > 0.9)
                                ?? throw new SizerException($"neither {edges[0]} nor {edges[1]} of the panel faces the {(axis == shortAx ? "short" : "long")} side - check the panel's named faces");
                            string supName = o.Support[Idx(axis, 0)];
                            if (supName == null) throw new SizerException("no profile or divider found on that side");
                            dynamic seat = SupportFace(occs.ItemByName(supName), new[] { "Seat_1", "Seat_2", "Seat_Side_A", "Seat_Side_B", "Seat_A", "Seat_B" }, AxisVec(m, axis, 1), "opening");
                            cons.AddMateConstraint(edge, seat, gap, kNoInference, kNoInference); mated++;
                        }
                    });
                }
                catch (Exception e) { problems.Add($"{o.Label} ({name}), {step}: {e.Message}"); Log.Write($"  problem at {step}: {e.Message}"); }
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
