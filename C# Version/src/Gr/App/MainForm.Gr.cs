// The GR (unit frame) part of the window: its events, the box and divider lists and the
// presets. Kept apart from the panel code (FP / TP / RP) so the two do not affect each other.
// GR files live in GR\ next to the app: GR\GR.json (part rules) and GR\presets\ (designs).

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    public partial class MainForm
    {
        // GR controls built in BuildUi and wired here
        FlatButton btnAddR, btnAddL, btnAddF, btnRemove, btnUp;
        GrData grData;   // the export / Inventor data the GR was built from, to build it again with another up axis
        RibbonButton btnGrApply;
        RibbonGroup gGrSource;

        // GR\GR.json; standards\GR.json of older versions as a fallback.
        string GrRulesFile
        {
            get
            {
                string f = Path.Combine(Root, "GR", "GR.json");
                return File.Exists(f) ? f : Path.Combine(StandardsDir, "GR.json");
            }
        }

        // GR designs saved by older versions in presets\GR\ are copied into GR\presets\ once.
        void MoveOldGrPresets()
        {
            try
            {
                string old = Path.Combine(Root, "presets", "GR");
                if (!Directory.Exists(old)) return;
                Directory.CreateDirectory(GrPresetDir);
                foreach (var f in Directory.GetFiles(old, "*.json"))
                {
                    string to = Path.Combine(GrPresetDir, Path.GetFileName(f));
                    if (!File.Exists(to)) File.Copy(f, to);
                }
            }
            catch (Exception e) { Log.Write($"GR presets in presets\\GR could not be copied to GR\\presets ({e.Message})"); }
        }

        void WireGrEvents()
        {
            grFiles.Add("Load GR export...", (s, e) => Step("Load GR export", () =>
            {
                using (var d = new OpenFileDialog { Filter = "GR export (*.xml)|*.xml", Title = "GR export: select the tree and the constraints file (or one export that has both)", Multiselect = true })
                    if (d.ShowDialog(this) == DialogResult.OK) SetGr(BuildGr(Gr.ReadExportFiles(d.FileNames)));
            }));
            grFiles.Separator();
            grFiles.Add("Load GR design...", (s, e) => Step("Load GR design", () =>
            {
                using (var d = new OpenFileDialog { InitialDirectory = GrPresetDir, Filter = "GR design (*.json)|*.json" })
                    if (d.ShowDialog(this) == DialogResult.OK) { grData = null; SetGr(Gr.ReadDesign(d.FileName)); }
            }));
            grFiles.Add("Save GR design...", (s, e) => Step("Save GR design", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                Directory.CreateDirectory(GrPresetDir);
                using (var d = new SaveFileDialog { InitialDirectory = GrPresetDir, Filter = "GR design (*.json)|*.json", FileName = gr.Name + "-design.json" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Gr.SaveDesign(gr, d.FileName); Log.Write($"Saved GR design {d.FileName}"); UpdatePresets(); }
            }));
            grFiles.Separator();
            grFiles.Add("Save cut list (CSV)...", (s, e) => Step("Save cut list", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                using (var d = new SaveFileDialog { Filter = "Cut list (*.csv)|*.csv", FileName = gr.Name + "-cut-list.csv" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Gr.ExportCutList(gr, d.FileName); Log.Write($"Saved cut list {d.FileName}"); }
            }));
            grFiles.Separator();
            grFiles.Add("Check named faces (Inventor)", (s, e) => Step("Check named faces", () =>
            {
                var lines = Inv.CheckNamedFaces();
                foreach (var l in lines) Log.Write(l);
                Toast.Show(this, lines.Any(l => l.Contains("MISSING")) ? ToastKind.Error : ToastKind.Done, "Named faces", lines[0]);
            }, true));
            gGrSource.Fit();   // the Files button grew its menu arrow
            btnGrInv.Click += (s, e) => Step("Read GR from Inventor", () => SetGr(Log.Measure("Build the GR model", () => BuildGr(Inv.ReadGr()))), true);
            btnOpenGr.Click += (s, e) => Step("Open GR", () =>
            {
                using (var d = new OpenFileDialog { Filter = "Inventor assembly (*.iam)|*.iam", Title = "Open a GR assembly" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                        using (Progress.Run("Open GR", ("gr.open", 1)))
                        {
                            Progress.Next("gr.open", $"Opening {Path.GetFileName(d.FileName)} in Inventor");
                            Inv.OpenVisible(d.FileName);
                            SetGr(Log.Measure("Build the GR model", () => BuildGr(Inv.ReadGr())));
                        }
            }, true);
            presetBox.SelectedIndexChanged += (s, e) =>
            {
                if (syncing || presetBox.SelectedIndex < 0) return;
                var file = Path.Combine(GrPresetDir, presetBox.SelectedItem + ".json");
                Step("Load GR preset", () => { grData = null; SetGr(Gr.ReadDesign(file)); });
            };
            grGrid.DataBindingComplete += (s, e) =>
            {
                foreach (var c in new[] { "Box", "P150 L / W / H", "Panels", "Dividers" }) if (grGrid.Columns[c] != null) grGrid.Columns[c].ReadOnly = true;
                foreach (var c in new[] { "Outer L", "Outer W", "Outer H" }) if (grGrid.Columns[c] != null) { grGrid.Columns[c].HeaderText = c + " (mm)"; Glass.DarkInputColumn(grGrid, c); }
            };
            grGrid.DataError += (s, e) => { Log.Write("Enter a number in mm."); Toast.Show(this, ToastKind.Error, "Enter a number in mm"); e.Cancel = true; };
            grTable.ColumnChanged += (s, e) =>
            {
                if (syncing) return;
                var dims = new Dictionary<string, string> { ["Outer L"] = "L", ["Outer W"] = "W", ["Outer H"] = "H" };
                if (!dims.TryGetValue(e.Column.ColumnName, out var dim) || e.Row[e.Column] is DBNull) return;
                int no = Gr.NoFromLabel((string)e.Row["Box"]);
                double profile = (double)e.Row[e.Column] - Gr.CornerAdd;   // the outer size includes the 2 corners
                BeginInvoke(new Action(() => GrEdit("Edit box", () =>
                {
                    var changed = Gr.SetFrameSize(gr, no, dim, profile, chkShared.Checked);
                    if (changed.Count > 0) Log.Write($"GR: outer {dim} = {U.F(profile + Gr.CornerAdd)} mm (P150 {U.F(profile)}) on {string.Join(", ", changed)}");
                })));
            };
            grDivGrid.DataBindingComplete += (s, e) =>
            {
                foreach (var c in new[] { "Divider", "Box", "Length", "Placed by" }) if (grDivGrid.Columns[c] != null) grDivGrid.Columns[c].ReadOnly = true;
                Glass.DarkInputColumn(grDivGrid, "Position");
                foreach (DataGridViewRow r in grDivGrid.Rows)
                    if (!Convert.ToString(r.Cells["Placed by"].Value).StartsWith("offset")) { r.Cells["Position"].ReadOnly = true; r.Cells["Position"].Style.BackColor = Theme.Glass; r.Cells["Position"].Style.ForeColor = Theme.GlassMuted; }
            };
            grDivGrid.DataError += (s, e) => { Log.Write("Enter a number in mm."); Toast.Show(this, ToastKind.Error, "Enter a number in mm"); e.Cancel = true; };
            grDivTable.ColumnChanged += (s, e) =>
            {
                if (syncing || e.Column.ColumnName != "Position" || e.Row["Position"] is DBNull) return;
                string n = (string)e.Row["Divider"]; double v = (double)e.Row["Position"];
                BeginInvoke(new Action(() => GrEdit("Move divider", () => { Gr.SetDividerPosition(gr, n, v); Log.Write($"GR: {n} at {U.F(v)} mm"); })));
            };
            // new boxes next to the selected box; left / right / front / back as seen from the service side
            btnAddB = new FlatButton("+ Back", ButtonKind.Ghost, 0, 30) { Margin = new Padding(2, 0, 0, 0) };
            btnAddF.Parent?.Controls.Add(btnAddB);
            btnAddF.Parent?.Controls.SetChildIndex(btnAddB, btnAddF.Parent.Controls.GetChildIndex(btnAddF) + 1);
            foreach (var (b, side) in new[] { (btnAddR, "Right"), (btnAddL, "Left"), (btnAddF, "Front"), (btnAddB, "Back") })
                b.Click += (s, e) => { int refNo; try { refNo = SelectedBox(); } catch (SizerException ex) { Toast.Show(this, ToastKind.Error, "Add box", ex.Message + " Or right-click a box in the twin."); return; } AddBoxAt(refNo, side); };
            // Up axis: Z for the standard GR; another axis for a GR modelled lying down. Builds the GR
            // again from its data, so it is changed before editing.
            btnUp = new FlatButton("Up: Z", ButtonKind.Ghost, 0, 30) { Margin = new Padding(2, 0, 0, 0) };
            var actions = btnAddR.Parent;
            actions.Controls.Add(btnUp); actions.Controls.SetChildIndex(btnUp, 0);
            actions.Parent?.PerformLayout();
            btnUp.Click += (s, e) => GrEdit("Change up", () =>
            {
                if (grData == null) throw new SizerException("Up can be changed right after reading a GR (from Inventor or an export): read it again, then change up.");
                if (gr.Edited) throw new SizerException("Change up before editing: load the GR again, then change up.");
                int up = (gr.Up + 2) % 3;   // Z -> Y -> X -> Z
                gr = Gr.Build(grData, up);
                Log.Write($"GR: up = {Gr.AxisName(up)}" + (gr.RealMap == null ? " (no real positions: the sizes stay a guess)" : $", length along {Gr.AxisName(gr.RealMap[0])}"));
            });
            btnRemove.Click += (s, e) => GrEdit("Remove box", () => { int no = SelectedBox(); Gr.RemoveFrame(gr, no); Log.Write($"GR: removed Box {no - 1} with its panels and dividers"); });
            BuildGrEditUi();
            btnGrApply.Click += (s, e) => Step("Write new GR to Inventor", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                Inv.ApplyGr(gr, grNewName.Text, grPrefix.Text, new Inv.PanelJob { Prefix = grPrefix.Text.Trim(), MakePanel = MakeGrPanel });
                grData = null;   // the new GR differs from the data it was read from
                UpdateGrGrid();
            }, true);
        }

        GrModel BuildGr(GrData d)
        {
            grData = d;
            return Gr.Build(d);
        }

        void SetGr(GrModel m)
        {
            gr = m;
            ResetGrView();
            Log.Write($"GR {m.Name}: {m.Frames.Count} box(es), {m.Components.Values.Count(c => c.Kind == "panel")} panels, outer about {U.F(m.Length)} x {U.F(m.OuterWidth)} x {U.F(m.OuterHeight)} mm ({m.Source})");
            foreach (var w in m.Warnings) Log.Write("  note: " + w);
            if (mode != "GR") SetMode("GR"); else { UpdateGrGrid(); UpdateDrawing(); }
        }

        void UpdatePresets()
        {
            syncing = true;
            try
            {
                presetBox.Items.Clear();
                if (Directory.Exists(GrPresetDir)) foreach (var f in Directory.GetFiles(GrPresetDir, "*.json").OrderBy(f => f)) presetBox.Items.Add(Path.GetFileNameWithoutExtension(f));
            }
            finally { syncing = false; }
        }

        void UpdateGrGrid()
        {
            syncing = true;
            try
            {
                grTable.Rows.Clear(); grDivTable.Rows.Clear();
                if (gr != null)
                {
                    foreach (var f in gr.Frames.OrderBy(f => f.No))
                        grTable.Rows.Add(f.Label, f.Lx + Gr.CornerAdd, f.Wy + Gr.CornerAdd, f.Hz + Gr.CornerAdd, $"{U.F(f.Lx)} / {U.F(f.Wy)} / {U.F(f.Hz)}", f.Panels.Count, f.Dividers.Count);
                    foreach (var c in gr.Components.Values.Where(c => c.Kind == "divider").OrderBy(c => gr.ById(c.Frame)?.No ?? 0).ThenBy(c => c.Name))
                        grDivTable.Rows.Add(c.Name, gr.ById(c.Frame)?.Label ?? "", c.Length, c.Position.HasValue ? (object)c.Position.Value : DBNull.Value,
                            c.PosConstraint != "" ? $"offset from {Short(c.PosPartner)} ({c.PosConstraint})" : c.Leader != "" ? $"moves with {Short(c.Leader)}" : "joints");
                }
                grTable.AcceptChanges(); grDivTable.AcceptChanges();
            }
            finally { syncing = false; }
            if (btnUp != null) btnUp.Text = "Up: " + Gr.AxisName(gr?.Up ?? 2);
            UpdateOpenings();
            grInfo.Text = gr == null ? "Choose a preset, load a GR export, or read a GR from Inventor." :
                $"{gr.Name}{(gr.Edited ? " (edited)" : "")}: outer {U.F(gr.Length)} x {U.F(gr.OuterWidth)} x {U.F(gr.OuterHeight)} mm. Edit the outer sizes (incl. corners); the P150 profiles are outer - {U.F(Gr.CornerAdd)}. Select a box to add a box next to it.";
        }

        int SelectedBox()
        {
            if (gr == null) throw new SizerException("Load a GR first.");
            var row = grGrid.CurrentRow ?? throw new SizerException("Select a box in the list first.");
            return Gr.NoFromLabel(Convert.ToString(row.Cells["Box"].Value));
        }

        void GrEdit(string title, Action body)
        {
            try { if (gr == null) throw new SizerException("Load a GR first."); Log.Measure(title, body); }
            catch (Exception e) { Log.Write($"ERROR ({title}): {e.Message}"); Toast.Show(this, ToastKind.Error, title, e.Message); }
            UpdateGrGrid();
            UpdateDrawing();
        }
    }
}
