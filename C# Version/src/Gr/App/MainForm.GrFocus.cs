// Editing one box in the twin: a click on a box greys out the others and shows the dimensions of
// that box, between every profile; a blue dimension is clicked to change it (a box size, or the gap
// to a P350, which moves that P350). The cut list's lengths are clicked to change them too. A P350
// is added across a face from a profile or from a P350 (horizontal between a P150 and a P350,
// vertical on an end face between two P150). On the right, the Output panel shows where Write new
// GR puts everything, following every edit.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    public partial class MainForm
    {
        Card grOutCard;
        TreeView grOutTree;

        // Esc ends box editing (when this window is the active one, not while a dialog is open).
        sealed class EscFilter : IMessageFilter
        {
            public Func<bool> Esc;
            public bool PreFilterMessage(ref Message m) => m.Msg == 0x0100 && (int)m.WParam == 0x1B && Esc();
        }

        void WireGrFocus()
        {
            Application.AddMessageFilter(new EscFilter
            {
                Esc = () =>
                {
                    if (mode != "GR" || gr == null || gr.Focus == 0 || ActiveForm != this || IsDisposed) return false;
                    EndFocus(); return true;
                }
            });
            BuildGrOutputUi();
        }

        // ------------------------------------------------------------------ box focus

        void FocusBox(int no)
        {
            var f = gr.Frame(no);
            if (gr.Focus == f.Id) return;
            gr.Focus = f.Id;
            Log.Dev($"GR: editing {f.Label}");
            UpdateDrawing();
        }

        void EndFocus()
        {
            if (gr == null || gr.Focus == 0) return;
            gr.Focus = 0;
            UpdateDrawing();
        }

        // A clicked dimension: a box size (outer, incl. corners) or the gap to a P350.
        // `at`: where it was clicked (screen), the small value box opens there.
        void EditDimension(string tag, Point at)
        {
            var p = tag.Split(new[] { ':' }, 6);
            if (p.Length >= 4 && p[1] == "size")
            {
                int no = int.Parse(p[2]); string dim = p[3];
                var f = gr.Frame(no);
                double outer = f.Get(dim) + Gr.CornerAdd;
                string text = DimValueBox.Ask(this, at, U.F(outer));
                if (string.IsNullOrWhiteSpace(text)) return;
                GrEdit("Edit box", () =>
                {
                    double v = Mm(text);
                    var changed = Gr.SetFrameSize(gr, no, dim, v - Gr.CornerAdd, chkShared.Checked);
                    if (changed.Count > 0) Log.Write($"GR: outer {dim} = {U.F(v)} mm (P150 {U.F(v - Gr.CornerAdd)}) on {string.Join(", ", changed)}");
                });
            }
            else if (p.Length == 6 && p[1] == "gap")
            {
                int axis = int.Parse(p[2]), sign = int.Parse(p[3]); double now = U.P(p[4]); string name = p[5];
                string text = DimValueBox.Ask(this, at, U.F(now));
                if (string.IsNullOrWhiteSpace(text)) return;
                GrEdit("Move divider", () =>
                {
                    double v = Mm(text);
                    string moved = Gr.MoveDivider(gr, name, axis, (v - now) * sign);
                    Log.Write($"GR: gap {U.F(now)} -> {U.F(v)} mm: {moved} now at {U.F(gr.Components[moved].Position ?? 0)} mm");
                });
            }
        }

        static double Mm(string text)
        {
            try { return U.P(text.Trim().Replace("mm", "").Trim()); } catch { throw new SizerException($"'{text}' is not a number in mm."); }
        }

        // A click on the P150 or P350 row of the cut list: its lengths, each to change.
        void CutMenu(string kind, Point at)
        {
            var menu = Glass.Menu();
            menu.Items.Add(new ToolStripMenuItem(kind == "frameProfile" ? "P150 frame profiles: change a length" : "P350 dividers: change a length") { Enabled = false });
            foreach (var g in Gr.CutGroups(gr, kind))
            {
                var item = new ToolStripMenuItem($"{g.Base}   {U.Times}{g.Count}");
                var grp = g;
                item.Click += (s, e) =>
                {
                    string text = SetupTextDialog.Prompt(this, "Change a length", $"New length of the {grp.Count} x {grp.Base}, in mm.\n{Gr.CutEffect(gr, kind, grp.Base)}.", U.F(grp.Length));
                    if (string.IsNullOrWhiteSpace(text)) return;
                    GrEdit("Change length", () =>
                    {
                        double v = Mm(text);
                        var done = Gr.SetCutLength(gr, kind, grp.Base, v);
                        Log.Write(done.Count > 0 ? $"GR: {grp.Base} -> {U.F(v)} mm: {string.Join(", ", done)}" : $"GR: {grp.Base} is {U.F(v)} mm already");
                    });
                };
                menu.Items.Add(item);
            }
            menu.Show(twin, at);
        }

        // ------------------------------------------------------------------ P350 across a face

        // "Add a P350 across the <face> from here": from a profile (P150) or a P350 on an outside face of
        // box f, running along `along` (away from it), at `position` mm from the box's outer start along
        // the face's other axis; `near` (layout mm along `along`) picks the two supports it hangs between.
        ToolStripMenuItem SpanItem(GrFrame f, int faceAxis, int side, int along, double position, double near, string text)
        {
            string faceName = GrOpenings.FaceName(gr, faceAxis, side);
            int run = 3 - faceAxis - along;
            var item = new ToolStripMenuItem(text);
            if (GrOpenings.TouchesOtherBox(gr, f, faceAxis, side)) { item.Enabled = false; item.Text += " (inside the unit)"; return item; }
            var (lo, hi) = Gr.SupportsAround(Gr.Supports(gr, f, faceAxis, side, along, f.Pos[run] + position), near);
            if (lo == null) { item.Enabled = false; item.Text += " (needs the GR read from Inventor)"; return item; }
            bool milled = faceAxis == 2;   // top and bottom: P350 gefräst
            string template = Gr.SpanTemplate(gr, milled);
            if (template == null) { item.Enabled = false; item.Text += milled ? " (no P350 gefräst in this GR to copy)" : " (no P350 on joints in this GR to copy)"; return item; }
            item.Click += (s, e) =>
            {
                double len = Math.Round(hi.Lo - lo.Hi + Gr.SpanAddOf(gr.Components[template]));
                string answer = SetupTextDialog.Prompt(this, "Add a P350 across a face",
                    (milled
                        ? $"A copy of {Short(template)} (top and bottom take a P350 gefräst, no joints), on the {faceName} of {f.Label}, running along {"LWH"[along]} between {Short(lo.Name)} and {Short(hi.Name)} (about {U.F(len)} mm).\n" +
                          "In Inventor its end mates to the seat of the first support, its Out is flush with the outside.\n"
                        : $"A copy of {Short(template)} with its two joints, on the {faceName} of {f.Label}, running along {"LWH"[along]} between {Short(lo.Name)} and {Short(hi.Name)} (about {U.F(len)} mm).\n" +
                          "In Inventor its joints mate to their seats (joint Base to Seat_1 / Seat_2 of a P150, Seat_Side_A / Seat_Side_B of a P350).\n") +
                    $"Position in mm from the start of {f.Label} along {"LWH"[run]} (outer edge):", U.F(Math.Round(position)));
                if (string.IsNullOrWhiteSpace(answer)) return;
                GrEdit("Add P350", () =>
                {
                    double v = Mm(answer);
                    var add = Gr.AddSpan(gr, f.Id, faceAxis, side, along, v, near);
                    var c = gr.Components[add.Names.Values.First()];
                    Log.Write($"GR: new {c.Base} across the {faceName} of {f.Label} at {U.F(v)} mm along {"LWH"[run]}, between {Short(add.Supports[0])} and {Short(add.Supports[1])}: {c.Name} (made in Inventor on Write new GR)");
                });
            };
            return item;
        }

        // From a P150 edge: across each outside face it borders, away from it.
        void EdgeSpanItems(ContextMenuStrip menu, GrFrame f, (double[] A, double[] B, int Axis, (int Axis, int Side)[] Faces) e, int axis, double rel)
        {
            foreach (var face in e.Faces)
            {
                int along = 3 - axis - face.Axis;
                // which end of the face this profile is at, along `along`: start just inside it
                double edgeAt = e.A[GrDrawing.DrawAxis(along)];
                bool atStart = edgeAt - f.Pos[along] < (f.Size[along] + Gr.CornerAdd) / 2;
                double near = edgeAt + (atStart ? 120 : -120);
                string dir = GrOpenings.FaceName(gr, along, atStart ? 1 : 0);
                menu.Items.Add(SpanItem(f, face.Axis, face.Side, along, rel, near,
                    $"Add a P350 across the {GrOpenings.FaceName(gr, face.Axis, face.Side)} from here (toward {dir}, {(along == 2 ? "vertical" : "horizontal")})..."));
            }
        }

        // From a P350 on an outside face: across that face to either side of it.
        void DividerSpanItems(ContextMenuStrip menu, string name)
        {
            if (!gr.Components.TryGetValue(name, out var c)) return;
            var f = gr.ById(c.Frame); var b = GrOpenings.LayoutBox(gr, c);
            var face = Gr.FaceOf(gr, c);
            if (f == null || b == null || face.Axis < 0) return;
            var ext = Enumerable.Range(0, 3).Select(i => b.Value.Max[i] - b.Value.Min[i]).ToArray();
            int run = Array.IndexOf(ext, ext.Max()), along = 3 - run - face.Axis;
            double mid = Math.Round((b.Value.Min[run] + b.Value.Max[run]) / 2 - f.Pos[run]);
            foreach (int dirSide in new[] { 0, 1 })
            {
                double near = dirSide == 0 ? b.Value.Min[along] - 120 : b.Value.Max[along] + 120;
                menu.Items.Add(SpanItem(f, face.Axis, face.Side, along, mid, near,
                    $"Add a P350 from here toward {GrOpenings.FaceName(gr, along, dirSide)} ({(along == 2 ? "vertical" : "horizontal")}, on the {GrOpenings.FaceName(gr, face.Axis, face.Side)})..."));
            }
        }

        // ------------------------------------------------------------------ the Output panel

        void BuildGrOutputUi()
        {
            grOutTree = new TreeView
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Ink, Font = Theme.Small,
                ShowLines = true, ShowPlusMinus = true, ShowRootLines = false, FullRowSelect = true, HideSelection = true, ItemHeight = 20, ShowNodeToolTips = true
            };
            grOutCard = new Card("Output", subtitle: "where Write new GR puts everything") { Dock = DockStyle.Right, Width = 340, Visible = false };
            grOutCard.Controls.Add(grOutTree);
            split.Panel2.Controls.Add(grOutCard);
            grNewName.TextChanged += (s, e) => UpdateGrOutput();
            grPrefix.TextChanged += (s, e) => UpdateGrOutput();
        }

        static Color OutColor(string state)
        {
            switch (state)
            {
                case "new": return Theme.Accent;
                case "write": return Color.FromArgb(0xC2, 0x5A, 0x00);
                case "warn": return Theme.MacRed;
                case "exists": case "keep": return Theme.Muted;
                case "folder": return Theme.Ink;
                default: return Theme.InkSoft;
            }
        }

        void UpdateGrOutput()
        {
            if (grOutTree == null || mode != "GR") return;
            GrOutNode tree;
            try { tree = GrOutput.Tree(gr, grNewName.Text, grPrefix.Text); }
            catch (Exception e) { tree = new GrOutNode { Text = "Output could not be worked out", State = "warn", Note = e.Message }; Log.Dev("GR output: " + e); }
            grOutTree.BeginUpdate();
            try
            {
                grOutTree.Nodes.Clear();
                TreeNode Make(GrOutNode n)
                {
                    string icon = n.State == "folder" ? "\U0001F4C1 " : n.State == "new" ? "+ " : n.State == "write" ? "! " : n.State == "warn" ? "⚠ " : n.State == "change" ? "• " : "  ";
                    var t = new TreeNode(icon + n.Text + (n.Note != "" && n.State != "change" ? "  — " + n.Note : "")) { ForeColor = OutColor(n.State), ToolTipText = n.Note != "" ? n.Text + "\n" + n.Note : n.Text };
                    if (n.State == "folder") t.NodeFont = new Font(Theme.Small, FontStyle.Bold);
                    foreach (var c in n.Children) t.Nodes.Add(Make(c));
                    return t;
                }
                grOutTree.Nodes.Add(Make(tree));
                grOutTree.ExpandAll();
                if (grOutTree.Nodes.Count > 0) grOutTree.TopNode = grOutTree.Nodes[0];
            }
            finally { grOutTree.EndUpdate(); }
            grOutCard.Subtitle = gr == null ? "where Write new GR puts everything" : gr.Edited ? "where Write new GR puts everything (edited)" : "where Write new GR puts everything";
            grOutCard.Invalidate();
        }
    }
}
