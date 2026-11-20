using Kbg.NppPluginNET.PluginInfrastructure;
using System.Drawing;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

/// <summary>
/// 
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus
{
    /// <summary>
    /// 
    /// </summary>
    public class MarkdownViewerConfiguration
    {
        /// <summary>
        /// 
        /// </summary>
        public struct Options
        {
            /// <summary>
            /// 
            /// </summary>
            public bool synchronizeScrolling;
            /// <summary>
            /// 
            /// </summary>
            public string fileExtensions;
            /// <summary>
            /// 
            /// </summary>
            public bool inclNewFiles;
            /// <summary>
            /// 
            /// </summary>
            private string htmlCssStyle;
            /// <summary>
            /// 
            /// </summary>
            public string HtmlCssStyle {
                get {
                    if (htmlCssStyle == null) return "";
                    return htmlCssStyle.Replace(@" \n ", Environment.NewLine);
                }
                set {
                    htmlCssStyle = value.Replace(Environment.NewLine, @" \n ");
                }
            }
            /// <summary>
            /// 
            /// </summary>
            public PdfOrientation pdfOrientation;
            /// <summary>
            /// 
            /// </summary>
            public PdfPageSize pdfPageSize;
            /// <summary>
            /// 
            /// </summary>
            public string margins;
            
            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public int[] GetMargins()
            {
                try
                {
                    return this.margins.Split(',').Select(x => int.Parse(x)).ToArray();
                }
                catch { }
                //Default to 5mm
                return new int[] { 5, 5, 5, 5 };
            }

            /// <summary>
            /// 
            /// </summary>
            public bool pdfOpenExport;

            /// <summary>
            /// 
            /// </summary>
            public bool htmlOpenExport;

            /// <summary>Use the Notepad++ editor colors (light/dark theme) for the viewer</summary>
            public bool followNppTheme;

            /// <summary>Viewer shown in its own window instead of the Notepad++ panel</summary>
            public bool undocked;

            /// <summary>"x,y,width,height" of the undocked window</summary>
            public string floatingBounds;
        }

        /// <summary>
        /// 
        /// </summary>
        protected string iniFilePath = null;

        /// <summary>
        /// 
        /// </summary>
        protected string assemblyName = "";

        /// <summary>
        /// 
        /// </summary>
        public Options options;

        /// <summary>
        /// 
        /// </summary>
        public MarkdownViewerConfiguration()
        {
            this.assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            Init();
        }

        /// <summary>
        /// Used for initing and re-initing
        /// </summary>
        public void Init()
        {
            //
            StringBuilder sbIniFilePath = new StringBuilder(Win32.MAX_PATH);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_GETPLUGINSCONFIGDIR, Win32.MAX_PATH, sbIniFilePath);
            this.iniFilePath = sbIniFilePath.ToString();

            // if config path doesn't exist, we create it
            if (!Directory.Exists(iniFilePath))
            {
                Directory.CreateDirectory(iniFilePath);
            }

            //
            this.iniFilePath = Path.Combine(iniFilePath, this.assemblyName + ".ini");
            Load();
        }

        /// <summary>
        /// Load all configuration settings
        /// </summary>
        public void Load()
        {
            //Grab ini file settings based on struct members
            this.options = GetDefaultOptions();
            //Unbox/Box magic to set structs
            object options = this.options;
            foreach (FieldInfo field in this.options.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(bool))
                {
                    bool current = (bool)field.GetValue(options);
                    field.SetValue(options, (Win32.GetPrivateProfileInt(this.assemblyName, field.Name, current ? 1 : 0, iniFilePath) != 0));
                }
                else if (field.FieldType == typeof(string))
                {
                    StringBuilder sbFieldValue = new StringBuilder(32767);
                    string current = (string)field.GetValue(options) ?? "";
                    Win32.GetPrivateProfileString(this.assemblyName, field.Name, current, sbFieldValue, 32767, iniFilePath);
                    field.SetValue(options, sbFieldValue.ToString());
                }
                else if (field.FieldType.IsEnum)
                {
                    StringBuilder sbFieldValue = new StringBuilder(Win32.MAX_PATH);
                    Win32.GetPrivateProfileString(this.assemblyName, field.Name, "", sbFieldValue, Win32.MAX_PATH, iniFilePath);
                    try
                    {
                        field.SetValue(options, Enum.Parse(field.FieldType, sbFieldValue.ToString()));
                    }
                    catch { }
                }
            }
            //Unbox/Box magic to set structs
            this.options = (Options)options;
        }

        /// <summary>
        /// Save all made changes to the configuration
        /// </summary>
        public void Save()
        {
            //Save ini file settings based on struct members
            foreach (FieldInfo field in this.options.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(this.options);
                value = value != null ? value : "";
                if (field.FieldType == typeof(bool))
                {
                    Win32.WritePrivateProfileString(this.assemblyName, field.Name, ((bool)value) ? "1" : "0", iniFilePath);
                }
                else if (field.FieldType == typeof(string) || field.FieldType.IsEnum)
                {
                    Win32.WritePrivateProfileString(this.assemblyName, field.Name, value.ToString(), iniFilePath);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileExtension"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public bool ValidateFileExtension(string fileExtension, string fileName = "")
        {
            //Nothing set -> Render all
            if (this.options.fileExtensions == null || this.options.fileExtensions == "") return true;
            //Something set but nothing given, check for "new " files (dirty dirty ^^)
            if (fileExtension == null || fileExtension == "") return this.options.inclNewFiles && fileName.StartsWith("new ");
            //Otherwise check            
            return this.options.fileExtensions.ToLower().Contains(fileExtension.ToLower());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>An Options object containing some pre-set default options</returns>
        protected Options GetDefaultOptions()
        {
            Options options = new Options()
            {
                inclNewFiles = true,
                pdfOrientation = PdfOrientation.Portrait,
                pdfPageSize = PdfPageSize.A4,
                followNppTheme = true,
                undocked = false,
                floatingBounds = "",
                synchronizeScrolling = true,
                margins = "5,5,5,5",
                pdfOpenExport = false,
                htmlOpenExport = false
            };
            return options;
        }
    }

    /// <summary>PDF page orientation (names kept compatible with the old PdfSharp settings)</summary>
    public enum PdfOrientation { Portrait, Landscape }

    /// <summary>PDF page sizes (names kept compatible with the old PdfSharp settings)</summary>
    public enum PdfPageSize { A3, A4, A5, B4, B5, Letter, Legal, Tabloid, Ledger, Executive }

    public static class PdfPageSizes
    {
        /// <summary>Page size in inches (portrait)</summary>
        public static SizeF Inches(PdfPageSize size)
        {
            const float mm = 1f / 25.4f;
            switch (size)
            {
                case PdfPageSize.A3: return new SizeF(297 * mm, 420 * mm);
                case PdfPageSize.A5: return new SizeF(148 * mm, 210 * mm);
                case PdfPageSize.B4: return new SizeF(250 * mm, 353 * mm);
                case PdfPageSize.B5: return new SizeF(176 * mm, 250 * mm);
                case PdfPageSize.Letter: return new SizeF(8.5f, 11f);
                case PdfPageSize.Legal: return new SizeF(8.5f, 14f);
                case PdfPageSize.Tabloid: return new SizeF(11f, 17f);
                case PdfPageSize.Ledger: return new SizeF(17f, 11f);
                case PdfPageSize.Executive: return new SizeF(7.25f, 10.5f);
                default: return new SizeF(210 * mm, 297 * mm);
            }
        }
    }
}
