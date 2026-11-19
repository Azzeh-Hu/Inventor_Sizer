// GR (Geraeterahmen, the unit frame): frames, profiles, corners, dividers and panels, built from
// component names and constraints (iLogic exports or Inventor), optionally with real positions.
//   - A frame (box) is a cuboid of 8 corners (APS Ecke) and 12 frame profiles (P150), 4 per size.
//   - Boxes touch corner to corner; dividers (P350) hang on the frames through joints; panels
//     mate to the profiles of one box with a 2 mm gap.
//   - Outer size = profile + 2 x 130 (corner cube); panel length = profile + 36.
// GR coordinates: 0 = length (X), 1 = width (Y), 2 = height (Z), mm.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace InventorSizer.Core
{
    public class Follow { public string Dim; public double Offset; public int Part; }

    public class Geo { public double[] Min, Max, Size0; public double? Pos0; }

    public class GrComponent
    {
        public string Name, File = "", Base, OrigBase, Kind;
        public double Length;
        public int Frame;
        public List<Follow> Follow = new List<Follow>();
        public double? Position, OrigPosition;
        public string PosConstraint = "", PosPartner = "";
        public double[] Box;   // range box from Inventor, assembly axes, mm: min xyz, max xyz
        public Geo Geo;
    }

    public class GrConstraint { public string Name = "", Type = "", A = "", B = "", Value = ""; public bool Suppressed; }

    public class GrData
    {
        public string Name = "", Source = "", SourcePath = "";
        public Dictionary<string, GrComponent> Components = new Dictionary<string, GrComponent>();
        public List<GrConstraint> Constraints = new List<GrConstraint>();
    }

    public class GrFrame
    {
        public int Id, No, Parent, Dir = 1;
        public string Place = "loose";
        public double[] Sizes;
        public double Lx, Wy, Hz, X, Y, Z;
        public List<string> Panels = new List<string>(), Dividers = new List<string>(), Other = new List<string>();
        public List<int> Neighbours = new List<int>();
        public double[] Pos => new[] { X, Y, Z };
        public double[] Size => new[] { Lx, Wy, Hz };
        public double Get(string dim) => dim == "L" ? Lx : dim == "W" ? Wy : Hz;
        public void Set(string dim, double v) { if (dim == "L") Lx = v; else if (dim == "W") Wy = v; else Hz = v; }
        public string Label => "Box " + (No - 1);
    }

    public class GrModel
    {
        public string Name = "", Source = "", SourcePath = "";
        public List<GrFrame> Frames = new List<GrFrame>();
        public Dictionary<string, GrComponent> Components = new Dictionary<string, GrComponent>();
        public List<int> Order = new List<int>();
        public double Length, OuterWidth, OuterHeight;
        public int Corners, Joints, ConstraintCount;
        public List<string> Unplaced = new List<string>(), Warnings = new List<string>(), Removed = new List<string>();
        public bool Edited;
        public int[] RealMap;
        public GrFrame Frame(int no) => Frames.FirstOrDefault(f => f.No == no) ?? throw new SizerException($"There is no Box {no - 1}.");
        public GrFrame ById(int id) => Frames.FirstOrDefault(f => f.Id == id);
    }

    public class GrPending
    {
        public List<(string Kind, string From, string To, double Length, List<string> Names)> Profiles = new List<(string, string, string, double, List<string>)>();
        public List<(string Name, string From, string To)> Panels = new List<(string, string, string)>();
        public List<(string Name, string Constraint, double From, double To)> Dividers = new List<(string, string, double, double)>();
        public List<string> Blocked = new List<string>();
    }

    public static class Gr
    {
        public static double CornerAdd = 260;   // outer = profile + 2 corners of 130
        public static double PanelAdd = 36;
        public static Dictionary<string, string> LengthParams = new Dictionary<string, string> { ["frameProfile"] = "d89", ["divider"] = "d0" };
        public static List<(string Kind, Regex Pattern)> PartRules = new List<(string, Regex)>
        {
            ("corner", new Regex("^APS Ecke")), ("joint", new Regex(@"^Joint\d*$")), ("frameProfile", new Regex("^P150 ")), ("divider", new Regex(@"^P\d+ \d")),
            ("panel", new Regex(@"(?:^|\s)(FP|RP|TP)-\d")), ("base", new Regex("Grundrahmen")), ("damper", new Regex("Jalousie")), ("stub", new Regex("D.mmstutzen|Stutzen")),
        };

        // standards\GR.json: part-name rules, length parameters, corner size.
        public static void LoadRules(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var j = (JsonObject)Json.Parse(File.ReadAllText(path));
                var rules = j.Arr("rules").OfType<JsonObject>().Select(r => (r.Str("kind"), new Regex(r.Str("pattern")))).ToList();
                if (rules.Count > 0) PartRules = rules;
                var lp = j.Obj("lengthParameters");
                if (lp != null) foreach (var k in lp.OrderedKeys) LengthParams[k] = lp.Str(k);
                CornerAdd = j.Num("cornerAdd", CornerAdd); PanelAdd = j.Num("panelAdd", PanelAdd);
                Log.Write($"GR part rules: {PartRules.Count} from {path}");
            }
            catch (Exception e) { Log.Write($"GR part rules in {path} could not be read: {e.Message}"); }
        }

        public static string Kind(string baseName)
        {
            foreach (var r in PartRules) if (r.Pattern.IsMatch(baseName)) return r.Kind;
            return "other";
        }

        public static double LengthOf(string baseName)
        {
            var m = Regex.Match(baseName ?? "", @"(\d+(?:[.,]\d+)?)\s*mm");
            return m.Success ? U.P(m.Groups[1].Value) : 0;
        }

        public static double? Mm(string text)
        {
            var m = Regex.Match(text ?? "", @"^\s*(-?\d+(?:[.,]\d+)?)\s*mm\s*$");
            return m.Success ? U.P(m.Groups[1].Value) : (double?)null;
        }

        public static GrComponent NewComponent(string name, string file)
        {
            string b = !string.IsNullOrEmpty(file) ? U.LeafName(file) : Regex.Replace(name, @":\d+$", "");
            return new GrComponent { Name = name, File = file ?? "", Base = b, OrigBase = b, Kind = Kind(b), Length = LengthOf(b) };
        }

        public static string Label(GrFrame f) => f.Label;
        public static int NoFromLabel(string label) => int.Parse(Regex.Match(label ?? "", @"\d+").Value, U.Inv) + 1;

        // ------------------------------------------------------------------ reading exports

        public static GrData ReadExportFiles(IEnumerable<string> paths)
        {
            var d = new GrData();
            var names = new List<string>();
            foreach (var path in paths)
            {
                names.Add(Path.GetFileName(path));
                var xml = new XmlDocument();
                xml.Load(path);
                var root = xml.DocumentElement;
                if (d.SourcePath == "" && root.GetAttribute("source") != "") d.SourcePath = root.GetAttribute("source");
                switch (root.Name)
                {
                    case "AssemblyTree":
                        foreach (XmlNode c in root.SelectNodes("Component"))
                        {
                            int level = int.Parse(c.SelectSingleNode("Level").InnerText, U.Inv);
                            string name = c.SelectSingleNode("Tree").InnerText.Trim();
                            if (level == 0) { if (d.Name == "") d.Name = name; continue; }
                            if (level == 1) d.Components[name] = NewComponent(name, c.SelectSingleNode("FileName")?.InnerText);
                        }
                        break;
                    case "AssemblyExport":
                        var t = (XmlElement)root.SelectSingleNode("Component");
                        if (d.Name == "") d.Name = t.GetAttribute("name");
                        foreach (XmlElement c in t.SelectNodes("Components/Component")) d.Components[c.GetAttribute("name")] = NewComponent(c.GetAttribute("name"), c.GetAttribute("file"));
                        foreach (XmlElement k in t.SelectNodes("Constraints/Constraint"))
                            d.Constraints.Add(new GrConstraint { Name = k.GetAttribute("name"), Type = k.GetAttribute("type"), A = k.GetAttribute("component1"), B = k.GetAttribute("component2"), Value = k.GetAttribute("value"), Suppressed = k.GetAttribute("suppressed") == "True" });
                        break;
                    case "AssemblyConstraints":
                        string asmName = U.LeafName(root.GetAttribute("source"));
                        if (d.Name == "") d.Name = asmName;
                        foreach (XmlNode k in root.SelectNodes("Constraint"))
                        {
                            if (k.SelectSingleNode("Assembly")?.InnerText != asmName) continue;   // sub-assembly constraints
                            d.Constraints.Add(new GrConstraint
                            {
                                Name = k.SelectSingleNode("Name")?.InnerText ?? "", Type = k.SelectSingleNode("Type")?.InnerText ?? "",
                                A = k.SelectSingleNode("Component1")?.InnerText ?? "", B = k.SelectSingleNode("Component2")?.InnerText ?? "",
                                Value = k.SelectSingleNode("OffsetAngle")?.InnerText ?? "", Suppressed = k.SelectSingleNode("Suppressed")?.InnerText == "True"
                            });
                        }
                        break;
                    default: throw new SizerException($"{Path.GetFileName(path)} is not a GR export (root element '{root.Name}').");
                }
            }
            if (d.Constraints.Count == 0) throw new SizerException("No constraints found. Load the constraints export too (or the export that contains them).");
            foreach (var k in d.Constraints)
                foreach (var n in new[] { k.A, k.B })
                    if (!string.IsNullOrEmpty(n) && !d.Components.ContainsKey(n)) d.Components[n] = NewComponent(n, "");
            d.Source = string.Join(", ", names);
            return d;
        }

        // ------------------------------------------------------------------ building the model

        static double CommonSize(IEnumerable<double[]> lists)
        {
            var count = new Dictionary<double, int>();
            foreach (var l in lists) foreach (var v in l.Distinct()) count[v] = count.TryGetValue(v, out var c) ? c + 1 : 1;
            return count.Count == 0 ? 0 : count.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
        }

        static double[] RemoveOne(double[] values, double v)
        {
            var l = values.ToList(); int i = l.IndexOf(v); if (i >= 0) l.RemoveAt(i); return l.ToArray();
        }

        public static GrModel Build(GrData data)
        {
            var comps = data.Components;
            string KindOf(string n) => comps.TryGetValue(n, out var c) ? c.Kind : "other";
            var active = data.Constraints.Where(c => !c.Suppressed && !string.IsNullOrEmpty(c.A) && !string.IsNullOrEmpty(c.B)).ToList();
            var warnings = new List<string>();

            // Frames: connected groups of corners and frame profiles.
            var adj = new Dictionary<string, HashSet<string>>();
            foreach (var c in active)
            {
                string ka = KindOf(c.A), kb = KindOf(c.B);
                if ((ka == "corner" && kb == "frameProfile") || (ka == "frameProfile" && kb == "corner"))
                {
                    if (!adj.ContainsKey(c.A)) adj[c.A] = new HashSet<string>();
                    if (!adj.ContainsKey(c.B)) adj[c.B] = new HashSet<string>();
                    adj[c.A].Add(c.B); adj[c.B].Add(c.A);
                }
            }
            var owner = new Dictionary<string, int>();
            var frames = new List<GrFrame>();
            foreach (var start in adj.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (owner.ContainsKey(start)) continue;
                int id = frames.Count + 1;
                var stack = new Stack<string>(); stack.Push(start);
                var members = new List<string>();
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (owner.ContainsKey(n)) continue;
                    owner[n] = id; members.Add(n);
                    foreach (var m in adj[n]) if (!owner.ContainsKey(m)) stack.Push(m);
                }
                var profiles = members.Where(m => comps[m].Kind == "frameProfile").ToList();
                var corners = members.Where(m => comps[m].Kind == "corner").ToList();
                var sizes = new List<double>();
                foreach (var g in profiles.GroupBy(p => comps[p].Length).OrderBy(g => g.Key))
                    for (int k = 0; k < Math.Round(g.Count() / 4.0, MidpointRounding.AwayFromZero); k++) sizes.Add(g.Key);
                if (corners.Count != 8 || profiles.Count != 12 || sizes.Count != 3)
                {
                    warnings.Add($"Frame {id} is not a closed cuboid ({corners.Count} corners, {profiles.Count} profiles) - its sizes are a guess.");
                    while (sizes.Count < 3) sizes.Add(sizes.Count > 0 ? sizes[sizes.Count - 1] : 0);
                    sizes = sizes.Take(3).ToList();
                }
                frames.Add(new GrFrame { Id = id, Sizes = sizes.ToArray() });
            }
            if (frames.Count == 0) throw new SizerException("No frames found: no constraints between corners (APS Ecke) and frame profiles (P150).");
            var byId = frames.ToDictionary(f => f.Id);
            Log.Dev($"GR build: {frames.Count} frames found");

            // Frame links: corner-to-corner constraints between 2 frames.
            foreach (var c in active)
            {
                if (owner.TryGetValue(c.A, out var oa) && owner.TryGetValue(c.B, out var ob) && oa != ob && KindOf(c.A) == "corner" && KindOf(c.B) == "corner")
                    if (!byId[oa].Neighbours.Contains(ob)) { byId[oa].Neighbours.Add(ob); byId[ob].Neighbours.Add(oa); }
            }

            // Everything else belongs to the frame it is constrained to most often; the frame is
            // passed along chains (divider on a joint, panel next to a panel).
            var assigned = new Dictionary<string, int>(owner);
            for (int round = 0; round < 10; round++)
            {
                var votes = new Dictionary<string, Dictionary<int, int>>();
                void Vote(string n, int f) { if (!votes.TryGetValue(n, out var v)) votes[n] = v = new Dictionary<int, int>(); v[f] = v.TryGetValue(f, out var x) ? x + 1 : 1; }
                foreach (var c in active)
                {
                    bool ha = assigned.TryGetValue(c.A, out var fa), hb = assigned.TryGetValue(c.B, out var fb);
                    if (ha && !hb) Vote(c.B, fa);
                    if (hb && !ha) Vote(c.A, fb);
                }
                if (votes.Count == 0) break;
                foreach (var kv in votes) assigned[kv.Key] = kv.Value.OrderByDescending(x => x.Value).First().Key;
            }
            var unplaced = new List<string>();
            foreach (var n in comps.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var k = comps[n].Kind;
                if (k == "corner" || k == "frameProfile" || k == "joint") continue;
                if (!assigned.TryGetValue(n, out var fid)) { unplaced.Add(n); continue; }
                var f = byId[fid];
                if (k == "panel") f.Panels.Add(n); else if (k == "divider") f.Dividers.Add(n); else f.Other.Add(n);
            }
            Log.Dev($"GR build: {assigned.Count} parts assigned to frames");

            // Divider positions: mated to a frame profile with an offset other than the -25 set-back.
            foreach (var c in active)
            {
                if (!Regex.IsMatch(c.Type, "Mate|Flush")) continue;
                foreach (var pair in new[] { (c.A, c.B), (c.B, c.A) })
                {
                    if (!comps.TryGetValue(pair.Item1, out var d) || !comps.TryGetValue(pair.Item2, out var o)) continue;
                    if (d.Kind != "divider" || o.Kind != "frameProfile" || d.PosConstraint != "") continue;
                    var mm = Mm(c.Value);
                    if (mm == null || mm == -25 || mm == 0) continue;
                    d.Position = mm; d.OrigPosition = mm; d.PosConstraint = c.Name; d.PosPartner = pair.Item2;
                }
            }

            // Orientation: width = the size most frames share, height = the next most shared one.
            double width = frames.Count == 1 ? frames[0].Sizes.OrderBy(x => x).ElementAt(1) : CommonSize(frames.Select(f => f.Sizes));
            var rests = frames.ToDictionary(f => f.Id, f => RemoveOne(f.Sizes, width));
            double height = CommonSize(rests.Values);
            bool heightShared = frames.Count(f => rests[f.Id].Contains(height)) >= 2;
            if (frames.Count == 1) { heightShared = true; height = frames[0].Sizes.OrderBy(x => x).First(); }
            foreach (var f in frames)
            {
                var r = rests[f.Id];
                f.Wy = f.Sizes.Contains(width) ? width : f.Sizes.Max();
                if (r.Length < 2) r = r.Concat(new[] { 0.0 }).ToArray();
                if (heightShared && r.Contains(height)) { f.Hz = height; f.Lx = RemoveOne(r, height)[0]; }
                else { f.Lx = r.Min(); f.Hz = r.Max(); }
            }

            // Placement relations (kept, so edits re-use them).
            var root = frames.OrderByDescending(f => f.Neighbours.Count).ThenBy(f => f.Id).First();
            root.Place = "root"; root.Dir = 0;
            var order = new List<int> { root.Id };
            var queue = new Queue<int>(); queue.Enqueue(root.Id);
            while (queue.Count > 0)
            {
                var a = byId[queue.Dequeue()];
                int nextSide = a.Dir != 0 ? a.Dir : 1;
                foreach (var nid in a.Neighbours)
                {
                    var b = byId[nid];
                    if (b.Place != "loose") continue;
                    b.Parent = a.Id;
                    if (a.Lx == b.Lx && a.Wy == b.Wy && a.Hz != b.Hz) { b.Place = "stack"; b.Dir = a.Dir; }
                    else { b.Place = "side"; b.Dir = nextSide; if (a.Dir == 0) nextSide = -nextSide; }
                    order.Add(nid); queue.Enqueue(nid);
                }
            }
            foreach (var f in frames.Where(f => f.Place == "loose")) { order.Add(f.Id); warnings.Add($"Frame {f.Id} is not connected to the other frames - drawn separately."); }

            foreach (var kv in comps) { kv.Value.Frame = assigned.TryGetValue(kv.Key, out var fid) ? fid : 0; kv.Value.Follow = new List<Follow>(); }
            foreach (var f in frames) SetFollowRules(f, comps);

            var model = new GrModel
            {
                Name = data.Name, Source = data.Source, SourcePath = data.SourcePath, Frames = frames, Components = comps, Order = order,
                Unplaced = unplaced, Warnings = warnings, ConstraintCount = active.Count
            };
            SetRealGeometry(model);
            UpdateLayout(model);
            int no = 0;
            foreach (var f in frames.OrderBy(f => f.Y).ThenBy(f => f.X).ThenBy(f => f.Z).ToList()) f.No = ++no;
            UpdateLayout(model);
            Log.Dev("GR build: layout done" + (model.RealMap != null ? " (from the real part positions)" : " (inferred from the constraints)"));
            return model;
        }

        static Follow Nearest(GrFrame f, double len, double tol)
        {
            Follow best = null;
            foreach (var d in new[] { ("L", f.Lx), ("W", f.Wy), ("H", f.Hz) })
            {
                double off = len - d.Item2;
                if (Math.Abs(off) <= tol && (best == null || Math.Abs(off) < Math.Abs(best.Offset))) best = new Follow { Dim = d.Item1, Offset = off };
            }
            return best;
        }

        // A frame profile, divider or panel side follows a frame size when its length is that size
        // plus a small constant: profiles exactly (4 per size), dividers and panel sides within 60 mm.
        public static void SetFollowRules(GrFrame f, Dictionary<string, GrComponent> comps)
        {
            var quota = new Dictionary<string, int> { ["L"] = 4, ["W"] = 4, ["H"] = 4 };
            foreach (var c in comps.Values.Where(c => c.Frame == f.Id).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (c.Kind == "frameProfile")
                {
                    foreach (var d in new[] { ("L", f.Lx), ("W", f.Wy), ("H", f.Hz) })
                        if (c.Length == d.Item2 && quota[d.Item1] > 0) { quota[d.Item1]--; c.Follow = new List<Follow> { new Follow { Dim = d.Item1 } }; break; }
                }
                else if (c.Kind == "divider") { var r = Nearest(f, c.Length, 60); if (r != null) c.Follow = new List<Follow> { r }; }
                else if (c.Kind == "panel")
                {
                    var m = U.SizeRegex.Match(c.Base);
                    if (!m.Success) continue;
                    c.Follow = new List<Follow>();
                    for (int part = 1; part <= 2; part++) { var r = Nearest(f, U.P(m.Groups[part].Value), 60); if (r != null) { r.Part = part; c.Follow.Add(r); } }
                }
            }
        }

        // With real positions (range boxes): which assembly axis is length / width / height, the
        // placement directions between boxes, and where dividers and joints sit inside their box.
        public static void SetRealGeometry(GrModel m)
        {
            var real = new Dictionary<int, (double[] Min, double[] Max)>();
            foreach (var f in m.Frames)
            {
                var boxes = m.Components.Values.Where(c => c.Frame == f.Id && (c.Kind == "corner" || c.Kind == "frameProfile") && c.Box != null).ToList();
                if (boxes.Count < 8) continue;
                real[f.Id] = (Enumerable.Range(0, 3).Select(k => boxes.Min(b => b.Box[k])).ToArray(), Enumerable.Range(0, 3).Select(k => boxes.Max(b => b.Box[k + 3])).ToArray());
            }
            var first = m.Frames.FirstOrDefault(f => real.ContainsKey(f.Id));
            if (first == null) return;
            var ext = Enumerable.Range(0, 3).Select(k => real[first.Id].Max[k] - real[first.Id].Min[k]).ToArray();
            var want = new[] { first.Lx + CornerAdd, first.Wy + CornerAdd, first.Hz + CornerAdd };
            var map = new[] { -1, -1, -1 }; var used = new bool[3];
            foreach (int g in new[] { 2, 0, 1 })
            {
                int best = -1; double diff = double.MaxValue;
                for (int a = 0; a < 3; a++) if (!used[a] && Math.Abs(ext[a] - want[g]) < diff) { best = a; diff = Math.Abs(ext[a] - want[g]); }
                if (diff > 5) { Log.Dev($"GR: real frame size {string.Join(" x ", ext.Select(U.F))} does not match profile + corners - using the inferred layout"); return; }
                map[g] = best; used[best] = true;
            }
            m.RealMap = map;
            double[] ToGr(double[] p) => new[] { p[map[0]], p[map[1]], p[map[2]] };
            double[] Centre(int id) => ToGr(Enumerable.Range(0, 3).Select(k => (real[id].Min[k] + real[id].Max[k]) / 2).ToArray());
            foreach (var f in m.Frames)
            {
                if ((f.Place != "side" && f.Place != "stack" && f.Place != "front") || !real.ContainsKey(f.Id) || !real.ContainsKey(f.Parent)) continue;
                var cb = Centre(f.Id); var ca = Centre(f.Parent);
                var d = new[] { cb[0] - ca[0], cb[1] - ca[1], cb[2] - ca[2] };
                var ad = d.Select(Math.Abs).ToArray();
                int k = Array.IndexOf(ad, ad.Max());
                f.Place = new[] { "side", "front", "stack" }[k];
                f.Dir = d[k] >= 0 ? 1 : -1;
            }
            foreach (var c in m.Components.Values)
            {
                if ((c.Kind != "divider" && c.Kind != "joint") || c.Box == null || !real.ContainsKey(c.Frame)) continue;
                var f = m.ById(c.Frame);
                var o = ToGr(real[c.Frame].Min);
                var a = ToGr(new[] { c.Box[0], c.Box[1], c.Box[2] }); var b = ToGr(new[] { c.Box[3], c.Box[4], c.Box[5] });
                c.Geo = new Geo
                {
                    Min = Enumerable.Range(0, 3).Select(i => Math.Min(a[i], b[i]) - o[i]).ToArray(),
                    Max = Enumerable.Range(0, 3).Select(i => Math.Max(a[i], b[i]) - o[i]).ToArray(),
                    Size0 = f.Size, Pos0 = c.Kind == "divider" ? c.Position : null
                };
            }
        }

        // Positions (mm) from the frame sizes and placement relations; also the overall size.
        public static void UpdateLayout(GrModel m)
        {
            var byId = m.Frames.ToDictionary(f => f.Id);
            double Outer(double v) => v + CornerAdd;
            var loose = new List<GrFrame>();
            foreach (var id in m.Order)
            {
                if (!byId.TryGetValue(id, out var b)) continue;
                if (b.Place != "root" && !byId.ContainsKey(b.Parent)) { loose.Add(b); continue; }
                switch (b.Place)
                {
                    case "root": b.X = b.Y = b.Z = 0; break;
                    case "stack": { var a = byId[b.Parent]; b.X = a.X; b.Y = a.Y; b.Z = a.Z + Outer(a.Hz); break; }
                    case "front": { var a = byId[b.Parent]; b.X = a.X; b.Z = a.Z; b.Y = b.Dir >= 0 ? a.Y + Outer(a.Wy) : a.Y - Outer(b.Wy); break; }
                    case "side": { var a = byId[b.Parent]; b.Y = a.Y; b.Z = a.Z; b.X = b.Dir > 0 ? a.X + Outer(a.Lx) : a.X - Outer(b.Lx); break; }
                    default: loose.Add(b); break;
                }
            }
            foreach (var f in loose)
            {
                double end = m.Frames.Where(x => x.Place != "loose" && !loose.Contains(x)).Select(x => x.X + Outer(x.Lx)).DefaultIfEmpty(0).Max();
                f.X = end + 200; f.Y = 0; f.Z = 0;
            }
            if (m.Frames.Count == 0) return;
            double minX = m.Frames.Min(f => f.X), minY = m.Frames.Min(f => f.Y);
            foreach (var f in m.Frames) { f.X -= minX; f.Y -= minY; }
            m.Length = m.Frames.Max(f => f.X + Outer(f.Lx));
            m.OuterWidth = m.Frames.Max(f => f.Y + Outer(f.Wy));
            m.OuterHeight = m.Frames.Max(f => f.Z + Outer(f.Hz));
            m.Corners = m.Components.Values.Count(c => c.Kind == "corner");
            m.Joints = m.Components.Values.Count(c => c.Kind == "joint");
            m.Frames = m.Frames.OrderBy(f => f.No).ToList();
        }

        // ------------------------------------------------------------------ editing

        static void SetComponentLength(GrComponent c, int part, double len)
        {
            string txt = U.F(len);
            if (c.Kind == "panel")
            {
                var m = U.SizeRegex.Match(c.Base);
                if (!m.Success) return;
                var v = new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value };
                v[part - 1] = txt;
                c.Base = c.Base.Substring(0, m.Index) + string.Join("x", v) + c.Base.Substring(m.Index + m.Length);
            }
            else
            {
                c.Length = len;
                c.Base = new Regex(@"\d+(?:[.,]\d+)?(?=\s*mm)").Replace(c.Base, txt, 1);
            }
        }

        // One size of a box (profile length). Shared: every box with the same width (height)
        // changes too. Profiles, dividers and panel sides that follow the size change with it.
        public static List<string> SetFrameSize(GrModel m, int no, string dim, double value, bool shared)
        {
            if (value <= 0) throw new SizerException("A frame size must be greater than 0.");
            var f = m.Frame(no);
            double old = f.Get(dim);
            if (old == value) return new List<string>();
            var targets = shared && dim != "L" ? m.Frames.Where(x => x.Get(dim) == old).ToList() : new List<GrFrame> { f };
            foreach (var t in targets)
            {
                t.Set(dim, value);
                foreach (var c in m.Components.Values.Where(c => c.Frame == t.Id))
                    foreach (var r in c.Follow.Where(r => r.Dim == dim)) SetComponentLength(c, r.Part, value + r.Offset);
            }
            m.Edited = true;
            UpdateLayout(m);
            return targets.Select(t => t.Label).ToList();
        }

        public static void SwitchAxes(GrModel m, string a, string b)
        {
            foreach (var f in m.Frames) { double va = f.Get(a); f.Set(a, f.Get(b)); f.Set(b, va); }
            foreach (var c in m.Components.Values) foreach (var r in c.Follow) r.Dim = r.Dim == a ? b : r.Dim == b ? a : r.Dim;
            UpdateLayout(m);
        }

        // A new box next to box likeNo (Right / Left along the length, Front across the width), with
        // its size, 8 corners and 12 frame profiles named like the existing ones. A spot already taken
        // moves the new box past it.
        public static GrFrame AddFrame(GrModel m, int likeNo, string side = "Right")
        {
            var like = m.Frame(likeNo);
            string place = side == "Front" ? "front" : "side";
            int dir = side == "Left" ? -1 : 1;
            double Outer(double v) => v + CornerAdd;
            var last = like;
            for (int guard = 0; guard < 100; guard++)
            {
                double x = last.X, y = last.Y, z = last.Z;
                if (place == "front") y = last.Y + Outer(last.Wy);
                else if (dir > 0) x = last.X + Outer(last.Lx);
                else x = last.X - Outer(like.Lx);
                double x1 = x + Outer(like.Lx), y1 = y + Outer(like.Wy), z1 = z + Outer(like.Hz);
                var hit = m.Frames.FirstOrDefault(f => f.X < x1 - 1 && f.X + Outer(f.Lx) > x + 1 && f.Y < y1 - 1 && f.Y + Outer(f.Wy) > y + 1 && f.Z < z1 - 1 && f.Z + Outer(f.Hz) > z + 1);
                if (hit == null) break;
                last = hit;
            }
            int id = m.Frames.Select(f => f.Id).DefaultIfEmpty(0).Max() + 1;
            int no = m.Frames.Select(f => f.No).DefaultIfEmpty(0).Max() + 1;
            var nf = new GrFrame { Id = id, No = no, Sizes = like.Size, Lx = like.Lx, Wy = like.Wy, Hz = like.Hz, Parent = last.Id, Place = place, Dir = dir };
            nf.Neighbours.Add(last.Id); last.Neighbours.Add(id);
            string cornerBase = m.Components.Values.FirstOrDefault(c => c.Kind == "corner")?.Base ?? "APS Ecke";
            var prof = m.Components.Values.FirstOrDefault(c => c.Kind == "frameProfile");
            string series = prof != null ? Regex.Replace(prof.Base, @"\d+(?:[.,]\d+)?\s*mm.*$", "") : "P150 ";
            for (int k = 1; k <= 8; k++)
            {
                var c = NewComponent($"{cornerBase}:F{no}-{k}", ""); c.Kind = "corner"; c.Base = cornerBase; c.OrigBase = ""; c.Frame = id;   // "" = not in Inventor yet
                m.Components[c.Name] = c;
            }
            int n = 0;
            foreach (var d in new[] { ("L", nf.Lx), ("W", nf.Wy), ("H", nf.Hz) })
                for (int j = 0; j < 4; j++)
                {
                    n++;
                    string b = $"{series}{U.F(d.Item2)}mm";
                    var c = NewComponent($"{b}:F{no}-{n}", ""); c.Kind = "frameProfile"; c.Base = b; c.Length = d.Item2; c.OrigBase = ""; c.Frame = id;
                    c.Follow = new List<Follow> { new Follow { Dim = d.Item1 } };
                    m.Components[c.Name] = c;
                }
            m.Frames.Add(nf); m.Order.Add(id); m.Edited = true;
            UpdateLayout(m);
            return nf;
        }

        // Removes a box with everything on it; boxes placed against it move to its place.
        public static void RemoveFrame(GrModel m, int no)
        {
            if (m.Frames.Count <= 1) throw new SizerException("A GR needs at least one box.");
            var f = m.Frame(no);
            foreach (var n in m.Components.Keys.ToList())
            {
                var c = m.Components[n];
                if (c.Frame != f.Id) continue;
                if (!string.IsNullOrEmpty(c.OrigBase)) m.Removed.Add(n);
                m.Components.Remove(n);
            }
            var children = m.Frames.Where(x => x.Parent == f.Id && x.Id != f.Id).ToList();
            if (f.Place == "root")
            {
                if (children.Count > 0)
                {
                    var nr = children[0]; nr.Place = "root"; nr.Parent = 0; nr.Dir = 0;
                    foreach (var c in children.Skip(1)) c.Parent = nr.Id;
                }
            }
            else foreach (var c in children) { c.Parent = f.Parent; if (c.Place == "side") c.Dir = f.Dir; }
            foreach (var o in m.Frames) o.Neighbours.Remove(f.Id);
            m.Frames.Remove(f);
            m.Order.Remove(f.Id);
            // parents before children
            var ordered = new List<int>(); var pending = m.Order.ToList();
            while (pending.Count > 0)
            {
                var next = pending.Where(id => { var p = m.ById(id)?.Parent ?? 0; return p == 0 || ordered.Contains(p) || m.ById(p) == null; }).ToList();
                if (next.Count == 0) next = new List<int> { pending[0] };
                ordered.AddRange(next);
                pending = pending.Except(next).ToList();
            }
            m.Order = ordered; m.Edited = true;
            UpdateLayout(m);
        }

        public static void SetDividerPosition(GrModel m, string name, double value)
        {
            if (!m.Components.TryGetValue(name, out var c) || c.Kind != "divider") throw new SizerException($"'{name}' is not a divider.");
            if (c.PosConstraint == "") throw new SizerException($"'{name}' has no position of its own: it hangs on joints and follows the dividers it joins.");
            c.Position = value; m.Edited = true;
        }

        // What an edited GR changes in Inventor.
        public static GrPending Pending(GrModel m)
        {
            var p = new GrPending();
            foreach (var g in m.Components.Values.Where(c => (c.Kind == "frameProfile" || c.Kind == "divider") && !string.IsNullOrEmpty(c.OrigBase) && c.Base != c.OrigBase)
                                                 .GroupBy(c => c.OrigBase + "|" + c.Base).OrderBy(g => g.Key))
            {
                var c = g.First();
                p.Profiles.Add((c.Kind, c.OrigBase, c.Base, c.Length, g.Select(x => x.Name).OrderBy(x => x).ToList()));
            }
            foreach (var c in m.Components.Values.Where(c => c.Kind == "panel" && !string.IsNullOrEmpty(c.OrigBase) && c.Base != c.OrigBase).OrderBy(c => c.Name))
                p.Panels.Add((c.Name, c.OrigBase, c.Base));
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.PosConstraint != "" && c.Position != c.OrigPosition).OrderBy(c => c.Name))
                p.Dividers.Add((c.Name, c.PosConstraint, c.OrigPosition ?? 0, c.Position ?? 0));
            int added = m.Components.Values.Count(c => string.IsNullOrEmpty(c.OrigBase));
            if (added > 0) p.Blocked.Add($"{added} part(s) of added boxes are not in Inventor; adding boxes in Inventor is not automated yet.");
            if (m.Removed.Count > 0) p.Blocked.Add($"{m.Removed.Count} part(s) of removed boxes are still in Inventor; removing boxes in Inventor is not automated yet.");
            return p;
        }

        // ------------------------------------------------------------------ designs (JSON)

        static List<object> Nums(IEnumerable<double> v) => v.Select(x => (object)x).ToList();
        static double[] ArrD(object o) => (o as List<object>)?.Select(x => Convert.ToDouble(x, U.Inv)).ToArray();

        public static void SaveDesign(GrModel m, string path)
        {
            var j = new JsonObject
            {
                ["format"] = "InventorSizer GR design 1", ["name"] = m.Name, ["source"] = m.Source, ["sourcePath"] = m.SourcePath,
                ["saved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["cornerAdd"] = CornerAdd, ["order"] = m.Order.Select(x => (object)(double)x).ToList(),
                ["frames"] = m.Frames.Select(f => (object)new JsonObject
                {
                    ["id"] = (double)f.Id, ["no"] = (double)f.No, ["length"] = f.Lx, ["width"] = f.Wy, ["height"] = f.Hz, ["parent"] = (double)f.Parent, ["place"] = f.Place, ["dir"] = (double)f.Dir,
                    ["neighbours"] = f.Neighbours.Select(x => (object)(double)x).ToList(), ["panels"] = f.Panels.Cast<object>().ToList(), ["dividers"] = f.Dividers.Cast<object>().ToList(), ["other"] = f.Other.Cast<object>().ToList()
                }).ToList(),
                ["components"] = m.Components.Values.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c =>
                {
                    var o = new JsonObject
                    {
                        ["name"] = c.Name, ["file"] = c.File, ["base"] = c.Base, ["origBase"] = c.OrigBase, ["kind"] = c.Kind, ["length"] = c.Length, ["frame"] = (double)c.Frame,
                        ["follow"] = c.Follow.Select(r => (object)new JsonObject { ["dim"] = r.Dim, ["offset"] = r.Offset, ["part"] = (double)r.Part }).ToList(),
                        ["position"] = c.Position, ["origPosition"] = c.OrigPosition, ["posConstraint"] = c.PosConstraint, ["posPartner"] = c.PosPartner
                    };
                    if (c.Geo != null) o["geo"] = new JsonObject { ["Min"] = Nums(c.Geo.Min), ["Max"] = Nums(c.Geo.Max), ["Size0"] = Nums(c.Geo.Size0), ["Pos0"] = c.Geo.Pos0 };
                    return (object)o;
                }).ToList(),
                ["warnings"] = m.Warnings.Cast<object>().ToList(), ["unplaced"] = m.Unplaced.Cast<object>().ToList(), ["removed"] = m.Removed.Cast<object>().ToList(),
                ["realMap"] = m.RealMap == null ? null : m.RealMap.Select(x => (object)(double)x).ToList()
            };
            File.WriteAllText(path, Json.Write(j), new UTF8Encoding(false));
        }

        public static GrModel ReadDesign(string path)
        {
            var j = Json.Parse(File.ReadAllText(path)) as JsonObject;
            if (j == null || !j.Str("format").StartsWith("InventorSizer GR design")) throw new SizerException($"{Path.GetFileName(path)} is not a GR design saved by this app.");
            List<string> Strs(object o) => (o as List<object> ?? new List<object>()).Where(x => x != null).Select(x => Convert.ToString(x, U.Inv)).ToList();
            List<int> Ints(object o) => (o as List<object> ?? new List<object>()).Where(x => x != null).Select(x => Convert.ToInt32(x, U.Inv)).ToList();
            double? NumOrNull(JsonObject o, string k) => o[k] == null ? (double?)null : o.Num(k);
            var m = new GrModel { Name = j.Str("name"), Source = Path.GetFileName(path), SourcePath = j.Str("sourcePath"), Order = Ints(j["order"]) };
            foreach (var co in j.Arr("components").OfType<JsonObject>())
            {
                var c = new GrComponent
                {
                    Name = co.Str("name"), File = co.Str("file"), Base = co.Str("base"), OrigBase = co.ContainsKey("origBase") ? co.Str("origBase") : co.Str("base"),
                    Kind = co.Str("kind"), Length = co.Num("length"), Frame = (int)co.Num("frame"), Position = NumOrNull(co, "position"), OrigPosition = NumOrNull(co, "origPosition"),
                    PosConstraint = co.Str("posConstraint"), PosPartner = co.Str("posPartner"),
                    Follow = co.Arr("follow").OfType<JsonObject>().Select(r => new Follow { Dim = r.Str("dim"), Offset = r.Num("offset"), Part = (int)r.Num("part") }).ToList()
                };
                var g = co.Obj("geo");
                if (g != null) c.Geo = new Geo { Min = ArrD(g["Min"]), Max = ArrD(g["Max"]), Size0 = ArrD(g["Size0"]), Pos0 = NumOrNull(g, "Pos0") };
                m.Components[c.Name] = c;
            }
            foreach (var fo in j.Arr("frames").OfType<JsonObject>())
            {
                var f = new GrFrame
                {
                    Id = (int)fo.Num("id"), No = (int)fo.Num("no"), Lx = fo.Num("length"), Wy = fo.Num("width"), Hz = fo.Num("height"),
                    Parent = (int)fo.Num("parent"), Place = fo.Str("place", "loose"), Dir = (int)fo.Num("dir"),
                    Neighbours = Ints(fo["neighbours"]), Panels = Strs(fo["panels"]), Dividers = Strs(fo["dividers"]), Other = Strs(fo["other"])
                };
                f.Sizes = f.Size;
                m.Frames.Add(f);
            }
            m.Warnings = Strs(j["warnings"]); m.Unplaced = Strs(j["unplaced"]); m.Removed = Strs(j["removed"]);
            var rm = Ints(j["realMap"]);
            if (rm.Count == 3) m.RealMap = rm.ToArray();
            UpdateLayout(m);
            return m;
        }

        // ------------------------------------------------------------------ lists

        public static (int Count, string Text) LengthList(GrModel m, string kind)
        {
            var items = m.Components.Values.Where(c => c.Kind == kind).ToList();
            var parts = items.GroupBy(c => c.Base).OrderByDescending(g => g.First().Length)
                             .Select(g => $"{Regex.Replace(g.Key, @"^P\d+\s+", "").Replace("mm", "").Trim()} {U.Times}{g.Count()}");
            return (items.Count, string.Join(", ", parts));
        }

        static string PanelText(IEnumerable<string> names, Dictionary<string, GrComponent> comps)
        {
            return string.Join(", ", names.Select(n => Regex.Replace(comps[n].Base, @"^\S+\s+(?=(FP|RP|TP)-)", "")).GroupBy(x => x).OrderBy(g => g.Key, StringComparer.Ordinal)
                                          .Select(g => g.Count() > 1 ? $"{g.Key} {U.Times}{g.Count()}" : g.Key));
        }

        public static (List<string[]> Frames, List<string[]> Parts) Tables(GrModel m)
        {
            string x = U.Times;
            var frames = m.Frames.Select(f =>
            {
                var content = new List<string>();
                if (f.Panels.Count > 0) content.Add($"{f.Panels.Count} panels");
                if (f.Dividers.Count > 0) content.Add($"{f.Dividers.Count} dividers");
                if (f.Other.Count > 0) content.Add(string.Join(", ", f.Other.Where(n => m.Components.ContainsKey(n)).Select(n => Regex.Replace(m.Components[n].Base, @"^\S+-3\s+", "")).Distinct()));
                return new[] { f.Label, $"{U.F(f.Lx)} {x} {U.F(f.Wy)} {x} {U.F(f.Hz)}", string.Join("; ", content) };
            }).ToList();
            var p150 = LengthList(m, "frameProfile"); var div = LengthList(m, "divider");
            var parts = new List<string[]>
            {
                new[] { "Frame profiles P150", p150.Count.ToString(), p150.Text }, new[] { "Dividers P350", div.Count.ToString(), div.Text },
                new[] { "Corners (APS Ecke)", m.Corners.ToString(), "" }, new[] { "Joints", m.Joints.ToString(), "" },
            };
            var panels = m.Components.Values.Where(c => c.Kind == "panel").ToList();
            foreach (var t in new[] { "FP", "RP", "TP" })
            {
                var names = panels.Where(c => Regex.IsMatch(c.Base, $@"(?:^|\s){t}-\d")).Select(c => c.Name).ToList();
                if (names.Count > 0) parts.Add(new[] { "Panels " + t, names.Count.ToString(), PanelText(names, m.Components) });
            }
            foreach (var k in new[] { ("damper", "Dampers"), ("stub", "Duct stubs"), ("base", "Base frame"), ("other", "Other") })
            {
                var items = m.Components.Values.Where(c => c.Kind == k.Item1).ToList();
                if (items.Count > 0) parts.Add(new[] { k.Item2, items.Count.ToString(), string.Join(", ", items.Select(c => c.Base).Distinct()) });
            }
            return (frames, parts);
        }

        public static void ExportCutList(GrModel m, string path)
        {
            var label = new Dictionary<string, string> { ["frameProfile"] = "Frame profile", ["divider"] = "Divider", ["corner"] = "Corner", ["joint"] = "Joint", ["panel"] = "Panel", ["damper"] = "Damper", ["stub"] = "Duct stub", ["base"] = "Base frame", ["other"] = "Other" };
            var culture = CultureInfo.CurrentCulture; string sep = culture.TextInfo.ListSeparator;
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, new[] { "Kind", "Part", "Length", "Qty", "Boxes" }.Select(Q)));
            foreach (var g in m.Components.Values.GroupBy(c => c.Kind + "|" + c.Base).OrderBy(g => g.First().Kind).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                var c = g.First();
                string len = c.Kind == "frameProfile" || c.Kind == "divider" ? c.Length.ToString("0.###", culture) : "";
                string boxes = string.Join(" ", g.Select(x => x.Frame).Where(x => x > 0).Distinct().Select(id => m.ById(id)?.Label).Where(x => x != null));
                sb.AppendLine(string.Join(sep, new[] { label.TryGetValue(c.Kind, out var l) ? l : c.Kind, c.Base, len, g.Count().ToString(), boxes }.Select(Q)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
