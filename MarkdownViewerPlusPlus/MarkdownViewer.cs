using com.insanitydesign.MarkdownViewerPlusPlus.Forms;
using com.insanitydesign.MarkdownViewerPlusPlus.Helper;
using com.insanitydesign.MarkdownViewerPlusPlus.Npp;
using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using com.insanitydesign.MarkdownViewerPlusPlus.Rendering;
using Kbg.NppPluginNET;
using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static com.insanitydesign.MarkdownViewerPlusPlus.MarkdownViewerConfiguration;

namespace com.insanitydesign.MarkdownViewerPlusPlus
{
    /// <summary>
    /// Plugin controller: reacts to Notepad++ events, renders the current document and keeps editor and viewer in sync.
    /// </summary>
    public class MarkdownViewer
    {
        // Plugin menu entries (index = position in the Plugins > MarkdownViewer++ menu)
        public const int commandId = 0;
        public const int commandIdSynchronize = 2;
        public const int commandIdTheme = 3;
        public const int commandIdUndock = 4;
        public const int commandIdOptions = 6;
        public const int commandIdAbout = 7;

        private static readonly HashSet<string> markdownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "md", "markdown", "mdown", "mkd", "mkdn", "mdwn", "mdtxt", "mdtext", "rmd", "txt"
        };

        private MarkdownViewerConfiguration configuration;
        private readonly MarkdownRenderer renderer = new MarkdownRenderer();

        private ViewerPanel panel;
        private DockForm dockForm;
        private FloatingForm floatingForm;
        private Timer renderTimer;

        private string lastRenderedPath;
        private string pendingFragment;
        private string pendingFragmentPath;
        private DateTime ignoreEditorScrollUntil = DateTime.MinValue;
        private Icon toolbarIcon, toolbarIconDark;

        public Options Options => this.configuration.options;

        // ------------------------------------------------------------------ Menu / toolbar

        public void CommandMenuInit()
        {
            this.configuration = new MarkdownViewerConfiguration();

            PluginBase.SetCommand(commandId, Main.PluginName, MarkdownViewerCommand, new ShortcutKey(true, false, true, Keys.M));
            PluginBase.SetCommand(commandId + 1, "---", null);
            PluginBase.SetCommand(commandIdSynchronize, "Synchronize scrolling (Editor <-> Viewer)", ToggleSynchronizeScrolling, Options.synchronizeScrolling);
            PluginBase.SetCommand(commandIdTheme, "Follow Notepad++ theme (light/dark)", ToggleFollowTheme, Options.followNppTheme);
            PluginBase.SetCommand(commandIdUndock, "Undock / dock viewer (separate window)", ToggleDocking);
            PluginBase.SetCommand(commandIdUndock + 1, "---", null);
            PluginBase.SetCommand(commandIdOptions, "Options", OptionsCommand);
            PluginBase.SetCommand(commandIdAbout, "About", AboutCommand);
        }

        private int CmdId(int index) => PluginBase._funcItems.Items[index]._cmdID;

        [StructLayout(LayoutKind.Sequential)]
        private struct ToolbarIconsWithDarkMode
        {
            public IntPtr hToolbarBmp;
            public IntPtr hToolbarIcon;
            public IntPtr hToolbarIconDarkMode;
        }

