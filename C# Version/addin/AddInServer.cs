// The add-in itself: Inventor loads this class (see InventorSizer.Reader.addin), and it adds
// the "Assembly Reader" button to the Tools tab of the assembly and part ribbons.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inventor;

namespace InventorSizer.AsmReader
{
    [ComVisible(true), Guid("32FE73A4-DAFE-449E-91A8-2B8797BA0AFB")]
    public class AddInServer : ApplicationAddInServer
    {
        const string ClientId = "{32FE73A4-DAFE-449E-91A8-2B8797BA0AFB}";
        const string ButtonId = "InventorSizer.Reader.Show";

        Inventor.Application _app;
        ButtonDefinition _button;
        readonly List<RibbonPanel> _panels = new List<RibbonPanel>();
        ReaderForm _form;

        class Owner : IWin32Window { public IntPtr Handle { get; set; } }

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            try
            {
                _app = addInSiteObject.Application;
                Tree.App = _app;
                ControlDefinitions defs = _app.CommandManager.ControlDefinitions;
                try
                {
                    _button = defs.AddButtonDefinition("Assembly\nReader", ButtonId, CommandTypesEnum.kQueryOnlyCmdType, ClientId,
                        "Shows everything inside the active assembly or part", "Assembly Reader");
                }
                catch { _button = (ButtonDefinition)defs[ButtonId]; }   // still there from an earlier load
                _button.OnExecute += OnExecute;

                foreach (string ribbon in new[] { "Assembly", "Part" })
                {
                    try
                    {
                        RibbonTab tab = _app.UserInterfaceManager.Ribbons[ribbon].RibbonTabs["id_TabTools"];
                        RibbonPanel panel = tab.RibbonPanels.Add("Sizer", "InventorSizer.Reader.Panel." + ribbon, ClientId);
                        panel.CommandControls.AddButton(_button, true);
                        _panels.Add(panel);
                    }
                    catch { }   // the panel is still there from an earlier load
                }
            }
            catch (Exception e) { MessageBox.Show("The Assembly Reader add-in could not start:\n" + e.Message, "Assembly Reader"); }
        }

        void OnExecute(NameValueMap context)
        {
            try
            {
                if (_form == null || _form.IsDisposed)
                {
                    _form = new ReaderForm(_app);
                    _form.Show(new Owner { Handle = new IntPtr(Convert.ToInt64(Api.Get(_app, "MainFrameHWND"))) });
                }
                _form.Activate();
                _form.Reload();
            }
            catch (Exception e) { MessageBox.Show("The Assembly Reader could not open:\n" + e, "Assembly Reader"); }
        }

        public void Deactivate()
        {
            try { if (_form != null && !_form.IsDisposed) _form.Close(); } catch { }
            try { if (_button != null) _button.OnExecute -= OnExecute; } catch { }
            foreach (RibbonPanel p in _panels) { try { p.Delete(); } catch { } }
            try { _button?.Delete(); } catch { }
            _panels.Clear();
            _button = null;
            _app = null;
            Tree.App = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID) { }

        public object Automation => null;
    }
}
