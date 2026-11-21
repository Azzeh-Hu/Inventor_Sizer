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
        public string Leader = "";        // a divider without its own position follows this divider (flush to it)
        public string CloneOf = "";       // not in Inventor yet: copied from this part (a new P350 ring), or "box" (a new box)
        public int MoveAxis = -1;         // GR axis a positioned divider moves along; MoveSign: + = away from its partner
        public double MoveSign = 1;
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
        public double[] RealOrigin, Size0;   // with real positions: the box's min corner in GR axes (assembly mm) and its sizes then
        public int CloneOf;                  // a new box, not in Inventor yet: copied from this box (Id)
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
        public int ServiceSide = 1;   // the service side (TP) is the width side at +1 = max / -1 = min; the other is the back (RP)
        public Dictionary<string, string> PanelChoice = new Dictionary<string, string>();   // opening key -> FP / TP / RP / keep
        public string SelectedOpening;   // opening key highlighted in the drawing (not saved)
        public int Focus;                // the box being edited (Id): the others greyed out, its dimensions shown (not saved)
        public List<GrRingAdd> RingAdds = new List<GrRingAdd>();        // P350 rings to copy in Inventor
        public List<List<string>> RingRemoves = new List<List<string>>(); // P350 rings (with their joints) to delete in Inventor
        public int[] RealMap;   // assembly axis (0 X, 1 Y, 2 Z) of GR length, width, height
        public int Up = 2;       // assembly axis that is up (Z unless the user changes it)
        public GrFrame Frame(int no) => Frames.FirstOrDefault(f => f.No == no) ?? throw new SizerException($"There is no Box {no - 1}.");
        public GrFrame ById(int id) => Frames.FirstOrDefault(f => f.Id == id);
    }

    // A new P350 ring: a copy of the ring led by Template, at Position (offset from the same partner,
    // or from the matching profile of box Frame when it goes into another box). Single: one P350 on
    // one face (a copy of Template), at Position mm from the box's outer start along Axis (GR), grounded.
    // Span: one P350 (a copy of Template with its two joints) across a face (normal FaceAxis, side
    // FaceSide), running along Along between two supports (P150 or P350), at Position mm from the
    // box's outer start along Axis; its joints mate to the supports' seats through the named faces.
    public class GrRingAdd
    {
        public bool Span;
        public int Along = -1, FaceAxis = -1, FaceSide;
        public string[] Supports = new string[2];   // the support at the low and at the high end along Along
        public string Template;
        public double Position;
        public Dictionary<string, string> Names = new Dictionary<string, string>();   // template member -> new member in the model
        public int Frame;          // the box it goes into (Id); 0 = the template's box
        public bool Single;
        public int Axis = -1;
    }

    public class GrPending
    {
        public List<string> Boxes = new List<string>(), Rings = new List<string>();
        public List<(string Kind, string From, string To, double Length, List<string> Names)> Profiles = new List<(string, string, string, double, List<string>)>();
        public List<(string Name, string From, string To)> Panels = new List<(string, string, string)>();
        public List<(string Name, string Constraint, double From, double To)> Dividers = new List<(string, string, double, double)>();
        public List<string> Blocked = new List<string>();
    }

    public static partial class Gr
    {
        // Outer size = profile + CornerAdd (2 corners). Each corner type adds its own amount per side
        // (APS Ecke Guss 70, Kunststoff 75; its 130 mm leg sits inside the profile). Set from the
        // corners of the loaded GR; CornerAdd is the default when no corner type matches.
        public static double CornerAdd = 140;
        public static List<(Regex Pattern, double PerSide)> CornerTypes = new List<(Regex, double)> { (new Regex("^APS Ecke Gu"), 70), (new Regex("^APS Ecke Kunststoff"), 75) };
        public static double PanelAdd = 36;

        // Panel sizing: panel = seat-to-seat - 2 x clearance, seat-to-seat = clear space between
        // the profiles + 2 x SeatDepth. W = the shorter side. A side over MaxPanel needs a divider.
        public class PanelRule { public string Type; public double Clearance, Height; }
        public static double SeatDepth = 20, MaxPanel = 2400, SplitBelow = 500;
        public static int DefaultServiceSide = 1;   // GR.json "serviceSide": +1 = the width side at W max is the service side (TP), -1 = the one at W 0
        public static Dictionary<string, PanelRule> PanelRules = new Dictionary<string, PanelRule>
        {
            ["FP"] = new PanelRule { Type = "FP", Clearance = 2, Height = 47 },
            ["TP"] = new PanelRule { Type = "TP", Clearance = 3, Height = 42 },
            ["RP"] = new PanelRule { Type = "RP", Clearance = 2, Height = 44 },
        };
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
                CornerAdd = j.Num("cornerAdd", CornerAdd); PanelAdd = j.Num("panelAdd", PanelAdd); SpanAdd = j.Num("spanAdd", SpanAdd);
                DefaultServiceSide = j.Num("serviceSide", DefaultServiceSide) < 0 ? -1 : 1;
                GrOpenings.ProfileWidth = j.Num("profileWidth", GrOpenings.ProfileWidth); GrOpenings.MinOpening = j.Num("minOpening", GrOpenings.MinOpening);
                var corners = j.Arr("corners").OfType<JsonObject>().Select(r => (new Regex(r.Str("pattern")), r.Num("perSide", 70))).ToList();
                if (corners.Count > 0) CornerTypes = corners;
                var fn = j.Obj("faceNames");
                if (fn != null) foreach (var k in fn.OrderedKeys) FaceNames[k] = fn.Arr(k).Select(x => Convert.ToString(x, U.Inv)).ToArray();
                var pn = j.Obj("panels");
                if (pn != null)
                {
                    SeatDepth = pn.Num("seatDepth", SeatDepth); MaxPanel = pn.Num("maxSize", MaxPanel); SplitBelow = pn.Num("oneInnerPanelBelow", SplitBelow);
                    var types = pn.Obj("types");
                    if (types != null)
                        foreach (var k in types.OrderedKeys)
                        {
                            var t = types.Obj(k);
                            if (t != null) PanelRules[k] = new PanelRule { Type = k, Clearance = t.Num("clearance", 2), Height = t.Num("height", 0) };
                        }
                }
                Log.Write($"GR part rules: {PartRules.Count} from {path}");
            }
            catch (Exception e) { Log.Write($"GR part rules in {path} could not be read: {e.Message}"); }
        }

        public static string Kind(string baseName)
        {
            foreach (var r in PartRules) if (r.Pattern.IsMatch(baseName)) return r.Kind;
            return "other";
        }

        // CornerAdd for the corner type the GR uses (the first corner that matches a corner type).
        public static void SetCornerAdd(IEnumerable<GrComponent> comps)
        {
            foreach (var c in comps.Where(c => c.Kind == "corner"))
                foreach (var t in CornerTypes)
                    if (t.Pattern.IsMatch(c.Base)) { CornerAdd = 2 * t.PerSide; Log.Dev($"GR: corner {c.Base} adds {U.F(t.PerSide)} mm per side"); return; }
        }

        public class PanelSize { public string Type, Name, Inner; public double W, L, H, OpenW, OpenL; public string Problem; }

        // The panel for an opening, from the clear space between its profiles (mm, either order).
        public static PanelSize PanelFor(string type, double clearA, double clearB)
        {
            if (!PanelRules.TryGetValue(type ?? "", out var r)) throw new SizerException($"No panel rule for '{type}' in GR.json.");
            double oa = clearA + 2 * SeatDepth, ob = clearB + 2 * SeatDepth;
            double openW = Math.Min(oa, ob), openL = Math.Max(oa, ob);
            var p = new PanelSize { Type = type, OpenW = openW, OpenL = openL, W = openW - 2 * r.Clearance, L = openL - 2 * r.Clearance, H = r.Height };
            p.Inner = openW < SplitBelow ? "1T" : "2T";
            p.Name = $"{type}-{U.F(p.W)}x{U.F(p.L)}x{U.F(p.H)}";
            if (p.W <= 0) p.Problem = "the opening is too small for a panel";
            else if (p.L > MaxPanel) p.Problem = $"{U.F(p.L)} mm is over the {U.F(MaxPanel)} mm maximum: add a divider";
            return p;
        }

        // Faces each library part must have named (iLogic "Assign Name") so the app can mate it.
        // "panelAu" = the -Au tray part of a panel. From GR.json "faceNames".
        public static Dictionary<string, string[]> FaceNames = new Dictionary<string, string[]>
        {
            ["frameProfile"] = new[] { "End_A", "End_B", "Out_1", "Out_2", "Seat_1", "Seat_2" },
            ["divider"] = new[] { "End_A", "End_B", "Out", "Seat_Side_A", "Seat_Side_B" },
            ["corner"] = new[] { "Leg_X", "Leg_Y", "Leg_Z", "Out_X", "Out_Y", "Out_Z" },
            ["joint"] = new[] { "Base", "End", "Out" },
            ["panelAu"] = new[] { "Top", "Edge_W0", "Edge_W1", "Edge_L0", "Edge_L1" },
        };

        public static string FaceKind(string fileLeaf) => Regex.IsMatch(fileLeaf ?? "", @"-Au$") ? "panelAu" : Kind(fileLeaf ?? "");

        // One line per part: the names it has and the ones it is missing (null = a part that needs no names).
        public static string NameReport(string fileLeaf, IEnumerable<string> have, out bool ok)
        {
            ok = true;
            if (!FaceNames.TryGetValue(FaceKind(fileLeaf), out var want)) return null;
            var got = new HashSet<string>(have ?? Enumerable.Empty<string>());
            var missing = want.Where(n => !got.Contains(n)).ToList();
            var extra = got.Where(n => !want.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            ok = missing.Count == 0;
            return $"{fileLeaf}: " + (ok ? "all " + want.Length + " names" : "missing " + string.Join(", ", missing)) + (extra.Count > 0 ? $" (other names: {string.Join(", ", extra)})" : "");
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
                    case "InventorReader":
                        ReadInventorReader(root, d);
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

        // InventorReader export (test data): a tree of <Node Text=...> with <Property Name=...>.
        // The GR is the assembly, top level or a sub-assembly (e.g. 8185 GR inside 8185 ZB), whose
        // own components hold the most corners. Range boxes are "a x b x c mm, from (x, y, z) mm".
        static void ReadInventorReader(XmlElement root, GrData d)
        {
            string Prop(XmlNode n, string name) => n.SelectSingleNode($"Property[@Name='{name}']")?.InnerText;
            var listRx = new Regex(@"^(Components|Constraints) \(\d+\)$");
            XmlNode Section(XmlNode asm, string what) => asm.SelectNodes("Node").Cast<XmlNode>().FirstOrDefault(n => (n.Attributes?["Text"]?.Value ?? "").StartsWith(what + " (") && listRx.IsMatch(n.Attributes["Text"].Value));
            XmlNode best = null; int bestCorners = 0;
            foreach (XmlNode n in root.SelectNodes("//Node"))
            {
                var comps = Section(n, "Components");
                if (comps == null) continue;
                int corners = comps.SelectNodes("Node").Cast<XmlNode>().Count(c => Kind(Regex.Replace(c.Attributes?["Text"]?.Value ?? "", @":\d+$", "")) == "corner");
                if (Section(n, "Constraints") != null) corners += 10000;   // the assembly's own definition, with its constraints
                if (corners > bestCorners) { best = n; bestCorners = corners; }
            }
            if (best == null) throw new SizerException("No assembly with corners (APS Ecke) found in the InventorReader export.");
            string text = Regex.Replace(best.Attributes["Text"].Value, "^File: ", "");
            if (d.Name == "") d.Name = Regex.Replace(Path.GetFileNameWithoutExtension(text), @":\d+$", "");
            if (d.SourcePath == "") d.SourcePath = Prop(best, "File") ?? Prop(best.ParentNode, "File") ?? "";
            var num = new Regex(@"-?\d+(?:\.\d+)?");
            foreach (XmlNode c in Section(best, "Components").SelectNodes("Node"))
            {
                string name = c.Attributes["Text"].Value.Trim();
                var comp = NewComponent(name, Prop(c, "File"));
                var rb = Prop(c, "RangeBox");
                if (rb != null)
                {
                    var v = num.Matches(rb).Cast<Match>().Select(x => U.P(x.Value)).ToArray();
                    if (v.Length == 6 && v.Take(3).Any(x => x != 0)) comp.Box = new[] { v[3], v[4], v[5], v[3] + v[0], v[4] + v[1], v[5] + v[2] };
                }
                d.Components[name] = comp;
            }
            var cons = Section(best, "Constraints");
            if (cons == null) return;
            foreach (XmlNode k in cons.SelectNodes("Node"))
            {
                string t = k.Attributes["Text"].Value;
                var tm = Regex.Match(t, @"\[(\w+?)(?:Constraint)?\]");
                string offset = k.SelectNodes("Property[@Name='Offset']").Cast<XmlNode>().Select(x => x.InnerText).FirstOrDefault(x => Mm(x) != null)
                                ?? k.SelectNodes("Property[@Name='Angle']").Cast<XmlNode>().Select(x => x.InnerText).FirstOrDefault() ?? "";
                d.Constraints.Add(new GrConstraint
                {
                    Name = Prop(k, "Name") ?? Regex.Match(t, @"^\S+").Value, Type = tm.Success ? tm.Groups[1].Value : "",
                    A = Prop(k, "Component one") ?? "", B = Prop(k, "Component two") ?? "", Value = offset, Suppressed = Prop(k, "Suppressed") == "True"
                });
            }
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

        public static string AxisName(int axis) => "XYZ"[axis].ToString();

        // up = the assembly axis that is up: 0 X, 1 Y, 2 Z (the standard GR has Z up).
        public static GrModel Build(GrData data, int up = 2)
        {
            var comps = data.Components;
            SetCornerAdd(comps.Values);
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
            // ... or to another divider (a second section ring placed from the first, e.g. Mate 700 mm).
            for (int round = 0; round < 5; round++)
            {
                bool any = false;
                foreach (var c in active.Where(c => Regex.IsMatch(c.Type, "Mate|Flush")))
                {
                    var mm = Mm(c.Value);
                    if (mm == null || mm == -25 || mm == 0 || !comps.TryGetValue(c.A, out var a) || !comps.TryGetValue(c.B, out var b)) continue;
                    if (a.Kind != "divider" || b.Kind != "divider" || a.PosConstraint == c.Name || b.PosConstraint == c.Name) continue;
                    var (d, o) = a.PosConstraint == "" && b.PosConstraint != "" ? (a, b) : b.PosConstraint == "" ? (b, a) : (null, null);
                    if (d == null || (o.PosConstraint == "" && round < 4)) continue;   // place from a divider that is placed itself
                    d.Position = mm; d.OrigPosition = mm; d.PosConstraint = c.Name; d.PosPartner = o.Name; any = true;
                }
                if (!any && round > 0) break;
            }
            // Dividers without a position of their own follow the divider they are flush with (its ring).
            foreach (var c in active.Where(c => Regex.IsMatch(c.Type, "Flush|Mate") && (Mm(c.Value) ?? 0) == 0))
                foreach (var pair in new[] { (c.A, c.B), (c.B, c.A) })
                    if (comps.TryGetValue(pair.Item1, out var d) && comps.TryGetValue(pair.Item2, out var o) && d.Kind == "divider" && o.Kind == "divider" && d.PosConstraint == "" && d.Leader == "" && o.Leader != d.Name)
                        d.Leader = o.Name;

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

            // With real positions: height along the up axis, length along the horizontal axis the GR
            // is longest on, width across. Replaces the guess above.
            var realMap = OrientFromReal(frames, comps, owner, up);

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
                Unplaced = unplaced, Warnings = warnings, ConstraintCount = active.Count, RealMap = realMap, Up = up, ServiceSide = DefaultServiceSide
            };
            SetRealGeometry(model);
            UpdateLayout(model);
            int no = 0;
            foreach (var f in frames.OrderBy(f => f.Y).ThenBy(f => f.X).ThenBy(f => f.Z).ToList()) f.No = ++no;
            UpdateLayout(model);
            Log.Dev("GR build: layout done" + (model.RealMap != null ? " (from the real part positions)" : " (inferred from the constraints)"));
            return model;
        }

        static int[] OrientFromReal(List<GrFrame> frames, Dictionary<string, GrComponent> comps, Dictionary<string, int> owner, int up)
        {
            var real = new Dictionary<int, (double[] Min, double[] Max)>();
            foreach (var f in frames)
            {
                var boxes = owner.Where(o => o.Value == f.Id && comps[o.Key].Box != null).Select(o => comps[o.Key].Box).ToList();
                if (boxes.Count < 8) continue;
                real[f.Id] = (Enumerable.Range(0, 3).Select(k => boxes.Min(b => b[k])).ToArray(), Enumerable.Range(0, 3).Select(k => boxes.Max(b => b[k + 3])).ToArray());
            }
            if (real.Count == 0) return null;
            var flat = new[] { 0, 1, 2 }.Where(a => a != up).ToArray();
            double Span(int a) => real.Values.Max(r => r.Max[a]) - real.Values.Min(r => r.Min[a]);
            int lenAxis = Span(flat[0]) >= Span(flat[1]) ? flat[0] : flat[1];
            var map = new[] { lenAxis, flat[0] == lenAxis ? flat[1] : flat[0], up };
            var sizes = new Dictionary<int, double[]>();
            foreach (var f in frames.Where(f => real.ContainsKey(f.Id)))
            {
                var left = f.Sizes.ToList(); var s = new double[3];
                for (int g = 0; g < 3; g++)
                {
                    double want = real[f.Id].Max[map[g]] - real[f.Id].Min[map[g]] - CornerAdd;
                    var best = left.OrderBy(v => Math.Abs(v - want)).First();
                    if (Math.Abs(best - want) > 5) { Log.Dev($"GR: frame {f.Id} real size does not match its profiles + corners - the sizes are a guess"); return null; }
                    s[g] = best; left.Remove(best);
                }
                sizes[f.Id] = s;
            }
            foreach (var kv in sizes) { var f = frames.First(x => x.Id == kv.Key); f.Lx = kv.Value[0]; f.Wy = kv.Value[1]; f.Hz = kv.Value[2]; }
            Log.Dev($"GR: up = {AxisName(up)}, length along {AxisName(map[0])}, width along {AxisName(map[1])}");
            return map;
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
            var map = m.RealMap;
            if (map == null)
            {
                var ext = Enumerable.Range(0, 3).Select(k => real[first.Id].Max[k] - real[first.Id].Min[k]).ToArray();
                var want = new[] { first.Lx + CornerAdd, first.Wy + CornerAdd, first.Hz + CornerAdd };
                map = new[] { -1, -1, -1 }; var used = new bool[3];
                foreach (int g in new[] { 2, 0, 1 })
                {
                    int best = -1; double diff = double.MaxValue;
                    for (int a = 0; a < 3; a++) if (!used[a] && Math.Abs(ext[a] - want[g]) < diff) { best = a; diff = Math.Abs(ext[a] - want[g]); }
                    if (diff > 5) { Log.Dev($"GR: real frame size {string.Join(" x ", ext.Select(U.F))} does not match profile + corners - using the inferred layout"); return; }
                    map[g] = best; used[best] = true;
                }
                m.RealMap = map;
            }
            foreach (var f in m.Frames.Where(f => real.ContainsKey(f.Id)))
            {
                var mn = real[f.Id].Min; f.RealOrigin = new[] { mn[map[0]], mn[map[1]], mn[map[2]] }; f.Size0 = f.Size;
            }
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
                if ((c.Kind != "divider" && c.Kind != "joint" && c.Kind != "panel" && c.Kind != "damper" && c.Kind != "stub") || c.Box == null || !real.ContainsKey(c.Frame)) continue;
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
            // The axis a positioned divider moves along: across its length, where it is furthest from its partner.
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.PosConstraint != "" && c.Box != null))
            {
                if (!m.Components.TryGetValue(c.PosPartner, out var p) || p.Box == null) continue;
                var ca = ToGr(new[] { (c.Box[0] + c.Box[3]) / 2, (c.Box[1] + c.Box[4]) / 2, (c.Box[2] + c.Box[5]) / 2 });
                var pa = ToGr(new[] { (p.Box[0] + p.Box[3]) / 2, (p.Box[1] + p.Box[4]) / 2, (p.Box[2] + p.Box[5]) / 2 });
                var ext = Enumerable.Range(0, 3).Select(i => c.Geo.Max[i] - c.Geo.Min[i]).ToArray();
                int along = Array.IndexOf(ext, ext.Max());
                int axis = Enumerable.Range(0, 3).Where(i => i != along).OrderByDescending(i => Math.Abs(ca[i] - pa[i])).First();
                c.MoveAxis = axis; c.MoveSign = ca[axis] >= pa[axis] ? 1 : -1;
            }
        }

        // How far a divider has moved from where it was read (its own offset change plus the moves of
        // the divider it is placed from or follows), and along which GR axis.
        public static (int Axis, double Delta) DividerShift(GrModel m, GrComponent c, int depth = 0)
        {
            if (c == null || depth > 20) return (-1, 0);
            int axis = c.MoveAxis; double delta = 0;
            if (c.PosConstraint != "" && c.Position != null && c.Geo?.Pos0 != null) delta = (c.Position.Value - c.Geo.Pos0.Value) * c.MoveSign;
            string up = c.PosConstraint != "" ? c.PosPartner : c.Leader;
            if (up != "" && m.Components.TryGetValue(up, out var u) && u.Kind == "divider")
            {
                var s = DividerShift(m, u, depth + 1);
                if (axis < 0) axis = s.Axis;
                if (s.Axis == axis) delta += s.Delta;
            }
            return (axis, delta);
        }

        // A part got a new occurrence name in Inventor (Replace names it after the new file).
        public static void RenameComponent(GrModel m, string old, string now)
        {
            if (old == now || string.IsNullOrEmpty(now) || !m.Components.TryGetValue(old, out var c) || m.Components.ContainsKey(now)) return;
            m.Components.Remove(old); c.Name = now; m.Components[now] = c;
            foreach (var f in m.Frames) foreach (var l in new[] { f.Panels, f.Dividers, f.Other }) { int i = l.IndexOf(old); if (i >= 0) l[i] = now; }
            foreach (var o in m.Components.Values) { if (o.PosPartner == old) o.PosPartner = now; if (o.Leader == old) o.Leader = now; if (o.CloneOf == old) o.CloneOf = now; }
            foreach (var r in m.RingAdds)
            {
                if (r.Template == old) r.Template = now;
                if (r.Names.TryGetValue(old, out var nn)) { r.Names.Remove(old); r.Names[now] = nn; }
                foreach (var k in r.Names.Keys.ToList()) if (r.Names[k] == old) r.Names[k] = now;
                for (int i = 0; i < r.Supports.Length; i++) if (r.Supports[i] == old) r.Supports[i] = now;
            }
            int u = m.Unplaced.IndexOf(old); if (u >= 0) m.Unplaced[u] = now;
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

        // A new box next to box likeNo (Right / Left along the length: + / - L; Front / Back across the
        // width: + / - W), with its size, 8 corners and 12 frame profiles named like the existing ones.
        // A spot already taken moves the new box past it.
        public static GrFrame AddFrame(GrModel m, int likeNo, string side = "Right")
        {
            var like = m.Frame(likeNo);
            string place = side == "Front" || side == "Back" ? "front" : "side";
            int dir = side == "Left" || side == "Back" ? -1 : 1;
            double Outer(double v) => v + CornerAdd;
            var last = like;
            for (int guard = 0; guard < 100; guard++)
            {
                double x = last.X, y = last.Y, z = last.Z;
                if (place == "front") y = dir > 0 ? last.Y + Outer(last.Wy) : last.Y - Outer(like.Wy);
                else if (dir > 0) x = last.X + Outer(last.Lx);
                else x = last.X - Outer(like.Lx);
                double x1 = x + Outer(like.Lx), y1 = y + Outer(like.Wy), z1 = z + Outer(like.Hz);
                var hit = m.Frames.FirstOrDefault(f => f.X < x1 - 1 && f.X + Outer(f.Lx) > x + 1 && f.Y < y1 - 1 && f.Y + Outer(f.Wy) > y + 1 && f.Z < z1 - 1 && f.Z + Outer(f.Hz) > z + 1);
                if (hit == null) break;
                last = hit;
            }
            int id = m.Frames.Select(f => f.Id).DefaultIfEmpty(0).Max() + 1;
            int no = m.Frames.Select(f => f.No).DefaultIfEmpty(0).Max() + 1;
            // in Inventor the new box is a copy of a box that is there already (a new box is not yet)
            int source = like.CloneOf != 0 ? like.CloneOf : like.Id;
            var nf = new GrFrame { Id = id, No = no, Sizes = like.Size, Lx = like.Lx, Wy = like.Wy, Hz = like.Hz, Parent = last.Id, Place = place, Dir = dir, CloneOf = source };
            nf.Neighbours.Add(last.Id); last.Neighbours.Add(id);
            string cornerBase = m.Components.Values.FirstOrDefault(c => c.Kind == "corner")?.Base ?? "APS Ecke";
            var prof = m.Components.Values.FirstOrDefault(c => c.Kind == "frameProfile");
            string series = prof != null ? Regex.Replace(prof.Base, @"\d+(?:[.,]\d+)?\s*mm.*$", "") : "P150 ";
            for (int k = 1; k <= 8; k++)
            {
                var c = NewComponent($"{cornerBase}:F{no}-{k}", ""); c.Kind = "corner"; c.Base = cornerBase; c.OrigBase = ""; c.Frame = id; c.CloneOf = "box";   // "" = not in Inventor yet
                m.Components[c.Name] = c;
            }
            int n = 0;
            foreach (var d in new[] { ("L", nf.Lx), ("W", nf.Wy), ("H", nf.Hz) })
                for (int j = 0; j < 4; j++)
                {
                    n++;
                    string b = $"{series}{U.F(d.Item2)}mm";
                    var c = NewComponent($"{b}:F{no}-{n}", ""); c.Kind = "frameProfile"; c.Base = b; c.Length = d.Item2; c.OrigBase = ""; c.Frame = id; c.CloneOf = "box";
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
            if (f.Neighbours.Count > 1) throw new SizerException($"{f.Label} sits between other boxes: only a box at the end can be removed.");
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
            var f = m.ById(c.Frame);
            if (value <= 0 || (f != null && c.MoveAxis >= 0 && value >= f.Size[c.MoveAxis] + CornerAdd)) throw new SizerException($"{U.F(value)} mm is outside the box ({U.F((f?.Size[Math.Max(0, c.MoveAxis)] ?? 0) + CornerAdd)} mm).");
            c.Position = value; m.Edited = true;
            foreach (var r in m.RingAdds.Where(r => r.Names.TryGetValue(r.Template, out var nl) && nl == name)) { r.Position = value; c.OrigPosition = value; }
        }

        // ------------------------------------------------------------------ P350 rings

        // The divider that places a ring: follow the "moves with" links to the one with its own position.
        public static string RingOf(GrModel m, string divider)
        {
            var seen = new HashSet<string>();
            string n = divider;
            while (m.Components.TryGetValue(n, out var c) && c.PosConstraint == "" && c.Leader != "" && seen.Add(n)) n = c.Leader;
            return n;
        }

        // A ring: its leader and every divider that moves with it.
        public static List<string> RingMembers(GrModel m, string leader) =>
            m.Components.Values.Where(c => c.Kind == "divider" && RingOf(m, c.Name) == leader).Select(c => c.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        // A new ring: a copy of the ring of `divider` at `position` mm from the same partner. In the
        // model at once (drawing, openings); in Inventor on Write new GR.
        public static GrRingAdd AddRing(GrModel m, string divider, double position, int targetFrame = 0)
        {
            string leader = RingOf(m, divider);
            if (!m.Components.TryGetValue(leader, out var lc) || lc.PosConstraint == "")
                throw new SizerException($"{divider} has no position of its own and does not move with a divider that has one: it cannot be copied.");
            if (lc.CloneOf != "") throw new SizerException("Copy a ring that is in Inventor already (write the GR first).");
            var from = m.ById(lc.Frame);
            var to = targetFrame == 0 ? from : m.ById(targetFrame) ?? throw new SizerException("That box is not in the GR.");
            bool other = to != from;
            if (other)
            {
                if (!m.Components.TryGetValue(lc.PosPartner, out var pp) || (pp.Kind != "frameProfile" && pp.Kind != "corner"))
                    throw new SizerException($"The ring of {leader} is placed from {lc.PosPartner}, not from a frame profile: it can only be copied within {from?.Label}.");
                if (to.RealOrigin == null && to.CloneOf == 0) throw new SizerException($"{to.Label} has no real positions: read the GR from Inventor first.");
            }
            if (position <= 0 || (lc.MoveAxis >= 0 && position >= to.Size[lc.MoveAxis] + CornerAdd)) throw new SizerException($"{U.F(position)} mm is outside {to.Label} ({U.F(to.Size[Math.Max(0, lc.MoveAxis)] + CornerAdd)} mm).");
            // a ring placed from the far end of its box stays that far from the far end of the other box
            double farShift = 0;
            if (other && lc.MoveAxis >= 0)
            {
                var pb = GrOpenings.LayoutBox(m, m.Components[lc.PosPartner]) ?? GrOpenings.ProfileBox(m, from, lc.PosPartner, -1);
                if (pb != null && (pb.Value.Min[lc.MoveAxis] + pb.Value.Max[lc.MoveAxis]) / 2 - from.Pos[lc.MoveAxis] > (from.Size[lc.MoveAxis] + CornerAdd) / 2)
                    farShift = to.Size[lc.MoveAxis] - from.Size[lc.MoveAxis];
            }
            var add = new GrRingAdd { Template = leader, Position = position, Frame = other ? to.Id : 0 };
            int k = m.Components.Count(c => c.Value.CloneOf != "") + 1;
            foreach (var name in RingMembers(m, leader))
            {
                var c = m.Components[name];
                string nn;
                do nn = $"{Regex.Replace(c.Name, @":\d+$", "")}:new{k++}"; while (m.Components.ContainsKey(nn));
                add.Names[name] = nn;
            }
            foreach (var kv in add.Names)
            {
                var c = m.Components[kv.Key];
                var n = new GrComponent
                {
                    Name = kv.Value, File = c.File, Base = c.Base, OrigBase = "", Kind = c.Kind, Length = c.Length, Frame = to?.Id ?? c.Frame, CloneOf = kv.Key,
                    Follow = c.Follow.Select(r => new Follow { Dim = r.Dim, Offset = r.Offset, Part = r.Part }).ToList(),
                    Box = c.Box, MoveAxis = c.MoveAxis, MoveSign = c.MoveSign, PosPartner = c.PosPartner,
                    Leader = c.Leader != "" && add.Names.TryGetValue(c.Leader, out var ln) ? ln : c.Leader,
                    Geo = c.Geo == null ? null : new Geo { Min = (double[])c.Geo.Min.Clone(), Max = (double[])c.Geo.Max.Clone(), Size0 = c.Geo.Size0, Pos0 = c.Geo.Pos0 }
                };
                if (kv.Key == leader) { n.Position = position; n.OrigPosition = position; n.PosConstraint = "(new)"; if (n.Geo != null) n.Geo.Pos0 = c.Geo.Pos0; }
                if (other)
                {
                    foreach (var r in n.Follow) SetComponentLength(n, r.Part, to.Get(r.Dim) + r.Offset);   // the other box's size
                    if (n.Geo != null && farShift != 0) { n.Geo.Min[lc.MoveAxis] += farShift; n.Geo.Max[lc.MoveAxis] += farShift; }
                }
                m.Components[n.Name] = n;
                m.ById(n.Frame)?.Dividers.Add(n.Name);
            }
            m.RingAdds.Add(add); m.Edited = true;
            return add;
        }

        // P350 that could be copied as a single one onto a face of a box: in Inventor already, lying on a
        // face with the same normal (GR axis faceAxis) and the same side of its own box, and running
        // along longAxis. The ones in the box itself first.
        public static List<string> SingleTemplates(GrModel m, int frameId, int faceAxis, int side, int longAxis)
        {
            var list = new List<(string Name, bool Same)>();
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.CloneOf == "" && !string.IsNullOrEmpty(c.OrigBase)))
            {
                var f = m.ById(c.Frame); var b = GrOpenings.LayoutBox(m, c);
                if (f == null || b == null) continue;
                var ext = Enumerable.Range(0, 3).Select(i => b.Value.Max[i] - b.Value.Min[i]).ToArray();
                if (Array.IndexOf(ext, ext.Max()) != longAxis || ext[faceAxis] > 150) continue;
                double plane = f.Pos[faceAxis] + (side == 0 ? 0 : f.Size[faceAxis] + CornerAdd);
                if (Math.Abs((b.Value.Min[faceAxis] + b.Value.Max[faceAxis]) / 2 - plane) > 150) continue;
                list.Add((c.Name, f.Id == frameId));
            }
            return list.OrderByDescending(x => x.Same).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Name).ToList();
        }

        // One P350 on a face: a copy of `template` in box frameId, at `position` mm from the box's outer
        // start along GR axis `axis`. It hangs on nothing in Inventor: it is placed there and grounded.
        public static GrRingAdd AddSingle(GrModel m, string template, int frameId, int axis, double position)
        {
            if (!m.Components.TryGetValue(template, out var c) || c.Kind != "divider") throw new SizerException($"'{template}' is not a divider.");
            if (c.CloneOf != "" || c.Geo == null) throw new SizerException("Copy a P350 that is in Inventor already (write the GR first).");
            var to = m.ById(frameId) ?? throw new SizerException("That box is not in the GR.");
            if (to.RealOrigin == null && to.CloneOf == 0) throw new SizerException($"{to.Label} has no real positions: read the GR from Inventor first.");
            if (position <= 0 || position >= to.Size[axis] + CornerAdd) throw new SizerException($"{U.F(position)} mm is outside {to.Label} ({U.F(to.Size[axis] + CornerAdd)} mm).");
            int k = 0; string nn;
            do nn = $"{Regex.Replace(c.Name, @":\d+$", "")}:new{++k + m.Components.Count(x => x.Value.CloneOf != "")}"; while (m.Components.ContainsKey(nn));
            var geo = new Geo { Min = (double[])c.Geo.Min.Clone(), Max = (double[])c.Geo.Max.Clone(), Size0 = c.Geo.Size0 };
            double half = (geo.Max[axis] - geo.Min[axis]) / 2, at = (geo.Max[axis] + geo.Min[axis]) / 2;
            geo.Min[axis] += position - at; geo.Max[axis] += position - at;
            var n = new GrComponent
            {
                Name = nn, File = c.File, Base = c.Base, OrigBase = "", Kind = c.Kind, Length = c.Length, Frame = to.Id, CloneOf = template,
                Follow = c.Follow.Select(r => new Follow { Dim = r.Dim, Offset = r.Offset, Part = r.Part }).ToList(), Box = c.Box, Geo = geo,
                // movable (dimensions, the Dividers window) until it is written; grounded in Inventor then
                Position = position, OrigPosition = position, PosConstraint = "(new)", MoveAxis = axis, MoveSign = 1
            };
            geo.Pos0 = position;
            foreach (var r in n.Follow) SetComponentLength(n, r.Part, to.Get(r.Dim) + r.Offset);
            m.Components[nn] = n;
            to.Dividers.Add(nn);
            var add = new GrRingAdd { Template = template, Position = position, Frame = to.Id, Single = true, Axis = axis };
            add.Names[template] = nn;
            m.RingAdds.Add(add); m.Edited = true;
            return add;
        }

        // Removes the ring of `divider` (with its joints). Refused while another ring is placed from it.
        public static List<string> RemoveRing(GrModel m, string divider)
        {
            string leader = RingOf(m, divider);
            var members = RingMembers(m, leader);
            var dependent = m.Components.Values.FirstOrDefault(c => c.Kind == "divider" && !members.Contains(c.Name) && members.Contains(c.PosPartner));
            if (dependent != null) throw new SizerException($"{dependent.Name} is placed from this ring: remove or move that ring first.");
            var hanging = m.RingAdds.FirstOrDefault(r => r.Span && r.Supports.Any(members.Contains));
            if (hanging != null) throw new SizerException($"The new P350 {hanging.Names.Values.FirstOrDefault()} hangs on this ring: remove it first.");
            var add = m.RingAdds.FirstOrDefault(r => r.Names.ContainsValue(leader));
            var gone = new List<string>(members);
            if (add == null)
            {
                // the joints the ring hangs on: joints touching one of its dividers
                var boxes = members.Select(n => GrOpenings.LayoutBox(m, m.Components[n])).Where(b => b != null).Select(b => b.Value).ToList();
                foreach (var j in m.Components.Values.Where(c => c.Kind == "joint"))
                {
                    var jb = GrOpenings.LayoutBox(m, j);
                    if (jb != null && boxes.Any(b => Enumerable.Range(0, 3).All(i => jb.Value.Min[i] < b.Max[i] + 5 && jb.Value.Max[i] > b.Min[i] - 5))) gone.Add(j.Name);
                }
                m.RingRemoves.Add(gone.Where(n => !string.IsNullOrEmpty(m.Components[n].OrigBase)).ToList());
            }
            else m.RingAdds.Remove(add);
            foreach (var n in gone)
            {
                m.Components.Remove(n);
                foreach (var f in m.Frames) { f.Dividers.Remove(n); f.Other.Remove(n); }
            }
            m.Edited = true;
            return gone;
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
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.PosConstraint != "" && c.CloneOf == "" && c.Position != c.OrigPosition).OrderBy(c => c.Name))
                p.Dividers.Add((c.Name, c.PosConstraint, c.OrigPosition ?? 0, c.Position ?? 0));
            foreach (var f in m.Frames.Where(f => f.CloneOf != 0)) p.Boxes.Add($"new {f.Label}, a copy of {m.ById(f.CloneOf)?.Label ?? "a removed box"}");
            if (m.Removed.Count > 0) p.Boxes.Add($"{m.Removed.Count} part(s) of removed boxes deleted");
            foreach (var r in m.RingAdds)
                p.Rings.Add(r.Span ? SpanText(m, r) : r.Single ? $"new P350 like {r.Template} on {m.ById(r.Frame)?.Label}, {U.F(r.Position)} mm along {"LWH"[Math.Max(0, r.Axis)]}"
                    : $"new P350 ring like {r.Template} at {U.F(r.Position)} mm ({r.Names.Count} P350){(r.Frame != 0 ? " in " + m.ById(r.Frame)?.Label : "")}");
            foreach (var r in m.RingRemoves) p.Rings.Add($"P350 ring removed: {string.Join(", ", r)}");
            int lost = m.Components.Values.Count(c => string.IsNullOrEmpty(c.OrigBase) && c.CloneOf == "");
            if (lost > 0) p.Blocked.Add($"{lost} part(s) are not in Inventor and not a copy of anything there.");
            if (m.Frames.Any(f => f.CloneOf != 0 && m.ById(f.CloneOf) == null)) p.Blocked.Add("A new box is a copy of a box that was removed: load the GR again.");
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
                    ["neighbours"] = f.Neighbours.Select(x => (object)(double)x).ToList(), ["cloneOf"] = (double)f.CloneOf, ["panels"] = f.Panels.Cast<object>().ToList(), ["dividers"] = f.Dividers.Cast<object>().ToList(), ["other"] = f.Other.Cast<object>().ToList()
                }).ToList(),
                ["components"] = m.Components.Values.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c =>
                {
                    var o = new JsonObject
                    {
                        ["name"] = c.Name, ["file"] = c.File, ["base"] = c.Base, ["origBase"] = c.OrigBase, ["kind"] = c.Kind, ["length"] = c.Length, ["frame"] = (double)c.Frame,
                        ["follow"] = c.Follow.Select(r => (object)new JsonObject { ["dim"] = r.Dim, ["offset"] = r.Offset, ["part"] = (double)r.Part }).ToList(),
                        ["position"] = c.Position, ["origPosition"] = c.OrigPosition, ["posConstraint"] = c.PosConstraint, ["posPartner"] = c.PosPartner,
                        ["leader"] = c.Leader, ["moveAxis"] = (double)c.MoveAxis, ["moveSign"] = c.MoveSign, ["cloneOf"] = c.CloneOf
                    };
                    if (c.Geo != null) o["geo"] = new JsonObject { ["Min"] = Nums(c.Geo.Min), ["Max"] = Nums(c.Geo.Max), ["Size0"] = Nums(c.Geo.Size0), ["Pos0"] = c.Geo.Pos0 };
                    return (object)o;
                }).ToList(),
                ["warnings"] = m.Warnings.Cast<object>().ToList(), ["unplaced"] = m.Unplaced.Cast<object>().ToList(), ["removed"] = m.Removed.Cast<object>().ToList(),
                ["realMap"] = m.RealMap == null ? null : m.RealMap.Select(x => (object)(double)x).ToList(), ["up"] = (double)m.Up, ["serviceSide"] = (double)m.ServiceSide,
                ["ringAdds"] = m.RingAdds.Select(r => (object)new JsonObject
                {
                    ["template"] = r.Template, ["position"] = r.Position, ["names"] = r.Names.Select(kv => (object)new List<object> { kv.Key, kv.Value }).ToList(),
                    ["frame"] = (double)r.Frame, ["single"] = r.Single, ["axis"] = (double)r.Axis,
                    ["span"] = r.Span, ["along"] = (double)r.Along, ["faceAxis"] = (double)r.FaceAxis, ["faceSide"] = (double)r.FaceSide, ["supports"] = r.Supports.Select(x => (object)(x ?? "")).ToList()
                }).ToList(),
                ["ringRemoves"] = m.RingRemoves.Select(r => (object)r.Cast<object>().ToList()).ToList(),
                ["panelChoice"] = m.PanelChoice.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (object)new JsonObject { ["key"] = kv.Key, ["type"] = kv.Value }).ToList()
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
                    Leader = co.Str("leader"), CloneOf = co.Str("cloneOf"), MoveAxis = co.ContainsKey("moveAxis") ? (int)co.Num("moveAxis") : -1, MoveSign = co.Num("moveSign", 1),
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
                    Neighbours = Ints(fo["neighbours"]), CloneOf = (int)fo.Num("cloneOf"), Panels = Strs(fo["panels"]), Dividers = Strs(fo["dividers"]), Other = Strs(fo["other"])
                };
                f.Sizes = f.Size;
                m.Frames.Add(f);
            }
            m.Warnings = Strs(j["warnings"]); m.Unplaced = Strs(j["unplaced"]); m.Removed = Strs(j["removed"]);
            if (j["cornerAdd"] != null) CornerAdd = j.Num("cornerAdd", CornerAdd);
            SetCornerAdd(m.Components.Values);
            m.Up = j["up"] == null ? 2 : (int)j.Num("up");
            m.ServiceSide = j["serviceSide"] == null ? 1 : (int)j.Num("serviceSide");
            foreach (var pc in j.Arr("panelChoice").OfType<JsonObject>()) m.PanelChoice[pc.Str("key")] = pc.Str("type");
            foreach (var ro in j.Arr("ringAdds").OfType<JsonObject>())
            {
                var r = new GrRingAdd
                {
                    Template = ro.Str("template"), Position = ro.Num("position"), Frame = (int)ro.Num("frame"), Single = ro.Bool("single"), Axis = (int)ro.Num("axis", -1),
                    Span = ro.Bool("span"), Along = (int)ro.Num("along", -1), FaceAxis = (int)ro.Num("faceAxis", -1), FaceSide = (int)ro.Num("faceSide")
                };
                var sups = ro.Arr("supports").Select(x => Convert.ToString(x, U.Inv)).ToList();
                for (int i = 0; i < Math.Min(2, sups.Count); i++) r.Supports[i] = sups[i];
                foreach (var pair in ro.Arr("names").OfType<List<object>>()) if (pair.Count == 2) r.Names[Convert.ToString(pair[0], U.Inv)] = Convert.ToString(pair[1], U.Inv);
                m.RingAdds.Add(r);
            }
            foreach (var rr in j.Arr("ringRemoves").OfType<List<object>>()) m.RingRemoves.Add(rr.Select(x => Convert.ToString(x, U.Inv)).ToList());
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
