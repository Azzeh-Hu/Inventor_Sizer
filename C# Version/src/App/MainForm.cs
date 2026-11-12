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
        Button btnApply, btnReload, btnDevSave, btnSvg, btnBrowse, btnOpenGr, btnGrInv, btnActivity;
        MenuButton devMenu, grFiles;
        ToolStripMenuItem miOpenMaster, miSetMaster, miLearn;
        Label statusLabel, masterLabel, folderInfo, grInfo;
        Segmented modeSwitch;
        CheckBox chkFolder, chkHidden, chkShared;
        NumericUpDown gapBox, thickBox, splitBox, pitchBox;
        DataTable table, grTable, grDivTable;
        DataGridView grid, grGrid, grDivGrid;
        Card paramsCard, settingsCard, twinCard;
        Panel header, footer, panelColumn, grColumn;
        FlowLayoutPanel panelBar, grBar, panelActions, grActions;
        SplitContainer split;
        PictureBox twin;
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
            FormClosing += (s, e) => { try { SaveFormSettings(); } catch { } };
        }

        // ------------------------------------------------------------------ layout

        static NumericUpDown Num(int dec, decimal max, decimal inc, decimal min = 0) => new NumericUpDown { DecimalPlaces = dec, Maximum = max, Minimum = min, Increment = inc, Font = Theme.Body, TextAlign = HorizontalAlignment.Right };
        static FlowLayoutPanel Row(Color back, bool rtl = false) => new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = back, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Padding = new Padding(0), Margin = new Padding(0) };
        static Panel Spacer(DockStyle dock, int size) => new Panel { Dock = dock, Height = size, Width = size, BackColor = Theme.App };
        static void Hairline(Control c, bool bottom) => c.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) { int y = bottom ? c.Height - 1 : 0; e.Graphics.DrawLine(p, 0, y, c.Width, y); } };

        void BuildUi()
        {
            Text = "Inventor Sizer - panel editing";
            Size = new Size(1380, 900);
            MinimumSize = new Size(1120, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Body;
            BackColor = Theme.App;
            DoubleBuffered = true;

            // ---------------------------------------------------------- body: left column | drawing
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.App, Padding = new Padding(18, 4, 18, 12) };
            split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Theme.App, SplitterWidth = 8, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.BackColor = split.Panel2.BackColor = Theme.App;
            body.Controls.Add(split);

            // parameter table (panel mode)
            table = new DataTable();
            table.Columns.Add("Name", typeof(string)); table.Columns.Add("Value", typeof(double)); table.Columns.Add("Unit", typeof(string)); table.Columns.Add("Description", typeof(string));
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, DataSource = table };
            Glass.StyleGrid(grid, Theme.Card);
            btnReload = new FlatButton("Reload file", ButtonKind.Ghost, 0, 30);
            paramsCard = new Card("Parameters", subtitle: "Parameters.xml") { Dock = DockStyle.Fill };
            paramsCard.Actions.Controls.Add(btnReload);
            paramsCard.Controls.Add(grid);

            // settings (collapsible)
            settingsCard = new Card("Settings", subtitle: "Hole rules and output folder") { Dock = DockStyle.Bottom, Height = 318, Collapsible = true };
            var sg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7, BackColor = Theme.Card };
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label SLbl(string t) => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.InkSoft, Font = Theme.Body, Margin = new Padding(0, 6, 8, 6) };
            chkFolder = new Toggle("Folder for new panels (this session only)") { Margin = new Padding(0, 0, 0, 6) };
            folderBox = new TextBox { Font = Theme.Body };
            var folderField = new Field(folderBox, 200) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 8, 4) };
            btnBrowse = new FlatButton("Browse...", ButtonKind.Secondary, 0, 32) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 0, 4) };
            folderInfo = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 0, 0, 10) };
            gapBox = Num(1, 1000, 0.5m); thickBox = Num(2, 50, 0.1m, 0.1m); splitBox = Num(0, 100000, 10); pitchBox = Num(0, 100000, 10);
            Field NumField(NumericUpDown n) => new Field(n, 110, 30) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 2, 0, 2) };
            sg.Controls.Add(chkFolder, 0, 0); sg.SetColumnSpan(chkFolder, 3);
            sg.Controls.Add(folderField, 0, 1); sg.SetColumnSpan(folderField, 2); sg.Controls.Add(btnBrowse, 2, 1);
            sg.Controls.Add(folderInfo, 0, 2); sg.SetColumnSpan(folderInfo, 3);
            var nums = new (string, NumericUpDown)[] { ("Minimum hole gap (mm)", gapBox), ("Sheet thickness, one sheet (Staerke, mm)", thickBox), ("Split inner panel above width (mm, 0 = never)", splitBox), ("Max. hole pitch for automatic counts (mm, 0 = by hand)", pitchBox) };
            for (int i = 0; i < nums.Length; i++) { var f = NumField(nums[i].Item2); sg.Controls.Add(SLbl(nums[i].Item1), 0, 3 + i); sg.Controls.Add(f, 1, 3 + i); sg.SetColumnSpan(f, 2); }
            settingsCard.Controls.Add(sg);

            panelColumn = new Panel { Dock = DockStyle.Fill, BackColor = Theme.App, Padding = new Padding(6) };
            panelColumn.Controls.Add(paramsCard); panelColumn.Controls.Add(Spacer(DockStyle.Bottom, Theme.Gap)); panelColumn.Controls.Add(settingsCard);
            Card.CastShadows(panelColumn);
            settingsCard.CollapsedChanged += () => panelColumn.Invalidate();

            // GR editor (GR mode)
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
            grColumn = new Panel { Dock = DockStyle.Fill, BackColor = Theme.App, Padding = new Padding(6), Visible = false };
            grColumn.Controls.Add(boxesCard); grColumn.Controls.Add(Spacer(DockStyle.Bottom, Theme.Gap)); grColumn.Controls.Add(dividersCard);
            Card.CastShadows(grColumn);
            split.Panel1.Controls.Add(grColumn); split.Panel1.Controls.Add(panelColumn);

            // the drawing: a white card with its own toolbar
            twinCard = new Card(null, Theme.Paper) { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };
            twin = new PictureBox { Dock = DockStyle.Fill, BackColor = Theme.Paper };
            var twinBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Paper, Padding = new Padding(6, 6, 2, 6) };
            viewBox = new ThemedCombo(140);
            viewBox.Items.AddRange(Draw.Views);
            chkHidden = new Toggle("Hidden edges") { Margin = new Padding(4, 2, 0, 0) };
            var twinLeft = Row(Theme.Paper); twinLeft.Dock = DockStyle.Left;
            twinLeft.Controls.AddRange(new Control[] { new Caption("View") { Margin = new Padding(2, 8, 8, 0) }, viewBox, chkHidden });
            btnSvg = new FlatButton("Export SVG", ButtonKind.Secondary, 0, 30) { Margin = new Padding(0) };
            var twinRight = Row(Theme.Paper, true); twinRight.Dock = DockStyle.Right; twinRight.Controls.Add(btnSvg);
            twinBar.Controls.Add(twinLeft); twinBar.Controls.Add(twinRight);
            twinCard.Controls.Add(twin); twinCard.Controls.Add(twinBar);
            split.Panel2.Padding = new Padding(6);
            split.Panel2.Controls.Add(twinCard);
            Card.CastShadows(split.Panel2);

            // ---------------------------------------------------------- context bar (under the header)
            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.App, Padding = new Padding(24, 14, 24, 0) };
            typeBox = new ThemedCombo(250);
            subBox = new ThemedCombo(124);   // sub type: which master is used. Auto follows the split width in Settings.
            subBox.Items.AddRange(new object[] { "Auto (width)", "1T", "2T" });
            subBox.SelectedIndex = 0;
            nameBox = new TextBox { Font = Theme.Ui(10.5f) };
            devMenu = new MenuButton("Master", ButtonKind.Secondary) { Height = 32, Margin = new Padding(8, 0, 0, 0) };
            miOpenMaster = devMenu.Add("Open master", (s, e) => Step("Open master", () => { Inv.OpenVisible(std.MasterFor(CurrentSub())); Log.Write($"Master {std.Id} {CurrentSub()} open; the name is unchanged."); }, true));
            miSetMaster = devMenu.Add("Set up master...", (s, e) => Step("Set up master", SetUpMaster, true));
            devMenu.Separator();
            miLearn = devMenu.Add("Learn hardware holes", (s, e) => Step("Learn hardware holes", () => { Inv.LearnHardware(standards, std, Standards.Get(GridValues(), "HoleDia"), Standards.Get(GridValues(), "HoleZ")); UpdatePreview(); }, true));
            panelBar = Row(Theme.App); panelBar.Dock = DockStyle.Left;
            panelBar.Controls.AddRange(new Control[] { new Caption("Type"), typeBox, subBox, new Caption("Name") { Margin = new Padding(14, 9, 6, 0) }, new Field(nameBox, 330, 32), devMenu });

            presetBox = new ThemedCombo(250);
            btnOpenGr = new FlatButton("Open GR...", ButtonKind.Secondary, 0, 32);
            btnGrInv = new FlatButton("Read from Inventor", ButtonKind.Secondary, 0, 32);
            grFiles = new MenuButton("Files", ButtonKind.Secondary) { Height = 32 };
            grBar = Row(Theme.App); grBar.Dock = DockStyle.Left; grBar.Visible = false;
            grBar.Controls.AddRange(new Control[] { new Caption("Preset"), presetBox, btnOpenGr, btnGrInv, grFiles });
            bar.Controls.Add(grBar); bar.Controls.Add(panelBar);

            // ---------------------------------------------------------- header
            header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Paper, Padding = new Padding(20, 0, 20, 1) };
            Hairline(header, true);
            var logo = new Panel { Size = new Size(30, 30), Margin = new Padding(0, 15, 10, 0), BackColor = Theme.Paper };
            logo.Paint += (s, e) => PaintLogo(e.Graphics);
            var title = new Label { Text = "Inventor Sizer", Font = Theme.Title, ForeColor = Theme.Ink, AutoSize = true, Margin = new Padding(0, 17, 22, 0), BackColor = Theme.Paper };
            modeSwitch = new Segmented("Panels", "GR frame") { Margin = new Padding(0, 12, 0, 0) };
            var headLeft = Row(Theme.Paper); headLeft.Dock = DockStyle.Left;
            headLeft.Controls.AddRange(new Control[] { logo, title, modeSwitch });
            statusLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 22, 14, 0), BackColor = Theme.Paper };
            btnActivity = new FlatButton("Activity", ButtonKind.Secondary, 0, 32) { Margin = new Padding(0, 14, 0, 0) };
            var headRight = Row(Theme.Paper, true); headRight.Dock = DockStyle.Right;
            headRight.Controls.Add(btnActivity);
            if (Program.Dev) headRight.Controls.Add(new FlatButton("Dev run", ButtonKind.Primary, 0, 24) { Margin = new Padding(0, 18, 12, 0), Font = Theme.Ui(8f, FontStyle.Bold), Cursor = Cursors.Default, TabStop = false });
            headRight.Controls.Add(statusLabel);
            header.Controls.Add(headLeft); header.Controls.Add(headRight);

            // ---------------------------------------------------------- footer: the main action, right aligned
            footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Paper, Padding = new Padding(24, 14, 24, 0) };
            var footLeft = Row(Theme.Paper); footLeft.Dock = DockStyle.Left;
            Hairline(footer, false);
            masterLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 11, 0, 0), BackColor = Theme.Paper };
            footLeft.Controls.Add(masterLabel);
            btnApply = new FlatButton("Apply to Inventor", ButtonKind.Primary, 190, 36) { Margin = new Padding(0) };
            panelActions = Row(Theme.Paper, true); panelActions.Dock = DockStyle.Right;
            panelActions.Controls.Add(btnApply);
            grNewName = new TextBox { Font = Theme.Body }; grPrefix = new TextBox { Font = Theme.Body };
            var btnGrApply = new FlatButton("Write new GR to Inventor", ButtonKind.Primary, 0, 36) { Margin = new Padding(0) };
            grActions = Row(Theme.Paper, true); grActions.Dock = DockStyle.Right; grActions.Visible = false;
            grActions.Controls.AddRange(new Control[] { btnGrApply, new Field(grPrefix, 90, 34) { Margin = new Padding(0, 1, 18, 0) }, new Caption("Part prefix") { Margin = new Padding(0, 10, 8, 0) },
                new Field(grNewName, 220, 34) { Margin = new Padding(0, 1, 16, 0) }, new Caption("New GR name") { Margin = new Padding(0, 10, 8, 0) } });
            footer.Controls.Add(footLeft); footer.Controls.Add(panelActions); footer.Controls.Add(grActions);

            // dock order: fill first, then the edges from the inside out
            Controls.Add(body); Controls.Add(bar); Controls.Add(header); Controls.Add(footer);

            // ---------------------------------------------------------- floating glass: activity panel, toasts
            activity = new ActivityPanel();
            logBox = activity.LogBox; devBox = activity.DevBox; btnDevSave = activity.SaveDev;
            Toast.Anchor = () => RectangleToScreen(new Rectangle(0, header.Bottom, ClientSize.Width, footer.Top - header.Bottom));
            Move += (s, e) => FloatLayout(); Resize += (s, e) => FloatLayout();
            btnActivity.Click += (s, e) => ToggleActivity();

            redraw = new Timer { Interval = 150 };
            redraw.Tick += (s, e) => { redraw.Stop(); UpdateDrawing(); };

            // events
            grid.DataBindingComplete += (s, e) =>
            {
                foreach (var c in new[] { "Name", "Unit", "Description" }) if (grid.Columns[c] != null) grid.Columns[c].ReadOnly = true;
                Glass.InputColumn(grid, "Value");
                var weights = new Dictionary<string, float> { ["Name"] = 26, ["Value"] = 18, ["Unit"] = 10, ["Description"] = 46 };
                foreach (var kv in weights) if (grid.Columns[kv.Key] != null) grid.Columns[kv.Key].FillWeight = kv.Value;
                if (grid.Columns["Description"] != null) grid.Columns["Description"].DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Muted, Font = Theme.Small };
                if (grid.Columns["Unit"] != null) grid.Columns["Unit"].DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Muted };
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
            typeBox.SelectedIndexChanged += (s, e) => { if (!syncing && typeBox.SelectedIndex >= 0) StandardFromList(); };
            subBox.SelectedIndexChanged += (s, e) => { if (syncing || std == null) return; SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); ShowMaster(); };
            viewBox.SelectedIndexChanged += (s, e) => UpdateDrawing();
            twin.Paint += (s, e) => Painter.Paint(e.Graphics, shapes, twin.ClientSize, mode == "GR" ? null : hud);
            twin.Resize += (s, e) => { redraw.Stop(); redraw.Start(); };
            twin.MouseClick += (s, e) => { if (mode != "GR" && hud.Hit.Contains(e.Location)) { hud.Collapsed = !hud.Collapsed; twin.Invalidate(); } };
            twin.MouseMove += (s, e) => twin.Cursor = mode != "GR" && hud.Hit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            chkHidden.CheckedChanged += (s, e) => UpdateDrawing();
            chkFolder.CheckedChanged += (s, e) => FolderControls();
            foreach (var nb in new[] { gapBox, thickBox, splitBox, pitchBox }) nb.ValueChanged += (s, e) => { if (!syncing) { SettingsFromForm(); UpdateGridLocks(); UpdatePreview(); } };
            btnBrowse.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Folder for new panels (this session only)" }) if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = d.SelectedPath; };

            modeSwitch.Picked += i => Step("Switch mode", () => SetMode(i == 1 ? "GR" : "Panel"));
            btnApply.Click += (s, e) => Step("Apply to Inventor", ApplyPanel, true);
            btnReload.Click += (s, e) => Step("Reload file", () => { SetGrid(ParamFile.Read(ParamPath)); NameFromGrid(); UpdatePreview(); });
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

        // Keeps the floating glass (toasts, activity panel) in place when the window moves.
        void FloatLayout()
        {
            Toast.Layout();
            if (activity != null && footer != null)
            {
                var r = RectangleToScreen(ClientRectangle);
                int w = Math.Min(680, Math.Max(480, ClientSize.Width / 2));
                activity.Size = new Size(w, 340);
                activity.Location = new Point(r.Right - w - 24, r.Y + footer.Top - activity.Height - 12);
            }
        }

        // A small isometric box: the panel the app draws.
        static void PaintLogo(Graphics g)
        {
            Theme.Smooth(g);
            float cx = 15, cy = 15, s = 11;
            var top = new[] { new PointF(cx, cy - s), new PointF(cx + s * 0.87f, cy - s * 0.5f), new PointF(cx, cy), new PointF(cx - s * 0.87f, cy - s * 0.5f) };
            var left = new[] { new PointF(cx - s * 0.87f, cy - s * 0.5f), new PointF(cx, cy), new PointF(cx, cy + s), new PointF(cx - s * 0.87f, cy + s * 0.5f) };
            var right = new[] { new PointF(cx, cy), new PointF(cx + s * 0.87f, cy - s * 0.5f), new PointF(cx + s * 0.87f, cy + s * 0.5f), new PointF(cx, cy + s) };
            using (var b = new SolidBrush(Theme.Ink)) g.FillPolygon(b, right);
            using (var b = new SolidBrush(Theme.InkSoft)) g.FillPolygon(b, left);
            using (var b = new SolidBrush(Theme.Card)) g.FillPolygon(b, top);
            using (var p = new Pen(Theme.Ink, 1.2f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round }) { g.DrawPolygon(p, top); g.DrawPolygon(p, left); g.DrawPolygon(p, right); }
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
                devMenu.Visible = Program.Dev;
                split.SplitterDistance = Math.Max(400, Math.Min(500, split.Width * 2 / 5));
                settingsCard.SetCollapsed(true);
                standards = Standards.Read(StandardsDir, Root);
                typeBox.Items.Clear();
                foreach (var s in standards) typeBox.Items.Add(s.ToString());
                SelectStandard(standards[0]);
                SetGrid(ParamFile.Read(ParamPath));
                NameFromGrid();
            });
            Log.Write("Ready. Inventor is not read at start: it is used when you make a new version (Apply / Write) or open / read something.");
            Toast.Show(this, ToastKind.Info, "Ready", "Inventor is only used when you apply, write, open or read something.");
            UpdatePreview();
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
                if (inventor) statusLabel.Text = Inv.Status();
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
                gr.DefaultCellStyle.ForeColor = locked ? Theme.Muted : Theme.Ink;
                gr.Cells["Value"].Style.BackColor = locked ? Theme.Card : Theme.Paper;
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
            foreach (Control c in new Control[] { panelBar, panelActions, masterLabel, chkHidden, panelColumn }) c.Visible = !g;
            devMenu.Visible = !g && Program.Dev;   // dev run only
            foreach (Control c in new Control[] { grBar, grActions, grColumn }) c.Visible = g;
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
