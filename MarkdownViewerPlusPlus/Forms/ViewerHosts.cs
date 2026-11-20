using com.insanitydesign.MarkdownViewerPlusPlus.Npp;
using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using Kbg.NppPluginNET;
using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Forms
{
    internal static class ViewerIcons
    {
        private static Icon icon;

        /// <summary>The 16x16 Markdown icon with its fuchsia background made transparent.</summary>
        public static Icon MarkdownIcon
        {
            get
            {
                if (icon != null) return icon;
                using (Bitmap bmp = new Bitmap(16, 16))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    var attr = new ImageAttributes();
                    attr.SetRemapTable(new[] { new ColorMap { OldColor = Color.Fuchsia, NewColor = Color.Transparent } });
                    g.DrawImage(Resources.markdown_16x16_solid_bmp, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, attr);
                    icon = Icon.FromHandle(bmp.GetHicon());
                }
                return icon;
            }
        }
    }

    /// <summary>
    /// The window registered with the Notepad++ docking manager. Hosts the <see cref="ViewerPanel"/> while docked.
    /// </summary>
    internal class DockForm : Form
    {
        private const int WM_NOTIFY = 0x004E;
        private const int DMN_CLOSE = 1050 + 1;
        private const int NPPM_DMMREGASDCKDLG = 0x400 + 1000 + 33;

        private readonly MarkdownViewer viewer;
        private IntPtr tbDataPtr = IntPtr.Zero;

        public DockForm(MarkdownViewer viewer, int commandIndex)
        {
            this.viewer = viewer;
            this.Text = Main.PluginName;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(400, 600);

            var tbData = new NppTbData
            {
                hClient = this.Handle,
                pszName = Main.PluginName,
                dlgID = commandIndex,
                uMask = NppTbMsg.DWS_DF_CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR,
                hIconTab = ViewerIcons.MarkdownIcon.Handle,
                pszModuleName = "MarkdownViewerPlusPlus.dll"
            };
            // Notepad++ keeps the pointer: allocate once and keep it alive for the lifetime of the plugin
            this.tbDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(tbData));
            Marshal.StructureToPtr(tbData, this.tbDataPtr, false);
            NppApi.Npp(NPPM_DMMREGASDCKDLG, 0, this.tbDataPtr);
        }

        public bool IsShown => this.IsHandleCreated && NppApi.IsWindowVisible(this.Handle);

        public void ShowPanel() => NppApi.Npp(NppApi.NPPM_DMMSHOW, 0, this.Handle);

        public void HidePanel() => NppApi.Npp(NppApi.NPPM_DMMHIDE, 0, this.Handle);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NOTIFY)
            {
                var nmhdr = (Windows.NMHDR)Marshal.PtrToStructure(m.LParam, typeof(Windows.NMHDR));
                if ((nmhdr.code & 0xFFFF) == DMN_CLOSE)
                {
                    this.viewer.OnPanelClosedByUser();
                }
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Independent top-level window for the undocked viewer (can be moved to another monitor, has its own taskbar button).
    /// </summary>
    internal class FloatingForm : Form
    {
        private readonly MarkdownViewer viewer;

        public FloatingForm(MarkdownViewer viewer)
        {
            this.viewer = viewer;
            this.Text = Main.PluginName;
            this.Icon = ViewerIcons.MarkdownIcon;
            this.ShowInTaskbar = true;
            this.StartPosition = FormStartPosition.Manual;
            this.MinimumSize = new Size(240, 160);
            this.Bounds = LoadBounds(viewer.Options.floatingBounds);
        }

        private static Rectangle LoadBounds(string saved)
        {
            try
            {
                int[] v = (saved ?? "").Split(',').Select(int.Parse).ToArray();
                if (v.Length == 4)
                {
                    var rect = new Rectangle(v[0], v[1], v[2], v[3]);
                    // Only reuse it if it is still on a connected screen
                    if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return rect;
                }
            }
            catch { }
            Rectangle area = Screen.FromHandle(NppApi.NppHandle).WorkingArea;
            int w = Math.Min(900, area.Width * 2 / 5), h = area.Height * 4 / 5;
            return new Rectangle(area.Right - w - 40, area.Top + (area.Height - h) / 2, w, h);
        }

        public string SavedBounds
        {
            get
            {
                Rectangle r = this.WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;
                return $"{r.X},{r.Y},{r.Width},{r.Height}";
            }
        }

        public void SetDocumentTitle(string fileName)
        {
            this.Text = string.IsNullOrEmpty(fileName) ? Main.PluginName : fileName + " - " + Main.PluginName;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing with the X button just hides the viewer; it is reused next time
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.viewer.HideViewer();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
