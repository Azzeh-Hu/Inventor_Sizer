// The GR twin as a 3D view: turned, panned and zoomed with the mouse like in Inventor, and the
// right-click menus on a box: P350 rings and single P350 where the profile was clicked, and new
// boxes on any side of any box. Left, right, front and back are as seen from the front, which
// is the service side (TP), the side the twin shows first.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    public partial class MainForm
    {
        GrCamera grCam = new GrCamera();
        Point grDown; MouseButtons grButton; bool grDragging, grDragged;
        FlatButton btnAddB;

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);

        // The mouse wheel goes to the focused control, which a picture box never is: a filter sends
        // the wheel over the twin to the twin (zoom), and leaves it alone everywhere else.
        sealed class WheelFilter : IMessageFilter
        {
            public Func<Point, int, bool> Wheel;
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x020A) return false;
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                return Wheel(Cursor.Position, delta);
            }
        }

        void WireGrView()
        {
            twin.MouseDown += (s, e) =>
            {
                if (mode != "GR" || gr == null) return;
                grDown = e.Location; grButton = e.Button; grDragging = false; grDragged = false;
            };
            twin.MouseMove += (s, e) =>
            {
                if (mode != "GR" || gr == null || e.Button == MouseButtons.None || (e.Button & (MouseButtons.Left | MouseButtons.Middle)) == 0) return;
                int dx = e.X - grDown.X, dy = e.Y - grDown.Y;
                if (!grDragging && Math.Abs(dx) + Math.Abs(dy) < 5) return;
                if (!grDragging) { grDragging = true; grDragged = true; }
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                // left: turn; middle or Shift + left: pan; Shift + middle: turn (as in Inventor)
                bool pan = grButton == MouseButtons.Middle ? !shift : shift;
                if (grCam.Scale <= 0) return;
                if (pan) { grCam.PanX += dx; grCam.PanY += dy; twin.Cursor = Cursors.SizeAll; }
                else { grCam.Orbit(dx, dy); twin.Cursor = Cursors.NoMove2D; }
                grDown = e.Location;
                UpdateDrawing();
            };
            twin.MouseUp += (s, e) => { if (grDragging) { grDragging = false; twin.Cursor = Cursors.Default; } };
            twin.MouseDoubleClick += (s, e) =>
            {
                if (mode != "GR" || gr == null || GrHit(e.Location).Kind != null) return;
                grCam.Fit(); UpdateDrawing();   // double-click on empty space: everything in view again
            };
            twin.Resize += (s, e) => { if (mode == "GR") grCam.Fit(); };
            viewBox.SelectedIndexChanged += (s, e) =>
            {
                if (mode != "GR") return;
                grCam.Set((string)viewBox.SelectedItem, gr?.ServiceSide ?? 1);
                UpdateDrawing();
            };
            Application.AddMessageFilter(new WheelFilter
            {
                Wheel = (p, delta) =>
                {
                    if (mode != "GR" || gr == null || !twin.Visible || IsDisposed || WindowFromPoint(p) != twin.Handle) return false;
                    var at = twin.PointToClient(p);
                    grCam.Zoom(Math.Pow(1.18, delta / 120.0), at.X, at.Y);
                    UpdateDrawing();
                    return true;
                }
            });
        }

        // A new GR, or the service side swapped: the twin starts from the chosen view again.
        void ResetGrView()
        {
            grCam.Set((string)viewBox.SelectedItem ?? "Isometric", gr?.ServiceSide ?? 1);
        }

        // Left / right / front / back as seen from the front (the service side) -> the model's sides
        // (Right = + length, Front = + width).
        string ModelSide(string seen)
        {
            bool plus = (gr?.ServiceSide ?? 1) >= 0;
            switch (seen)
            {
                case "Front": return plus ? "Front" : "Back";
                case "Back": return plus ? "Back" : "Front";
                case "Right": return plus ? "Right" : "Left";
                default: return plus ? "Left" : "Right";
            }
        }

        void AddBoxAt(int no, string seen) => GrEdit("Add box", () =>
        {
            var f = Gr.AddFrame(gr, no, ModelSide(seen));
            Log.Write($"GR: added {f.Label} ({seen.ToLower()} of Box {no - 1}, seen from the service side), outer {U.F(f.Lx + Gr.CornerAdd)} x {U.F(f.Wy + Gr.CornerAdd)} x {U.F(f.Hz + Gr.CornerAdd)} mm");
        });

        void BoxItems(ContextMenuStrip menu, int no)
        {
            var add = new ToolStripMenuItem("Add a box");
            foreach (var side in new[] { "Left", "Right", "Front", "Back" })
            {
                var it = new ToolStripMenuItem(side + (side == "Front" ? " (service side)" : ""));
                string sd = side;
                it.Click += (s, e) => AddBoxAt(no, sd);
                add.DropDownItems.Add(it);
            }
            bool editing = gr.Focus == gr.Frame(no).Id;
            var focus = new ToolStripMenuItem(editing ? "Done editing this box" : "Edit this box (dimensions)");
            focus.Click += (s, e) => { if (editing) EndFocus(); else FocusBox(no); };
            menu.Items.Add(focus);
            var sizes = new ToolStripMenuItem("Edit sizes...");
            sizes.Click += (s, e) => { popBoxes.Popup(PopAt(0)); SelectRow(grGrid, "Box", gr.Frame(no).Label, null); };
            var remove = new ToolStripMenuItem("Remove this box");
            remove.Click += (s, e) => GrEdit("Remove box", () => { Gr.RemoveFrame(gr, no); Log.Write($"GR: removed Box {no - 1} with its panels and dividers"); });
            menu.Items.AddRange(new ToolStripItem[] { add, sizes, remove });
        }

        void BoxMenu(int no, Point at)
        {
            var menu = Glass.Menu();
            menu.Items.Add(new ToolStripMenuItem(gr.Frame(no).Label) { Enabled = false });
            BoxItems(menu, no);
            menu.Show(twin, at);
        }

        // Where along an edge of the twin a point is (0 = its first end, 1 = the other).
        double AlongEdge(string tag, Point p)
        {
            var s = shapes?.FirstOrDefault(x => x.Tag == tag && x.Points != null && x.Points.Length >= 4);
            if (s == null) return 0.5;
            double ax = s.Points[0], ay = s.Points[1], dx = s.Points[2] - ax, dy = s.Points[3] - ay, len2 = dx * dx + dy * dy;
            return len2 < 1e-9 ? 0.5 : Math.Max(0, Math.Min(1, ((p.X - ax) * dx + (p.Y - ay) * dy) / len2));
        }

        // Right-click on a profile of a box: a P350 ring across the box here (rings run around the
        // length), a single P350 on one of the two faces the profile edges, or a box next to this one.
        void EdgeMenu(int no, int edge, Point at)
        {
            var f = gr.Frame(no);
            var b = GrDrawing.Boxes(gr).First(x => x.F.No == no);
            var e = GrDrawing.Edge(b, edge);
            int axis = GrDrawing.GrAxis(e.Axis);
            double t = AlongEdge($"edge:{no}:{edge}", at);
            double rel = Math.Round(e.A[e.Axis] + (e.B[e.Axis] - e.A[e.Axis]) * t - f.Pos[axis]);
            string axisName = "LWH"[axis].ToString();
            var menu = Glass.Menu();
            menu.Items.Add(new ToolStripMenuItem($"{f.Label}: P150 along {axisName}, about {U.F(rel)} mm from its start") { Enabled = false });

            var ring = new ToolStripMenuItem("Add a P350 ring here...");
            if (axis != 0) { ring.Enabled = false; ring.Text = "Add a P350 ring here (rings go around the length: right-click a profile along L)"; }
            else ring.Click += (s, ev) => RingHere(f, rel);
            menu.Items.Add(ring);

            var outer = GrOpenings.Find(gr).Where(o => o.Frame == f).Select(o => (o.Axis, o.Side)).Distinct().ToList();
            foreach (var face in e.Faces)
            {
                string faceName = GrOpenings.FaceName(gr, face.Axis, face.Side);
                int along = 3 - axis - face.Axis;
                var templates = Gr.SingleTemplates(gr, f.Id, face.Axis, face.Side, along);
                var it = new ToolStripMenuItem($"Add a single P350 on the {faceName} here...");
                if (!outer.Contains(face)) { it.Enabled = false; it.Text = $"Add a single P350 on the {faceName} (inside the unit)"; }
                else if (templates.Count == 0) { it.Enabled = false; it.Text = $"Add a single P350 on the {faceName} (no P350 like that in this GR to copy)"; }
                else
                {
                    var fc = face;
                    it.Click += (s, ev) =>
                    {
                        string text = SetupTextDialog.Prompt(this, "Add a single P350",
                            $"A copy of {templates[0]} on the {faceName} of {f.Label}, running along {"LWH"[along]}.\nIt is grounded in Inventor where it is placed.\nPosition in mm from the start of {f.Label} along {axisName} (outer edge):", U.F(rel));
                        if (string.IsNullOrWhiteSpace(text)) return;
                        GrEdit("Add P350", () =>
                        {
                            double v; try { v = U.P(text); } catch { throw new SizerException($"'{text}' is not a number in mm."); }
                            var add = Gr.AddSingle(gr, templates[0], f.Id, axis, v);
                            Log.Write($"GR: new P350 like {templates[0]} on the {faceName} of {f.Label} at {U.F(v)} mm along {axisName}: {string.Join(", ", add.Names.Values)} (made in Inventor on Write new GR)");
                        });
                    };
                }
                menu.Items.Add(it);
            }
            EdgeSpanItems(menu, f, e, axis, rel);
            menu.Items.Add(new ToolStripSeparator());
            BoxItems(menu, no);
            menu.Show(twin, at);
        }

        // A ring for box f at about `rel` mm along its length: a copy of a ring of this box, or of
        // another box when this one has none.
        void RingHere(GrFrame f, double rel)
        {
            var leaders = gr.Components.Values.Where(c => c.Kind == "divider" && c.PosConstraint != "" && c.CloneOf == "" && c.MoveAxis == 0 && Gr.RingOf(gr, c.Name) == c.Name).ToList();
            bool FromProfile(GrComponent c) => gr.Components.TryGetValue(c.PosPartner, out var p) && (p.Kind == "frameProfile" || p.Kind == "corner");
            var lead = leaders.Where(c => c.Frame == f.Id).OrderBy(c => c.Name).FirstOrDefault()
                    ?? leaders.Where(FromProfile).OrderBy(c => gr.ById(c.Frame)?.No ?? 0).ThenBy(c => c.Name).FirstOrDefault();
            if (lead == null) { Toast.Show(this, ToastKind.Error, "Add ring", "There is no P350 ring in this GR that is placed from a frame profile, so there is nothing to copy."); return; }
            var from = gr.ById(lead.Frame);
            var lb = GrOpenings.LayoutBox(gr, lead);
            double guess = rel;
            if (lb != null && lead.Position != null)
            {
                double rel0 = (lb.Value.Min[0] + lb.Value.Max[0]) / 2 - from.Pos[0];
                if (from != f && gr.Components.TryGetValue(lead.PosPartner, out var pp))
                {
                    var pb = GrOpenings.ProfileBox(gr, from, pp.Name, -1);
                    if (pb != null && (pb.Value.Min[0] + pb.Value.Max[0]) / 2 - from.Pos[0] > (from.Lx + Gr.CornerAdd) / 2) rel0 += f.Lx - from.Lx;
                }
                guess = Math.Round(lead.Position.Value + (rel - rel0) * lead.MoveSign);
            }
            string text = SetupTextDialog.Prompt(this, "Add a P350 ring",
                $"A copy of the ring of {lead.Name} ({Gr.RingMembers(gr, lead.Name).Count} P350 and their joints){(from != f ? $" from {from.Label}" : "")} into {f.Label}.\n" +
                $"Position in mm, measured like {lead.Name}: {U.F(lead.Position ?? 0)} mm from {Short(lead.PosPartner)}" + (from != f ? " (its match in " + f.Label + ")" : "") + ":", U.F(guess));
            if (string.IsNullOrWhiteSpace(text)) return;
            GrEdit("Add ring", () =>
            {
                double v; try { v = U.P(text); } catch { throw new SizerException($"'{text}' is not a number in mm."); }
                var add = Gr.AddRing(gr, lead.Name, v, f.Id);
                Log.Write($"GR: new P350 ring like {lead.Name} in {f.Label} at {U.F(v)} mm: {string.Join(", ", add.Names.Values)} (made in Inventor on Write new GR)");
            });
        }

        // The divider menu's "copy this ring to another box".
        ToolStripMenuItem CopyRingItem(string divider)
        {
            var item = new ToolStripMenuItem("Copy this ring to");
            string leader = Gr.RingOf(gr, divider);
            if (!gr.Components.TryGetValue(leader, out var lc)) { item.Enabled = false; return item; }
            foreach (var f in gr.Frames.Where(f => f.Id != lc.Frame).OrderBy(f => f.No))
            {
                var it = new ToolStripMenuItem(f.Label);
                var target = f;
                it.Click += (s, e) =>
                {
                    string text = SetupTextDialog.Prompt(this, "Copy a P350 ring",
                        $"A copy of the ring of {leader} into {target.Label}.\nPosition in mm, measured like {leader} ({U.F(lc.Position ?? 0)} mm from {Short(lc.PosPartner)}; in {target.Label} from its match):", U.F(lc.Position ?? 0));
                    if (string.IsNullOrWhiteSpace(text)) return;
                    GrEdit("Add ring", () =>
                    {
                        double v; try { v = U.P(text); } catch { throw new SizerException($"'{text}' is not a number in mm."); }
                        var add = Gr.AddRing(gr, leader, v, target.Id);
                        Log.Write($"GR: new P350 ring like {leader} in {target.Label} at {U.F(v)} mm: {string.Join(", ", add.Names.Values)} (made in Inventor on Write new GR)");
                    });
                };
                item.DropDownItems.Add(it);
            }
            if (item.DropDownItems.Count == 0) item.Enabled = false;
            return item;
        }
    }
}
