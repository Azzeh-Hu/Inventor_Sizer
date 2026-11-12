// Product types: one JSON file per family in .\standards (FP.json, TP.json, RP.json). A type
// sets the name prefix, the height it is recognised by, the master assembly, type defaults,
// the master setup (user parameters + formulas per role), the hinge / handle holes and the parts.
//
// Assembly names look like  [project ]<prefix><W>x<L>x<H>[ tail],  e.g. "6357-3 TP-519x1194x42".
// The project text in front of the prefix (the "lead") is kept when the size or type changes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InventorSizer.Core
{
    public class StandardPart
    {
        public string Suffix, SplitSuffix, Description, Role;
        public int Qty;
    }

    public class Standard
    {
        public string Id, Name, Family, Prefix, File = "", FilePath = "";
        // One master per sub type: "1T" (one inner panel) and "2T" (inner panel in 2 parts).
        public Dictionary<string, string> Masters = new Dictionary<string, string>();
        public string MasterFor(string sub) => Masters.TryGetValue(sub, out var m) ? m : "";
        public bool HasMaster(string sub) => MasterFor(sub) != "" && System.IO.File.Exists(MasterFor(sub));
        public List<StandardPart> Parts = new List<StandardPart>();
        public double Height;
        public Dictionary<string, double> Defaults = new Dictionary<string, double>();
        public int MinHolesL;
        public string SplitAxis = "auto";
        public JsonObject Setup;            // per role: { user: [{name,value,unit}], formulas: [{name,formula}] }
        public int SetupVersion => Setup == null ? 0 : (int)Setup.Num("version");   // 0 = the master is not checked
        public HoleTemplate Hardware;       // hinge / handle holes
        public Dictionary<string, (string Param, string PitchParam)> HardwareCounts = new Dictionary<string, (string, string)>();

        public override string ToString() => $"{Name}   [{Prefix}...]";

        public IEnumerable<string> PartSuffixes(string role)
        {
            foreach (var p in Parts.Where(p => p.Role == role))
            {
                if (!string.IsNullOrEmpty(p.SplitSuffix)) yield return p.SplitSuffix;
                yield return p.Suffix;
            }
        }
    }

    public class PanelName
    {
        public string Lead = "", Prefix = "", Token = "", Tail = "";
        public Standard Standard;
    }

    public class BomRow { public string Pos, Part, Desc, Qty, Size; }

    public class Bom
    {
        public string Title, Note;
        public List<BomRow> Rows = new List<BomRow>();
    }

    public static class Standards
    {
        public static List<Standard> Read(string dir, string root)
        {
            var list = new List<Standard>();
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    JsonObject json;
                    try { json = Json.Parse(System.IO.File.ReadAllText(file)) as JsonObject; }
                    catch (Exception e) { Log.Write($"Standard {Path.GetFileName(file)} could not be read: {e.Message}"); continue; }
                    if (json == null || !json.ContainsKey("variants")) continue;   // other settings files, e.g. GR.json
                    foreach (var vo in json.Arr("variants"))
                    {
                        var v = vo as JsonObject;
                        if (v == null || !v.Bool("enabled", true)) continue;
                        if (v.Str("id") == "" || v.Str("prefix") == "" || v.Arr("parts").Count == 0)
                        {
                            Log.Write($"Standard {Path.GetFileName(file)}: a type is missing id, prefix or parts - skipped");
                            continue;
                        }
                        var s = new Standard
                        {
                            Id = v.Str("id"), Name = v.Str("name"), Family = json.Str("family"), Prefix = v.Str("prefix"),
                            File = Path.GetFileName(file), FilePath = file, Height = v.Num("height"), MinHolesL = (int)v.Num("minHolesL"),
                            SplitAxis = v.Str("splitAxis", "auto"), Setup = v.Obj("setup")
                        };
                        foreach (var po in v.Arr("parts").OfType<JsonObject>())
                        {
                            // Rule for every type: a split inner panel is a file ending in <suffix>-2T.
                            var split = po.Str("splitSuffix");
                            if (split == "" && po.Str("role") == "inner") split = po.Str("suffix") + "-2T";
                            s.Parts.Add(new StandardPart { Suffix = po.Str("suffix"), SplitSuffix = split, Description = po.Str("description"), Role = po.Str("role"), Qty = (int)po.Num("qty", 1) });
                        }
                        var defs = v.Obj("defaults");
                        if (defs != null) foreach (var k in defs.OrderedKeys) s.Defaults[k] = defs.Num(k);
                        if (v.Obj("hardware") != null) s.Hardware = HoleTemplate.FromJson(v.Obj("hardware"));
                        var counts = v.Obj("hardwareCounts");
                        if (counts != null) foreach (var k in counts.OrderedKeys) { var c = counts.Obj(k); s.HardwareCounts[k] = (c.Str("param"), c.Str("pitchParam")); }
                        foreach (var sub in SubTypes) s.Masters[sub] = ResolveMaster(v.Obj("masters")?.Str(sub), s.Id, sub, root);
                        list.Add(s);
                    }
                }
            }
            if (list.Count == 0)
            {
                var s = new Standard { Id = "FP", Name = "FP - Festes Paneel", Family = "FP", Prefix = "FP-", File = "(built in)", Height = 47 };
                s.Parts.Add(new StandardPart { Suffix = "-Au", Description = "Outer tray (Au)", Role = "outer", Qty = 1 });
                s.Parts.Add(new StandardPart { Suffix = "-In", SplitSuffix = "-In-2T", Description = "Inner panel (In)", Role = "inner", Qty = 1 });
                list.Add(s);
                Log.Write("No standards found in .\\standards - using the built-in FP standard");
            }
            return list;
        }

        public static readonly string[] SubTypes = { "1T", "2T" };
        public static string SubOf(int split) => split >= 2 ? "2T" : "1T";

        // The path in the file (relative = to the app folder), else the first assembly in
        // masters\<type id>\<sub type>\ next to the app.

        public static string ResolveMaster(string stored, string id, string sub, string root)
        {
            string master = (stored ?? "").Replace('\\', Path.DirectorySeparatorChar);
            if (master != "" && !Path.IsPathRooted(master)) master = Path.Combine(root, master);
            if (master == "" || !System.IO.File.Exists(master))
            {
                var dir = Path.Combine(root, "masters", id, sub);
                if (Directory.Exists(dir))
                {
                    var found = Directory.GetFiles(dir, "*.iam").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (found != null) master = found;
                }
            }
            return master;
        }

        // Writes a change into the type's variant in its file (master, hardware) and keeps the rest.
        public static void UpdateVariant(Standard s, Action<JsonObject> change)
        {
            var json = (JsonObject)Json.Parse(System.IO.File.ReadAllText(s.FilePath));
            var v = json.Arr("variants").OfType<JsonObject>().First(x => x.Str("id") == s.Id);
            change(v);
            System.IO.File.WriteAllText(s.FilePath, Json.Write(json), new UTF8Encoding(false));
        }

        public static void SetMaster(Standard s, string sub, string path, string root)
        {
            var r = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var store = path.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? path.Substring(r.Length) : path;
            UpdateVariant(s, v =>
            {
                if (v.Obj("masters") == null) v["masters"] = new JsonObject();
                v.Obj("masters")[sub] = store;
                v.Remove("master");   // the single master of older versions
            });
            s.Masters[sub] = path;
            Log.Write($"Master of {s.Name}, sub type {sub}: {path} (saved in {s.File})");
        }

        public static void UpsertSetupAssignment(Standard s, SetupAssignment assignment)
        {
            if (s == null || s.Setup == null) throw new SizerException("The selected type has no setup data to update.");
            string key = assignment.IsFormula ? "formulas" : "user";
            var roleSetup = s.Setup.Obj(assignment.Role);
            if (roleSetup == null) throw new SizerException($"The {assignment.Role} role has no setup section in {s.File}.");
            var items = roleSetup.Arr(key);
            JsonObject entry = items.OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x.Str("name"), assignment.Target, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new JsonObject();
                items.Add(entry);
                roleSetup[key] = items;
            }
            entry["name"] = assignment.Target;
            entry[assignment.IsFormula ? "formula" : "value"] = assignment.Expression;
            if (!assignment.IsFormula) entry["unit"] = assignment.Unit;
            if (assignment.PurposeDeclared) entry["label"] = assignment.Purpose;

            UpdateVariant(s, v =>
            {
                v["setup"] = s.Setup;
            });
        }

        // Splits an assembly name around its size. The type is the one whose prefix sits directly
        // in front of the size (longest wins); without a known prefix, the type with the same
        // height. Returns null when the name has no size.
        public static PanelName Split(IEnumerable<Standard> standards, string name)
        {
            var m = U.SizeRegex.Match(name ?? "");
            if (!m.Success) return null;
            string before = name.Substring(0, m.Index);
            Standard best = null;
            foreach (var s in standards)
                if (before.EndsWith(s.Prefix, StringComparison.OrdinalIgnoreCase) && (best == null || s.Prefix.Length > best.Prefix.Length)) best = s;
            string prefix = "";
            if (best != null) prefix = before.Substring(before.Length - best.Prefix.Length);
            else
            {
                double h = U.P(m.Groups[3].Value);
                best = standards.FirstOrDefault(s => s.Height > 0 && s.Height == h);
            }
            return new PanelName { Lead = before.Substring(0, before.Length - prefix.Length), Prefix = prefix, Token = m.Value, Tail = name.Substring(m.Index + m.Length), Standard = best };
        }

        public static Standard ForName(IEnumerable<Standard> standards, string name) => Split(standards, name)?.Standard;

        public static string WithPrefix(IEnumerable<Standard> standards, string name, string prefix)
        {
            var p = Split(standards, name);
            return p == null ? name : p.Lead + prefix + p.Token + p.Tail;
        }

        // File name or Part Number for the new name: the size is swapped, and so is the project
        // lead when the text starts with it (or with the size, when there was no lead).
        public static string ConvertText(string text, PanelName oldN, PanelName newN)
        {
            var o = text.Replace(oldN.Token, newN.Token);
            if (oldN.Lead != newN.Lead)
            {
                if (oldN.Lead != "" && o.StartsWith(oldN.Lead, StringComparison.OrdinalIgnoreCase)) o = newN.Lead + o.Substring(oldN.Lead.Length);
                else if (oldN.Lead == "" && o.StartsWith(newN.Token)) o = newN.Lead + o;
            }
            return o;
        }

        // Inner-panel file name for the split state: -In <-> -In-2T.
        public static string ConvertSplitName(Standard s, string baseName, int split)
        {
            foreach (var p in s.Parts.Where(p => p.Role == "inner" && !string.IsNullOrEmpty(p.SplitSuffix)))
            {
                var want = split >= 2 ? p.SplitSuffix : p.Suffix;
                foreach (var have in new[] { p.SplitSuffix, p.Suffix })
                    if (baseName.EndsWith(have, StringComparison.OrdinalIgnoreCase)) return baseName.Substring(0, baseName.Length - have.Length) + want;
            }
            return baseName;
        }

        // Bill of materials for the drawing; a split inner panel is listed as 2 halves.
        public static Bom GetBom(Standard s, Dictionary<string, double> values, double thickness, string name, HoleLayout layout)
        {
            string x = U.Times;
            double W = Get(values, "Width"), L = Get(values, "Length"), H = Get(values, "Height"), C = Get(values, "Clearance");
            var m = U.SizeRegex.Match(name ?? "");
            string token = m.Success ? m.Value : $"{U.F(W)}x{U.F(L)}x{U.F(H)}";
            string lead = "";
            if (m.Success)
            {
                var before = name.Substring(0, m.Index);
                if (before.EndsWith(s.Prefix, StringComparison.OrdinalIgnoreCase)) lead = before.Substring(0, before.Length - s.Prefix.Length);
            }
            double inset = 2 * thickness + 2 * C;
            int split = layout?.Split ?? 1;
            var bom = new Bom { Title = $"Bill of materials - {name} ({s.Name})" };
            int pos = 0;
            foreach (var p in s.Parts)
            {
                pos++;
                string suffix = p.Suffix, size = "";
                int qty = p.Qty;
                if (p.Role == "outer") size = $"{U.F(W)} {x} {U.F(L)} {x} {U.F(H)}";
                else if (p.Role == "inner")
                {
                    if (split >= 2) { qty = p.Qty * split; if (!string.IsNullOrEmpty(p.SplitSuffix)) suffix = p.SplitSuffix; }
                    size = $"{U.F((W - inset) / split)} {x} {U.F(L - inset)} {x} {U.F(H - thickness)}";
                }
                bom.Rows.Add(new BomRow { Pos = pos.ToString(), Part = lead + token + suffix, Desc = p.Description, Qty = qty.ToString(), Size = size });
            }
            if (layout != null)
            {
                int nL = (int)Math.Round(layout.Length.Holes), nW = (int)Math.Round(layout.Width.Holes);
                bom.Note = $"Holes {U.Dia}{U.F(Get(values, "HoleDia"))} per tray: 2 {x} {nL} (length walls) + 2 {x} {nW} (width walls) = {2 * nL + 2 * nW}";
            }
            return bom;
        }

        public static double Get(Dictionary<string, double> v, string k, double def = 0) => v.TryGetValue(k, out var x) ? x : def;
    }
}
