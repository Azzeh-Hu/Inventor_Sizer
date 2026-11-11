// The reader window: tree on the left, all properties of the selected item on the right.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventorSizer.AsmReader
{
    public class ReaderForm : Form
    {
        readonly object _app;
        readonly TreeView _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        readonly ListView _props = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
        readonly TextBox _value = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        readonly ToolStripTextBox _find = new ToolStripTextBox { AutoSize = false, Width = 160 };
        readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        const string Placeholder = "\u0001";
        const string UnitNote = "Raw API values: lengths in cm, angles in radians. Values marked mm are converted.";

        public ReaderForm(object app)
        {
            _app = app;
            Text = "Assembly Reader";
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1100, 700);
            ShowInTaskbar = false;
            KeyPreview = true;

            var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            bar.Items.Add(Button("Read active document", (s, e) => Reload()));
            bar.Items.Add(Button("Select in Inventor", (s, e) => SelectInInventor()));
            bar.Items.Add(Button("Export XML...", (s, e) => Export()));
            bar.Items.Add(Button("Copy properties", (s, e) => CopyRows()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripLabel("Find:"));
            bar.Items.Add(_find);
            bar.Items.Add(Button("Next", (s, e) => FindNext()));
            _find.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };

            _props.Columns.Add("Property", 230);
            _props.Columns.Add("Value", 520);
            _props.SelectedIndexChanged += (s, e) => _value.Text = _props.SelectedItems.Count > 0 ? _props.SelectedItems[0].SubItems[1].Text.Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            _props.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { e.SuppressKeyPress = true; CopyRows(); } };

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            right.Panel1.Controls.Add(_props);
            right.Panel2.Controls.Add(_value);
            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_tree);
            split.Panel2.Controls.Add(right);

            var statusBar = new StatusStrip();
            statusBar.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(statusBar);

            _tree.BeforeExpand += (s, e) => Fill(e.Node);
            _tree.AfterSelect += (s, e) => ShowProperties(e.Node);
            _tree.NodeMouseDoubleClick += (s, e) => { if (e.Node.Nodes.Count == 0) SelectInInventor(); };
            _status.Text = UnitNote;
            Load += (s, e) => { split.SplitterDistance = 380; right.SplitterDistance = Math.Max(100, right.Height - 140); };
        }

        static ToolStripButton Button(string text, EventHandler click)
        {
            var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += click;
            return b;
        }

        // ------------------------------------------------------------------ tree

        TreeNode Make(Node n)
        {
            var t = new TreeNode(n.Text.Replace('\n', ' ')) { Tag = n };
            if (n.Raw) t.ForeColor = SystemColors.GrayText;
            if (n.Load != null) t.Nodes.Add(Placeholder);
            return t;
        }

        // Reads the children of a node the first time it is opened.
        void Fill(TreeNode t)
        {
            if (t.Nodes.Count != 1 || t.Nodes[0].Text != Placeholder) return;
            var n = (Node)t.Tag;
            Cursor = Cursors.WaitCursor;
            _tree.BeginUpdate();
            try
            {
                t.Nodes.Clear();
                t.Nodes.AddRange(n.Load().Select(Make).ToArray());
            }
            catch (Exception e) { t.Nodes.Add(new TreeNode("Could not be read: " + e.Message) { ForeColor = Color.Firebrick }); }
            finally { _tree.EndUpdate(); Cursor = Cursors.Default; }
        }

        public void Reload()
        {
            _tree.Nodes.Clear();
            _props.Items.Clear();
            _value.Text = "";
            object doc = Api.Get(_app, "ActiveDocument");
            if (doc == null) _status.Text = "No document is open in Inventor. Open an assembly and click \"Read active document\".";
            else
            {
                TreeNode root = Make(Tree.Document(doc));
                _tree.Nodes.Add(root);
                root.Expand();
                _tree.SelectedNode = root;
                _status.Text = UnitNote;
            }
            _tree.Nodes.Add(Make(Tree.RawNode("Inventor application", _app)));
        }

        void ShowProperties(TreeNode t)
        {
            _props.BeginUpdate();
            _props.Items.Clear();
            _value.Text = "";
            Cursor = Cursors.WaitCursor;
            try
            {
                if (t?.Tag is Node n)
                    _props.Items.AddRange(Tree.Rows(n).Select(r => new ListViewItem(new[] { r.Key, r.Value })).ToArray());
            }
            catch (Exception e) { _status.Text = "Could not read the properties: " + e.Message; }
            finally { _props.EndUpdate(); Cursor = Cursors.Default; }
        }

        // ------------------------------------------------------------------ commands

        // Selects the item in Inventor's window, when it belongs to the active document.
        void SelectInInventor()
        {
            object obj = (_tree.SelectedNode?.Tag as Node)?.Obj;
            if (obj == null) return;
            try
            {
                object set = Api.Get(_app, "ActiveDocument.SelectSet");
                Api.Call(set, "Clear");
                Api.Call(set, "Select", obj);
                _status.Text = "Selected in Inventor: " + _tree.SelectedNode.Text;
            }
            catch { _status.Text = "Inventor cannot select this item in the active document (it is not something you can click there)."; }
        }

        void Export()
        {
            if (!(_tree.SelectedNode?.Tag is Node n) || n.Raw) { _status.Text = "Select the document or a group in the tree first, then export."; return; }
            using (var dlg = new SaveFileDialog { Filter = "XML file|*.xml", FileName = "InventorReader.xml", Title = "Export \"" + n.Text + "\" and everything below it" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Cursor = Cursors.WaitCursor;
                try { _status.Text = $"Exported {Tree.Export(n, dlg.FileName)} items to {dlg.FileName}"; }
                catch (Exception e) { MessageBox.Show(this, "The export failed: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            }
        }

        void CopyRows()
        {
            var items = _props.SelectedItems.Count > 0 ? _props.SelectedItems.Cast<ListViewItem>() : _props.Items.Cast<ListViewItem>();
            string text = string.Join("\r\n", items.Select(i => i.Text + "\t" + i.SubItems[1].Text));
            if (text != "") Clipboard.SetText(text);
        }

        // Finds the next opened tree item that contains the text (items not opened yet are not read).
        void FindNext()
        {
            string q = _find.Text.Trim();
            if (q == "") return;
            var all = new List<TreeNode>();
            void Walk(TreeNodeCollection nodes) { foreach (TreeNode t in nodes) { if (t.Text != Placeholder) { all.Add(t); Walk(t.Nodes); } } }
            Walk(_tree.Nodes);
            int start = all.IndexOf(_tree.SelectedNode);
            for (int i = 1; i <= all.Count; i++)
            {
                TreeNode t = all[(start + i) % all.Count];
                if (t.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _tree.SelectedNode = t;
                t.EnsureVisible();
                return;
            }
            _status.Text = $"\"{q}\" was not found in the opened part of the tree.";
        }
    }
}
