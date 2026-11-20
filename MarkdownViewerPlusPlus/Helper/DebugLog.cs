using System;
using System.IO;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Helper
{
    /// <summary>Optional trace file (%TEMP%\MarkdownViewerPlusPlus.log), enabled with the environment variable MDV_DEBUG=1.</summary>
    internal static class DebugLog
    {
        private static readonly bool enabled = Environment.GetEnvironmentVariable("MDV_DEBUG") == "1";

        public static void Write(string message)
        {
            if (!enabled) return;
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "MarkdownViewerPlusPlus.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine); }
            catch { }
        }
    }
}
