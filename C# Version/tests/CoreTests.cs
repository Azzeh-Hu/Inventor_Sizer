// Core tests: the same cases and numbers as the PowerShell version's tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventorSizer.Core;
using J = InventorSizer.Core.Json;

namespace InventorSizer.Tests
{
    public static partial class Program
    {
        static Settings Set => new Settings { MinHoleGap = 10, Thickness = 1, SplitWidth = 500, MaxHolePitch = 400 };

        static Dictionary<string, double> V(double w, double l, double h, double nL, double nW) => new Dictionary<string, double>
        { ["Width"] = w, ["Length"] = l, ["Height"] = h, ["HoleEdge"] = 50, ["HoleZ"] = 20, ["HoleDia"] = 4.1, ["Clearance"] = 0.5, ["HolesL"] = nL, ["HolesW"] = nW };

        static string P(HoleLayout l, string n) => U.F(l.Params.First(p => p.Name == n).Value);

        static List<Standard> Stds() => Standards.Read(Path.Combine(Root, "standards"), Root);

        static void TestRules()
        {
            foreach (var c in new[] { (656.0, false, 3), (386, true, 2), (1616, false, 5), (726, true, 4), (1194, false, 4), (519, true, 4), (444, true, 2), (1197, false, 4), (100, false, 1), (500, false, 2), (900, true, 4), (1301, true, 6) })
                Check($"auto {c.Item1}", HoleRules.AutoCount(c.Item1, 50, 400, c.Item2), c.Item3);
            var tp = Stds().First(s => s.Id == "TP");
            var a = HoleRules.Layout(V(724, 1194, 42, 4, 2), Set, tp);
            Check("724 split", $"{a.Split} {a.InnerWidth} {a.InnerLength}", "2 360.5 1191");
            Check("724 pitchL", P(a, "HolePitchL"), "364.667");
            Check("724 width holes", string.Join(" ", a.Width.Positions.Select(U.F)), "50 313.5 410.5 674");
            Check("724 in pattern", $"{P(a, "InHolePitchW")} {P(a, "InHoleCountW")} {P(a, "InHoleMidOnW")} {P(a, "HoleMidOnW")} {P(a, "InSplit")}", "263.5 2 0 1 2");
            Check("724 errors", a.Errors.Count, 0);
            var fp = Stds().First(s => s.Id == "FP");
            var b = HoleRules.Layout(V(386, 656, 47, 3, 2), Set, fp);
            Check("386", $"{b.Split} {b.InnerWidth} {P(b, "InHolePitchW")} {P(b, "InHoleCountW")} {P(b, "HolePitchL")} notes={b.Notes.Count}", "1 383 286 2 278 notes=0");
            Check("500 not split", HoleRules.Layout(V(500, 656, 47, 3, 2), Set, fp).Split, 1);
            Check("501 split", HoleRules.Layout(V(501, 656, 47, 3, 2), Set, fp).Split, 2);
            Check("split 6 holes", HoleRules.Layout(V(724, 1194, 42, 4, 6), Set, tp).Errors.Count, 1);
            Check("tp min holes", HoleRules.Layout(V(400, 1194, 42, 1, 2), Set, tp).Errors.Count, 1);
            Check("height note", HoleRules.Layout(V(400, 1194, 47, 4, 2), Set, tp).Notes.Count, 1);
            var never = Set; never.SplitWidth = 0;
            Check("never split", HoleRules.Layout(V(724, 1194, 42, 4, 2), never, tp).Split, 1);
            var bom = Standards.GetBom(tp, V(724, 1194, 42, 4, 2), 1, "6357-3 TP-724x1194x42", a);
            Check("bom", string.Join(" ; ", bom.Rows.Select(r => $"{r.Part}|{r.Qty}|{r.Size}")), "6357-3 724x1194x42-Au|1|724 × 1194 × 42 ; 6357-3 724x1194x42-In-2T|2|360.5 × 1191 × 41");
        }

