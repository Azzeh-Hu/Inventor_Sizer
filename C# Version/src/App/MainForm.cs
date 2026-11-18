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
        static readonly string[] CountParams = { "HolesL", "HolesW" }, FixedParams = { "HoleDia_sides", "HoleDia_handle",
        "HoleDia_hinge_small", "HoleDia_hinge_large" };
        static readonly string[] AutoParams = { "HolesL", "HolesW" }, HardwareCountParams = { "HingeCount", "HandleCount" };
        // Fixed or automatic values: still pushed to Inventor, just not shown in the grid.
        static readonly string[] HiddenParams = { "HoleEdge", "HoleZ", "HoleDia", "Clearance", "HolePitchL", "HolePitchW",
        "HoleDia_sides", "HoleDia_handle", "HoleDia_hinge_small", "HoleDia_hinge_large" };

        // controls
        ComboBox viewBox, presetBox;
        TextBox nameBox, folderBox, logBox, devBox, grNewName, grPrefix;
        Button btnReload, btnDevSave, btnBrowse;
        RibbonButton btnApply, btnSvg, btnOpenGr, btnGrInv, btnParams, btnParamsView, devMenu, grFiles;
        FlatButton btnActivity;
        ToolStripMenuItem miOpenMaster, miSetMaster, miLearn;
        Label statusLabel, masterLabel, folderInfo, grInfo;
        Segmented modeSwitch;
        CheckBox chkFolder, chkHidden, chkShared;
        NumericUpDown gapBox, thickBox, splitBox, pitchBox;
        DataTable table, grTable, grDivTable;
        DataGridView grid, grGrid, grDivGrid;
        Ribbon ribbon;
        RibbonTab tabPanel, tabView, tabSettings, tabGr;
        RibbonGroup gMaster;
        TypeBrowser browser;
        Panel statusBar, grColumn;
        SplitContainer split;
        PictureBox twin;
        ParamPopup paramPop;
        Point popOffset = new Point(16, 76);   // the parameter pop-up, relative to the drawing's top left
        bool placingPop;
        int subPick;                          // sub type picked in the browser: 0 = Auto (width), 1 = 1T, 2 = 2T
        DateTime lastPick;
        readonly TwinOverlay hud = new TwinOverlay();
        ActivityPanel activity;
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
            FormClosing += (s, e) => { try { SaveFormSettings(); } catch { } try { Inv.ClosePreloaded(); } catch { } };
        }

        // ------------------------------------------------------------------ layout
        // Like Inventor: the ribbon on top, the type browser on the left, the drawing (the
        // graphics window) in all the rest, a status bar at the bottom. The parameter table
        // floats over the drawing in its own pop-up.

        static NumericUpDown Num(int dec, decimal max, decimal inc, decimal min = 0) => new NumericUpDown { DecimalPlaces = dec, Maximum = max, Minimum = min, Increment = inc, Font = Theme.Body, TextAlign = HorizontalAlignment.Right };
        static FlowLayoutPanel Row(Color back, bool rtl = false) => new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = back, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Padding = new Padding(0), Margin = new Padding(0) };
        static Panel Spacer(DockStyle dock, int size) => new Panel { Dock = dock, Height = size, Width = size, BackColor = Theme.App };
        static Caption Hint(string text) => new Caption(text) { Margin = new Padding(0, 0, 0, 4), BackColor = Theme.RibbonBody };

        // Ribbon rows "label .... [value]" with one label width per group.
        static Control[] NumRows(params (string Label, NumericUpDown Box)[] items)
        {
            int lw = items.Max(i => TextRenderer.MeasureText(i.Label, Theme.Body).Width) + 12;
            return items.Select(i =>
            {
                var p = new Panel { Size = new Size(lw + 100, 32), BackColor = Theme.RibbonBody, Margin = new Padding(0, 0, 0, 4) };
                p.Controls.Add(new Label { Text = i.Label, AutoSize = false, Size = new Size(lw, 20), Location = new Point(0, 7), ForeColor = Theme.InkSoft, Font = Theme.Body, BackColor = Theme.RibbonBody });
                p.Controls.Add(new Field(i.Box, 96, 28) { Location = new Point(lw, 2), Margin = new Padding(0) });
                return (Control)p;
            }).ToArray();
        }

        void BuildUi()
        {
            Text = "Inventor Sizer - panel editing";
            Size = new Size(1440, 920);
            MinimumSize = new Size(1120, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Body;
            BackColor = Theme.App;
            DoubleBuffered = true;

            // ---------------------------------------------------------- parameter table: a floating pop-up
            table = new DataTable();
            table.Columns.Add("Name", typeof(string)); table.Columns.Add("Value", typeof(double)); table.Columns.Add("Unit", typeof(string)); table.Columns.Add("Description", typeof(string));
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = table };
            Glass.StyleDarkGrid(grid);
            paramPop = new ParamPopup(grid) { Owner = this };
            btnReload = paramPop.Reload;

            // ---------------------------------------------------------- body: browser | drawing
            split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Theme.RibbonLine, SplitterWidth = 4, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.BackColor = Theme.App; split.Panel2.BackColor = Theme.Paper;
            browser = new TypeBrowser { Dock = DockStyle.Fill };

            // GR editor (GR mode): in the browser's place
            grTable = new DataTable();
            grTable.Columns.Add("Box", typeof(string));
            foreach (var c in new[] { "Outer L", "Outer W", "Outer H" }) grTable.Columns.Add(c, typeof(double));
            grTable.Columns.Add("P150 L / W / H", typeof(string)); grTable.Columns.Add("Panels", typeof(int)); grTable.Columns.Add("Dividers", typeof(int));
            grDivTable = new DataTable();
            grDivTable.Columns.Add("Divider", typeof(string)); grDivTable.Columns.Add("Box", typeof(string)); grDivTable.Columns.Add("Length", typeof(double)); grDivTable.Columns.Add("Position", typeof(double)); grDivTable.Columns.Add("Placed by", typeof(string));
            grGrid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = grTable };
            grDivGrid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = grDivTable };
            Glass.StyleGrid(grGrid, Theme.Card); Glass.StyleGrid(grDivGrid, Theme.Card);
            grInfo = new Label { Dock = DockStyle.Top, Height = 46, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(2, 0, 0, 8), BackColor = Theme.Card };
            chkShared = new Toggle("Boxes with the same width / height change together") { Checked = true };
            var sharedRow = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Card, Padding = new Padding(0, 10, 0, 0) };
            chkShared.Dock = DockStyle.Left; sharedRow.Controls.Add(chkShared);
            var btnAddR = new FlatButton("+ Right", ButtonKind.Ghost, 0, 30); var btnAddL = new FlatButton("+ Left", ButtonKind.Ghost, 0, 30);
            var btnAddF = new FlatButton("+ Front", ButtonKind.Ghost, 0, 30); var btnRemove = new FlatButton("Remove", ButtonKind.Ghost, 0, 30);
            foreach (var b in new[] { btnAddR, btnAddL, btnAddF, btnRemove }) b.Margin = new Padding(2, 0, 0, 0);
            var boxesCard = new Card("Boxes") { Dock = DockStyle.Fill };
            boxesCard.Actions.Controls.AddRange(new Control[] { btnAddR, btnAddL, btnAddF, btnRemove });
            boxesCard.Controls.Add(grGrid); boxesCard.Controls.Add(sharedRow); boxesCard.Controls.Add(grInfo);
            var dividersCard = new Card("Dividers", subtitle: "P350, position = offset from the frame profile") { Dock = DockStyle.Bottom, Height = 260 };
            dividersCard.Controls.Add(grDivGrid);
            grColumn = new Panel { Dock = DockStyle.Fill, BackColor = Theme.App, Padding = new Padding(10), Visible = false };
            grColumn.Controls.Add(boxesCard); grColumn.Controls.Add(Spacer(DockStyle.Bottom, Theme.Gap)); grColumn.Controls.Add(dividersCard);
            Card.CastShadows(grColumn);
            split.Panel1.Controls.Add(grColumn); split.Panel1.Controls.Add(browser);

            // the drawing: all the rest of the window
            twin = new PictureBox { Dock = DockStyle.Fill, BackColor = Theme.Paper };
            split.Panel2.Controls.Add(twin);

            // ---------------------------------------------------------- ribbon
            ribbon = new Ribbon();
            var logo = new Panel { Size = new Size(26, 26), Margin = new Padding(0, 7, 10, 0), BackColor = Theme.TitleBar };
            logo.Paint += (s, e) => Icons.Draw(e.Graphics, "master", new RectangleF(0, 0, 26, 26));
            var title = new Label { Text = "Inventor Sizer", Font = Theme.Serif(13f), ForeColor = Theme.TitleText, AutoSize = true, Margin = new Padding(0, 10, 22, 0), BackColor = Theme.TitleBar };
            modeSwitch = new Segmented("Panels", "GR frame") { Dark = true, Margin = new Padding(0, 5, 0, 0) };
            modeSwitch.Height = 30;
            ribbon.TitleLeft.Controls.AddRange(new Control[] { logo, title, modeSwitch });
            btnActivity = new FlatButton("Activity", ButtonKind.Glass, 0, 28) { Margin = new Padding(0, 6, 0, 0) };
            ribbon.TitleRight.Controls.Add(btnActivity);
            if (Program.Dev) ribbon.TitleRight.Controls.Add(new FlatButton("Dev run", ButtonKind.GlassPrimary, 0, 22) { Margin = new Padding(0, 9, 12, 0), Font = Theme.Ui(8f, FontStyle.Bold), Cursor = Cursors.Default, TabStop = false });

            // Panel tab
            var btnOpenPanel = new RibbonButton("Load\nassembly", "open");
            devMenu = new RibbonButton("Master", "master");
            miOpenMaster = devMenu.Add("Open master", (s, e) => Step("Open master", () => { Inv.OpenVisible(std.MasterFor(CurrentSub())); Log.Write($"Master {std.Id} {CurrentSub()} open; the name is unchanged."); }, true));
            miSetMaster = devMenu.Add("Set up master...", (s, e) => Step("Set up master", SetUpMaster, true));
            devMenu.Separator();
            miLearn = devMenu.Add("Learn hardware holes", (s, e) => Step("Learn hardware holes", () => { Inv.LearnHardware(standards, std, Standards.Get(GridValues(), "HoleDia"), Standards.Get(GridValues(), "HoleZ")); UpdatePreview(); }, true));
            nameBox = new TextBox { Font = Theme.Ui(10.5f) };
            btnParams = new RibbonButton("Parameters", "fx");
            chkFolder = new Toggle("Folder for new panels (this session only)") { Margin = new Padding(0, 0, 0, 2), Height = 26 };
            folderBox = new TextBox { Font = Theme.Body };
            btnBrowse = new RibbonButton("Browse...", "folder", false) { Margin = new Padding(6, 0, 0, 0) };
            folderInfo = new Label { AutoSize = false, AutoEllipsis = true, Size = new Size(360, 16), ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.RibbonBody, Margin = new Padding(0, 1, 0, 0) };
            btnApply = new RibbonButton("Apply to\nInventor", "apply");
            tabGr = ribbon.AddTab("GR frame");   // first: in GR mode it leads, in panel mode it is hidden
            tabPanel = ribbon.AddTab("Panel");
            tabPanel.AddGroup("Assembly", btnOpenPanel);
            gMaster = tabPanel.AddGroup("Master", devMenu);
            tabPanel.AddGroup("Panel name", Ribbon.Stack(Hint("Type and size, e.g. TP-900x1194x42"), new Field(nameBox, 290, 32) { Margin = new Padding(0) }));
            tabPanel.AddGroup("Parameters", btnParams);
            tabPanel.AddGroup("Output", Ribbon.Stack(chkFolder, Ribbon.Line(new Field(folderBox, 262, 26) { Margin = new Padding(0) }, btnBrowse), folderInfo));
            tabPanel.AddGroup("Inventor", btnApply);

            // View tab
            viewBox = new ThemedCombo(160) { Margin = new Padding(0) };
            viewBox.Items.AddRange(Draw.Views);
            chkHidden = new Toggle("Hidden edges") { Margin = new Padding(0) };
            btnSvg = new RibbonButton("Export\nSVG", "svg");
            btnParamsView = new RibbonButton("Parameters", "fx");
            var btnActivityView = new RibbonButton("Activity", "activity");
            tabView = ribbon.AddTab("View");
            tabView.AddGroup("Orientation", Ribbon.Stack(Hint("View"), viewBox), Ribbon.Stack(chkHidden));
            tabView.AddGroup("Export", btnSvg);
            tabView.AddGroup("Windows", btnParamsView, btnActivityView);

            // Settings tab
            gapBox = Num(1, 1000, 0.5m); thickBox = Num(2, 50, 0.1m, 0.1m); splitBox = Num(0, 100000, 10); pitchBox = Num(0, 100000, 10);
            tabSettings = ribbon.AddTab("Settings");
            tabSettings.AddGroup("Hole rules", Ribbon.Stack(NumRows(("Minimum hole gap (mm)", gapBox), ("Max. hole pitch for automatic counts (mm, 0 = by hand)", pitchBox))));
            tabSettings.AddGroup("Sheet and split", Ribbon.Stack(NumRows(("Sheet thickness, one sheet (Staerke, mm)", thickBox), ("Split inner panel above width (mm, 0 = never)", splitBox))));

            // GR frame tab
            presetBox = new ThemedCombo(230) { Margin = new Padding(0) };
            btnOpenGr = new RibbonButton("Open\nGR...", "open");
            btnGrInv = new RibbonButton("Read from\nInventor", "read");
            grFiles = new RibbonButton("Files", "files");
            grNewName = new TextBox { Font = Theme.Body }; grPrefix = new TextBox { Font = Theme.Body };
            var btnGrApply = new RibbonButton("Write new GR\nto Inventor", "apply");
            var gGrSource = tabGr.AddGroup("GR source", Ribbon.Stack(Hint("Preset"), presetBox), btnOpenGr, btnGrInv, grFiles);
            tabGr.AddGroup("New GR", Ribbon.Stack(Hint("New GR name"), new Field(grNewName, 220, 30) { Margin = new Padding(0) }),
                Ribbon.Stack(Hint("Part prefix"), new Field(grPrefix, 90, 30) { Margin = new Padding(0) }), btnGrApply);
            ribbon.ShowTab(tabGr, false);
            ribbon.Select(tabPanel);

            // ---------------------------------------------------------- status bar
            statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.RibbonTabs, Padding = new Padding(12, 0, 12, 0) };
            statusBar.Paint += (s, e) => { using (var p = new Pen(Theme.RibbonLine)) e.Graphics.DrawLine(p, 0, 0, statusBar.Width, 0); };
            masterLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 7, 0, 0), BackColor = Theme.RibbonTabs };
            statusLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 7, 0, 0), BackColor = Theme.RibbonTabs };
            var statLeft = Row(Theme.RibbonTabs); statLeft.Dock = DockStyle.Left; statLeft.Controls.Add(masterLabel);
            var statRight = Row(Theme.RibbonTabs, true); statRight.Dock = DockStyle.Right; statRight.Controls.Add(statusLabel);
            statusBar.Controls.Add(statLeft); statusBar.Controls.Add(statRight);

            // dock order: fill first, then the edges from the inside out
            Controls.Add(split); Controls.Add(ribbon); Controls.Add(statusBar);

            // ---------------------------------------------------------- floating glass: activity panel, toasts
            activity = new ActivityPanel();
            logBox = activity.LogBox; devBox = activity.DevBox; btnDevSave = activity.SaveDev;
            Toast.Anchor = () => RectangleToScreen(new Rectangle(0, ribbon.Bottom, ClientSize.Width, statusBar.Top - ribbon.Bottom));
            Move += (s, e) => FloatLayout(); Resize += (s, e) => FloatLayout();
            btnActivity.Click += (s, e) => ToggleActivity();
            btnActivityView.Click += (s, e) => ToggleActivity();
            btnParams.Click += (s, e) => ToggleParams();
            btnParamsView.Click += (s, e) => ToggleParams();
            paramPop.Move += (s, e) => { if (!placingPop && paramPop.Visible) { var o = twin.PointToScreen(Point.Empty); popOffset = new Point(paramPop.Left - o.X, paramPop.Top - o.Y); } };

            redraw = new Timer { Interval = 150 };
            redraw.Tick += (s, e) => { redraw.Stop(); UpdateDrawing(); };

            // events
            grid.DataBindingComplete += (s, e) =>
            {
                foreach (var c in new[] { "Name", "Unit", "Description" }) if (grid.Columns[c] != null) grid.Columns[c].ReadOnly = true;
                Glass.DarkInputColumn(grid, "Value");
                var weights = new Dictionary<string, float> { ["Name"] = 26, ["Value"] = 18, ["Unit"] = 10, ["Description"] = 46 };
                foreach (var kv in weights) if (grid.Columns[kv.Key] != null) grid.Columns[kv.Key].FillWeight = kv.Value;
                if (grid.Columns["Description"] != null) grid.Columns["Description"].DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.GlassMuted, Font = Theme.Small };
                if (grid.Columns["Unit"] != null) grid.Columns["Unit"].DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.GlassMuted };
                UpdateGridLocks();
            };
            grid.DataError += (s, e) => { Log.Write($"Invalid value in row {e.RowIndex + 1} - enter a number."); Toast.Show(this, ToastKind.Error, "Enter a number", $"Row {e.RowIndex + 1} needs a numeric value."); e.Cancel = true; };
            table.ColumnChanged += (s, e) =>
            {
                if (syncing || e.Column.ColumnName != "Value") return;
                if (U.SizeParams.Contains((string)e.Row["Name"])) NameFromGrid();
                UpdatePreview();
            };
            nameBox.TextChanged += (s, e) => { if (syncing) return; StandardFromName(); GridFromName(); UpdatePreview(); };
            browser.Picked += (s, sub) => { if (!syncing) PickType(s, sub); };
            browser.Opened += (s, sub) => { if (!syncing && s == std && sub == CurrentSub() && (DateTime.Now - lastPick).TotalSeconds > 1.5) LoadMaster(sub); };
            viewBox.SelectedIndexChanged += (s, e) => UpdateDrawing();
            twin.Paint += (s, e) => Painter.Paint(e.Graphics, shapes, twin.ClientSize, mode == "GR" ? null : hud);
            twin.Resize += (s, e) => { redraw.Stop(); redraw.Start(); };
            twin.MouseClick += (s, e) => { if (mode != "GR" && hud.Hit.Contains(e.Location)) { hud.Collapsed = !hud.Collapsed; twin.Invalidate(); } };
            twin.MouseMove += (s, e) => twin.Cursor = mode != "GR" && hud.Hit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            twin.DoubleClick += (s, e) => { if (mode != "GR") ToggleParams(); };
            chkHidden.CheckedChanged += (s, e) => UpdateDrawing();
            chkFolder.CheckedChanged += (s, e) => FolderControls();
            foreach (var nb in new[] { gapBox, thickBox, splitBox, pitchBox }) nb.ValueChanged += (s, e) => { if (!syncing) { SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); } };
            btnBrowse.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Folder for new panels (this session only)" }) if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = d.SelectedPath; };
            btnOpenPanel.Click += (s, e) => Step("Load panel assembly", LoadPanelAssembly, true);

            modeSwitch.Picked += i => Step("Switch mode", () => SetMode(i == 1 ? "GR" : "Panel"));
            btnApply.Click += (s, e) => Step("Apply to Inventor", ApplyPanel, true);
            btnReload.Click += (s, e) => Step("Reload file", () =>
            {
                SetGrid(ParamFile.Read(ParamPath));
                EnsureSizeRows();
                ClearPanelSize();
                UpdatePreview();
            });
            btnDevSave.Click += (s, e) => Step("Save dev log", () =>
            {
                using (var d = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "devlog.txt" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Log.SaveDev(d.FileName); Log.Write($"Saved dev log {d.FileName}"); }
            });
            btnSvg.Click += (s, e) => Step("Save drawing", SaveDrawing);

            // GR events
            grFiles.Add("Load GR export...", (s, e) => Step("Load GR export", () =>
            {
                using (var d = new OpenFileDialog { Filter = "GR export (*.xml)|*.xml", Title = "GR export: select the tree and the constraints file (or one export that has both)", Multiselect = true })
                    if (d.ShowDialog(this) == DialogResult.OK) SetGr(Gr.Build(Gr.ReadExportFiles(d.FileNames)));
            }));
            grFiles.Separator();
            grFiles.Add("Load GR design...", (s, e) => Step("Load GR design", () =>
            {
                using (var d = new OpenFileDialog { InitialDirectory = GrPresetDir, Filter = "GR design (*.json)|*.json" })
                    if (d.ShowDialog(this) == DialogResult.OK) SetGr(Gr.ReadDesign(d.FileName));
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
            gGrSource.Fit();   // the Files button grew its menu arrow
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
                foreach (var c in new[] { "Outer L", "Outer W", "Outer H" }) if (grGrid.Columns[c] != null) { grGrid.Columns[c].HeaderText = c + " (mm)"; Glass.InputColumn(grGrid, c); }
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
                Glass.InputColumn(grDivGrid, "Position");
                foreach (DataGridViewRow r in grDivGrid.Rows)
                    if (!Convert.ToString(r.Cells["Placed by"].Value).StartsWith("offset")) { r.Cells["Position"].ReadOnly = true; r.Cells["Position"].Style.BackColor = Theme.Card; r.Cells["Position"].Style.ForeColor = Theme.Muted; }
            };
            grDivGrid.DataError += (s, e) => { Log.Write("Enter a number in mm."); Toast.Show(this, ToastKind.Error, "Enter a number in mm"); e.Cancel = true; };
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
            btnGrApply.Click += (s, e) => Step("Write new GR to Inventor", () =>
            {
                if (gr == null) throw new SizerException("Load a GR first.");
                Inv.ApplyGr(gr, grNewName.Text, grPrefix.Text);
                UpdateGrGrid();
            }, true);
        }

        void ToggleActivity()
        {
            if (activity.Visible) { activity.Hide(); return; }
            FloatLayout();
            if (activity.Owner == null) activity.Show(this); else activity.Show();
            activity.BringToFront(); activity.Activate();
        }

        // Keeps the floating glass (toasts, activity panel, parameter pop-up) in place when the window moves.
        void FloatLayout()
        {
            Toast.Layout();
            if (activity != null && statusBar != null)
            {
                var r = RectangleToScreen(ClientRectangle);
                int w = Math.Min(680, Math.Max(480, ClientSize.Width / 2));
                activity.Size = new Size(w, 340);
                activity.Location = new Point(r.Right - w - 24, r.Y + statusBar.Top - activity.Height - 12);
            }
            if (paramPop != null && paramPop.Visible) PlacePop();
        }

        // ------------------------------------------------------------------ parameter pop-up

        void ToggleParams()
        {
            if (paramPop.Open) { paramPop.Dismiss(); return; }
            ShowParams();
        }

        void ShowParams()
        {
            if (mode == "GR" || WindowState == FormWindowState.Minimized) return;
            PlacePop();
            FitPop();
            paramPop.Popup();
        }

        // At its offset from the drawing's top left corner, kept inside the drawing.
        void PlacePop()
        {
            var o = twin.PointToScreen(Point.Empty);
            int x = Math.Max(0, Math.Min(popOffset.X, twin.Width - 120)), y = Math.Max(0, Math.Min(popOffset.Y, twin.Height - 60));
            placingPop = true;
            try { paramPop.Location = new Point(o.X + x, o.Y + y); } finally { placingPop = false; }
        }

        void FitPop()
        {
            if (paramPop == null) return;
            int rows = grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
            paramPop.FitRows(Math.Max(rows, 3), Math.Max(240, twin.Height - Math.Max(0, popOffset.Y) - 16));
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
                gMaster.Visible = Program.Dev;   // dev run only
                split.SplitterDistance = 290;
                standards = Standards.Read(StandardsDir, Root);
                browser.Build(standards);
                SelectStandard(standards[0]);
                SetGrid(ParamFile.Read(ParamPath));
                EnsureSizeRows();
                ClearPanelSize();
            });
            Log.Write("Ready. Pick 1T or 2T in the Types browser to open that master, or load a panel assembly to get its starting size.");
            Toast.Show(this, ToastKind.Info, "Ready", "Pick 1T or 2T in the Types browser to open the master, or load a panel assembly.");
            UpdatePreview();
            ShowParams();   // the parameter editor opens with the window
            PreloadMasters();
        }

        // Opens every master hidden in Inventor in the background (when Inventor is running), the
        // selected type's first, so opening a master or applying finds it loaded. Called again
        // after each Inventor step: Apply turns the master in Inventor into the new panel, so the
        // master is loaded again for the next Apply.
        void PreloadMasters()
        {
            var paths = new List<string>();
            if (std != null) paths.AddRange(Standards.SubTypes.Select(sub => std.MasterFor(sub)));
            foreach (var s in standards) paths.AddRange(Standards.SubTypes.Select(sub => s.MasterFor(sub)));
            Inv.PreloadMasters(paths.Distinct(StringComparer.OrdinalIgnoreCase), msg =>
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() => { if (msg != null) Log.Write(msg); else if (Enabled) statusLabel.Text = Inv.Status(); }));
                }
                catch { }
            });
        }

        // Runs a step with a wait cursor, logs it and times it in the dev log; errors go to a message box.
        // What happened shows as a toast (the last line the step logged); errors open a dialog.
        void Step(string title, Action body, bool inventor = false)
        {
            Cursor = Cursors.WaitCursor; Enabled = false;
            var lines = new List<string>();
            Action<string> grab = l => lines.Add(l.Length > 10 ? l.Substring(10) : l);
            Log.Written += grab;
            Exception failed = null;
            try
            {
                Log.Write("== " + title);
                Log.Measure(title, body);
                Log.Write("Done.");
            }
            catch (Exception e)
            {
                failed = e;
                var msg = e is SizerException ? e.Message : e.ToString();
                Log.Write("ERROR: " + msg);
            }
            finally
            {
                Log.Written -= grab;
                Enabled = true; Cursor = Cursors.Default;
                if (inventor) { try { PreloadMasters(); } catch { } statusLabel.Text = Inv.Status(); }
            }
            if (failed != null) { GlassDialog.Error(this, title, failed.Message); return; }
            if (QuietSteps.Contains(title)) return;
            var said = lines.LastOrDefault(l => !l.StartsWith("==") && l != "Done." && !l.StartsWith(" "));
            if (said != null && said.StartsWith("Cancelled")) Toast.Show(this, ToastKind.Info, "Cancelled", said.Substring(9).TrimStart(':', ' '));
            else Toast.Show(this, ToastKind.Done, title, said ?? "Done.");
        }

        static readonly HashSet<string> QuietSteps = new HashSet<string> { "Load", "Switch mode" };

        // ------------------------------------------------------------------ parameter grid

        DataRow FindRow(string name) => table.Rows.Cast<DataRow>().FirstOrDefault(r => (string)r["Name"] == name);

        void EnsureSizeRows()
        {
            foreach (var name in U.SizeParams)
                if (FindRow(name) == null) table.Rows.Add(name, DBNull.Value, "mm", "Read from the selected assembly name");
        }

        void ClearPanelSize()
        {
            syncing = true;
            try
            {
                EnsureSizeRows();
                foreach (var name in U.SizeParams) FindRow(name)["Value"] = DBNull.Value;
                nameBox.Clear();
            }
            finally { syncing = false; }
        }

        void LoadPanelAssembly()
        {
            using (var d = new OpenFileDialog { Filter = "Inventor assembly (*.iam)|*.iam", Title = "Choose a panel assembly" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) { Log.Write("Cancelled: no panel assembly chosen."); return; }
                string panelName = Path.GetFileNameWithoutExtension(d.FileName);
                var match = U.SizeRegex.Match(panelName);
                if (!match.Success) throw new SizerException("The selected assembly file name must contain a size like 386x656x47.");
                Inv.OpenVisible(d.FileName);
                syncing = true;
                try
                {
                    EnsureSizeRows();
                    for (int i = 0; i < U.SizeParams.Length; i++)
                        FindRow(U.SizeParams[i])["Value"] = U.P(match.Groups[i + 1].Value);
                    nameBox.Text = panelName;
                }
                finally { syncing = false; }
                StandardFromName();
                NameFromGrid();
                UpdatePreview();
                Log.Write($"Loaded panel size from {Path.GetFileName(d.FileName)}: {SizeToken()}");
            }
        }

        // Choosing 1T or 2T opens that sub type's master in Inventor. With no panel size yet,
        // the size is taken from the master's file name (e.g. 1111 TP-300x1194x42).
        void LoadMaster(string sub)
        {
            if (std == null || !std.HasMaster(sub)) { Log.Write($"No {std?.Id} {sub} master to open."); return; }
            string file = std.MasterFor(sub);
            Step($"Open {std.Id} {sub} master", () =>
            {
                Inv.OpenVisible(file);
                var m = U.SizeRegex.Match(Path.GetFileNameWithoutExtension(file));
                if (SizeToken() != null || !m.Success) return;
                syncing = true;
                try
                {
                    EnsureSizeRows();
                    for (int i = 0; i < U.SizeParams.Length; i++) FindRow(U.SizeParams[i])["Value"] = U.P(m.Groups[i + 1].Value);
                    nameBox.Text = std.Prefix + SizeToken();
                }
                finally { syncing = false; }
                Log.Write($"Panel size from the {sub} master: {SizeToken()}");
            }, true);
            UpdatePreview();
        }

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
            try { browser.Select(s, subPick > 0 ? Standards.SubTypes[subPick - 1] : null); } finally { syncing = false; }
            if (grid.Rows.Count > 0) UpdateGridLocks();
            ShowMaster();
        }

        // The sub type in use: the one chosen, or with Auto the one the width asks for.
        string CurrentSub()
        {
            if (subPick > 0) return Standards.SubTypes[subPick - 1];
            try { return Standards.SubOf(HoleRules.InnerSplit(Standards.Get(GridValues(), "Width"), settings)); } catch { return "1T"; }
        }

        // Shows which master the current type and sub type lead to.
        void ShowMaster()
        {
            if (std == null || mode == "GR") return;
            string sub = CurrentSub();
            bool has = std.HasMaster(sub);
            browser.Refresh(std, sub);
            miOpenMaster.Enabled = has;
            masterLabel.ForeColor = has ? Theme.Muted : Theme.Ink;
            masterLabel.Font = has ? Theme.Small : Theme.Ui(8.5f, FontStyle.Bold);
            masterLabel.Text = has ? $"Master {std.Id} {sub}  ·  {Path.GetFileName(std.MasterFor(sub))}" : $"No {std.Id} {sub} master in masters\\{std.Id}\\{sub}";
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

        // A node picked in the Types browser: the type, and a sub type (1T / 2T opens its master)
        // or none (Auto: the sub type follows the width).
        void PickType(Standard s, string sub)
        {
            subPick = sub == null ? 0 : Array.IndexOf(Standards.SubTypes, sub) + 1;   // first: the browser keeps this node selected
            if (s != std) StandardFromList(s);
            SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); ShowMaster();
            if (subPick > 0) { LoadMaster(CurrentSub()); lastPick = DateTime.Now; }
        }

        void StandardFromList(Standard s)
        {
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
            bool split = HoleRules.InnerSplit(v["Width"], settings) >= 2;
            double mid = v["HoleEdge"] - settings.Thickness - (v.TryGetValue("Clearance", out var c) ? c : 0.5);
            var want = new Dictionary<string, double>
            {
                ["HolesL"] = HoleRules.AutoCount(v["Length"], v["HoleEdge"], settings.MaxHolePitch, false),
                ["HolesW"] = split ? HoleRules.AutoCountSplitW(v["Width"], v["HoleEdge"], mid, settings.MaxHolePitch, std != null && std.EvenSplitHolesW)
                                   : HoleRules.AutoCount(v["Width"], v["HoleEdge"], settings.MaxHolePitch, false),
            };
            bool was = syncing; syncing = true;
            try { foreach (var kv in want) { var r = FindRow(kv.Key); if (r != null && (r["Value"] is DBNull || (double)r["Value"] != kv.Value)) r["Value"] = kv.Value; } }
            finally { syncing = was; }
        }

        // Height is fixed per type (e.g. TP 42 mm): the grid and the name always carry the type's height.
        void EnforceHeight()
        {
            if (std == null || std.Height <= 0) return;
            var r = FindRow("Height");
            if (r == null || (!(r["Value"] is DBNull) && (double)r["Value"] == std.Height)) return;
            bool was = syncing; syncing = true;
            try { r["Value"] = std.Height; } finally { syncing = was; }
            NameFromGrid();
        }

        void UpdateGridLocks()
        {
            if (grid.CurrentCell != null && HiddenParams.Contains(Convert.ToString(grid.Rows[grid.CurrentCell.RowIndex].Cells["Name"].Value))) grid.CurrentCell = null;
            foreach (DataGridViewRow gr in grid.Rows)
            {
                string name = Convert.ToString(gr.Cells["Name"].Value);
                try { gr.Visible = !HiddenParams.Contains(name); } catch (InvalidOperationException) { }
                bool locked = FixedParams.Contains(name) || (name == "Height" && std != null && std.Height > 0) || (settings.MaxHolePitch > 0 && AutoParams.Contains(name)) || (HardwareCountParams.Contains(name) && (std == null || std.HardwareCounts.Count == 0));
                gr.ReadOnly = locked;
                gr.DefaultCellStyle.ForeColor = locked ? Theme.GlassMuted : Theme.GlassText;
                gr.Cells["Value"].Style.BackColor = locked ? Theme.Glass : Theme.PopInput;
            }
            FitPop();
        }

        void UpdatePreview()
        {
            ShowMaster();
            if (mode == "GR") { UpdateDrawing(); return; }
            EnforceHeight();
            AutoHoles();
            values = GridValues();
            if (U.SizeParams.Any(k => !values.ContainsKey(k)))
            {
                layout = null;
                hw = new List<PlacedHole>();
                hud.Lines = new List<string> { "Load a panel assembly to initialize Width, Length and Height." };
                hud.Error = false;
                btnApply.Enabled = false;
                UpdateDrawing();
                return;
            }
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
            bool newIssues = errors.Count > 0 && !hud.Error;
            hud.Lines = lines.ToList(); hud.Error = errors.Count > 0;
            if (newIssues) hud.Collapsed = false;   // new problems always open the pop-up
            btnApply.Enabled = errors.Count == 0;
            UpdateDrawing();
        }

        Bom CurrentBom() => std == null ? null : Standards.GetBom(std, values, settings.Thickness, nameBox.Text.Trim(), layout);

        void UpdateDrawing()
        {
            var size = twin.ClientSize;
            string view = (string)viewBox.SelectedItem ?? "Isometric";
            shapes = mode != "GR" && U.SizeParams.Any(k => !values.ContainsKey(k))
                ? new List<Shape>()
                : Log.Measure("Draw", () => mode == "GR"
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
            settings.ForceSplit = subPick;   // 0 = Auto, 1 = 1T, 2 = 2T
        }

        void SaveFormSettings() { SettingsFromForm(); settings.Save(SettingsPath); }

        void FolderControls()
        {
            folderBox.Enabled = btnBrowse.Enabled = chkFolder.Checked;
            folderInfo.Text = chkFolder.Checked ? "This session only: new panels go into the folder above." : "New panels are made from the type's master; you choose their folder on Apply.";
        }

        string setupSub = "2T";   // the master being set up: 1T uses "setup1T" from the type's file

        // Each setup assignment is applied individually, then shown for manual inspection.
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
            string go = Choose("Set up master", $"{Path.GetFileName(file)}\n\nThe app opens this assembly and applies each user-parameter or formula assignment individually. After each one, inspect the target manually in Inventor. The app will not find, open, select, or highlight sketches.\n\nNothing is saved until you choose a save option. Use a copy the first time.",
                "Set up as 1T master", "Set up as 2T master", "Cancel");
            if (go != "Set up as 1T master" && go != "Set up as 2T master") { Log.Write("Cancelled: nothing was changed."); return; }
            setupSub = go == "Set up as 1T master" ? "1T" : "2T";
            if (setupSub == "1T" && !std.Has1TSetup) Log.Write($"  note: {std.File} has no \"setup1T\" section - the 2T setup is used for this 1T master.");
            Inv.OpenVisible(file);
            var report = Inv.MasterSetup(std, setupSub);
            GlassDialog.Report(this, "Set up master - assignment report", report.Format());
            if (!report.Complete)
            {
                Log.Write(report.Cancelled
                    ? "Setup cancelled. Changes already applied in Inventor are unsaved; close without saving to undo them."
                    : "Setup incomplete. Changes already applied in Inventor are unsaved; close without saving to undo them.");
                return;
            }

            string sub = setupSub;   // as chosen above (the inner part's file name can say -In-2T on a 1T master)
            Log.Write($"  this master is {std.Id} {sub}");
            string store = Path.Combine(Root, "masters", std.Id, sub);
            string save = Choose($"Save the {std.Id} {sub} master?", $"The setup assignments succeeded, and this is the {std.Id} {sub} master ({(sub == "2T" ? "2 inner panels" : "1 inner panel")}). The changes are currently unsaved. Inspect the model in Inventor before saving.\n\n" +
                $"Save into Inventor Sizer: saves the assembly and its {string.Join(" / ", std.Parts.Select(p => p.Suffix))} parts as copies in\n{store}\nand uses that copy as the master. Users then need no setup. The files you opened stay as they were on disk.\n\n" +
                "Save where it is: saves the opened files themselves.\n\nNot now: leaves it open and unsaved; close it without saving to undo the setup.",
                "Save into Inventor Sizer", "Save where it is", "Not now");
            if (save == "Save into Inventor Sizer")
            {
                Inv.ActivateAssembly(file);
                string copy = Inv.SaveMasterIntoApp(std, store);
                Standards.SetMaster(std, sub, copy, Root);
                SelectStandard(std);
            }
            else if (save == "Save where it is")
            {
                Inv.ActivateAssembly(file);
                Inv.SaveActiveAssembly();
                Standards.SetMaster(std, sub, file, Root);
                SelectStandard(std);
            }
            else Log.Write("The master remains open and unsaved, and was not registered as a master. Close it without saving to undo the setup.");
        }

        SetupStepDecision ReviewSetupAssignment(SetupAssignment item, bool hasNext)
        {
            var decision = new SetupStepDecision();
            string status = item.Status.ToString().ToUpperInvariant();
            string purposeSource = item.PurposeDeclared
                ? "Purpose label: provided by the standard setup data."
                : "Purpose label: inferred from the parameter/expression; this is not a sketch mapping.";
            string text = $"{status}\n\n" +
                $"File: {item.File}\nRole: {item.Role}\nTarget Inventor parameter/dimension: {item.Target}\n" +
                $"Expression: {item.Expression}\nPurpose: {item.Purpose}\n{purposeSource}\n\n" +
                (item.Error == "" ? "" : $"Details: {item.Error}\n\n") +
                "Inspect the result manually in Inventor. No sketch was located or highlighted. " +
                "Any changes already applied are currently unsaved.\n\n" +
                (hasNext
                    ? "Continue moves to the next assignment. Edit expression reapplies this target with your correction. Add parameter creates a user parameter in this role. Skip leaves the next target unchanged. Cancel leaves the remaining assignments unapplied."
                    : "This is the final assignment. Choose Finish to review the report, or cancel the setup; all changes remain unsaved.");
            string answer = hasNext
                ? Choose("Inspect setup assignment", text, "Continue", "Edit expression...", "Add parameter...", "Skip next assignment", "Cancel remaining setup")
                : Choose("Inspect final setup assignment", text, "Finish setup", "Edit expression...", "Add parameter...", "Cancel remaining setup");

            if (answer == "Edit expression...")
            {
                string expression = SetupDialog.EditExpression(this, item);
                if (expression == null || expression.Trim() == "" || expression == item.Expression) return decision;
                expression = expression.Trim();
                if (Choose("Update setup code?", $"This writes the corrected expression to {std.File} and applies it in Inventor:\n\n{item.Role} | {item.Target} = {expression}\n\nContinue?", "Update code and apply", "Keep current expression") != "Update code and apply")
                    return decision;
                try
                {
                    var edited = CopySetupAssignment(item);
                    edited.Expression = expression;
                    Standards.UpsertSetupAssignment(std, edited, setupSub);
                    decision.Expression = expression;
                    Log.Write($"Updated {std.File}: {item.Role} {item.Target} = {expression}");
                }
                catch (Exception e) { decision.Error = $"Could not update {std.File}: {e.Message}"; }
                return decision;
            }

            if (answer == "Add parameter...")
            {
                var added = SetupDialog.AddParameter(this, item.Role, item.File);
                if (added == null) return decision;
                var roleSetup = std.SetupFor(setupSub)?.Obj(item.Role);
                var existing = roleSetup != null &&
                    roleSetup.Arr("user").Concat(roleSetup.Arr("formulas")).OfType<JsonObject>()
                        .Any(p => string.Equals(p.Str("name"), added.Target, StringComparison.OrdinalIgnoreCase));
                if (existing)
                {
                    GlassDialog.Error(this, "Parameter already defined", $"{added.Target} already exists in the {item.Role} setup in {std.File}. Edit that assignment instead.");
                    return decision;
                }
                if (Choose("Add parameter to setup code?", $"This writes a new user-parameter assignment to {std.File} and then applies it in Inventor:\n\n{added.Role} | {added.Target} = {added.Expression} {added.Unit}\n\nContinue?", "Add to code and apply", "Cancel") != "Add to code and apply")
                    return decision;
                try
                {
                    Standards.UpsertSetupAssignment(std, added, setupSub);
                    decision.AddedAssignment = added;
                    Log.Write($"Added {std.File}: {added.Role} {added.Target} = {added.Expression} {added.Unit}");
                }
                catch (Exception e) { decision.Error = $"Could not add the parameter to {std.File}: {e.Message}"; }
                return decision;
            }

            if (answer == "Skip next assignment") decision.Action = SetupStepAction.SkipNext;
            else if (answer == "Cancel remaining setup") decision.Action = SetupStepAction.CancelRemaining;
            return decision;
        }

        static SetupAssignment CopySetupAssignment(SetupAssignment item) => new SetupAssignment
        {
            Role = item.Role, File = item.File, Target = item.Target, Expression = item.Expression,
            Purpose = item.Purpose, Unit = item.Unit, IsFormula = item.IsFormula,
            PurposeDeclared = item.PurposeDeclared, Status = item.Status
        };

        // A question with named buttons; the first is the default, the last answers Esc and
        // closing the box. Returns the text of the clicked button.
        string Choose(string title, string text, params string[] buttons) => GlassDialog.Ask(this, title, text, buttons);

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
            modeSwitch.SetSelected(g ? 1 : 0);
            Text = "Inventor Sizer - " + (g ? "GR editing" : "panel editing") + titleTail;
            ribbon.ShowTab(tabPanel, !g); ribbon.ShowTab(tabSettings, !g); ribbon.ShowTab(tabGr, g);
            ribbon.Select(g ? tabGr : tabPanel);
            foreach (Control c in new Control[] { masterLabel, chkHidden, browser }) c.Visible = !g;
            btnParamsView.Enabled = !g;
            grColumn.Visible = g;
            split.SplitterDistance = g ? Math.Min(560, split.Width / 2) : 290;
            if (g) paramPop.Dismiss();
            if (g) { UpdatePresets(); UpdateGrGrid(); UpdateDrawing(); } else UpdatePreview();
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
            catch (Exception e) { Log.Write($"ERROR ({title}): {e.Message}"); Toast.Show(this, ToastKind.Error, title, e.Message); }
            UpdateGrGrid();
            UpdateDrawing();
        }
    }
}
