// Where Write new GR puts everything, as a tree that follows every edit: the new GR assembly next
// to the source GR, the old one kept in OldVersions\, new profile lengths and panels in
// Einzelteile\, and what changes inside the GR. Read only: nothing here touches Inventor or a file.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InventorSizer.Core
{
    public class GrOutNode
    {
        public string Text, Note = "", State = "";   // State: folder, new, write, keep, exists, change, warn
        public List<GrOutNode> Children = new List<GrOutNode>();
        public GrOutNode Add(string text, string state, string note = "") { var n = new GrOutNode { Text = text, State = state, Note = note ?? "" }; Children.Add(n); return n; }
    }

    public static class GrOutput
    {
        public static GrOutNode Tree(GrModel m, string newName, string prefix)
        {
            newName = (newName ?? "").Trim(); prefix = (prefix ?? "").Trim();
            if (m == null) return new GrOutNode { Text = "No GR loaded", State = "warn", Note = "choose a preset, open a GR or read one from Inventor" };
            string src = m.SourcePath ?? "";
            bool found = src != "" && File.Exists(src);
            string folder = src != "" ? Path.GetDirectoryName(src) : "";
            string ext = src != "" ? Path.GetExtension(src) : ".iam";
            var root = new GrOutNode
            {
                Text = folder != "" ? folder : "(the folder of the GR open in Inventor)", State = "folder",
                Note = found ? "next to the source GR" : src != "" ? "not found on this PC: the GR open in Inventor is used" : "the GR open in Inventor is used"
            };

            // the new GR
            bool valid = newName != "" && newName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            string target = valid && folder != "" ? Path.Combine(folder, newName + ext) : null;
            bool self = valid && (newName == m.Name || (target != null && string.Equals(target, src, StringComparison.OrdinalIgnoreCase)));
            bool exists = target != null && File.Exists(target);
            if (!valid) root.Add(newName == "" ? "New GR name?" : newName + ext, "warn", newName == "" ? "enter a New GR name in the ribbon" : "not a valid file name");
            else if (self) root.Add(newName + ext, "write", "the GR being edited: overwritten (you are asked first)");
            else if (exists) root.Add(newName + ext, "write", "exists: overwritten (you are asked first)");
            else root.Add(newName + ext, "new", $"new GR, a copy of {m.Name}{ext} with the changes; {m.Name}{ext} stays as it is");
            if (valid && (self || exists))
                root.Add("OldVersions", "folder").Add($"{newName}.<date-time>{ext}", "new", "the old file, kept");

            // Einzelteile: profile lengths and panels
            string parts = folder != "" ? Path.Combine(folder, "Einzelteile") : "";
            var ein = new GrOutNode { Text = "Einzelteile", State = "folder" };
            string P(string name) => prefix != "" ? $"{prefix} {name}" : name;
            var pending = Gr.Pending(m);
            var files = new Dictionary<string, (int Count, string From)>();
            foreach (var g in pending.Profiles) files[g.To] = (g.Names.Count + (files.TryGetValue(g.To, out var x) ? x.Count : 0), g.From);
            // new parts (copied boxes and P350) whose length is not in Inventor yet
            var have = new HashSet<string>(m.Components.Values.Where(c => !string.IsNullOrEmpty(c.OrigBase)).Select(c => c.OrigBase));
            foreach (var c in m.Components.Values.Where(c => string.IsNullOrEmpty(c.OrigBase) && (c.Kind == "frameProfile" || c.Kind == "divider") && !have.Contains(c.Base)))
                files[c.Base] = ((files.TryGetValue(c.Base, out var y) ? y.Count : 0) + 1, y.From ?? "a copy");
            foreach (var kv in files.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                string file = P(kv.Key) + ".ipt";
                bool there = parts != "" && File.Exists(Path.Combine(parts, file));
                ein.Add(file, there ? "exists" : "new", (there ? "exists: used as it is" : $"new length, from {kv.Value.From}") + $"  ({kv.Value.Count} x)");
            }
            List<GrOpening> planned;
            try { planned = GrOpenings.Planned(GrOpenings.Find(m)); } catch { planned = new List<GrOpening>(); }
            foreach (var g in planned.GroupBy(o => P(o.Panel.Name)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var o = g.First();
                string file = g.Key + ".iam";
                bool there = parts != "" && File.Exists(Path.Combine(parts, file));
                var n = ein.Add(file, o.Panel.Problem != null ? "warn" : there ? "exists" : "new",
                    (o.Panel.Problem ?? (there ? "exists: used as it is" : $"new {o.Panel.Type} panel ({o.Panel.Inner}) with its parts")) + $"  ({g.Count()} x: {string.Join(", ", g.Select(x => x.Id))})");
            }
            if (ein.Children.Count == 0) ein.Note = "nothing new: every part is in Inventor already";
            root.Children.Add(ein);

            // inside the GR
            var inside = new GrOutNode { Text = $"Inside {(valid ? newName : "the new GR")}", State = "folder" };
            foreach (var b in pending.Boxes) inside.Add(b, "change");
            foreach (var r in pending.Rings) inside.Add(r, "change");
            foreach (var g in pending.Profiles) inside.Add($"{g.Names.Count} x {g.From} -> {P(g.To)}", "change", "replaced");
            foreach (var d in pending.Dividers) inside.Add($"{d.Name}: {U.F(d.From)} -> {U.F(d.To)} mm", "change", d.Constraint);
            foreach (var p in pending.Panels) inside.Add($"panel {p.Name}: needs {p.To}", "warn", "set its opening to a panel type to replace it");
            if (planned.Count > 0) inside.Add($"{planned.Count} panel(s) placed and mated", "change", string.Join(", ", planned.Select(o => o.Id)));
            foreach (var b in pending.Blocked) inside.Add(b, "warn", "not written until this is fixed");
            if (inside.Children.Count == 0) inside.Note = m.Edited ? "" : "no change yet";
            root.Children.Add(inside);
            return root;
        }
    }
}
