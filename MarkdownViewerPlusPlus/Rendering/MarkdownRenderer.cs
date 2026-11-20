using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.IO;

namespace com.insanitydesign.MarkdownViewerPlusPlus.Rendering
{
    /// <summary>
    /// Converts Markdown to HTML with Markdig. Every block gets a <c>data-line</c> attribute with its
    /// 0-based source line, which the viewer uses to keep editor and preview in sync.
    /// </summary>
    internal class MarkdownRenderer
    {
        private readonly MarkdownPipeline pipeline;

        public MarkdownRenderer()
        {
            this.pipeline = new MarkdownPipelineBuilder()
                // Same set as UseAdvancedExtensions(), but with GitHub-style heading ids
                // so links like [Sección](#sección) work as they do on GitHub.
                .UseAlertBlocks()
                .UseAbbreviations()
                .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
                .UseCitations()
                .UseCustomContainers()
                .UseDefinitionLists()
                .UseEmphasisExtras()
                .UseFigures()
                .UseFooters()
                .UseFootnotes()
                .UseGridTables()
                .UseMathematics()
                .UseMediaLinks()
                .UsePipeTables()
                .UseListExtras()
                .UseTaskLists()
                .UseDiagrams()
                .UseAutoLinks()
                // :smile: style shortcodes -> emoji (smileys like :) are left alone, like GitHub)
                .UseEmojiAndSmiley(false)
                .UseYamlFrontMatter()
                .UseGenericAttributes()
                .Build();
        }

        /// <param name="markdown">Source text.</param>
        /// <param name="mapLocalPath">Turns an absolute local file path into a URL the viewer can load (or null).</param>
        public string ToHtml(string markdown, Func<string, string> mapLocalPath)
        {
            MarkdownDocument document = Markdown.Parse(markdown ?? "", this.pipeline);

            foreach (var node in document.Descendants())
            {
                if (node is Block block && !(block is MarkdownDocument))
                {
                    block.GetAttributes().AddPropertyIfNotExist("data-line", block.Line.ToString());
                }
                else if (node is LinkInline link && link.IsImage && mapLocalPath != null && !string.IsNullOrEmpty(link.Url))
                {
                    string localPath = TryGetAbsoluteLocalPath(link.Url);
                    if (localPath != null)
                    {
                        string mapped = mapLocalPath(localPath);
                        if (mapped != null) link.Url = mapped;
                    }
                }
            }

            using (var writer = new StringWriter())
            {
                var renderer = new HtmlRenderer(writer);
                this.pipeline.Setup(renderer);
                renderer.Render(document);
                writer.Flush();
                return writer.ToString();
            }
        }

        /// <summary>Returns the local path for "C:\x.png", "C:/x.png", "file:///C:/x.png" or "\\server\share\x.png", else null.</summary>
        internal static string TryGetAbsoluteLocalPath(string url)
        {
            try
            {
                if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    return new Uri(url).LocalPath;
                }
                if ((url.Length > 2 && char.IsLetter(url[0]) && url[1] == ':' && (url[2] == '\\' || url[2] == '/'))
                    || url.StartsWith("\\\\"))
                {
                    return Uri.UnescapeDataString(url).Replace('/', '\\');
                }
            }
            catch { }
            return null;
        }
    }
}
