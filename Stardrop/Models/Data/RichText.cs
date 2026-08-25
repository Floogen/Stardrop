using System;
using System.Collections.Generic;

namespace Stardrop.Models.Data
{
    /// <summary>
    /// A single run of display text. Plain runs hold one word, so that a wrapping panel can break a line in the
    /// same places a text block would.
    /// </summary>
    public class RichTextSegment
    {
        public string Text { get; set; } = String.Empty;
        /// <summary>The web address this run opens, or null when the run is plain text</summary>
        public string? Uri { get; set; }
        public bool IsLink => String.IsNullOrEmpty(Uri) is false;

        public static RichTextSegment CreateText(string text)
        {
            return new RichTextSegment() { Text = text };
        }

        public static RichTextSegment CreateLink(string text, string uri)
        {
            return new RichTextSegment() { Text = text, Uri = uri };
        }
    }

    /// <summary>
    /// One line of a parsed message. Leading whitespace becomes an indent width, as the segments are laid out by a
    /// panel rather than by a text block.
    /// </summary>
    public class RichTextLine
    {
        public double IndentWidth { get; set; }
        public List<RichTextSegment> Segments { get; set; } = new List<RichTextSegment>();
    }
}
