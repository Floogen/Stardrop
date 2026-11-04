using Stardrop.Models.Data;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Stardrop.Utilities.Internal
{
    /// <summary>
    /// Turns free text into lines of display segments, marking anything that resolves to a web address as a link.
    /// Curator supplied text arrives as either plain text or light markdown, so bare addresses and [label](url)
    /// pairs are both handled.
    /// </summary>
    internal static partial class HyperlinkParser
    {
        //private static readonly Regex _markdownLinkPattern = new(@"\[((?:\\.|[^\]\\])*)\]\(\s*(\S+?)\s*\)", RegexOptions.Compiled);
        //private static readonly Regex _bareLinkPattern = new(@"(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        //private static readonly Regex _wordPattern = new(@"\S+", RegexOptions.Compiled);
        //private static readonly Regex _escapeSequencePattern = new(@"\\(.)", RegexOptions.Compiled);
        [GeneratedRegex(@"\[((?:\\.|[^\]\\])*)\]\(\s*(\S+?)\s*\)", RegexOptions.Compiled)]
        private static partial Regex MarkdownLinkPattern();
        [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
        private static partial Regex BareLinkPattern();
        [GeneratedRegex(@"\S+", RegexOptions.Compiled)]
        private static partial Regex WordPattern();
        [GeneratedRegex(@"\\(.)", RegexOptions.Compiled)]
        private static partial Regex EscapeSequencePattern();
        private static readonly char[] _lineWhitespace = [' ', '\t'];
        private static readonly char[] _trailingPunctuation = ['.', ',', ';', ':', '!', '?', '"', '\'', ')', ']', '}', '>'];

        /// <summary>Width given to each leading space, so indented list entries keep their shape</summary>
        private const double _indentWidthPerSpace = 4;
        /// <summary>
        /// Segments are laid out side by side rather than as flowing text, so the gaps between words are carried by
        /// the words themselves. A non-breaking space is used because a trailing plain space is not measured.
        /// </summary>
        private const string _spacePlaceholder = "\u00A0";

        /// <summary>A web address found within a line, along with where it sits so the words around it keep their spacing</summary>
        private sealed record LinkMatch(int Start, int End, string Label, string Uri);

        /// <summary>
        /// Splits text into lines of segments. Lines are kept as their own objects rather than being flattened, as
        /// the message relies on its line breaks to separate the summary from the lists below it.
        /// </summary>
        public static List<RichTextLine> Parse(string? text)
        {
            var lines = new List<RichTextLine>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                lines.Add(ParseLine(rawLine));
            }

            return lines;
        }

        /// <summary>
        /// Builds a markdown style link for text that will later be given to <see cref="Parse"/>. Falls back to the
        /// escaped label on its own when there is no usable address, so callers do not have to check first.
        /// </summary>
        public static string CreateLink(string? label, string? uri)
        {
            var escapedLabel = Escape(label);
            if (Toolkit.TryGetWebAddress(uri, out var webAddress) is false)
            {
                return escapedLabel;
            }

            return $"[{escapedLabel}]({webAddress})";
        }

        /// <summary>
        /// Protects text that is not meant to be parsed, such as a mod name that happens to contain brackets.
        /// </summary>
        public static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
        }

        private static RichTextLine ParseLine(string rawLine)
        {
            var content = rawLine.TrimStart(_lineWhitespace);
            var line = new RichTextLine() { IndentWidth = (rawLine.Length - content.Length) * _indentWidthPerSpace };

            // A line with no segments would collapse to no height, taking the blank line separating each list with it
            if (string.IsNullOrWhiteSpace(content))
            {
                line.Segments.Add(RichTextSegment.CreateText(_spacePlaceholder));
                return line;
            }

            int index = 0;
            foreach (var link in FindLinks(content))
            {
                AddWords(line, content, index, link.Start);
                line.Segments.Add(RichTextSegment.CreateLink(link.Label, link.Uri));

                // Carried as its own segment rather than as part of the label, to keep the underline off the gap
                var spacing = GetSpacing(content, link.End);
                if (string.IsNullOrEmpty(spacing) is false)
                {
                    line.Segments.Add(RichTextSegment.CreateText(spacing));
                }

                index = link.End;
            }

            AddWords(line, content, index, content.Length);
            return line;
        }

        /// <summary>
        /// Locates every address in a line, in the order they appear. Markdown links are matched first, with bare
        /// addresses only looked for in the gaps between them so that a link's own address is not matched twice.
        /// </summary>
        private static List<LinkMatch> FindLinks(string content)
        {
            var links = new List<LinkMatch>();
            int index = 0;

            foreach (Match match in MarkdownLinkPattern().Matches(content))
            {
                var label = Unescape(match.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(label) || Toolkit.TryGetWebAddress(match.Groups[2].Value, out var webAddress) is false)
                {
                    continue;
                }

                links.AddRange(FindBareLinks(content, index, match.Index));
                links.Add(new LinkMatch(match.Index, match.Index + match.Length, label, webAddress));
                index = match.Index + match.Length;
            }

            links.AddRange(FindBareLinks(content, index, content.Length));
            return links;
        }

        private static List<LinkMatch> FindBareLinks(string content, int start, int end)
        {
            var links = new List<LinkMatch>();
            if (end <= start)
            {
                return links;
            }

            foreach (Match match in BareLinkPattern().Matches(content.Substring(start, end - start)))
            {
                // Sentence punctuation sitting against an address belongs to the sentence rather than to the address
                var candidate = match.Value.TrimEnd(_trailingPunctuation);
                if (Toolkit.TryGetWebAddress(candidate, out var webAddress) is false)
                {
                    continue;
                }

                var linkStart = start + match.Index;
                links.Add(new LinkMatch(linkStart, linkStart + candidate.Length, candidate, webAddress));
            }

            return links;
        }

        private static void AddWords(RichTextLine line, string content, int start, int end)
        {
            if (end <= start)
            {
                return;
            }

            foreach (Match match in WordPattern().Matches(content.Substring(start, end - start)))
            {
                var wordEnd = start + match.Index + match.Length;
                line.Segments.Add(RichTextSegment.CreateText(Unescape(match.Value) + GetSpacing(content, wordEnd)));
            }
        }

        private static string GetSpacing(string content, int index)
        {
            if (index < content.Length && char.IsWhiteSpace(content[index]))
            {
                return _spacePlaceholder;
            }

            return string.Empty;
        }

        private static string Unescape(string text)
        {
            return EscapeSequencePattern().Replace(text, "$1");
        }
    }
}
