// The GR openings in the window: a list of the empty spaces on the box faces, where the user
// picks a panel type (several rows at once), with the panel the rules give. The drawing shows the
// openings as a hue and outlines the selected one. Kept apart from the panel code (FP / TP / RP).

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
        DataGridView grOpGrid;
        Card openingsCard;
        List<GrOpening> grOpenings = new List<GrOpening>();
        const string NoPanel = "-", Keep = "keep";

        void BuildOpeningsUi()
        {
            grOpGrid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EditMode = DataGridViewEditMode.EditOnEnter
            };
            Glass.StyleGrid(grOpGrid, Theme.Card);
            grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", Visible = false });
            foreach (var (name, weight) in new[] { ("Opening", 36f), ("Clear (mm)", 18f), ("Now", 30f) })
                grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = name, FillWeight = weight, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            var type = new DataGridViewComboBoxColumn { Name = "Type", HeaderText = "Panel type", FillWeight = 16, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, SortMode = DataGridViewColumnSortMode.NotSortable };
            type.Items.AddRange(NoPanel, Keep, "FP", "TP", "RP");
            grOpGrid.Columns.Add(type);
            grOpGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Panel", HeaderText = "Panel (rules)", FillWeight = 36, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            Glass.InputColumn(grOpGrid, "Type");

            openingsCard = new Card("Openings", subtitle: "empty spaces on the box faces: select rows, pick a panel type") { Dock = DockStyle.Bottom, Height = 300 };
            var btnDefaults = new FlatButton("Defaults", ButtonKind.Ghost, 0, 30) { Margin = new Padding(2, 0, 0, 0) };
            var btnSides = new FlatButton("Swap sides", ButtonKind.Ghost, 0, 30) { Margin = new Padding(2, 0, 0, 0) };
            var btnList = new FlatButton("Panel list...", ButtonKind.Ghost, 0, 30) { Margin = new Padding(2, 0, 0, 0) };
            openingsCard.Actions.Controls.AddRange(new Control[] { btnDefaults, btnSides, btnList });
            openingsCard.Controls.Add(grOpGrid);
            grColumn.Controls.Add(Spacer(DockStyle.Bottom, Theme.Gap));
            grColumn.Controls.Add(openingsCard);

            btnDefaults.Click += (s, e) => GrEdit("Default panels", () =>
            {
                int n = GrOpenings.FillDefaults(gr, GrOpenings.Find(gr));
                Log.Write(n > 0 ? $"GR: default panel types on {n} empty opening(s): FP top, bottom and ends, TP service side, RP back side" : "GR: every empty opening has a panel type already");
            });
            btnSides.Click += (s, e) => GrEdit("Swap sides", () =>
            {
                gr.ServiceSide = -gr.ServiceSide;
                Log.Write($"GR: the service side (TP) is now the width side at {(gr.ServiceSide > 0 ? "W max" : "W 0")}; types already chosen stay");
            });
            btnList.Click += (s, e) => Step("Save panel list", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                var ops = GrOpenings.Find(gr);
                if (GrOpenings.Planned(ops).Count == 0) throw new SizerException("No panels chosen yet: pick a panel type for some openings (or press Defaults).");
                using (var d = new SaveFileDialog { Filter = "Panel list (*.csv)|*.csv", FileName = gr.Name + "-panels.csv" })
                    if (d.ShowDialog(this) == DialogResult.OK) { GrOpenings.ExportPanelList(gr, ops, d.FileName); Log.Write($"Saved panel list {d.FileName}"); }
            });

            grOpGrid.CurrentCellDirtyStateChanged += (s, e) => { if (grOpGrid.IsCurrentCellDirty && grOpGrid.CurrentCell is DataGridViewComboBoxCell) grOpGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grOpGrid.EditingControlShowing += (s, e) => { if (e.Control is ComboBox c) { c.BackColor = Theme.Paper; c.ForeColor = Theme.Ink; } };
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
        }

        // The picked type on every selected opening it fits: "-" removes the panel, "keep" leaves an
        // existing panel as it is, FP / TP / RP plans a panel (a replacement where one exists).
        void ChooseType(List<string> keys, string value)
        {
            var ops = GrOpenings.Find(gr).Where(o => keys.Contains(o.Key)).ToList();
            int n = 0;
            foreach (var o in ops)
            {
                if (o.State == "blocked") continue;
                string t = value == NoPanel || value == Keep ? "" : value;
                if (o.State == "filled" && value == NoPanel) continue;   // an existing panel is kept or replaced, not removed here
                GrOpenings.Choose(gr, o, t);
                n++;
            }
            if (n > 0) Log.Write($"GR: {(value == NoPanel || value == Keep ? "no new panel" : value)} on {n} opening(s)");
        }

        void UpdateOpenings()
        {
            if (grOpGrid == null) return;
            bool was = syncing; syncing = true;
            try
            {
                string current = grOpGrid.CurrentRow == null ? null : Convert.ToString(grOpGrid.CurrentRow.Cells["Key"].Value);
                grOpGrid.Rows.Clear();
                grOpenings = gr == null ? new List<GrOpening>() : GrOpenings.Find(gr);
                foreach (var o in grOpenings)
                {
                    string now = o.State == "filled" ? Short(o.Existing) : o.State == "blocked" ? "blocked: " + Short(o.Existing) : "empty";
                    string type = o.State == "filled" ? (o.Type == "" ? Keep : o.Type) : (o.Type == "" ? NoPanel : o.Type);
                    string panel = "";
                    if (o.State == "filled" && o.Type == "") { var chk = GrOpenings.ExistingCheck(gr, o); panel = chk == "" ? "matches the rules" : chk; }
                    else if (o.Panel != null) panel = $"{o.Panel.Name} ({o.Panel.Inner})" + (o.Panel.Problem != null ? " - " + o.Panel.Problem : "");
                    int i = grOpGrid.Rows.Add(o.Key, $"{o.Frame.Label} {o.Face}", $"{U.F(o.ClearU)} x {U.F(o.ClearV)}", now, type, panel);
                    var row = grOpGrid.Rows[i];
                    var cell = (DataGridViewComboBoxCell)row.Cells["Type"];
                    cell.Items.Clear();
                    if (o.State == "blocked") { cell.Items.Add(NoPanel); cell.ReadOnly = true; row.DefaultCellStyle.ForeColor = Theme.Muted; }
                    else cell.Items.AddRange(o.State == "filled" ? Keep : NoPanel, "FP", "TP", "RP");
                    cell.Value = type;
                    if (o.Panel?.Problem != null) row.Cells["Panel"].Style.ForeColor = Color.Firebrick;
                    if (o.Key == current) grOpGrid.CurrentCell = row.Cells["Opening"];
                }
                if (gr == null || gr.RealMap == null)
                    openingsCard.Subtitle = "needs real part positions: read the GR from Inventor or load an InventorReader export";
                else
                {
                    int planned = GrOpenings.Planned(grOpenings).Count, empty = grOpenings.Count(o => o.State == "empty" && o.Type == "");
                    openingsCard.Subtitle = $"{grOpenings.Count} openings, {empty} empty without a panel, {planned} panel(s) planned";
                }
                openingsCard.Invalidate();
            }
            finally { syncing = was; }
        }

        static string Short(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @":\d+$", "");
    }
}
