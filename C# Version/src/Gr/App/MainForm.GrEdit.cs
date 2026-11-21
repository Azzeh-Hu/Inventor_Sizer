// GR editing in the window: three floating glass windows (Boxes = sizes, Dividers, Openings)
// opened from the ribbon or by clicking the twin, and the twin itself as an editor: click an
// opening, divider or box to edit it, right-click an opening to pick its panel type.
// Openings are numbered U0.. (bottom), D0.. (top), S0.. (sides and ends). A GR loads with no
// panel planned; "Auto calculate panels" gives every empty opening its default type (TP service
// side, RP back, FP elsewhere), which the user can change opening by opening.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    public partial class MainForm
    {
        DataGridView grOpGrid;
        GrPopup popBoxes, popDividers, popOpenings;
        GlassToggle glassShared;
        List<GrOpening> grOpenings = new List<GrOpening>();
        const string NoPanel = "-", Keep = "keep";

        void BuildGrEditUi()
        {
            // --- Openings grid
            grOpGrid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EditMode = DataGridViewEditMode.EditOnEnter
            };
            Glass.StyleDarkGrid(grOpGrid);
            grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", Visible = false });
            foreach (var (name, header, weight) in new[] { ("No", "No.", 10f), ("Opening", "Box / face", 30f), ("Clear", "Clear (mm)", 20f), ("Now", "Now", 24f) })
                grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            var type = new DataGridViewComboBoxColumn { Name = "Type", HeaderText = "Panel", FillWeight = 14, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, SortMode = DataGridViewColumnSortMode.NotSortable };
            type.Items.AddRange(NoPanel, Keep, "FP", "TP", "RP");
            grOpGrid.Columns.Add(type);
            grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Panel", HeaderText = "Panel (rules)", FillWeight = 36, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            Glass.DarkInputColumn(grOpGrid, "Type");

            // --- the three windows
            popBoxes = new GrPopup("Boxes", new Size(640, 300)) { Owner = this };
            popDividers = new GrPopup("Dividers", new Size(600, 340)) { Owner = this };
            popOpenings = new GrPopup("Openings", new Size(720, 420)) { Owner = this };
            popDividers.Subtitle = "P350; position = offset from the profile or divider it is placed from";

            // Boxes: the size grid, the add / remove buttons, the "same width / height" switch
            grGrid.Parent?.Controls.Remove(grGrid);
            Glass.StyleDarkGrid(grGrid);
            popBoxes.Body.Controls.Add(grGrid);
            grInfo.Parent?.Controls.Remove(grInfo);
            grInfo.BackColor = Theme.Glass; grInfo.ForeColor = Theme.GlassMuted; grInfo.Dock = DockStyle.Bottom; grInfo.Height = 40; grInfo.Padding = new Padding(2, 8, 0, 0);
            popBoxes.Body.Controls.Add(grInfo);
            glassShared = new GlassToggle("Same width / height change together") { Checked = chkShared.Checked };
            glassShared.CheckedChanged += (s, e) => chkShared.Checked = glassShared.Checked;
            foreach (var b in new[] { btnUp, btnAddR, btnAddL, btnAddF, btnAddB, btnRemove })
            {
                b.Parent?.Controls.Remove(b);
                b.Kind = ButtonKind.Glass; b.Height = 28; b.FitWidth(); b.Margin = new Padding(0, 0, 6, 0);
                popBoxes.Actions.Controls.Add(b);
            }
            popBoxes.Actions.Controls.Add(glassShared);

            // Dividers: the grid, and adding / removing P350 rings (a ring = a P350 and those moving with it)
            grDivGrid.Parent?.Controls.Remove(grDivGrid);
            Glass.StyleDarkGrid(grDivGrid);
            popDividers.Body.Controls.Add(grDivGrid);
            var btnRingAdd = new FlatButton("Add ring like selected...", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            var btnRingRemove = new FlatButton("Remove ring", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            popDividers.Actions.Controls.AddRange(new Control[] { btnRingAdd, btnRingRemove });
            string SelectedDivider() => grDivGrid.CurrentRow == null ? throw new SizerException("Select a divider in the list first.") : Convert.ToString(grDivGrid.CurrentRow.Cells["Divider"].Value);
            btnRingAdd.Click += (s, e) => { if (gr != null) AddRingDialog(SelectedDivider()); };
            btnRingRemove.Click += (s, e) => GrEdit("Remove ring", () => RemoveRingNow(SelectedDivider()));

            // Openings
            popOpenings.Body.Controls.Add(grOpGrid);
            var btnDefaults = new FlatButton("Auto calculate panels", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            var btnClear = new FlatButton("Clear panels", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            var btnSides = new FlatButton("Swap sides", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            var btnList = new FlatButton("Panel list...", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 0, 6, 0) };
            popOpenings.Actions.Controls.AddRange(new Control[] { btnDefaults, btnClear, btnSides, btnList });

            // ribbon: open / close the windows
            var bBoxes = new RibbonButton("Boxes", "master");
            var bDividers = new RibbonButton("Dividers", "fx");
            var bOpenings = new RibbonButton("Openings", "type");
            tabGr.AddGroup("Edit", bBoxes, bDividers, bOpenings);
            var bAuto = new RibbonButton("Auto panels", "apply");
            var bClear = new RibbonButton("Clear panels", "type");
            tabGr.AddGroup("Panels", bAuto, bClear);
            bBoxes.Click += (s, e) => popBoxes.Toggle(PopAt(0));
            bDividers.Click += (s, e) => popDividers.Toggle(PopAt(1));
            bOpenings.Click += (s, e) => popOpenings.Toggle(PopAt(2));

            void AutoPanels() => GrEdit("Auto calculate panels", () =>
            {
                if (gr.RealMap == null) throw new SizerException("Panels need real part positions: read the GR from Inventor.");
                int n = GrOpenings.FillDefaults(gr, GrOpenings.Find(gr));
                Log.Write(n > 0 ? $"GR: panels on {n} empty opening(s): TP service side, RP back side, FP elsewhere. Change any of them by right-clicking it." : "GR: every empty opening has a panel type already");
            });
            void ClearPanels() => GrEdit("Clear panels", () =>
            {
                int n = gr.PanelChoice.Count;
                gr.PanelChoice.Clear(); gr.Edited = true;
                Log.Write($"GR: panel choices cleared ({n}); no new panel is planned");
            });
            btnDefaults.Click += (s, e) => AutoPanels();
            bAuto.Click += (s, e) => AutoPanels();
            btnClear.Click += (s, e) => ClearPanels();
            bClear.Click += (s, e) => ClearPanels();
            btnSides.Click += (s, e) => GrEdit("Swap sides", () =>
            {
                gr.ServiceSide = -gr.ServiceSide;
                // side openings still on the old default follow the swap (TP now on the back -> RP, and the other way)
                foreach (var o in GrOpenings.Find(gr).Where(o => o.State == "empty" && (o.Face == "Service side" || o.Face == "Back side")))
                    if (o.Type == (o.Face == "Service side" ? "RP" : "TP")) gr.PanelChoice[o.Key] = GrOpenings.DefaultType(o.Face);
                Log.Write($"GR: the service side (TP) is now the width side at {(gr.ServiceSide > 0 ? "W max" : "W 0")}");
                ResetGrView();   // the front of the twin follows the service side
            });
            btnList.Click += (s, e) => Step("Save panel list", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                var ops = GrOpenings.Find(gr);
                if (GrOpenings.Planned(ops).Count == 0) throw new SizerException("No panels planned: every opening is set to - (no panel).");
                using (var d = new SaveFileDialog { Filter = "Panel list (*.csv)|*.csv", FileName = gr.Name + "-panels.csv" })
                    if (d.ShowDialog(this) == DialogResult.OK) { GrOpenings.ExportPanelList(gr, ops, d.FileName); Log.Write($"Saved panel list {d.FileName}"); }
            });

            grOpGrid.CurrentCellDirtyStateChanged += (s, e) => { if (grOpGrid.IsCurrentCellDirty && grOpGrid.CurrentCell is DataGridViewComboBoxCell) grOpGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grOpGrid.EditingControlShowing += (s, e) => { if (e.Control is ComboBox c) { c.BackColor = Theme.PopInput; c.ForeColor = Theme.GlassText; } };
            grOpGrid.DataError += (s, e) => { e.ThrowException = false; };
            grOpGrid.CellValueChanged += (s, e) =>
            {
                if (syncing || e.RowIndex < 0 || grOpGrid.Columns[e.ColumnIndex].Name != "Type") return;
                string value = Convert.ToString(grOpGrid.Rows[e.RowIndex].Cells["Type"].Value);
                var keys = grOpGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells["Key"].Value)).ToList();
                string here = Convert.ToString(grOpGrid.Rows[e.RowIndex].Cells["Key"].Value);
                if (!keys.Contains(here)) keys = new List<string> { here };
                BeginInvoke(new Action(() => GrEdit("Panel type", () => ChooseType(keys, value))));
            };
            grOpGrid.SelectionChanged += (s, e) =>
            {
                if (syncing || gr == null) return;
                gr.SelectedOpening = grOpGrid.CurrentRow == null ? null : Convert.ToString(grOpGrid.CurrentRow.Cells["Key"].Value);
                UpdateDrawing();
            };

            // --- the twin as an editor
            twin.MouseMove += (s, e) => { if (mode == "GR" && gr != null && !grDragging && e.Button == MouseButtons.None) twin.Cursor = GrHit(e.Location).Kind != null ? Cursors.Hand : Cursors.Default; };
            twin.MouseClick += (s, e) =>
            {
                if (mode != "GR" || gr == null) return;
                if (grDragged) { grDragged = false; return; }   // the end of a turn or pan, not a click
                var hit = GrHit(e.Location);
                if (hit.Kind == null) { if (e.Button == MouseButtons.Left) EndFocus(); return; }   // empty space: done editing the box
                if (e.Button == MouseButtons.Left && hit.Kind == "dim") { EditDimension(hit.Id, twin.PointToScreen(e.Location)); return; }
                if (hit.Kind == "cut") { CutMenu(hit.Id, e.Location); return; }
                if (e.Button == MouseButtons.Right && hit.Kind == "open") { OpeningMenu(hit.Id, e.Location); return; }
                if (e.Button == MouseButtons.Right && hit.Kind == "div") { DividerMenu(hit.Id, e.Location); return; }
                if (e.Button == MouseButtons.Right && hit.Kind == "edge") { var p = hit.Id.Split(':'); EdgeMenu(int.Parse(p[0]), int.Parse(p[1]), e.Location); return; }
                if (e.Button == MouseButtons.Right && hit.Kind == "box") { BoxMenu(int.Parse(hit.Id), e.Location); return; }
                if (e.Button != MouseButtons.Left) return;
                if (hit.Kind == "open") { gr.SelectedOpening = hit.Id; popOpenings.Popup(PopAt(2)); SelectRow(grOpGrid, "Key", hit.Id, null); UpdateDrawing(); }
                else if (hit.Kind == "div") { gr.SelectedDivider = gr.SelectedDivider == hit.Id ? null : hit.Id; UpdateDrawing(); }   // picked: its own dimensions, on any face
                else if (hit.Kind == "box" || hit.Kind == "edge") FocusBox(int.Parse(hit.Id.Split(':')[0]));   // edit this box: the others grey, its dimensions shown
            };
            WireGrView();
            WireGrFocus();
        }

        // Where a window first appears: at the top left of the twin, one step further per window.
        Point PopAt(int n) => twin.PointToScreen(new Point(18 + 34 * n, 18 + 34 * n));

        // GR mode uses the whole width for the twin; the editing windows close outside it.
        void GrModeChanged(bool gr)
        {
            split.Panel1Collapsed = gr;
            if (!gr) foreach (var p in new[] { popBoxes, popDividers, popOpenings }) p?.Dismiss();
            if (grOutCard != null) { grOutCard.Visible = gr; if (gr) UpdateGrOutput(); }
        }

        static void SelectRow(DataGridView g, string column, string value, string editColumn)
        {
            foreach (DataGridViewRow r in g.Rows)
            {
                if (Convert.ToString(r.Cells[column].Value) != value) continue;
                g.ClearSelection();
                var cell = editColumn != null && !r.Cells[editColumn].ReadOnly ? r.Cells[editColumn] : r.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
                g.CurrentCell = cell;
                r.Selected = true;
                if (editColumn != null && cell.OwningColumn.Name == editColumn) { g.Focus(); g.BeginEdit(true); }
                return;
            }
        }

        // What the mouse is over on the twin: a divider line, an opening turned to the viewer, or a box.
        (string Kind, string Id) GrHit(Point p)
        {
            if (shapes == null) return (null, null);
            // dimensions that can be changed (line or label) and the cut list's P150 / P350 rows
            foreach (var s in shapes.Where(s => s.Tag != null && s.Tag.StartsWith("dim:")))
                if (s.IsText ? OnText(s, p) : s.Points != null && NearLine(s.Points, p, 4)) return ("dim", s.Tag);
            foreach (var s in shapes.Where(s => s.Tag != null && s.Tag.StartsWith("cut:") && s.IsText))
                if (OnText(s, p)) return ("cut", s.Tag.Substring(4));
            foreach (var s in shapes.Where(s => s.Tag != null && s.Tag.StartsWith("div:") && s.Points != null))
                if (NearLine(s.Points, p, 6)) return ("div", s.Tag.Substring(4));
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                if (s.Tag != null && s.Tag.StartsWith("open:") && s.Points != null && Inside(s.Points, p)) return ("open", s.Tag.Substring(5));
            }
            // box profiles (the edges seen in front last in the list: found first), then the box names
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                if (s.Tag != null && s.Tag.StartsWith("edge:") && s.Points != null && NearLine(s.Points, p, 5)) return ("edge", s.Tag.Substring(5));
            }
            foreach (var s in shapes.Where(s => s.Tag != null && s.Tag.StartsWith("box:") && s.IsText))
                if (Math.Abs(p.X - s.X) < 40 && Math.Abs(p.Y - s.Y) < 12) return ("box", s.Tag.Substring(4));
            return (null, null);
        }

        // Is a point on a text shape (drawn turned by its angle around X, Y, Dy below; about 0.6 em a letter)?
        static bool OnText(Shape s, Point p)
        {
            double fs = Styles.All.TryGetValue(s.Kind ?? "", out var st) && st.FontSize > 0 ? st.FontSize : 12;
            double w = 0.6 * fs * (s.Text ?? "").Length + 8, a = -s.Angle * Math.PI / 180;
            double dx = p.X - s.X, dy = p.Y - s.Y;
            double lx = dx * Math.Cos(a) - dy * Math.Sin(a), ly = dx * Math.Sin(a) + dy * Math.Cos(a) - s.Dy;
            bool inX = s.Align == "left" ? lx > -4 && lx < w : Math.Abs(lx) < w / 2;
            return inX && Math.Abs(ly) < fs * 0.75;
        }

        static bool NearLine(double[] pts, Point p, double tol)
        {
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                double ax = pts[i], ay = pts[i + 1], bx = pts[i + 2], by = pts[i + 3];
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - ax) * dx + (p.Y - ay) * dy) / len2));
                double ex = ax + t * dx - p.X, ey = ay + t * dy - p.Y;
                if (ex * ex + ey * ey <= tol * tol) return true;
            }
            return false;
        }

        static bool Inside(double[] pts, Point p)
        {
            bool inside = false; int n = pts.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = pts[2 * i], yi = pts[2 * i + 1], xj = pts[2 * j], yj = pts[2 * j + 1];
                if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        // Right-click on a divider in the twin: its position, a copy of its ring, or removing the ring.
        void DividerMenu(string name, Point at)
        {
            if (!gr.Components.TryGetValue(name, out var c)) return;
            string leader = Gr.RingOf(gr, name);
            var menu = Glass.Menu();
            menu.Items.Add(new ToolStripMenuItem($"{name}" + (leader != name ? $"  (moves with {leader})" : "")) { Enabled = false });
            var pos = new ToolStripMenuItem("Edit position...");
            pos.Click += (s, e) => { popDividers.Popup(PopAt(1)); SelectRow(grDivGrid, "Divider", leader, "Position"); };
            var add = new ToolStripMenuItem("Add a ring like this...");
            add.Click += (s, e) => AddRingDialog(name);
            var remove = new ToolStripMenuItem("Remove this ring");
            remove.Click += (s, e) => GrEdit("Remove ring", () => RemoveRingNow(name));
            menu.Items.AddRange(new ToolStripItem[] { pos, add, CopyRingItem(name), remove });
            menu.Items.Add(new ToolStripSeparator());
            DividerSpanItems(menu, name);
            menu.Show(twin, at);
        }

        void AddRingDialog(string divider)
        {
            string leader = Gr.RingOf(gr, divider);
            if (!gr.Components.TryGetValue(leader, out var lc) || lc.PosConstraint == "")
            {
                Toast.Show(this, ToastKind.Error, "Add ring", $"{divider} has no position of its own: pick a divider placed by an offset.");
                return;
            }
            string from = Short(lc.PosPartner);
            string text = SetupTextDialog.Prompt(this, "Add a P350 ring",
                $"A copy of the ring of {leader} ({Gr.RingMembers(gr, leader).Count} P350 and their joints).\nPosition in mm, measured like {leader} ({U.F(lc.Position ?? 0)} mm from {from}):", U.F((lc.Position ?? 0) + 300));
            if (string.IsNullOrWhiteSpace(text)) return;
            GrEdit("Add ring", () =>
            {
                double v;
                try { v = U.P(text); } catch { throw new SizerException($"'{text}' is not a number in mm."); }
                var add = Gr.AddRing(gr, divider, v);
                Log.Write($"GR: new P350 ring like {leader} at {U.F(v)} mm: {string.Join(", ", add.Names.Values)} (made in Inventor on Write new GR)");
            });
        }

        void RemoveRingNow(string divider)
        {
            var gone = Gr.RemoveRing(gr, divider);
            Log.Write($"GR: P350 ring removed: {string.Join(", ", gone)} (deleted in Inventor on Write new GR)");
        }

        // Right-click on an opening in the twin: its panel type.
        void OpeningMenu(string key, Point at)
        {
            var o = GrOpenings.Find(gr).FirstOrDefault(x => x.Key == key);
            if (o == null) return;
            gr.SelectedOpening = key; UpdateDrawing();
            var menu = Glass.Menu();
            var head = new ToolStripMenuItem($"{o.Id}  {o.Frame.Label} {o.Face}, {U.F(o.ClearU)} x {U.F(o.ClearV)}") { Enabled = false };
            menu.Items.Add(head);
            if (o.State == "blocked") { menu.Items.Add(new ToolStripMenuItem("blocked by " + Short(o.Existing)) { Enabled = false }); menu.Show(twin, at); return; }
            var choices = new List<(string Text, string Value)> { ("FP", "FP"), ("TP", "TP"), ("RP", "RP") };
            choices.Add(o.State == "filled" ? ("Keep " + Short(o.Existing), Keep) : ("No panel", NoPanel));
            string now = o.State == "filled" && !o.HasType ? Keep : o.HasType ? o.Type : NoPanel;
            foreach (var c in choices)
            {
                var item = new ToolStripMenuItem(c.Text) { Checked = c.Value == now };
                string v = c.Value;
                item.Click += (s, e) => GrEdit("Panel type", () => ChooseType(new List<string> { key }, v));
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var table = new ToolStripMenuItem("Show in the Openings window");
            table.Click += (s, e) => { popOpenings.Popup(PopAt(2)); SelectRow(grOpGrid, "Key", key, null); };
            menu.Items.Add(table);
            menu.Show(twin, at);
        }

        // The picked type on every selected opening it fits: "-" leaves an empty opening empty,
        // "keep" leaves an existing panel, FP / TP / RP plans a panel (a replacement where one exists).
        void ChooseType(List<string> keys, string value)
        {
            int n = 0;
            foreach (var o in GrOpenings.Find(gr).Where(o => keys.Contains(o.Key)))
            {
                if (o.State == "blocked") continue;
                if (o.State == "filled" && value == NoPanel) continue;   // an existing panel is kept or replaced, not removed here
                if (o.State == "empty" && value == Keep) continue;
                GrOpenings.Choose(gr, o, value == NoPanel ? "none" : value);
                n++;
            }
            if (n > 0) Log.Write($"GR: {(value == NoPanel ? "no panel" : value == Keep ? "existing panel kept" : value)} on {n} opening(s)");
        }

        void UpdateOpenings()
        {
            if (grOpGrid == null) return;
            bool was = syncing; syncing = true;
            try
            {
                string current = gr?.SelectedOpening;
                grOpGrid.Rows.Clear();
                grOpenings = gr == null ? new List<GrOpening>() : GrOpenings.Find(gr);
                foreach (var o in grOpenings)
                {
                    string now = o.State == "filled" ? Short(o.Existing) : o.State == "blocked" ? "blocked: " + Short(o.Existing) : "empty";
                    string type = o.HasType ? o.Type : o.State == "filled" ? Keep : NoPanel;
                    string panel = "";
                    if (o.State == "filled" && !o.HasType) { var chk = GrOpenings.ExistingCheck(gr, o); panel = chk == "" ? "matches the rules" : chk; }
                    else if (o.Panel != null) panel = $"{o.Panel.Name} ({o.Panel.Inner})" + (o.Panel.Problem != null ? " - " + o.Panel.Problem : "");
                    int i = grOpGrid.Rows.Add(o.Key, o.Id, $"{o.Frame.Label} {o.Face}", $"{U.F(o.ClearU)} x {U.F(o.ClearV)}", now, type, panel);
                    var row = grOpGrid.Rows[i];
                    var cell = (DataGridViewComboBoxCell)row.Cells["Type"];
                    cell.Items.Clear();
                    if (o.State == "blocked") { cell.Items.Add(NoPanel); cell.ReadOnly = true; row.DefaultCellStyle.ForeColor = Theme.GlassMuted; }
                    else cell.Items.AddRange(o.State == "filled" ? Keep : NoPanel, "FP", "TP", "RP");
                    cell.Value = type;
                    if (o.Panel?.Problem != null) row.Cells["Panel"].Style.ForeColor = Theme.MacRed;
                    if (o.Key == current) { grOpGrid.CurrentCell = row.Cells["No"]; row.Selected = true; }
                }
                if (gr == null || gr.RealMap == null)
                    popOpenings.Subtitle = "need real part positions: read the GR from Inventor";
                else
                {
                    int planned = GrOpenings.Planned(grOpenings).Count, none = grOpenings.Count(o => o.State == "empty" && !o.HasType);
                    popOpenings.Subtitle = planned == 0 && none > 0 ? $"{grOpenings.Count} openings, no panel planned yet: Auto calculate panels, or pick a type per opening"
                        : $"{grOpenings.Count} openings: {planned} panel(s) planned, {none} left empty";
                }
                popBoxes.Subtitle = gr == null ? "" : $"{gr.Frames.Count} box(es), outer sizes in mm";
            }
            finally { syncing = was; }
        }

        // One GR panel made with the panel tool (unchanged): the stored panel values with the type's
        // defaults and this size, into the given folder. A file of that name is used as it is.
        string MakeGrPanel(string name, string folder)
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, name + ".iam");
            if (File.Exists(target)) { Log.Write($"  {name}.iam exists in {Path.GetFileName(folder)} - used as it is"); return target; }
            var s = Standards.ForName(standards, name) ?? throw new SizerException($"No panel type for '{name}'.");
            // The stored panel values as the base (hole edge, diameters, clearance ...), but what the
            // panel window would set itself for a new size: the type's defaults, the default hinge /
            // handle counts, automatic hole counts, and the inner split from the width (not the 1T /
            // 2T picked in the Types browser).
            var ps = File.Exists(ParamPath) ? ParamFile.Read(ParamPath) : GridParams();
            void Set(string n, double v, string unit = "mm") { var p = ps.FirstOrDefault(x => x.Name == n); if (p != null) p.Value = v; else ps.Add(new Param(n, v, unit)); }
            foreach (var d in ParamFile.Defaults.Where(d => d[0] == "HingeCount" || d[0] == "HandleCount" || d[0] == "HandleDir")) Set(d[0], U.P(d[1]), d[2]);
            foreach (var kv in s.Defaults) Set(kv.Key, kv.Value);
            var m = U.SizeRegex.Match(name);
            for (int i = 0; i < 3; i++) Set(U.SizeParams[i], U.P(m.Groups[i + 1].Value));
            var set = settings.Clone(); set.OutputFolder = folder; set.ForceSplit = 0;
            double pitch = set.MaxHolePitch > 0 ? set.MaxHolePitch : 400;
            var v = ps.ToDictionary(p => p.Name, p => p.Value);
            double edge = v.TryGetValue("HoleEdge", out var he) ? he : 50, mid = edge - set.Thickness - (v.TryGetValue("Clearance", out var cl) ? cl : 0.5);
            bool split = HoleRules.InnerSplit(v["Width"], set) >= 2;
            Set("HolesL", Math.Max(s.MinHolesL, HoleRules.AutoCount(v["Length"], edge, pitch, false)), "ul");
            Set("HolesW", split ? HoleRules.AutoCountSplitW(v["Width"], edge, mid, pitch, s.EvenSplitHolesW) : HoleRules.AutoCount(v["Width"], edge, pitch, false), "ul");
            string paramCopy = Path.Combine(Path.GetTempPath(), "InventorSizer-GR-panel-params.xml");   // the panel tool's own Parameters.xml stays as it is
            Inv.ApplyPanel(name, ps, set, paramCopy, standards, s, show: false);   // saved and closed there
            return target;
        }

        static string Short(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @":\d+$", "");
    }
}
