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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "Inventor Sizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }
}
