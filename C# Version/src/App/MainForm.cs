// The window: panel editing (FP / RP / TP) and GR editing, the drawing, the log and the dev log.
// Inventor is only used when a new version is made (or when you ask to read / open something).

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
    public class MainForm : Form
    {
        readonly string Root, ParamPath, SettingsPath, StandardsDir, GrPresetDir;
        string titleTail = "";
        Settings settings;
        List<Standard> standards = new List<Standard>();
        Standard std;
        Dictionary<string, double> values = new Dictionary<string, double>();
        HoleLayout layout;
        List<PlacedHole> hw = new List<PlacedHole>();
        List<Shape> shapes;
        string mode = "Panel", defaultsFor = "";
        GrModel gr;
        bool syncing;
        static readonly string[] AutoParams = { "HolesL", "HolesW" }, FixedParams = { "HoleDia" }, CountParams = { "HingeCount", "HandleCount" };

        // controls
        ComboBox typeBox, subBox, viewBox, presetBox;
        TextBox nameBox, folderBox, logBox, devBox, grNewName, grPrefix;
        Button btnMaster, btnSetMaster, btnMode, btnOpenGr, btnApply, btnReload, btnLearn, btnDevSave, btnSvg, btnGrLoad, btnGrInv, btnBrowse;
        Label statusLabel, masterLabel, modeLabel, errLabel, folderInfo, grInfo, presetLabel;
        CheckBox chkFolder, chkHidden, chkShared;
        NumericUpDown gapBox, thickBox, splitBox, pitchBox;
        DataTable table, grTable, grDivTable;
        DataGridView grid, grGrid, grDivGrid;
        GroupBox settingsBox;
        Panel grPanel;
        PictureBox twin;
        Timer redraw;

        public MainForm()
        {
            Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            ParamPath = Path.Combine(Root, "Parameters.xml");
            SettingsPath = Path.Combine(Root, "settings.json");
            StandardsDir = Path.Combine(Root, "standards");
            GrPresetDir = Path.Combine(Root, "presets", "GR");
            settings = Settings.Read(SettingsPath);
            BuildUi();
            Log.Written += line => { if (!IsDisposed) logBox.AppendText(line + Environment.NewLine); };
            Log.DevWritten += line => { if (!IsDisposed) devBox.AppendText(line + Environment.NewLine); };
            devBox.AppendText(string.Join(Environment.NewLine, Log.DevSnapshot()) + Environment.NewLine);
            Shown += (s, e) => OnShownLoad();
            FormClosing += (s, e) => { try { SaveFormSettings(); } catch { } };
        }

        // ------------------------------------------------------------------ layout

        static T Make<T>(Action<T> init = null) where T : Control, new() { var c = new T(); init?.Invoke(c); return c; }
        static Button Btn(string text, int w, int h = 28) => new Button { Text = text, Size = new Size(w, h) };
        static Label Lbl(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

        void BuildUi()
        {
            Text = "Inventor Sizer - panel editing";
            Size = new Size(1280, 860);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);

            // left: parameter grid, notes, settings  /  GR editor
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            table = new DataTable();
            table.Columns.Add("Name", typeof(string)); table.Columns.Add("Value", typeof(double)); table.Columns.Add("Unit", typeof(string)); table.Columns.Add("Description", typeof(string));
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = table };
            errLabel = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 64, Visible = false, ForeColor = Color.DarkRed, Padding = new Padding(4) };

            settingsBox = new GroupBox { Dock = DockStyle.Bottom, Height = 228, Text = "Settings" };
            var sg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7 };
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            chkFolder = new CheckBox { Text = "Folder for new panels (this session only)", AutoSize = true, Anchor = AnchorStyles.Left };
            folderBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnBrowse = new Button { Text = "Browse...", AutoSize = true, Anchor = AnchorStyles.Right };
            folderInfo = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray };
            NumericUpDown Num(int dec, decimal max, decimal inc, decimal min = 0) => new NumericUpDown { Width = 80, DecimalPlaces = dec, Maximum = max, Minimum = min, Increment = inc, Anchor = AnchorStyles.Left };
            gapBox = Num(1, 1000, 0.5m); thickBox = Num(2, 50, 0.1m, 0.1m); splitBox = Num(0, 100000, 10); pitchBox = Num(0, 100000, 10);
            sg.Controls.Add(chkFolder, 0, 0); sg.SetColumnSpan(chkFolder, 3);
            sg.Controls.Add(Lbl("Folder:"), 0, 1); sg.Controls.Add(folderBox, 1, 1); sg.Controls.Add(btnBrowse, 2, 1);
            sg.Controls.Add(folderInfo, 0, 2); sg.SetColumnSpan(folderInfo, 3);
            sg.Controls.Add(Lbl("Minimum hole gap (mm):"), 0, 3); sg.Controls.Add(gapBox, 1, 3);
            sg.Controls.Add(Lbl("Sheet thickness, one sheet (Staerke, mm):"), 0, 4); sg.Controls.Add(thickBox, 1, 4);
            sg.Controls.Add(Lbl("Split inner panel above width (mm, 0 = never):"), 0, 5); sg.Controls.Add(splitBox, 1, 5);
            sg.Controls.Add(Lbl("Automatic hole counts, max. pitch (mm, 0 = by hand):"), 0, 6); sg.Controls.Add(pitchBox, 1, 6);
            settingsBox.Controls.Add(sg);

            // GR editor
            grTable = new DataTable();
            grTable.Columns.Add("Box", typeof(string));
            foreach (var c in new[] { "Outer L", "Outer W", "Outer H" }) grTable.Columns.Add(c, typeof(double));
            grTable.Columns.Add("P150 L / W / H", typeof(string)); grTable.Columns.Add("Panels", typeof(int)); grTable.Columns.Add("Dividers", typeof(int));
            grDivTable = new DataTable();
            grDivTable.Columns.Add("Divider", typeof(string)); grDivTable.Columns.Add("Box", typeof(string)); grDivTable.Columns.Add("Length", typeof(double)); grDivTable.Columns.Add("Position", typeof(double)); grDivTable.Columns.Add("Placed by", typeof(string));
            grPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            grGrid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = grTable };
            grInfo = new Label { Dock = DockStyle.Top, Height = 48, ForeColor = Color.DimGray, Padding = new Padding(4) };
            var grDivLabel = new Label { Dock = DockStyle.Bottom, Height = 20, Text = "Dividers (P350) - Position = offset from the frame profile, editable where the divider has its own offset", ForeColor = Color.DimGray };
            grDivGrid = new DataGridView { Dock = DockStyle.Bottom, Height = 190, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = grDivTable };
            var grButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 200, Padding = new Padding(2) };
            chkShared = new CheckBox { Text = "Boxes with the same width / height change together", AutoSize = true, Checked = true };
            var btnAddR = Btn("Add box right", 105); var btnAddL = Btn("Add box left", 100); var btnAddF = Btn("Add box front", 105); var btnRemove = Btn("Remove box", 100);
            var btnGrSave = Btn("Save GR design...", 130); var btnGrOpen = Btn("Load GR design...", 130); var btnGrCut = Btn("Save cut list (CSV)...", 150);
            grNewName = new TextBox { Width = 200 }; grPrefix = new TextBox { Width = 70 };
            var btnGrApply = new Button { Text = "Write new GR to Inventor", Size = new Size(180, 28), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            grButtons.Controls.AddRange(new Control[] { chkShared, btnAddR, btnAddL, btnAddF, btnRemove, btnGrSave, btnGrOpen, btnGrCut,
                new Label { Text = "New GR name:", AutoSize = true, Margin = new Padding(3, 9, 0, 0) }, grNewName,
                new Label { Text = "Part prefix:", AutoSize = true, Margin = new Padding(3, 9, 0, 0) }, grPrefix, btnGrApply });
            grPanel.Controls.Add(grGrid); grPanel.Controls.Add(grDivLabel); grPanel.Controls.Add(grDivGrid); grPanel.Controls.Add(grInfo); grPanel.Controls.Add(grButtons);
            split.Panel1.Controls.Add(grPanel); split.Panel1.Controls.Add(grid); split.Panel1.Controls.Add(errLabel); split.Panel1.Controls.Add(settingsBox);

            // right: drawing
            twin = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var twinBar = new Panel { Dock = DockStyle.Top, Height = 66 };
            viewBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(44, 5), Width = 110 };
            viewBox.Items.AddRange(Draw.Views);
            chkHidden = new CheckBox { Text = "Show hidden edges", AutoSize = true, Location = new Point(170, 8) };
            btnSvg = new Button { Text = "Save drawing (SVG)...", Location = new Point(310, 4), Size = new Size(150, 26) };
            btnGrLoad = new Button { Text = "Load GR export...", Location = new Point(4, 36), Size = new Size(130, 26), Visible = false };
            btnGrInv = new Button { Text = "Read GR from Inventor", Location = new Point(140, 36), Size = new Size(150, 26), Visible = false };
            presetLabel = new Label { Text = "Preset:", AutoSize = true, Location = new Point(300, 41), Visible = false };
            presetBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(350, 37), Width = 230, Visible = false };
            twinBar.Controls.AddRange(new Control[] { new Label { Text = "View:", AutoSize = true, Location = new Point(4, 9) }, viewBox, chkHidden, btnSvg, btnGrLoad, btnGrInv, presetLabel, presetBox });
            split.Panel2.Controls.Add(twin); split.Panel2.Controls.Add(twinBar);
            Controls.Add(split);

            // top
            var top = new Panel { Dock = DockStyle.Top, Height = 78 };
            typeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, 9), Width = 215 };
            // Sub type: which master is used. Auto follows the split width in Settings.
            subBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(330, 9), Width = 110 };
            subBox.Items.AddRange(new object[] { "Auto (width)", "1T", "2T" });
            subBox.SelectedIndex = 0;
            btnMaster = new Button { Text = "Open master", Location = new Point(450, 7), Size = new Size(120, 27) };
            btnSetMaster = new Button { Text = "Set up master...", Location = new Point(576, 7), Size = new Size(140, 27) };
            btnMode = new Button { Text = "Switch to GR editing", Location = new Point(730, 7), Size = new Size(190, 28), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            modeLabel = new Label { Text = "Mode: panel editing (FP / RP / TP)", AutoSize = true, Location = new Point(730, 48), ForeColor = Color.DarkBlue };
            statusLabel = new Label { AutoSize = true, Location = new Point(930, 12), ForeColor = Color.DimGray };
            masterLabel = new Label { AutoSize = false, AutoEllipsis = true, Location = new Point(450, 48), Size = new Size(275, 18), ForeColor = Color.DimGray };
            nameBox = new TextBox { Location = new Point(110, 43), Width = 330, Font = new Font("Segoe UI", 11) };
            btnOpenGr = new Button { Text = "Open GR...", Location = new Point(450, 42), Size = new Size(120, 28), Visible = false };
            top.Controls.AddRange(new Control[] { new Label { Text = "Type:", AutoSize = true, Location = new Point(10, 12) }, typeBox, subBox, masterLabel, btnMaster, btnSetMaster, btnMode, modeLabel, statusLabel,
                new Label { Text = "Name:", AutoSize = true, Location = new Point(10, 47) }, nameBox, btnOpenGr });
            Controls.Add(top);

            // bottom: buttons, log, dev log
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0) };
            btnApply = new Button { Text = "Apply to Inventor", Size = new Size(160, 30), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnReload = Btn("Reload file", 110, 30); btnLearn = Btn("Learn hardware holes", 160, 30); btnDevSave = Btn("Save dev log...", 120, 30);
            buttons.Controls.AddRange(new Control[] { btnApply, btnReload, btnLearn, btnDevSave });
            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
            devBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 8) };
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tLog = new TabPage("Log"); tLog.Controls.Add(logBox); var tDev = new TabPage("Dev log"); tDev.Controls.Add(devBox);
            tabs.TabPages.Add(tLog); tabs.TabPages.Add(tDev);
            bottom.Controls.Add(tabs); bottom.Controls.Add(buttons);
            Controls.Add(bottom);

            redraw = new Timer { Interval = 150 };
            redraw.Tick += (s, e) => { redraw.Stop(); UpdateDrawing(); };

            // events
            grid.DataBindingComplete += (s, e) => { foreach (var c in new[] { "Name", "Unit", "Description" }) if (grid.Columns[c] != null) grid.Columns[c].ReadOnly = true; UpdateGridLocks(); };
            grid.DataError += (s, e) => { Log.Write($"Invalid value in row {e.RowIndex + 1} - enter a number."); e.Cancel = true; };
            table.ColumnChanged += (s, e) =>
            {
                if (syncing || e.Column.ColumnName != "Value") return;
                if (U.SizeParams.Contains((string)e.Row["Name"])) NameFromGrid();
                UpdatePreview();
            };
            nameBox.TextChanged += (s, e) => { if (syncing) return; StandardFromName(); GridFromName(); UpdatePreview(); };
            typeBox.SelectedIndexChanged += (s, e) => { if (!syncing && typeBox.SelectedIndex >= 0) StandardFromList(); };
            subBox.SelectedIndexChanged += (s, e) => { if (syncing || std == null) return; SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); ShowMaster(); };
            viewBox.SelectedIndexChanged += (s, e) => UpdateDrawing();
            twin.Paint += (s, e) => Painter.Paint(e.Graphics, shapes);
            twin.Resize += (s, e) => { redraw.Stop(); redraw.Start(); };
            chkHidden.CheckedChanged += (s, e) => UpdateDrawing();
            chkFolder.CheckedChanged += (s, e) => FolderControls();
            foreach (var nb in new[] { gapBox, thickBox, splitBox, pitchBox }) nb.ValueChanged += (s, e) => { if (!syncing) { SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); } };
            btnBrowse.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Folder for new panels (this session only)" }) if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = d.SelectedPath; };

            btnMaster.Click += (s, e) => Step("Open master", () => { Inv.OpenVisible(std.MasterFor(CurrentSub())); Log.Write($"Master {std.Id} {CurrentSub()} open; the name is unchanged."); }, true);
            btnSetMaster.Click += (s, e) => Step("Set up master", SetUpMaster, true);
            btnMode.Click += (s, e) => Step("Switch mode", () => SetMode(mode == "GR" ? "Panel" : "GR"));
            btnApply.Click += (s, e) => Step("Apply to Inventor", ApplyPanel, true);
            btnReload.Click += (s, e) => Step("Reload file", () => { SetGrid(ParamFile.Read(ParamPath)); NameFromGrid(); UpdatePreview(); });
            btnLearn.Click += (s, e) => Step("Learn hardware holes", () => { Inv.LearnHardware(standards, std, Standards.Get(GridValues(), "HoleDia"), Standards.Get(GridValues(), "HoleZ")); UpdatePreview(); }, true);
            btnDevSave.Click += (s, e) => Step("Save dev log", () =>
            {
                using (var d = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "devlog.txt" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Log.SaveDev(d.FileName); Log.Write($"Saved dev log {d.FileName}"); }
            });
            btnSvg.Click += (s, e) => Step("Save drawing", SaveDrawing);

            // GR events
            btnGrLoad.Click += (s, e) => Step("Load GR export", () =>
            {
                using (var d = new OpenFileDialog { Filter = "GR export (*.xml)|*.xml", Title = "GR export: select the tree and the constraints file (or one export that has both)", Multiselect = true })
                    if (d.ShowDialog(this) == DialogResult.OK) SetGr(Gr.Build(Gr.ReadExportFiles(d.FileNames)));
            });
            btnGrInv.Click += (s, e) => Step("Read GR from Inventor", () => SetGr(Log.Measure("Build the GR model", () => Gr.Build(Inv.ReadGr()))), true);
            btnOpenGr.Click += (s, e) => Step("Open GR", () =>
            {
                using (var d = new OpenFileDialog { Filter = "Inventor assembly (*.iam)|*.iam", Title = "Open a GR assembly" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Inv.OpenVisible(d.FileName); SetGr(Log.Measure("Build the GR model", () => Gr.Build(Inv.ReadGr()))); }
            }, true);
            presetBox.SelectedIndexChanged += (s, e) =>
            {
                if (syncing || presetBox.SelectedIndex < 0) return;
                var file = Path.Combine(GrPresetDir, presetBox.SelectedItem + ".json");
                Step("Load GR preset", () => SetGr(Gr.ReadDesign(file)));
            };
            grGrid.DataBindingComplete += (s, e) =>
            {
                foreach (var c in new[] { "Box", "P150 L / W / H", "Panels", "Dividers" }) if (grGrid.Columns[c] != null) grGrid.Columns[c].ReadOnly = true;
                foreach (var c in new[] { "Outer L", "Outer W", "Outer H" }) if (grGrid.Columns[c] != null) grGrid.Columns[c].HeaderText = c + " (mm)";
            };
            grGrid.DataError += (s, e) => { Log.Write("Enter a number in mm."); e.Cancel = true; };
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
                foreach (DataGridViewRow r in grDivGrid.Rows)
                    if (!Convert.ToString(r.Cells["Placed by"].Value).StartsWith("offset")) { r.Cells["Position"].ReadOnly = true; r.Cells["Position"].Style.BackColor = Color.WhiteSmoke; }
            };
            grDivGrid.DataError += (s, e) => { Log.Write("Enter a number in mm."); e.Cancel = true; };
            grDivTable.ColumnChanged += (s, e) =>
            {
                if (syncing || e.Column.ColumnName != "Position" || e.Row["Position"] is DBNull) return;
                string n = (string)e.Row["Divider"]; double v = (double)e.Row["Position"];
                BeginInvoke(new Action(() => GrEdit("Move divider", () => { Gr.SetDividerPosition(gr, n, v); Log.Write($"GR: {n} at {U.F(v)} mm"); })));
            };
            foreach (var (b, side) in new[] { (btnAddR, "Right"), (btnAddL, "Left"), (btnAddF, "Front") })
                b.Click += (s, e) => GrEdit("Add box", () =>
                {
                    int refNo = SelectedBox();
                    var f = Gr.AddFrame(gr, refNo, side);
                    Log.Write($"GR: added {f.Label} ({side.ToLower()} of Box {refNo - 1}), outer {U.F(f.Lx + Gr.CornerAdd)} x {U.F(f.Wy + Gr.CornerAdd)} x {U.F(f.Hz + Gr.CornerAdd)} mm");
                });
            btnRemove.Click += (s, e) => GrEdit("Remove box", () => { int no = SelectedBox(); Gr.RemoveFrame(gr, no); Log.Write($"GR: removed Box {no - 1} with its panels and dividers"); });
            btnGrSave.Click += (s, e) => Step("Save GR design", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                Directory.CreateDirectory(GrPresetDir);
                using (var d = new SaveFileDialog { InitialDirectory = GrPresetDir, Filter = "GR design (*.json)|*.json", FileName = gr.Name + "-design.json" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Gr.SaveDesign(gr, d.FileName); Log.Write($"Saved GR design {d.FileName}"); UpdatePresets(); }
            });
            btnGrOpen.Click += (s, e) => Step("Load GR design", () =>
            {
                using (var d = new OpenFileDialog { InitialDirectory = GrPresetDir, Filter = "GR design (*.json)|*.json" })
                    if (d.ShowDialog(this) == DialogResult.OK) SetGr(Gr.ReadDesign(d.FileName));
            });
            btnGrCut.Click += (s, e) => Step("Save cut list", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                using (var d = new SaveFileDialog { Filter = "Cut list (*.csv)|*.csv", FileName = gr.Name + "-cut-list.csv" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Gr.ExportCutList(gr, d.FileName); Log.Write($"Saved cut list {d.FileName}"); }
            });
            btnGrApply.Click += (s, e) => Step("Write new GR to Inventor", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                Inv.ApplyGr(gr, grNewName.Text, grPrefix.Text);
                UpdateGrGrid();
            }, true);
        }

        // ------------------------------------------------------------------ start

        void OnShownLoad()
        {
            syncing = true;
            try
            {
                gapBox.Value = (decimal)settings.MinHoleGap; thickBox.Value = (decimal)Math.Max(0.1, settings.Thickness);
                splitBox.Value = (decimal)settings.SplitWidth; pitchBox.Value = (decimal)settings.MaxHolePitch;
                viewBox.SelectedIndex = 0;
            }
            finally { syncing = false; }
            FolderControls();
            Step("Load", () =>
            {
                // The build time shows at once whether this is the exe you just built.
                try { string built = File.GetLastWriteTime(Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm"); Log.Write($"Inventor Sizer, built {built} - {(Program.Dev ? "dev run" : "user run")}"); titleTail = $"  (built {built})" + (Program.Dev ? "  -  DEV RUN" : ""); Text += titleTail; } catch { }
                foreach (Control c in new Control[] { btnMaster, btnSetMaster, btnLearn }) c.Visible = Program.Dev;
                standards = Standards.Read(StandardsDir, Root);
                typeBox.Items.Clear();
                foreach (var s in standards) typeBox.Items.Add(s.ToString());
                SelectStandard(standards[0]);
                SetGrid(ParamFile.Read(ParamPath));
                NameFromGrid();
            });
            Log.Write("Ready. Inventor is not read at start: it is used when you make a new version (Apply / Write) or open / read something.");
            UpdatePreview();
        }

        // Runs a step with a wait cursor, logs it and times it in the dev log; errors go to a message box.
        void Step(string title, Action body, bool inventor = false)
        {
            Cursor = Cursors.WaitCursor; Enabled = false;
            try
            {
                Log.Write("== " + title);
                Log.Measure(title, body);
                Log.Write("Done.");
            }
            catch (Exception e)
            {
                var msg = e is SizerException ? e.Message : e.ToString();
                Log.Write("ERROR: " + msg);
                MessageBox.Show(this, e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Enabled = true; Cursor = Cursors.Default;
                if (inventor) statusLabel.Text = Inv.Status();
            }
        }

        // ------------------------------------------------------------------ parameter grid

        DataRow FindRow(string name) => table.Rows.Cast<DataRow>().FirstOrDefault(r => (string)r["Name"] == name);

        void SetGrid(List<Param> ps)
        {
            syncing = true;
            try { table.Rows.Clear(); foreach (var p in ps) table.Rows.Add(p.Name, p.Value, p.Unit, p.Description); table.AcceptChanges(); }
            finally { syncing = false; }
        }

        List<Param> GridParams()
        {
            grid.EndEdit();
            var o = new List<Param>();
            foreach (DataRow r in table.Rows)
            {
                if (r["Value"] is DBNull) throw new SizerException($"Parameter '{r["Name"]}' has no value.");
                o.Add(new Param((string)r["Name"], (double)r["Value"], (string)r["Unit"], r["Description"] as string));
            }
            return o;
        }

        Dictionary<string, double> GridValues()
        {
            var v = new Dictionary<string, double>();
            foreach (DataRow r in table.Rows) if (!(r["Value"] is DBNull)) v[(string)r["Name"]] = (double)r["Value"];
            return v;
        }

        string SizeToken()
        {
            var vals = new List<string>();
            foreach (var n in U.SizeParams) { var r = FindRow(n); if (r == null || r["Value"] is DBNull) return null; vals.Add(U.F((double)r["Value"])); }
            return string.Join("x", vals);
        }

        void NameFromGrid()
        {
            var token = SizeToken();
            if (token == null) return;
            syncing = true;
            try { nameBox.Text = U.SizeRegex.IsMatch(nameBox.Text) ? U.SizeRegex.Replace(nameBox.Text, token, 1) : std.Prefix + token; }
            finally { syncing = false; }
        }

        void GridFromName()
        {
            var m = U.SizeRegex.Match(nameBox.Text);
            if (!m.Success) return;
            syncing = true;
            try { for (int i = 0; i < 3; i++) { var r = FindRow(U.SizeParams[i]); if (r != null) r["Value"] = U.P(m.Groups[i + 1].Value); } }
            finally { syncing = false; }
        }

        // ------------------------------------------------------------------ types

        void SelectStandard(Standard s)
        {
            std = s;
            syncing = true;
            try { typeBox.SelectedIndex = standards.IndexOf(s); } finally { syncing = false; }
            if (grid.Rows.Count > 0) UpdateGridLocks();
            ShowMaster();
        }

        // The sub type in use: the one chosen, or with Auto the one the width asks for.
        string CurrentSub()
        {
            if (subBox.SelectedIndex > 0) return Standards.SubTypes[subBox.SelectedIndex - 1];
            try { return Standards.SubOf(HoleRules.InnerSplit(Standards.Get(GridValues(), "Width"), settings)); } catch { return "1T"; }
        }

        // Shows which master the current type and sub type lead to.
        void ShowMaster()
        {
            if (std == null || mode == "GR") return;
            string sub = CurrentSub();
            bool has = std.HasMaster(sub);
            btnMaster.Enabled = has;
            masterLabel.ForeColor = has ? Color.DimGray : Color.Firebrick;
            masterLabel.Text = has ? $"Master {std.Id} {sub}: {Path.GetFileName(std.MasterFor(sub))}" : $"No {std.Id} {sub} master in masters\\{std.Id}\\{sub}";
        }

        // The type's own values (e.g. HoleZ), once per type change; the height only when picked from the list.
        void SetTypeDefaults(Standard s, bool withHeight)
        {
            if (defaultsFor == s.Id && !withHeight) return;
            defaultsFor = s.Id;
            bool was = syncing; syncing = true;
            try
            {
                foreach (var kv in s.Defaults) { var r = FindRow(kv.Key); if (r != null && !Equals(r["Value"], kv.Value)) { r["Value"] = kv.Value; Log.Write($"  {s.Family}: {kv.Key} = {U.F(kv.Value)}"); } }
                if (withHeight && s.Height > 0) { var r = FindRow("Height"); if (r != null) r["Value"] = s.Height; }
            }
            finally { syncing = was; }
        }

        void StandardFromName()
        {
            var match = Standards.ForName(standards, nameBox.Text);
            if (match == null) return;
            if (match != std) { SelectStandard(match); Log.Write("Type: " + match.Name); }
            SetTypeDefaults(match, false);
        }

        void StandardFromList()
        {
            var s = standards[typeBox.SelectedIndex];
            SelectStandard(s);
            syncing = true;
            try { nameBox.Text = Standards.WithPrefix(standards, nameBox.Text, s.Prefix); } finally { syncing = false; }
            SetTypeDefaults(s, true);
            NameFromGrid();
            UpdatePreview();
        }

        // ------------------------------------------------------------------ rules + drawing

        void AutoHoles()
        {
            if (settings.MaxHolePitch <= 0) return;
            var v = GridValues();
            if (!v.ContainsKey("Width") || !v.ContainsKey("Length") || !v.ContainsKey("HoleEdge")) return;
            var want = new Dictionary<string, double> { ["HolesL"] = HoleRules.AutoCount(v["Length"], v["HoleEdge"], settings.MaxHolePitch, false), ["HolesW"] = HoleRules.AutoCount(v["Width"], v["HoleEdge"], settings.MaxHolePitch, true) };
            bool was = syncing; syncing = true;
            try { foreach (var kv in want) { var r = FindRow(kv.Key); if (r != null && (r["Value"] is DBNull || (double)r["Value"] != kv.Value)) r["Value"] = kv.Value; } }
            finally { syncing = was; }
        }

        void UpdateGridLocks()
        {
            foreach (DataGridViewRow gr in grid.Rows)
            {
                string name = Convert.ToString(gr.Cells["Name"].Value);
                bool locked = FixedParams.Contains(name) || (settings.MaxHolePitch > 0 && AutoParams.Contains(name)) || (CountParams.Contains(name) && (std == null || std.HardwareCounts.Count == 0));
                gr.ReadOnly = locked;
                gr.DefaultCellStyle.ForeColor = locked ? Color.Gray : Color.Black;
            }
        }

        void UpdatePreview()
        {
            ShowMaster();
            if (mode == "GR") { UpdateDrawing(); return; }
            AutoHoles();
            values = GridValues();
            var errors = new List<string>();
            var counts = Hardware.Counts(std, values);
            foreach (var k in counts.Keys.ToList())
                if (counts[k] < 1 || counts[k] != Math.Round(counts[k])) { errors.Add($"{k} count must be a whole number of 1 or more."); counts.Remove(k); }
            hw = std?.Hardware != null && Standards.Get(values, "Width") > 0 && Standards.Get(values, "Length") > 0 && Standards.Get(values, "Height") > 0
                ? Hardware.Place(std.Hardware, values["Width"], values["Length"], values["Height"], counts) : new List<PlacedHole>();
            try { layout = HoleRules.Layout(values, settings, std); errors.AddRange(layout.Errors); }
            catch (Exception e) { layout = null; errors.Add(e.Message); }
            if (!U.SizeRegex.IsMatch(nameBox.Text)) errors.Add("The name must contain a size like 386x656x47.");
            var lines = errors.Count == 0 && layout != null ? layout.Notes.Concat(Hardware.Notes(hw)).ToList() : errors;
            errLabel.ForeColor = errors.Count > 0 ? Color.DarkRed : Color.DimGray;
            errLabel.Text = string.Join(Environment.NewLine, lines);
            errLabel.Visible = lines.Count > 0;
            btnApply.Enabled = errors.Count == 0;
            UpdateDrawing();
        }

        Bom CurrentBom() => std == null ? null : Standards.GetBom(std, values, settings.Thickness, nameBox.Text.Trim(), layout);

        void UpdateDrawing()
        {
            var size = twin.ClientSize;
            string view = (string)viewBox.SelectedItem ?? "Isometric";
            shapes = Log.Measure("Draw", () => mode == "GR"
                ? GrDrawing.Window(gr, view, size.Width, size.Height)
                : PanelDrawing.Window(PanelValues.Of(values, settings.Thickness, hw), layout, view, size.Width, size.Height, chkHidden.Checked, CurrentBom()));
            twin.Invalidate();
        }

        void SaveDrawing()
        {
            using (var d = new SaveFileDialog { Filter = "SVG drawing (*.svg)|*.svg", FileName = (mode == "GR" ? gr?.Name ?? "GR" : nameBox.Text.Trim()) + ".svg" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sh = mode == "GR" ? GrDrawing.Sheet(gr) : PanelDrawing.Sheet(PanelValues.Of(values, settings.Thickness, hw), layout, CurrentBom(), chkHidden.Checked);
                File.WriteAllText(d.FileName, Draw.Svg(sh, 1400, 990, mode == "GR" ? gr?.Name ?? "GR" : nameBox.Text.Trim()));
                Log.Write($"Saved drawing {d.FileName}");
            }
        }

        // ------------------------------------------------------------------ settings, apply

        void SettingsFromForm()
        {
            if (syncing) return;
            settings.MinHoleGap = (double)gapBox.Value; settings.Thickness = (double)thickBox.Value;
            settings.SplitWidth = (double)splitBox.Value; settings.MaxHolePitch = (double)pitchBox.Value;
            settings.ForceSplit = subBox.SelectedIndex;   // 0 = Auto, 1 = 1T, 2 = 2T
        }

        void SaveFormSettings() { SettingsFromForm(); settings.Save(SettingsPath); }

        void FolderControls()
        {
            folderBox.Enabled = btnBrowse.Enabled = chkFolder.Checked;
            folderInfo.Text = chkFolder.Checked ? "This session only: new panels go into the folder above." : "New panels are made from the type's master; you choose their folder on Apply.";
        }

        // One button for the whole master job: choose the file, open it, write the type's
        // parameters and formulas into it, make it the master of the type, and offer to save it.
        void SetUpMaster()
        {
            string file;
            using (var d = new OpenFileDialog { Filter = "Inventor assembly (*.iam)|*.iam", Title = $"Choose the master assembly for {std.Name}" })
            {
                string cur = std.MasterFor(CurrentSub());
                if (std.HasMaster(CurrentSub())) { d.InitialDirectory = Path.GetDirectoryName(cur); d.FileName = Path.GetFileName(cur); }
                if (d.ShowDialog(this) != DialogResult.OK) { Log.Write("Cancelled: no master chosen."); return; }
                file = d.FileName;
            }
            string go = Choose("Set up master", $"{Path.GetFileName(file)}\n\nThe app opens this assembly, creates the user parameters and writes the formulas from {std.File} into it and its parts, and uses it as the master for {std.Name} from now on.\n\nNothing is saved until you say so. Use a copy the first time.",
                "Set up", "Cancel");
            if (go != "Set up") { Log.Write("Cancelled: nothing was changed."); return; }
            Inv.OpenVisible(file);
            Inv.MasterSetup(std);
            string sub = Inv.MasterSubType(std);   // 1 inner panel = 1T, 2 = 2T
            Log.Write($"  this master is {std.Id} {sub}");
            Standards.SetMaster(std, sub, file, Root);
            SelectStandard(std);
            string store = Path.Combine(Root, "masters", std.Id, sub);
            string save = Choose($"Save the {std.Id} {sub} master?", $"The setup is done, and this is the {std.Id} {sub} master ({(sub == "2T" ? "2 inner panels" : "1 inner panel")}). Look at the model in Inventor: nothing should have moved.\n\n" +
                $"Save into Inventor Sizer: saves the assembly and its {string.Join(" / ", std.Parts.Select(p => p.Suffix))} parts as copies in\n{store}\nand uses that copy as the master. Users then need no setup. The files you opened stay as they were on disk.\n\n" +
                "Save where it is: saves the opened files themselves.\n\nNot now: leaves it open and unsaved; close it without saving to undo the setup.",
                "Save into Inventor Sizer", "Save where it is", "Not now");
            if (save == "Save into Inventor Sizer")
            {
                string copy = Inv.SaveMasterIntoApp(std, store);
                Standards.SetMaster(std, sub, copy, Root);
                SelectStandard(std);
            }
            else if (save == "Save where it is") Inv.SaveActiveAssembly();
            else Log.Write("The master is not saved yet. Apply uses it only after it is saved.");
        }

        // A question with named buttons; the first is the default, the last answers Esc and
        // closing the box. Returns the text of the clicked button.
        string Choose(string title, string text, params string[] buttons)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Font = Font })
            {
                var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 12) };
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
                string answer = buttons[buttons.Length - 1];
                foreach (string b in buttons)
                {
                    var btn = new Button { Text = b, AutoSize = true, MinimumSize = new Size(100, 0) };
                    btn.Click += (s, e) => { answer = b; f.Close(); };
                    row.Controls.Add(btn);
                    if (b == buttons[0]) f.AcceptButton = btn;
                    if (b == buttons[buttons.Length - 1]) f.CancelButton = btn;
                }
                var stack = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
                stack.Controls.Add(label);
                stack.Controls.Add(row);
                f.Controls.Add(stack);
                f.ShowDialog(this);
                return answer;
            }
        }

        void ApplyPanel()
        {
            if (!chkFolder.Checked || folderBox.Text.Trim() == "")
            {
                using (var d = new FolderBrowserDialog { Description = "Folder for the new panel files (kept for this session)" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    chkFolder.Checked = true; folderBox.Text = d.SelectedPath;
                }
            }
            SaveFormSettings();
            var set = settings.Clone(); set.OutputFolder = folderBox.Text.Trim();
            // A panel of this name is there already: use it, replace it, or stop.
            bool overwrite = false;
            string target = Path.Combine(set.OutputFolder, nameBox.Text.Trim() + ".iam");
            if (File.Exists(target))
            {
                string choice = Choose("Panel already exists", $"{Path.GetFileName(target)} already exists in\n{set.OutputFolder}\n\n" +
                    "Use existing: opens that panel, nothing is written.\n" +
                    "Overwrite: replaces the assembly and its parts with the values from this window. Every assembly that uses these files changes with them.",
                    "Use existing", "Overwrite", "Cancel");
                if (choice == "Use existing") { Inv.OpenVisible(target); return; }
                if (choice != "Overwrite") { Log.Write("Cancelled: nothing was written."); return; }
                overwrite = true;
            }
            Func<List<string>, bool> confirm = files => Choose("Part files already exist",
                "These files already exist:\n" + string.Join("\n", files) + "\n\nOverwrite: replaces them. Every assembly that uses these files changes with them.",
                "Overwrite", "Cancel") == "Overwrite";
            var newName = Inv.ApplyPanel(nameBox.Text, GridParams(), set, ParamPath, standards, std, overwrite, confirm);
            syncing = true;
            try { nameBox.Text = newName; } finally { syncing = false; }
        }

        // ------------------------------------------------------------------ modes, GR

        void SetMode(string m)
        {
            if (m == "GR" && Gr.PartRules != null && mode != "GR" && gr == null) Gr.LoadRules(Path.Combine(StandardsDir, "GR.json"));
            mode = m;
            bool g = m == "GR";
            btnMode.Text = g ? "Switch to panel editing" : "Switch to GR editing";
            modeLabel.Text = g ? "Mode: GR editing (unit frame)" : "Mode: panel editing (FP / RP / TP)";
            Text = "Inventor Sizer - " + (g ? "GR editing" : "panel editing") + titleTail;
            foreach (Control c in new Control[] { typeBox, subBox, masterLabel, nameBox, btnApply, btnReload, chkHidden, grid, settingsBox }) c.Visible = !g;
            foreach (Control c in new Control[] { btnMaster, btnSetMaster, btnLearn }) c.Visible = !g && Program.Dev;   // dev run only
            foreach (Control c in new Control[] { btnGrLoad, btnGrInv, btnOpenGr, presetBox, presetLabel, grPanel }) c.Visible = g;
            if (g) { errLabel.Visible = false; UpdatePresets(); UpdateGrGrid(); UpdateDrawing(); } else UpdatePreview();
        }

        void SetGr(GrModel m)
        {
            gr = m;
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
                        grDivTable.Rows.Add(c.Name, gr.ById(c.Frame)?.Label ?? "", c.Length, c.Position.HasValue ? (object)c.Position.Value : DBNull.Value, c.PosConstraint != "" ? $"offset ({c.PosConstraint})" : "joints");
                }
                grTable.AcceptChanges(); grDivTable.AcceptChanges();
            }
            finally { syncing = false; }
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
            catch (Exception e) { Log.Write($"ERROR ({title}): {e.Message}"); }
            UpdateGrGrid();
            UpdateDrawing();
        }
    }
}
