using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("Inventor Sizer")]
[assembly: AssemblyProduct("Inventor Sizer")]
[assembly: AssemblyVersion("2.0.0.0")]

namespace InventorSizer
{
    static class Program
    {
        // Dev run (InventorSizer.exe /dev, or "Dev run.cmd"): also shows what prepares the app -
        // setting up and storing masters, learning hardware holes. The user run only makes panels.
        public static bool Dev;

        [STAThread]
        static void Main(string[] args)
        {
            Dev = args.Any(a => a.TrimStart('/', '-').Equals("dev", StringComparison.OrdinalIgnoreCase));
            // lock-panels.cmd: /lock writes panels.lock for the panel files as they are, /unlock removes it.
            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (args.Any(a => a.TrimStart('/', '-').Equals("lock", StringComparison.OrdinalIgnoreCase)))
            {
                int n = InventorSizer.Core.PanelLock.Write(root);
                MessageBox.Show($"Locked {n} panel files (FP / TP / RP masters, their standards and MODELS).\n\n{System.IO.Path.Combine(root, InventorSizer.Core.PanelLock.FileName)}\n\nCommit panels.lock together with the panel files.", "Inventor Sizer - lock panel files");
                return;
            }
            if (args.Any(a => a.TrimStart('/', '-').Equals("unlock", StringComparison.OrdinalIgnoreCase)))
            {
                var f = System.IO.Path.Combine(root, InventorSizer.Core.PanelLock.FileName);
                if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
                MessageBox.Show("Panel files unlocked (panels.lock removed). Make the change, then run lock-panels.cmd to lock them again.\nThe GitHub check fails until they are locked again.", "Inventor Sizer - unlock panel files");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "Inventor Sizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }
}
