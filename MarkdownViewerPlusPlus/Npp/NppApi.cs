using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Npp
{
    /// <summary>
    /// Thin, 64-bit safe wrapper around the Notepad++ and Scintilla messages this plugin needs.
    /// (The generated ScintillaGateway uses 32-bit positions, which is wrong for Notepad++ 8.3+.)
    /// </summary>
    internal static class NppApi
    {
        private const int NPPMSG = 0x400 + 1000;
        public const int NPPM_GETCURRENTSCINTILLA = NPPMSG + 4;
        public const int NPPM_DMMSHOW = NPPMSG + 30;
        public const int NPPM_DMMHIDE = NPPMSG + 31;
        public const int NPPM_SAVECURRENTFILE = NPPMSG + 38;
        public const int NPPM_SETMENUITEMCHECK = NPPMSG + 40;
        public const int NPPM_GETPLUGINSCONFIGDIR = NPPMSG + 46;
        public const int NPPM_DOOPEN = NPPMSG + 77;
        public const int NPPM_GETEDITORDEFAULTFOREGROUNDCOLOR = NPPMSG + 90;
        public const int NPPM_GETEDITORDEFAULTBACKGROUNDCOLOR = NPPMSG + 91;
        public const int NPPM_ISDARKMODEENABLED = NPPMSG + 107;
        public const int NPPM_GETDARKMODECOLORS = NPPMSG + 108;

        private const int RUNCOMMAND_USER = 0x400 + 3000;
        public const int NPPM_GETFULLCURRENTPATH = RUNCOMMAND_USER + 1;
        public const int NPPM_GETCURRENTDIRECTORY = RUNCOMMAND_USER + 2;
        public const int NPPM_GETFILENAME = RUNCOMMAND_USER + 3;

        public const uint NPPN_FIRST = 1000;
        public const uint NPPN_READY = NPPN_FIRST + 1;
        public const uint NPPN_FILESAVED = NPPN_FIRST + 8;
        public const uint NPPN_SHUTDOWN = NPPN_FIRST + 9;
        public const uint NPPN_BUFFERACTIVATED = NPPN_FIRST + 10;
        public const uint NPPN_WORDSTYLESUPDATED = NPPN_FIRST + 12;
        public const uint NPPN_FILERENAMED = NPPN_FIRST + 17;
        public const uint NPPN_DARKMODECHANGED = NPPN_FIRST + 27;

        public const uint SCN_UPDATEUI = 2007;
        public const uint SCN_MODIFIED = 2008;
        public const int SC_MOD_INSERTTEXT = 0x1;
        public const int SC_MOD_DELETETEXT = 0x2;
        public const int SC_UPDATE_V_SCROLL = 0x4;

        private const uint SCI_GETLENGTH = 2006;
        private const uint SCI_GOTOLINE = 2024;
        private const uint SCI_GETCODEPAGE = 2137;
        private const uint SCI_GETFIRSTVISIBLELINE = 2152;
        private const uint SCI_GETLINECOUNT = 2154;
        private const uint SCI_POSITIONFROMLINE = 2167;
        private const uint SCI_VISIBLEFROMDOCLINE = 2220;
        private const uint SCI_DOCLINEFROMVISIBLE = 2221;
        private const uint SCI_ENSUREVISIBLE = 2232;
        private const uint SCI_LINESONSCREEN = 2370;
        private const uint SCI_SETFOCUS = 2380;
        private const uint SCI_GRABFOCUS = 2400;
        private const uint SCI_GETCHARACTERPOINTER = 2520;
        private const uint SCI_SETFIRSTVISIBLELINE = 2613;

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static IntPtr NppHandle => PluginBase.nppData._nppHandle;

        public static IntPtr Npp(int msg, long wParam = 0, long lParam = 0)
            => SendMessage(NppHandle, (uint)msg, new IntPtr(wParam), new IntPtr(lParam));

        public static IntPtr Npp(int msg, long wParam, IntPtr lParam)
            => SendMessage(NppHandle, (uint)msg, new IntPtr(wParam), lParam);

        public static IntPtr CurrentScintilla
        {
            get
            {
                IntPtr which = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(which, 0);
                    SendMessage(NppHandle, NPPM_GETCURRENTSCINTILLA, IntPtr.Zero, which);
                    return Marshal.ReadInt32(which) == 0 ? PluginBase.nppData._scintillaMainHandle : PluginBase.nppData._scintillaSecondHandle;
                }
                finally { Marshal.FreeHGlobal(which); }
            }
        }

        private static long Sci(uint msg, long wParam = 0, long lParam = 0)
            => SendMessage(CurrentScintilla, msg, new IntPtr(wParam), new IntPtr(lParam)).ToInt64();

        private static string GetPathString(int msg)
        {
            var sb = new StringBuilder(32768);
            SendMessage(NppHandle, (uint)msg, new IntPtr(sb.Capacity), sb);
            return sb.ToString();
        }

        public static string CurrentFilePath => GetPathString(NPPM_GETFULLCURRENTPATH);
        public static string CurrentDirectory => GetPathString(NPPM_GETCURRENTDIRECTORY);
        public static string CurrentFileName => GetPathString(NPPM_GETFILENAME);
        public static string PluginsConfigDir => GetPathString(NPPM_GETPLUGINSCONFIGDIR);

        public static bool OpenFile(string path) => SendMessage(NppHandle, NPPM_DOOPEN, IntPtr.Zero, path) != IntPtr.Zero;

        public static void SaveCurrentFile() => Npp(NPPM_SAVECURRENTFILE);

        /// <summary>Full text of the current Scintilla document, decoded with the document code page.</summary>
        public static string GetText()
        {
            long length = Sci(SCI_GETLENGTH);
            if (length <= 0) return "";
            // SCI_GETCHARACTERPOINTER gives direct, gap-free access to the buffer (no size limits)
            IntPtr ptr = new IntPtr(Sci(SCI_GETCHARACTERPOINTER));
            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, (int)length);
            int codePage = (int)Sci(SCI_GETCODEPAGE);
            Encoding enc;
            if (codePage == 65001) enc = Encoding.UTF8;
            else if (codePage == 0) enc = Encoding.Default;
            else { try { enc = Encoding.GetEncoding(codePage); } catch { enc = Encoding.UTF8; } }
            return enc.GetString(bytes);
        }

        /// <summary>0-based document line shown at the top of the editor.</summary>
        public static int FirstVisibleDocLine => (int)Sci(SCI_DOCLINEFROMVISIBLE, Sci(SCI_GETFIRSTVISIBLELINE));

        public static int LinesOnScreen => (int)Sci(SCI_LINESONSCREEN);

        public static int LineCount => (int)Sci(SCI_GETLINECOUNT);

        /// <summary>Move the caret to a line and scroll it to the top of the editor.</summary>
        public static void RevealLineAtTop(int docLine)
        {
            if (docLine < 0) return;
            Sci(SCI_ENSUREVISIBLE, docLine);
            Sci(SCI_GOTOLINE, docLine);
            Sci(SCI_SETFIRSTVISIBLELINE, Sci(SCI_VISIBLEFROMDOCLINE, docLine));
        }

        /// <summary>Scroll the editor so the given line is at the top, without moving the caret.</summary>
        public static void ScrollToLine(int docLine)
        {
            if (docLine < 0) return;
            Sci(SCI_SETFIRSTVISIBLELINE, Sci(SCI_VISIBLEFROMDOCLINE, docLine));
        }

        public static void FocusEditor()
        {
            Sci(SCI_GRABFOCUS);
            Sci(SCI_SETFOCUS, 1);
        }

        public static void SetMenuItemCheck(int cmdId, bool check) => Npp(NPPM_SETMENUITEMCHECK, cmdId, check ? 1 : 0);

        // ----- Theme -----

        private static Color FromColorRef(long c) => Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));

        public static Color EditorBackground => FromColorRef(Npp(NPPM_GETEDITORDEFAULTBACKGROUNDCOLOR).ToInt64());
        public static Color EditorForeground => FromColorRef(Npp(NPPM_GETEDITORDEFAULTFOREGROUNDCOLOR).ToInt64());

        /// <summary>Notepad++ 8.4.1+ dark mode (Settings > Preferences > Dark Mode).</summary>
        public static bool IsDarkModeEnabled
        {
            get { try { return Npp(NPPM_ISDARKMODEENABLED) != IntPtr.Zero; } catch { return false; } }
        }

        public struct DarkModeColors
        {
            public Color Background, SofterBackground, HotBackground, PureBackground, ErrorBackground,
                         Text, DarkerText, DisabledText, LinkText, Edge, HotEdge, DisabledEdge;
        }

        public static DarkModeColors? GetDarkModeColors()
        {
            const int count = 12;
            IntPtr buf = Marshal.AllocHGlobal(count * 4);
            try
            {
                if (Npp(NPPM_GETDARKMODECOLORS, count * 4, buf) == IntPtr.Zero) return null;
                Color C(int i) => FromColorRef((uint)Marshal.ReadInt32(buf, i * 4));
                return new DarkModeColors
                {
                    Background = C(0), SofterBackground = C(1), HotBackground = C(2), PureBackground = C(3),
                    ErrorBackground = C(4), Text = C(5), DarkerText = C(6), DisabledText = C(7),
                    LinkText = C(8), Edge = C(9), HotEdge = C(10), DisabledEdge = C(11)
                };
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
