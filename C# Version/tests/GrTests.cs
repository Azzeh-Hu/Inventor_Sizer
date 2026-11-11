// GR tests: the same cases and numbers as the PowerShell version's tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.Core;

namespace InventorSizer.Tests
{
    public static partial class Program
    {
        static bool HaveData(string f) { if (Data != "" && File.Exists(Path.Combine(Data, f))) return true; Console.WriteLine($"skip (no test data {f})"); return false; }
        static string D(string f) => Path.Combine(Data, f);

        static void TestGrModel()
        {
            Gr.LoadRules(Path.Combine(Root, "standards", "GR.json"));
            if (!HaveData("t6357.xml")) return;
            var m = Log.Measure("build 6357", () => Gr.Build(Gr.ReadExportFiles(new[] { D("t6357.xml"), D("c6357.xml") })));
            Check("gr 6357 frames", string.Join(" ", m.Frames.Select(f => $"{f.Lx}x{f.Wy}x{f.Hz}@{f.X}")), "360x1580x1160@0 860x1580x1160@620 1840x1580x1160@1740 960x1580x1160@3840 360x1580x410@5060");
            Check("gr 6357 attached", $"{m.Frames.Sum(f => f.Panels.Count)} {m.Frames.Sum(f => f.Dividers.Count)} {m.Unplaced.Count}", "47 34 0");
            Check("gr 6357 outer", $"{m.Length} {m.OuterWidth} {m.OuterHeight}", "5680 1840 1420");
            Check("gr source path", m.SourcePath, @"K:\2. GS-H\GS-H-3500\2. Projekte\6357 Hotelneubau Huttenstraße\6357 2035-N1-ZK750-GR.iam");
            var placed = m.Components.Values.Where(c => c.Kind == "divider" && c.PosConstraint != "").ToList();
            Check("dividers placed by an offset", $"{placed.Count} {string.Join(",", placed.Select(c => U.F(c.Position.Value)).Distinct().OrderBy(x => double.Parse(x)))}", "9 280,450,525,730");
            Check("divider lines", GrDrawing.DividerLines(m).Count, 9);
            var t = Gr.Tables(m);
            Check("cut list P150", t.Parts[0][2], "1840 ×4, 1580 ×20, 1160 ×16, 960 ×4, 860 ×4, 410 ×4, 360 ×8");
            var m2 = Gr.Build(Gr.ReadExportFiles(new[] { D("t8185.xml") }));
            var m3 = Gr.Build(Gr.ReadExportFiles(new[] { D("c8185.xml") }));
            Check("gr 8185 frames", string.Join(" ", m2.Frames.Select(f => $"{f.Lx}x{f.Wy}x{f.Hz}")), "1880x1330x1220 1890x1330x1220");
            Check("gr 8185 same from both files", string.Join(",", m2.Frames.Select(f => f.X)), string.Join(",", m3.Frames.Select(f => f.X)));
            foreach (var v in Draw.Views) Check($"gr view {v}", GrDrawing.Window(m, v, 900, 700).Count > 50, true);
            Throws("gr tree only", () => Gr.ReadExportFiles(new[] { D("t6357.xml") }));
            File.WriteAllText(D("cs-gr-iso.svg"), Draw.Svg(GrDrawing.Window(m, "Isometric", 900, 700), 900, 700, "iso"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Gr.Build(Gr.ReadExportFiles(new[] { D("t6357.xml"), D("c6357.xml") }));
            Console.WriteLine($"timing: read + build 6357 GR: {sw.ElapsedMilliseconds / 10.0} ms each");
            sw.Restart();
            for (int i = 0; i < 10; i++) GrDrawing.Window(m, "Isometric", 900, 700);
            Console.WriteLine($"timing: GR drawing: {sw.ElapsedMilliseconds / 10.0} ms each");
            sw.Restart();
            for (int i = 0; i < 10; i++) Gr.SetFrameSize(m, 1, "W", 1580 + (i % 2) * 20, true);
            Console.WriteLine($"timing: GR edit: {sw.ElapsedMilliseconds / 10.0} ms each");
            File.WriteAllText(D("cs-gr-sheet.svg"), Draw.Svg(GrDrawing.Sheet(m), 1400, 990, "sheet"));
        }

        static void TestGrEdits()
        {
            if (!HaveData("t6357.xml")) return;
            var e = Gr.Build(Gr.ReadExportFiles(new[] { D("t6357.xml"), D("c6357.xml") }));
            var changed = Gr.SetFrameSize(e, 3, "W", 1700, true);
            Check("edit W shared", $"{string.Join(",", changed)} | {string.Join(",", e.Frames.Select(f => f.Wy))}", "Box 0,Box 1,Box 2,Box 3,Box 4 | 1700,1700,1700,1700,1700");
            Check("edit W profiles", Gr.LengthList(e, "frameProfile").Text, "1840 ×4, 1700 ×20, 1160 ×16, 960 ×4, 860 ×4, 410 ×4, 360 ×8");
            Check("edit W panels follow (+36)", string.Join(",", e.Components.Values.Where(c => c.Base.Contains("FP-521x")).Select(c => c.Base).Distinct()), "6357-3 FP-521x1736x47");
            Check("edit W milled divider (+40)", string.Join(",", e.Components.Values.Where(c => c.Kind == "divider" && c.Base.StartsWith("P350 17") && c.Base.Contains("gefr")).Select(c => c.Base).Distinct()), "P350 1740mm gefräst");
            Check("edit W outer", e.OuterWidth, 1960);
            Gr.SetFrameSize(e, 3, "L", 2000, false);
            Check("edit L one box", $"{string.Join(",", e.Frames.Select(f => f.Lx))} len {e.Length}", "360,860,2000,960,360 len 5840");
            Gr.SetFrameSize(e, 1, "H", 1200, true);
            Check("edit H shared (Box 4 keeps 410)", string.Join(",", e.Frames.Select(f => f.Hz)), "1200,1200,1200,1200,410");
            var nf = Gr.AddFrame(e, 2);
            Check("add box", $"{nf.Label} {nf.Lx}x{nf.Wy}x{nf.Hz} boxes {e.Frames.Count} corners {e.Corners} P150 {Gr.LengthList(e, "frameProfile").Count} len {e.Length}", "Box 5 860x1700x1200 boxes 6 corners 48 P150 72 len 6960");
            Check("added box part names", e.Components.Values.Any(c => c.Kind == "corner" && c.Name.StartsWith("APS Ecke") && c.Name.Contains(":F6-")), true);
            Gr.RemoveFrame(e, 3);
            Check("remove middle box", $"boxes {e.Frames.Count} len {e.Length} P150 {Gr.LengthList(e, "frameProfile").Count} xs {string.Join(" ", e.Frames.OrderBy(f => f.X).Select(f => $"{f.Label}@{f.X}"))}",
                  "boxes 5 len 4700 P150 60 xs Box 0@0 Box 1@620 Box 3@1740 Box 4@2960 Box 5@3580");
            var tmp = Path.GetTempFileName();
            Gr.SaveDesign(e, tmp);
            var back = Gr.ReadDesign(tmp);
            Check("design round trip", $"{back.Length} {back.OuterWidth} {back.OuterHeight} {Gr.LengthList(back, "frameProfile").Text}", $"{e.Length} {e.OuterWidth} {e.OuterHeight} {Gr.LengthList(e, "frameProfile").Text}");
            Gr.SetFrameSize(back, 2, "W", 1500, true);
            Check("edit after reload", back.OuterWidth, 1760);
            Check("pending blocked by added box", Gr.Pending(back).Blocked.Count, 2);
            // left / front
            var bx = Gr.Build(Gr.ReadExportFiles(new[] { D("c8185.xml") }));
            var l = Gr.AddFrame(bx, 1, "Left"); var fr = Gr.AddFrame(bx, 1, "Front"); var fr2 = Gr.AddFrame(bx, 1, "Front");
            Check("add box left/front", $"{l.Label} x={l.X} | {fr.Label} y={fr.Y} x={fr.X} | 2nd front y={fr2.Y} | outer {bx.Length} x {bx.OuterWidth}", "Box 2 x=0 | Box 3 y=1590 x=2140 | 2nd front y=3180 | outer 6430 x 4770");
            Check("labels", $"{Gr.NoFromLabel("Box 0")} {Gr.NoFromLabel("Box 12")}", "1 13");
            // dividers: move, pending, design keeps it
            var dv = Gr.Build(Gr.ReadExportFiles(new[] { D("t6357.xml"), D("c6357.xml") }));
            Throws("joint divider not movable", () => Gr.SetDividerPosition(dv, "P350 1620mm gefräst:5", 300));
            Gr.SetDividerPosition(dv, "P350 1160mm:1", 600);
            Check("divider pending", string.Join(";", Gr.Pending(dv).Dividers.Select(x => $"{x.Constraint} {x.From}->{x.To}")), "Mate:646 525->600");
            Gr.SaveDesign(dv, tmp);
            var dv2 = Gr.ReadDesign(tmp);
            var c1 = dv2.Components["P350 1160mm:1"];
            Check("divider design round trip", $"{c1.Position} {c1.OrigPosition} {c1.PosConstraint}", "600 525 Mate:646");
            var w = Gr.Build(Gr.ReadExportFiles(new[] { D("t6357.xml"), D("c6357.xml") }));
            Gr.SetFrameSize(w, 1, "W", 1600, true);
            Check("plan profiles", string.Join(" ; ", Gr.Pending(w).Profiles.Select(x => $"{x.From}->{x.To} x{x.Names.Count}")), "P150 1580mm->P150 1600mm x20 ; P350 1580mm->P350 1600mm x6 ; P350 1620mm gefräst->P350 1640mm gefräst x9");
            File.Delete(tmp);
            // reads the design files the PowerShell version saved, if present
            foreach (var f in new[] { "design.json", "div.json", "real.json" }.Where(f => File.Exists(D("../t/" + f))))
                Check($"reads PowerShell design {f}", Gr.ReadDesign(D("../t/" + f)).Frames.Count > 0, true);
        }

        // Synthetic GR read from Inventor with range boxes: length along assembly Y, width along X.
        static void TestGrReal()
        {
            var data = new GrData { Name = "Test GR", Source = "Inventor", SourcePath = "x" };
            GrComponent Add(string n, double[] box) { var c = Gr.NewComponent(n, ""); c.Box = box; data.Components[n] = c; return c; }
            int k = 0;
            foreach (var x in new[] { 0.0, 730 }) foreach (var y in new[] { 0.0, 1130 }) foreach (var z in new[] { 0.0, 530 }) { k++; Add($"APS Ecke Guß:{k}", new[] { x, y, z, x + 130, y + 130, z + 130 }); }
            foreach (var s in new[] { "P150 1000mm", "P150 600mm", "P150 400mm" }) for (int i = 1; i <= 4; i++) Add($"{s}:{i}", new double[] { 30, 130, 30, 100, 1130, 100 });
            Add("P350 600mm:1", new double[] { 130, 430, 300, 730, 480, 390 });
            Add("P350 600mm gefräst:1", new double[] { 130, 780, 300, 730, 830, 390 });
            Add("Joint:1", new double[] { 100, 430, 300, 150, 480, 360 });
            Add("Joint:2", new double[] { 100, 780, 300, 150, 830, 360 });
            int ci = 0;
            var corners = data.Components.Values.Where(c => c.Kind == "corner").ToList();
            foreach (var p in data.Components.Values.Where(c => c.Kind == "frameProfile").ToList())
                foreach (var cn in corners.Take(2)) data.Constraints.Add(new GrConstraint { Name = $"Mate:{++ci}", Type = "Mate", A = p.Name, B = cn.Name, Value = "0 mm" });
            foreach (var cn in corners) data.Constraints.Add(new GrConstraint { Name = $"Flush:{++ci}", Type = "Flush", A = "P150 1000mm:1", B = cn.Name, Value = "0 mm" });
            data.Constraints.Add(new GrConstraint { Name = "Mate:900", Type = "Mate", A = "P350 600mm:1", B = "P150 400mm:3", Value = "300 mm" });
            data.Constraints.Add(new GrConstraint { Name = "Mate:901", Type = "Mate", A = "P350 600mm gefräst:1", B = "P150 400mm:4", Value = "650 mm" });
            data.Constraints.Add(new GrConstraint { Name = "Mate:902", Type = "Mate", A = "Joint:1", B = "P150 400mm:1", Value = "0 mm" });
            data.Constraints.Add(new GrConstraint { Name = "Mate:903", Type = "Mate", A = "Joint:2", B = "P150 400mm:2", Value = "0 mm" });
            var m = Gr.Build(data);
            var f0 = m.Frames[0];
            Check("real: frame", $"{f0.Lx} x {f0.Wy} x {f0.Hz} map {string.Join(",", m.RealMap)}", "1000 x 600 x 400 map 1,0,2");
            Check("real: divider lines", string.Join(" ; ", GrDrawing.DividerLines(m).OrderBy(l => l.A[0]).Select(l => $"{l.Kind} along {l.Axis} at L={l.A[0]} W={l.A[1]}..{l.B[1]}")),
                  "divider along 1 at L=455 W=130..730 ; dividerMilled along 1 at L=805 W=130..730");
            Check("real: joints", string.Join(" ", GrDrawing.JointPoints(m).Select(p => $"{p[0]}/{p[1]}/{p[2]}").OrderBy(s => s)), "455/125/330 805/125/330");
            Gr.SetDividerPosition(m, "P350 600mm:1", 400);
            var moved = GrDrawing.DividerLines(m).First(l => l.Name == "P350 600mm:1");
            Check("real: moved divider", $"{moved.Kind} L={moved.A[0]}", "dividerMoved L=555");
            Gr.SetFrameSize(m, 1, "W", 800, false);
            var milled = GrDrawing.DividerLines(m).First(l => l.Name == "P350 600mm gefräst:1");
            Check("real: divider stretches with W", $"{milled.A[1]}..{milled.B[1]}", "130..930");
            var top = GrDrawing.Window(m, "Top", 900, 700);
            Check("real: spacing chain", string.Join(",", top.Where(s => s.Kind == "label" && System.Text.RegularExpressions.Regex.IsMatch(s.Text ?? "", @"^\d+$")).Select(s => s.Text)).Contains("485,250,385"), true);
            var tmp = Path.GetTempFileName();
            Gr.SaveDesign(m, tmp);
            Check("real: design keeps positions", string.Join(",", GrDrawing.DividerLines(Gr.ReadDesign(tmp)).OrderBy(l => l.A[0]).Select(l => l.A[0])), "555,805");
            File.Delete(tmp);
            if (Data != "") File.WriteAllText(D("cs-gr-real.svg"), Draw.Svg(GrDrawing.Window(m, "Isometric", 900, 700), 900, 700, "real"));
        }
    }
}
