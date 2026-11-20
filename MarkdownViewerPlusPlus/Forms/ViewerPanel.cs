using com.insanitydesign.MarkdownViewerPlusPlus.Helper;
using com.insanitydesign.MarkdownViewerPlusPlus.Npp;
using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using Kbg.NppPluginNET;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Forms
{
    /// <summary>
    /// The actual viewer: a menu bar plus a WebView2 (Edge) browser. It can live inside the
    /// Notepad++ docking panel or inside the independent (undocked) window.
    /// </summary>
    public class ViewerPanel : UserControl
    {
        internal const string AppHost = "mdviewer.app";
        private const string AppUrl = "https://" + AppHost + "/";

        private readonly MarkdownViewer viewer;
        private readonly ToolStrip menu;
        private readonly WebView2 webView;
        private readonly ToolStripButton dockItem;
        private readonly ToolStripMenuItem themeItem;
        private readonly ToolStripMenuItem syncItem;
        private readonly ToolStripMenuItem mailHtmlItem;
        private readonly ToolStripMenuItem mailTextItem;

        private bool initStarted;
        private bool pageReady;
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        /// <summary>Current HTML body (for exports).</summary>
        public string BodyHtml { get; private set; } = "";
        public string RawText { get; private set; } = "";
        public string FilePath { get; private set; } = "";

        public ViewerPanel(MarkdownViewer viewer)
        {
            this.viewer = viewer;
            this.Dock = DockStyle.Fill;

            this.webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };

            this.menu = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(2, 1, 2, 1), CanOverflow = true };
            var export = new ToolStripDropDownButton("Export as", Resources.fa_download_16x16) { ShowDropDownArrow = false };
            export.DropDownItems.Add(new ToolStripMenuItem("HTML...", Resources.fa_html5_16x16, (s, e) => ExportHtml()));
            export.DropDownItems.Add(new ToolStripMenuItem("PDF...", Resources.fa_file_pdf_o_16x16, async (s, e) => await ExportPdf()));

            var send = new ToolStripDropDownButton("Send", Resources.fa_upload_16x16) { ShowDropDownArrow = false };
            this.mailHtmlItem = new ToolStripMenuItem("as HTML E-mail", Resources.fa_html5_16x16, (s, e) => SendMail(true));
            this.mailTextItem = new ToolStripMenuItem("as Text E-mail", Resources.fa_envelope_16x16, (s, e) => SendMail(false));
            send.DropDownItems.Add(this.mailHtmlItem);
            send.DropDownItems.Add(this.mailTextItem);
            send.DropDownItems.Add(new ToolStripMenuItem("to Printer...", Resources.fa_print_16x16, (s, e) => Print()));
            send.DropDownItems.Add(new ToolStripMenuItem("HTML to Clipboard", Resources.fa_clipboard_16x16,
                (s, e) => ClipboardHelper.CopyToClipboard(BuildStandaloneHtml(), RawText)));
            send.DropDownOpening += (s, e) => { bool outlook = IsOutlookInstalled(); this.mailHtmlItem.Visible = outlook; this.mailTextItem.Visible = outlook; };

            var view = new ToolStripDropDownButton("View", Resources.fa_tv_16x16) { ShowDropDownArrow = false };
            view.DropDownItems.Add(new ToolStripMenuItem("Refresh", Resources.fa_refresh_16x16, (s, e) => this.viewer.Refresh()) { ShortcutKeyDisplayString = "F5" });
            this.syncItem = new ToolStripMenuItem("Synchronize scrolling", null, (s, e) => this.viewer.ToggleSynchronizeScrolling());
            this.themeItem = new ToolStripMenuItem("Follow Notepad++ theme", null, (s, e) => this.viewer.ToggleFollowTheme());
            view.DropDownItems.Add(this.syncItem);
            view.DropDownItems.Add(this.themeItem);
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(new ToolStripMenuItem("Close viewer", null, (s, e) => this.viewer.HideViewer()) { ShortcutKeyDisplayString = "Esc / Ctrl+Shift+M" });
            view.DropDownOpening += (s, e) =>
            {
                this.syncItem.Checked = this.viewer.Options.synchronizeScrolling;
                this.themeItem.Checked = this.viewer.Options.followNppTheme;
            };

            var help = new ToolStripDropDownButton("?") { ShowDropDownArrow = false };
            help.DropDownItems.Add(new ToolStripMenuItem("Options", Resources.fa_cog_16x16, (s, e) => this.viewer.OptionsCommand()));
            help.DropDownItems.Add(new ToolStripMenuItem("About", Resources.fa_info_16x16, (s, e) => this.viewer.AboutCommand()));

            this.dockItem = new ToolStripButton("Undock") { Alignment = ToolStripItemAlignment.Right, Overflow = ToolStripItemOverflow.Never, ToolTipText = "Show the viewer in its own window" };
            this.dockItem.Click += (s, e) => this.viewer.ToggleDocking();

            this.menu.Items.AddRange(new ToolStripItem[] { export, send, view, help, this.dockItem });

            this.Controls.Add(this.webView);
            this.Controls.Add(this.menu);
        }

        public void SetDockedState(bool undocked)
        {
            this.dockItem.Text = undocked ? "Dock" : "Undock";
            this.dockItem.ToolTipText = undocked ? "Put the viewer back into the Notepad++ panel" : "Show the viewer in its own window";
        }

        // ------------------------------------------------------------------ WebView2 setup

        public async void EnsureInitialized()
        {
            if (this.initStarted) return;
            this.initStarted = true;
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                try { CoreWebView2Environment.SetLoaderDllFolderPath(pluginDir); } catch { }
                // Notepad++'s folder (Program Files) is read-only: keep the browser profile in the plugin config dir
                string dataDir = Path.Combine(NppApi.PluginsConfigDir, "MarkdownViewerPlusPlus", "WebView2");
                Directory.CreateDirectory(dataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await this.webView.EnsureCoreWebView2Async(env);
                ConfigureCoreWebView2(this.webView.CoreWebView2);
                this.webView.CoreWebView2.Navigate(AppUrl + "viewer.html");
            }
            catch (Exception ex)
            {
                ShowInitError(ex);
            }
        }

        private void ShowInitError(Exception ex)
        {
            this.webView.Visible = false;
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                Text = "MarkdownViewer++ could not start the Microsoft Edge WebView2 runtime.\r\n\r\n" +
                       "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and restart Notepad++.\r\n\r\n" + ex.Message
            };
            this.Controls.Add(label);
            label.BringToFront();
        }

        private void ConfigureCoreWebView2(CoreWebView2 core)
        {
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.IsStatusBarEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsWebMessageEnabled = true;

            // Serve viewer.html/css/js from the embedded resources, and the document's local files
            // (images, icons, ...) under https://mdviewer.app/fs/<absolute path>
            core.AddWebResourceRequestedFilter(AppUrl + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (s, e) =>
            {
                try { e.Response = ServeResource(core.Environment, new Uri(e.Request.Uri)); }
                catch { e.Response = core.Environment.CreateWebResourceResponse(null, 500, "Error", ""); }
            };

            core.WebMessageReceived += OnWebMessage;

            // Never let the viewer navigate away from the shell page: open links ourselves
            core.NavigationStarting += (s, e) =>
            {
                if (e.Uri.StartsWith(AppUrl + "viewer.html", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
                if (e.IsUserInitiated) this.viewer.OpenLink(e.Uri);
            };
            core.NewWindowRequested += (s, e) => { e.Handled = true; this.viewer.OpenLink(e.Uri); };
            core.NavigationCompleted += (s, e) => { if (!e.IsSuccess) this.pageReady = false; };

            // Trim the context menu down to what makes sense in a viewer
            core.ContextMenuRequested += (s, e) =>
            {
                var keep = new HashSet<string> { "copy", "copyLinkLocation", "copyImage", "copyImageLocation", "selectAll", "print" };
                for (int i = e.MenuItems.Count - 1; i >= 0; i--)
                {
                    var item = e.MenuItems[i];
                    if (item.Kind == CoreWebView2ContextMenuItemKind.Separator) continue;
                    if (!keep.Contains(item.Name)) e.MenuItems.RemoveAt(i);
                }
                // tidy up leading/trailing/double separators
                for (int i = e.MenuItems.Count - 1; i >= 0; i--)
                {
                    bool sep = e.MenuItems[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
                    bool prevSep = i == 0 || e.MenuItems[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator;
                    if (sep && (prevSep || i == e.MenuItems.Count - 1)) e.MenuItems.RemoveAt(i);
                }
            };

            // Keyboard shortcuts while the viewer has the focus (the browser swallows them otherwise).
            // The WinForms WebView2 control turns browser accelerator keys into regular KeyDown events.
            this.webView.KeyDown += OnBrowserKeyDown;
        }

        private void OnBrowserKeyDown(object sender, KeyEventArgs e)
        {
            Keys key = e.KeyCode;
            bool ctrl = e.Control, shift = e.Shift, alt = e.Alt;
            Action action = null;

            if (ctrl && shift && !alt && key == Keys.M) action = this.viewer.HideViewer;
            else if (!ctrl && !shift && !alt && key == Keys.Escape) action = this.viewer.HideViewer;
            else if ((key == Keys.F5 && !ctrl) || (ctrl && !shift && key == Keys.R)) action = this.viewer.Refresh;
            else if (ctrl && !shift && !alt && key == Keys.S) action = NppApi.SaveCurrentFile;
            else if (alt && (key == Keys.Left || key == Keys.Right)) action = () => { }; // no history navigation

            if (action != null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                BeginInvoke(action);
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            DebugLog.Write("page -> " + e.WebMessageAsJson);
            try { msg = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
            catch (Exception ex) { DebugLog.Write("bad message: " + ex.Message); return; }
            if (msg == null || !msg.TryGetValue("type", out object typeObj)) return;

            try { HandleWebMessage(typeObj as string, msg); }
            catch (Exception ex) { DebugLog.Write("message handler failed: " + ex); }
        }

        private void HandleWebMessage(string type, Dictionary<string, object> msg)
        {
            switch (type)
            {
                case "ready":
                    this.pageReady = true;
                    this.viewer.OnViewerReady();
                    break;
                case "line":
                    this.viewer.OnViewerNavigatedToLine(Convert.ToInt32(msg["line"]));
                    break;
                case "scroll":
                    this.viewer.OnViewerScrolled(Convert.ToInt32(msg["line"]), msg.TryGetValue("atEnd", out object end) && end is bool b && b);
                    break;
                case "command":
                    switch (msg["command"] as string)
                    {
                        case "close": BeginInvoke((Action)this.viewer.HideViewer); break;
                        case "refresh": BeginInvoke((Action)this.viewer.Refresh); break;
                        case "save": BeginInvoke((Action)NppApi.SaveCurrentFile); break;
                    }
                    break;
                case "link":
                    this.viewer.OpenLink(msg["href"] as string);
                    break;
            }
        }

        /// <summary>Send a message to the page. Before the page is ready messages are dropped:
        /// the controller re-applies theme and content when it gets the "ready" message.</summary>
        private void Post(object message)
        {
            if (!this.pageReady || this.webView.CoreWebView2 == null) return;
            try { this.webView.CoreWebView2.PostWebMessageAsJson(json.Serialize(message)); } catch { }
        }

        // ------------------------------------------------------------------ Rendering API

        private const string FsPrefix = "/fs/";

        private CoreWebView2WebResourceResponse ServeResource(CoreWebView2Environment env, Uri uri)
        {
            string path = uri.AbsolutePath;
            Stream stream = null;
            string name;
            if (path.StartsWith(FsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string local = Uri.UnescapeDataString(path.Substring(FsPrefix.Length)).Replace('/', '\\');
                if (local.StartsWith("UNC\\", StringComparison.OrdinalIgnoreCase)) local = "\\" + local.Substring(3);
                name = local;
                if (File.Exists(local)) stream = new MemoryStream(File.ReadAllBytes(local));
            }
            else
            {
                name = path.TrimStart('/');
                stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            }
            if (stream == null) return env.CreateWebResourceResponse(null, 404, "Not Found", "");
            return env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {MimeType(name)}\r\nCache-Control: no-cache");
        }

        private static string MimeType(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".ico": return "image/x-icon";
                case ".avif": return "image/avif";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mp3": return "audio/mpeg";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                case ".json": return "application/json";
                default: return "application/octet-stream";
            }
        }

        /// <summary>URL under which the viewer can load the files of a local folder (relative images, etc.).</summary>
        public string GetFolderUrl(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try { return MapLocalFile(Path.GetFullPath(folder).TrimEnd('\\') + "\\"); } catch { return null; }
        }

        /// <summary>URL under which the viewer can load an absolute local path.</summary>
        public string MapLocalFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            bool isDir = path.EndsWith("\\");
            string p = path.StartsWith("\\\\") ? "UNC\\" + path.Substring(2) : path;
            string encoded = string.Join("/", p.Split('\\').Select(Uri.EscapeDataString));
            return AppUrl + FsPrefix.TrimStart('/') + encoded + (isDir && !encoded.EndsWith("/") ? "/" : "");
        }

        public bool IsReady => this.webView.CoreWebView2 != null;

        public void Render(string html, string rawText, string filePath, string userCss, bool resetScroll, string fragment)
        {
            BodyHtml = html;
            RawText = rawText;
            FilePath = filePath ?? "";
            string dir = "";
            try { dir = Path.GetDirectoryName(FilePath); } catch { }
            Post(new
            {
                type = "render",
                html,
                css = userCss ?? "",
                baseUrl = GetFolderUrl(dir) ?? AppUrl,
                title = Path.GetFileName(FilePath),
                resetScroll,
                fragment = fragment ?? ""
            });
        }

        public void SyncToLine(int line, bool atEnd) => Post(new { type = "syncToLine", line, atEnd });

        public void GoToFragment(string fragment) => Post(new { type = "fragment", fragment });

        public void ApplyTheme(bool dark, Color? background, Color? foreground, Color uiBack, Color uiFore, Color uiHot)
        {
            string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            Post(new
            {
                type = "theme",
                dark,
                bg = background.HasValue ? Hex(background.Value) : null,
                fg = foreground.HasValue ? Hex(foreground.Value) : null
            });

            this.webView.DefaultBackgroundColor = background ?? (dark ? Color.FromArgb(13, 17, 23) : Color.White);
            this.BackColor = this.webView.DefaultBackgroundColor;

            this.menu.Renderer = new ToolStripProfessionalRenderer(new ThemedColorTable(uiBack, uiHot, uiFore));
            this.menu.BackColor = uiBack;
            this.menu.ForeColor = uiFore;
            ApplyMenuColors(this.menu.Items, uiBack, uiFore);
        }

        private static void ApplyMenuColors(ToolStripItemCollection items, Color back, Color fore)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = back;
                item.ForeColor = fore;
                if (item is ToolStripDropDownItem mi)
                {
                    mi.DropDown.BackColor = back;
                    mi.DropDown.ForeColor = fore;
                    ApplyMenuColors(mi.DropDownItems, back, fore);
                }
            }
        }

        private class ThemedColorTable : ProfessionalColorTable
        {
            private readonly Color back, hot, fore;
            public ThemedColorTable(Color back, Color hot, Color fore) { this.back = back; this.hot = hot; this.fore = fore; UseSystemColors = false; }
            public override Color MenuStripGradientBegin => back;
            public override Color ToolStripGradientBegin => back;
            public override Color ToolStripGradientMiddle => back;
            public override Color ToolStripGradientEnd => back;
            public override Color ToolStripBorder => back;
            public override Color OverflowButtonGradientBegin => back;
            public override Color OverflowButtonGradientMiddle => back;
            public override Color OverflowButtonGradientEnd => back;
            public override Color ButtonSelectedHighlight => hot;
            public override Color ButtonSelectedGradientBegin => hot;
            public override Color ButtonSelectedGradientMiddle => hot;
            public override Color ButtonSelectedGradientEnd => hot;
            public override Color ButtonSelectedBorder => hot;
            public override Color ButtonPressedGradientBegin => hot;
            public override Color ButtonPressedGradientMiddle => hot;
            public override Color ButtonPressedGradientEnd => hot;
            public override Color ButtonPressedBorder => hot;
            public override Color MenuStripGradientEnd => back;
            public override Color ToolStripDropDownBackground => back;
            public override Color ImageMarginGradientBegin => back;
            public override Color ImageMarginGradientMiddle => back;
            public override Color ImageMarginGradientEnd => back;
            public override Color MenuItemSelected => hot;
            public override Color MenuItemSelectedGradientBegin => hot;
            public override Color MenuItemSelectedGradientEnd => hot;
            public override Color MenuItemPressedGradientBegin => hot;
            public override Color MenuItemPressedGradientMiddle => hot;
            public override Color MenuItemPressedGradientEnd => hot;
            public override Color MenuItemBorder => hot;
            public override Color MenuBorder => Color.FromArgb(Math.Min(255, hot.R + 20), Math.Min(255, hot.G + 20), Math.Min(255, hot.B + 20));
            public override Color SeparatorDark => hot;
            public override Color SeparatorLight => back;
            public override Color CheckBackground => hot;
            public override Color CheckSelectedBackground => hot;
            public override Color CheckPressedBackground => hot;
        }

        public void FocusBrowser()
        {
            try { this.webView.Focus(); } catch { }
        }

        // ------------------------------------------------------------------ Export / send

        private string GetBaseCss()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("viewer.css"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private string BuildStandaloneHtml()
        {
            string title = System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(FilePath));
            return $@"<!DOCTYPE html>
<html data-theme=""light"">
<head>
<meta charset=""UTF-8"" />
<meta name=""generator"" content=""MarkdownViewer++"" />
<title>{title}</title>
<style>
{GetBaseCss()}
{this.viewer.Options.HtmlCssStyle}
</style>
</head>
<body>
<article class=""markdown-body"">
{BodyHtml}
</article>
</body>
</html>";
        }

        private string AskSavePath(string extension, string filter)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.FileName = Path.GetFileNameWithoutExtension(FilePath) + extension;
                try { dialog.InitialDirectory = Path.GetDirectoryName(FilePath); } catch { }
                dialog.Filter = filter;
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private void ExportHtml()
        {
            string path = AskSavePath(".html", "HTML files (*.html)|*.html|All files (*.*)|*.*");
            if (path == null) return;
            File.WriteAllText(path, BuildStandaloneHtml(), new UTF8Encoding(false));
            if (this.viewer.Options.htmlOpenExport) Process.Start(path);
        }

        private async Task ExportPdf()
        {
            if (this.webView.CoreWebView2 == null) return;
            string path = AskSavePath(".pdf", "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*");
            if (path == null) return;

            var options = this.viewer.Options;
            var settings = this.webView.CoreWebView2.Environment.CreatePrintSettings();
            SizeF page = PdfPageSizes.Inches(options.pdfPageSize);
            settings.Orientation = options.pdfOrientation == PdfOrientation.Landscape
                ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = page.Width;
            settings.PageHeight = page.Height;
            int[] margins = options.GetMargins();
            settings.MarginLeft = margins[0] / 25.4;
            settings.MarginTop = margins[1] / 25.4;
            settings.MarginRight = margins[2] / 25.4;
            settings.MarginBottom = margins[3] / 25.4;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;

            bool ok = await this.webView.CoreWebView2.PrintToPdfAsync(path, settings);
            if (!ok)
            {
                MessageBox.Show(this, "The PDF could not be created.", Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (options.pdfOpenExport)
            {
                Process.Start(path);
            }
        }

        private void Print()
        {
            try { this.webView.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch { }
        }

        private static bool IsOutlookInstalled()
        {
            try { return Type.GetTypeFromProgID("Outlook.Application") != null; } catch { return false; }
        }

        private void SendMail(bool asHtml)
        {
            try
            {
                dynamic outlook = Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application"));
                dynamic mail = outlook.CreateItem(0); // olMailItem
                mail.Subject = Path.GetFileName(FilePath);
                if (asHtml)
                {
                    mail.BodyFormat = 2; // olFormatHTML
                    mail.HTMLBody = BuildStandaloneHtml();
                }
                else
                {
                    mail.BodyFormat = 1; // olFormatPlain
                    mail.Body = RawText;
                }
                mail.Display(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Outlook could not be started: " + ex.Message, Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { this.webView.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