        static void TestStandards()
        {
            var stds = Stds();
            Check("types", string.Join(",", stds.Select(s => s.Id)), "FP,RP,TP");
            var p = Standards.Split(stds, "6357-3 TP-519x1194x42");
            Check("lead", $"[{p.Lead}][{p.Prefix}][{p.Token}][{p.Tail}] {p.Standard.Id}", "[6357-3 ][TP-][519x1194x42][] TP");
            Check("by height", Standards.ForName(stds, "6357-3 571x996x44").Id, "RP");
            Check("swap", Standards.WithPrefix(stds, "6357-3 TP-519x1194x42", "RP-"), "6357-3 RP-519x1194x42");
            var tp = stds.First(s => s.Id == "TP"); var fp = stds.First(s => s.Id == "FP");
            var o = Standards.Split(stds, "6357-3 TP-519x1194x42"); var n = Standards.Split(stds, "7000-1 TP-724x1300x42");
            Check("part name", Standards.ConvertText("6357-3 519x1194x42-Au", o, n), "7000-1 724x1300x42-Au");
            Check("split name", Standards.ConvertSplitName(tp, "7000-1 724x1300x42-In", 2), "7000-1 724x1300x42-In-2T");
            Check("unsplit name", Standards.ConvertSplitName(tp, "a 400x1300x42-In-2T", 1), "a 400x1300x42-In");
            Check("fp split suffix", Standards.ConvertSplitName(fp, "600x700x47-In", 2), "600x700x47-In-2T");
            Check("add lead", Standards.ConvertText("386x656x47-Au", Standards.Split(stds, "FP-386x656x47"), Standards.Split(stds, "99 FP-400x656x47")), "99 400x656x47-Au");
            Check("suffixes", string.Join(",", tp.PartSuffixes("inner")), "-In-2T,-In");
            Check("tp defaults", $"{tp.Defaults["HoleZ"]} {tp.Height} {tp.MinHolesL}", "20 42 2");
            Check("tp setup roles", tp.Setup != null && tp.Setup.Obj("outer") != null && tp.Setup.Obj("inner") != null, true);
            Check("tp hardware", tp.Hardware.Holes.Count, 20);
            // master lookup and saving, in a temporary app folder
            var tmp = Path.Combine(Path.GetTempPath(), "isz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tmp, "standards")); Directory.CreateDirectory(Path.Combine(tmp, "masters", "TP"));
            File.Copy(Path.Combine(Root, "standards", "TP.json"), Path.Combine(tmp, "standards", "TP.json"));
            File.WriteAllText(Path.Combine(tmp, "masters", "TP", "6357-3 TP-724x1194x42.iam"), "x");
            var ms = Standards.Read(Path.Combine(tmp, "standards"), tmp);
            Check("master from masters folder", Path.GetFileName(ms[0].Master), "6357-3 TP-724x1194x42.iam");
            Standards.SetMaster(ms[0], Path.Combine(tmp, "masters", "TP", "6357-3 TP-724x1194x42.iam"), tmp);
            var again = Standards.Read(Path.Combine(tmp, "standards"), tmp)[0];
            Check("master stored relative, rest kept", $"{Path.GetFileName(again.Master)} holes {again.Hardware.Holes.Count} setup {again.Setup != null}", "6357-3 TP-724x1194x42.iam holes 20 setup True");
            Directory.Delete(tmp, true);
        }

        static void TestHardware()
        {
            var tp = Stds().First(s => s.Id == "TP");
            var hw = Hardware.Place(tp.Hardware, 724, 1194, 42);
            var sketch = new[] { (340.0, 358.0, 9.0), (340, 436, 9), (340, -436, 9), (340, -358, 9), (-354, 499, 4.1), (-326, 475, 4.1) };
            Check("template matches the master's sketch", sketch.Count(s => hw.Any(h => Math.Abs(h.X - (s.Item1 + 362)) < 0.01 && Math.Abs(h.Y - (s.Item2 + 597)) < 0.01 && h.Dia == s.Item3)), 6);
            foreach (var c in new[] { (2, 2, "974", "794", 20), (3, 2, "487", "794", 28), (4, 3, "324.667", "397", 38) })
            {
                var counts = new Dictionary<string, double> { ["hinge"] = c.Item1, ["handle"] = c.Item2 };
                var p = Hardware.Pitches(tp.Hardware, 1194, counts);
                var h = Hardware.Place(tp.Hardware, 724, 1194, 42, counts);
                Check($"counts {c.Item1}/{c.Item2}", $"{U.F(p["hinge"].Pitch)} {U.F(p["handle"].Pitch)} holes {h.Count} bad {h.Count(x => x.Bad)}", $"{c.Item3} {c.Item4} holes {c.Item5} bad 0");
            }
            Check("pitch params", string.Join(",", Hardware.PitchParams(tp, 1194, new Dictionary<string, double> { ["hinge"] = 3, ["handle"] = 2 }).Select(x => $"{x.Name}={U.F(x.Value)}")), "HingePitch=487,HandlePitch=794");
            int shortest = 0;
            for (int L = 700; L >= 200; L--) if (Hardware.Place(tp.Hardware, 519, L, 42).Any(x => x.Overlap)) { shortest = L + 1; break; }
            Check("shortest TP without collisions", shortest, 487);
            // learning from circles: length along assembly X, width along Y
            Circle C(double len, double wid, double z, double[] n, double d) => new Circle { Center = new[] { len, wid, z }, Normal = n, Dia = d };
            var up = new double[] { 0, 0, 1 }; var side = new double[] { 0, 1, 0 };
            var circles = new[] { C(161, 22, 0, up, 9), C(161, 22, 1, up, 9), C(1033, 22, 0, up, 9), C(98, 508, 0, up, 4.1), C(1096, 508, 0, up, 4.1), C(300, 318, 0, up, 6),
                                  C(50, 0, 20, side, 4.1), C(50, 1.5, 20, side, 4.1), C(300, 0, 30, side, 6), C(500, 100, 21, new[] { 0.7, 0, 0.7 }, 5) };
            var tpl = Hardware.FromCircles(circles, new double[] { 0, 0, 0 }, new double[] { 1194, 519, 42 }, 519, 1194, 42, 4.1, 20);
            Check("template", string.Join(" ; ", tpl.Holes.Select(x => $"{x.Wall}/{x.Side}/{U.F(x.Dia)}/{x.U.From}:{U.F(x.U.Value)}/{x.V.From}:{U.F(x.V.Value)}")),
                  "face/bottom/9/start:22/start:161 ; face/bottom/9/start:22/end:161 ; face/bottom/4.1/end:11/start:98 ; face/bottom/4.1/end:11/end:98 ; face/bottom/6/mid:58.5/start:300 ; length/x0/6/start:300/start:30");
            Check("rule skipped", tpl.RuleHolesSkipped, 1);
            Throws("size mismatch", () => Hardware.FromCircles(circles, new double[] { 0, 0, 0 }, new double[] { 1194, 519, 42 }, 724, 1194, 42, 4.1, 20));
            var placed = Hardware.Place(tpl, 724, 1300, 42);
            Check("placed", string.Join(" ", placed.Select(x => $"{U.F(x.X)},{U.F(x.Y)},{U.F(x.Z)},{x.Axis}")), "22,161,0,z 22,1139,0,z 713,98,0,z 713,1202,0,z 420.5,300,0,z 0,300,30,x");
            Check("bad at small size", Hardware.Notes(Hardware.Place(tpl, 100, 300, 42)).Count, 1);
        }

        static void TestDrawings()
        {
            var tp = Stds().First(s => s.Id == "TP");
            var v = V(724, 1300, 42, 4, 4);
            var lay = HoleRules.Layout(v, Set, tp);
            var hw = Hardware.Place(tp.Hardware, 724, 1300, 42);
            var pv = PanelValues.Of(v, 1, hw);
            foreach (var view in Draw.Views)
            {
                var shapes = PanelDrawing.Window(pv, lay, view, 900, 700, false, Standards.GetBom(tp, v, 1, "TP-724x1300x42", lay));
                Check($"panel view {view} has shapes", shapes.Count > 30, true);
            }
            int hwIso = PanelDrawing.Window(pv, lay, "Isometric", 900, 700, false, null).Count(s => s.Kind.StartsWith("hw"));
            Check("hardware in isometric", hwIso > 0, true);
            var svg = Draw.Svg(PanelDrawing.Sheet(pv, lay, Standards.GetBom(tp, v, 1, "TP-724x1300x42", lay), true), 1400, 990, "t");
            Check("svg", svg.Contains("<polygon") && svg.Contains("</svg>") && svg.Contains("ff8c00"), true);
            if (Data != "") File.WriteAllText(Path.Combine(Data, "cs-panel-sheet.svg"), svg);
        }

        static void TestJson()
        {
            var j = (JsonObject)J.Parse("{\"a\": 1.5, \"b\": [1, \"x\", true, null], \"c\": {\"d\": \"\\u00e4\\n\"}}");
            Check("json read", $"{j.Num("a")} {j.Arr("b").Count} {j.Obj("c").Str("d")}", "1.5 4 ä\n");
            var again = (JsonObject)J.Parse(J.Write(j));
            Check("json round trip", J.Write(again) == J.Write(j), true);
            foreach (var f in Directory.GetFiles(Path.Combine(Root, "standards"), "*.json"))
                Check($"standards file {Path.GetFileName(f)} reads and writes", J.Parse(J.Write(J.Parse(File.ReadAllText(f)))) is JsonObject, true);
        }
    }
}
