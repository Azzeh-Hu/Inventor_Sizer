// The small box that changes a dimension of the GR twin: "Enter Dimension Value ___ mm", next to
// where the dimension was clicked. Enter uses the value; Esc or a click elsewhere cancels.
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventorSizer
{
    sealed class DimValueBox : GlassForm
    {
        readonly TextBox input;

        DimValueBox(string value)
        {
            Size = new Size(230, 92);
            var heading = new Label { Text = "Enter Dimension Value", Font = Theme.Heading(9.5f), ForeColor = Theme.GlassText, AutoSize = true, Location = new Point(14, 12), BackColor = Color.Transparent };
            input = new TextBox { Text = value ?? "", Font = Theme.Ui(11f), Location = new Point(14, 42), Width = 160, TextAlign = HorizontalAlignment.Right };
            var unit = new Label { Text = "mm", Font = Theme.Body, ForeColor = Theme.GlassMuted, AutoSize = true, Location = new Point(input.Right + 8, 46), BackColor = Color.Transparent };
            Controls.AddRange(new Control[] { heading, input, unit });
            Shown += (s, e) => { input.Focus(); input.SelectAll(); };
            Deactivate += (s, e) => { if (DialogResult == DialogResult.None) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter) { DialogResult = DialogResult.OK; Close(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // The value typed (null = cancelled), the box just below and right of `at` (screen), kept on screen.
        public static string Ask(Form owner, Point at, string value)
        {
            using (var d = new DimValueBox(value))
            {
                var area = Screen.FromPoint(at).WorkingArea;
                int x = Math.Max(area.Left, Math.Min(at.X + 14, area.Right - d.Width));
                int y = at.Y + 14 + d.Height > area.Bottom ? at.Y - 14 - d.Height : at.Y + 14;
                d.Location = new Point(x, Math.Max(area.Top, y));
                return d.ShowDialog(owner) == DialogResult.OK ? d.input.Text : null;
            }
        }
    }
}