        public void SetToolBarIcon()
        {
            const int NPPM_ADDTOOLBARICON_DEPRECATED = 0x400 + 1000 + 41;
            const int NPPM_ADDTOOLBARICON_FORDARKMODE = 0x400 + 1000 + 101;

            this.toolbarIcon = ViewerIcons.MarkdownIcon;
            this.toolbarIconDark = CreateInvertedIcon(Resources.markdown_16x16_solid);
            var icons = new ToolbarIconsWithDarkMode
            {
                hToolbarBmp = Resources.markdown_16x16_solid.GetHbitmap(),
                hToolbarIcon = this.toolbarIcon.Handle,
                hToolbarIconDarkMode = this.toolbarIconDark.Handle
            };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(icons));
            Marshal.StructureToPtr(icons, ptr, false);
            if (NppApi.Npp(NPPM_ADDTOOLBARICON_FORDARKMODE, CmdId(commandId), ptr) == IntPtr.Zero)
            {
                // Notepad++ older than 8.0
                NppApi.Npp(NPPM_ADDTOOLBARICON_DEPRECATED, CmdId(commandId), ptr);
            }
            // Notepad++ keeps using the handles, so the memory is intentionally not freed
        }

        private static Icon CreateInvertedIcon(Bitmap source)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                var attr = new ImageAttributes();
                attr.SetColorMatrix(new ColorMatrix(new[]
                {
                    new float[] { -1, 0, 0, 0, 0 },
                    new float[] { 0, -1, 0, 0, 0 },
                    new float[] { 0, 0, -1, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 1, 1, 1, 0, 1 }
                }));
                g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attr);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // ------------------------------------------------------------------ Commands

        /// <summary>Ctrl+Shift+M: open or close the viewer.</summary>
        public void MarkdownViewerCommand()
        {
            if (IsVisible) HideViewer();
            else ShowViewer();
        }

        public bool IsVisible =>
            (this.floatingForm != null && this.floatingForm.Visible) ||
            (this.dockForm != null && this.dockForm.IsShown);

        public void ShowViewer()
        {
            EnsurePanel();
            if (Options.undocked)
            {
                if (this.floatingForm == null) this.floatingForm = new FloatingForm(this);
                MovePanelTo(this.floatingForm);
                this.floatingForm.Show();
                this.floatingForm.Activate();
            }
            else
            {
                if (this.dockForm == null) this.dockForm = new DockForm(this, commandId);
                MovePanelTo(this.dockForm);
                this.dockForm.ShowPanel();
            }
            this.panel.SetDockedState(Options.undocked);
            this.panel.EnsureInitialized();
            NppApi.SetMenuItemCheck(CmdId(commandId), true);
            ApplyTheme();
            Render(forceNewDocument: true);
            if (!Options.undocked) NppApi.FocusEditor();
        }

        public void HideViewer()
        {
            if (this.floatingForm != null && this.floatingForm.Visible)
            {
                this.configuration.options.floatingBounds = this.floatingForm.SavedBounds;
                this.floatingForm.Hide();
            }
            if (this.dockForm != null && this.dockForm.IsShown)
            {
                this.dockForm.HidePanel();
            }
            this.renderTimer?.Stop();
            NppApi.SetMenuItemCheck(CmdId(commandId), false);
            NppApi.FocusEditor();
        }

        /// <summary>The user closed the docking panel with its X button.</summary>
        internal void OnPanelClosedByUser()
        {
            this.renderTimer?.Stop();
            NppApi.SetMenuItemCheck(CmdId(commandId), false);
        }

        /// <summary>Move the viewer between the Notepad++ panel and its own window.</summary>
        public void ToggleDocking()
        {
            bool wasVisible = IsVisible;
            if (wasVisible) HideViewer();
            this.configuration.options.undocked = !this.configuration.options.undocked;
            ShowViewer();
        }

        public void ToggleSynchronizeScrolling()
        {
            this.configuration.options.synchronizeScrolling = !this.configuration.options.synchronizeScrolling;
            NppApi.SetMenuItemCheck(CmdId(commandIdSynchronize), Options.synchronizeScrolling);
            if (Options.synchronizeScrolling) SyncViewerToEditor();
        }

        public void ToggleFollowTheme()
        {
            this.configuration.options.followNppTheme = !this.configuration.options.followNppTheme;
            NppApi.SetMenuItemCheck(CmdId(commandIdTheme), Options.followNppTheme);
            ApplyTheme();
        }

        public void Refresh() => Render(forceNewDocument: false);

        public void OptionsCommand()
        {
            using (var options = new MarkdownViewerOptions(ref this.configuration))
            {
                if (options.ShowDialog(Control.FromHandle(NppApi.NppHandle)) == DialogResult.OK && IsVisible)
                {
                    ApplyTheme();
                    Render(forceNewDocument: false);
                }
            }
        }

        public void AboutCommand()
        {
            using (var about = new AboutDialog())
            {
                about.ShowDialog(Control.FromHandle(NppApi.NppHandle));
            }
        }

        // ------------------------------------------------------------------ Notifications

        public void OnNotification(ScNotification notification)
        {
            uint code = notification.Header.Code;
            if (code != NppApi.SCN_UPDATEUI || (notification.Updated & NppApi.SC_UPDATE_V_SCROLL) != 0)
                DebugLog.Write($"notify code={code} from={notification.Header.hwndFrom} mod={notification.ModificationType} upd={notification.Updated} visible={IsVisible} cur={NppApi.CurrentScintilla}");

            if (code == NppApi.NPPN_READY)
            {
                // Init the checkmarks of the toggles (Notepad++ only does it for _init2Check at startup)
                NppApi.SetMenuItemCheck(CmdId(commandIdSynchronize), Options.synchronizeScrolling);
                NppApi.SetMenuItemCheck(CmdId(commandIdTheme), Options.followNppTheme);
                return;
            }
            if (code == NppApi.NPPN_DARKMODECHANGED || code == NppApi.NPPN_WORDSTYLESUPDATED)
            {
                if (this.panel != null) ApplyTheme();
                return;
            }
            if (!IsVisible) return;

            switch (code)
            {
                case NppApi.SCN_MODIFIED:
                    if ((notification.ModificationType & (NppApi.SC_MOD_INSERTTEXT | NppApi.SC_MOD_DELETETEXT)) != 0
                        && notification.Header.hwndFrom == NppApi.CurrentScintilla)
                    {
                        ScheduleRender();
                    }
                    break;

                case NppApi.SCN_UPDATEUI:
                    if ((notification.Updated & NppApi.SC_UPDATE_V_SCROLL) != 0
                        && notification.Header.hwndFrom == NppApi.CurrentScintilla)
                    {
                        SyncViewerToEditor();
                    }
                    break;

                case NppApi.NPPN_BUFFERACTIVATED:
                case NppApi.NPPN_FILERENAMED:
                    Render(forceNewDocument: false);
                    break;

                case NppApi.NPPN_FILESAVED:
                    // Saving always refreshes the viewer immediately
                    this.renderTimer?.Stop();
                    Render(forceNewDocument: false);
                    break;
            }
        }

        private void ScheduleRender()
        {
            if (this.renderTimer == null)
            {
                this.renderTimer = new Timer { Interval = 300 };
                this.renderTimer.Tick += (s, e) => { this.renderTimer.Stop(); Render(forceNewDocument: false); };
            }
            // Restart: render once the user pauses typing
            this.renderTimer.Stop();
            this.renderTimer.Start();
        }

        // ------------------------------------------------------------------ Rendering

        private void EnsurePanel()
        {
            if (this.panel == null) this.panel = new ViewerPanel(this);
        }

        private void MovePanelTo(Control host)
        {
            if (this.panel.Parent != host)
            {
                host.SuspendLayout();
                this.panel.Parent = host;
                this.panel.Dock = DockStyle.Fill;
                host.ResumeLayout();
            }
        }

        /// <summary>The browser finished loading the viewer page.</summary>
        internal void OnViewerReady()
        {
            ApplyTheme();
            Render(forceNewDocument: true);
        }

        private void Render(bool forceNewDocument)
        {
            if (this.panel == null || !IsVisible) return;

            string path = NppApi.CurrentFilePath;
            string fileName = Path.GetFileName(path);
            string extension = Path.GetExtension(path).TrimStart('.');
            bool newDocument = forceNewDocument || !string.Equals(path, this.lastRenderedPath, StringComparison.OrdinalIgnoreCase);
            this.lastRenderedPath = path;

            string text = "";
            string html;
            if (this.configuration.ValidateFileExtension(extension, fileName))
            {
                try
                {
                    text = NppApi.GetText();
                    html = this.renderer.ToHtml(text, this.panel.MapLocalFile);
                }
                catch (Exception ex)
                {
                    html = "<p class=\"mdv-message\">Couldn't render the currently selected file: " + System.Net.WebUtility.HtmlEncode(ex.Message) + "</p>";
                }
            }
            else
            {
                html = $@"<p class=""mdv-message"">Your configuration settings do not include the currently selected file extension.<br />
The rendered file extensions are <b>'{System.Net.WebUtility.HtmlEncode(Options.fileExtensions)}'</b>.<br />
The current file is <i>'{System.Net.WebUtility.HtmlEncode(fileName)}'</i>.</p>";
            }

            string fragment = null;
            if (newDocument && this.pendingFragment != null && string.Equals(path, this.pendingFragmentPath, StringComparison.OrdinalIgnoreCase))
            {
                fragment = this.pendingFragment;
                this.pendingFragment = this.pendingFragmentPath = null;
            }

            this.panel.Render(html, text, path, Options.HtmlCssStyle, resetScroll: newDocument && fragment == null, fragment: fragment);
            this.floatingForm?.SetDocumentTitle(fileName);

            if (newDocument && fragment == null) SyncViewerToEditor();
        }

        private void SyncViewerToEditor()
        {
            if (this.panel == null || !Options.synchronizeScrolling || DateTime.Now < this.ignoreEditorScrollUntil) return;
            try
            {
                int first = NppApi.FirstVisibleDocLine;
                bool atEnd = first > 0 && first + NppApi.LinesOnScreen >= NppApi.LineCount;
                this.panel.SyncToLine(first, atEnd);
            }
            catch { }
        }

        /// <summary>An internal link (#heading) was clicked in the viewer: show the same place in the editor.</summary>
        internal void OnViewerNavigatedToLine(int line)
        {
            DebugLog.Write($"reveal line {line} in {NppApi.CurrentScintilla}");
            this.ignoreEditorScrollUntil = DateTime.Now.AddMilliseconds(500);
            NppApi.RevealLineAtTop(line);
        }

        /// <summary>The user scrolled the viewer.</summary>
        internal void OnViewerScrolled(int line, bool atEnd)
        {
            if (!Options.synchronizeScrolling) return;
            this.ignoreEditorScrollUntil = DateTime.Now.AddMilliseconds(400);
            NppApi.ScrollToLine(atEnd ? NppApi.LineCount : line);
        }

        // ------------------------------------------------------------------ Links

        /// <summary>Handles a link clicked in the viewer (raw href, or an absolute URL from the browser).</summary>
        internal void OpenLink(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return;
            try
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out Uri uri) && !uri.IsFile && !IsWindowsPath(href))
                {
                    if (uri.Host == ViewerPanel.AppHost)
                    {
                        return; // internal URLs (already handled by the page)
                    }
                    Process.Start(new ProcessStartInfo(href) { UseShellExecute = true }); // http, https, mailto, ...
                    return;
                }

                string target = href, fragment = null;
                int hash = target.IndexOf('#');
                if (hash >= 0) { fragment = target.Substring(hash + 1); target = target.Substring(0, hash); }
                int query = target.IndexOf('?');
                if (query >= 0) target = target.Substring(0, query);

                string currentPath = NppApi.CurrentFilePath;
                string path;
                if (target.Length == 0) path = currentPath;
                else if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) path = new Uri(target).LocalPath;
                else
                {
                    target = Uri.UnescapeDataString(target).Replace('/', '\\');
                    path = Path.IsPathRooted(target) && !target.StartsWith("\\") ? target
                         : target.StartsWith("\\") && !target.StartsWith("\\\\") ? Path.Combine(Path.GetPathRoot(currentPath), target.TrimStart('\\'))
                         : Path.Combine(Path.GetDirectoryName(currentPath) ?? "", target);
                    path = Path.GetFullPath(path);
                }

                if (File.Exists(path))
                {
                    string ext = Path.GetExtension(path).TrimStart('.');
                    bool openInNpp = markdownExtensions.Contains(ext) || this.configuration.ValidateFileExtension(ext, Path.GetFileName(path)) && !string.IsNullOrEmpty(Options.fileExtensions);
                    if (!openInNpp)
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    else if (string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(fragment)) this.panel.GoToFragment(fragment);
                    }
                    else
                    {
                        this.pendingFragment = string.IsNullOrEmpty(fragment) ? null : fragment;
                        this.pendingFragmentPath = path;
                        NppApi.OpenFile(path); // triggers NPPN_BUFFERACTIVATED -> Render
                    }
                }
                else if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else
                {
                    MessageBox.Show("File not found:\r\n" + path, Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the link:\r\n" + href + "\r\n\r\n" + ex.Message, Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool IsWindowsPath(string s) =>
            s.Length > 2 && char.IsLetter(s[0]) && s[1] == ':' || s.StartsWith("\\\\");

        // ------------------------------------------------------------------ Theme

        private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        public void ApplyTheme()
        {
            if (this.panel == null) return;

            bool follow = Options.followNppTheme;
            Color editorBack = Color.White, editorFore = Color.Black;
            try { editorBack = NppApi.EditorBackground; editorFore = NppApi.EditorForeground; } catch { }
            bool dark = follow && Luminance(editorBack) < 0.5;

            // Colors for the viewer's own menu bar
            Color uiBack = SystemColors.Control, uiFore = SystemColors.ControlText, uiHot = Color.FromArgb(0xCC, 0xE8, 0xFF);
            NppApi.DarkModeColors? dm = NppApi.IsDarkModeEnabled ? NppApi.GetDarkModeColors() : null;
            if (dm.HasValue && (follow || dark))
            {
                uiBack = dm.Value.PureBackground;
                uiFore = dm.Value.Text;
                uiHot = dm.Value.HotBackground;
            }
            else if (dark)
            {
                uiBack = Color.FromArgb(0x20, 0x20, 0x20);
                uiFore = Color.FromArgb(0xE0, 0xE0, 0xE0);
                uiHot = Color.FromArgb(0x40, 0x40, 0x40);
            }

            this.panel.ApplyTheme(dark, follow ? editorBack : (Color?)null, follow ? editorFore : (Color?)null, uiBack, uiFore, uiHot);
        }

        // ------------------------------------------------------------------ Shutdown

        public void PluginCleanUp()
        {
            if (this.floatingForm != null && this.floatingForm.Visible)
            {
                this.configuration.options.floatingBounds = this.floatingForm.SavedBounds;
            }
            this.configuration.Save();
            try { this.panel?.Dispose(); } catch { }
        }
    }
}
