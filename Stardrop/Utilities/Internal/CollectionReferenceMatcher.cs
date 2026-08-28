using Semver;
using Stardrop.Models.Data;
using Stardrop.Models.Nexus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Stardrop.Utilities.Internal
{
    /// <summary>
    /// Matches one end of a mod rule to a collection entry. Every identifier the reference carries has to agree
    /// rather than the first one that happens to line up, or a rule written for one mod will attach itself to
    /// another that shares a single field.
    /// </summary>
    internal static class CollectionReferenceMatcher
    {
        // A duplicate download marker is dropped off an archive name before comparing, in either the download
        // manager's own format or the one browsers use
        private static readonly Regex _duplicateSuffixPattern = new Regex(@"(?:\.\d+| \(\d+\))$", RegexOptions.Compiled);

        /// <summary>
        /// The entry a rule end points at, or -1 when nothing matches. The first match wins.
        /// </summary>
        public static int FindEntryIndex(List<CollectionModEntry> entries, CollectionModRuleReference? reference)
        {
            if (reference is null)
            {
                return -1;
            }

            var isFuzzy = IsFuzzyVersion(reference.VersionMatch);

            return entries.FindIndex(e => Matches(e, reference, isFuzzy));
        }

        private static bool Matches(CollectionModEntry entry, CollectionModRuleReference reference, bool isFuzzy)
        {
            // Without a marker that could match this entry there is nothing to go on and returning true would let
            // the rule land on whichever entry happens to be missing the same fields
            if (HasIdentifyingMarker(entry, reference, isFuzzy, true) is false)
            {
                return false;
            }

            if (String.IsNullOrEmpty(reference.Tag) is false)
            {
                if (String.Equals(entry.Tag, reference.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // The tags differ, so this can only still be the entry if something stricter says so
                if (HasIdentifyingMarker(entry, reference, isFuzzy, false) is false)
                {
                    return false;
                }
            }

            var hashMatches = String.IsNullOrEmpty(reference.FileMD5) is false && String.Equals(entry.Md5Checksum, reference.FileMD5, StringComparison.OrdinalIgnoreCase);

            // A hash pins the file outright, so it has to agree unless the reference asks for a range of versions
            if (String.IsNullOrEmpty(reference.FileMD5) is false && isFuzzy is false && hashMatches is false)
            {
                return false;
            }

            // Nothing in a collection carries repository details, which leaves a matching hash as identification
            // enough on its own
            if (hashMatches)
            {
                return true;
            }

            if (MatchesLogicalFileName(entry, reference) is false)
            {
                return false;
            }

            if (MatchesFileExpression(entry, reference) is false)
            {
                return false;
            }

            return MatchesVersion(entry, reference);
        }

        private static bool HasIdentifyingMarker(CollectionModEntry entry, CollectionModRuleReference reference, bool isFuzzy, bool allowTag)
        {
            if (isFuzzy is false && String.IsNullOrEmpty(reference.FileMD5) is false && String.IsNullOrEmpty(entry.Md5Checksum) is false)
            {
                return true;
            }

            if (String.IsNullOrEmpty(reference.FileExpression) is false && (String.IsNullOrEmpty(entry.FileExpression) is false || String.IsNullOrEmpty(entry.Name) is false))
            {
                return true;
            }

            if (String.IsNullOrEmpty(reference.LogicalFileName) is false && String.IsNullOrEmpty(entry.LogicalFilename) is false)
            {
                return true;
            }

            return allowTag && String.IsNullOrEmpty(reference.Tag) is false && String.IsNullOrEmpty(entry.Tag) is false;
        }

        private static bool MatchesLogicalFileName(CollectionModEntry entry, CollectionModRuleReference reference)
        {
            if (String.IsNullOrEmpty(reference.LogicalFileName))
            {
                return true;
            }

            if (String.Equals(entry.LogicalFilename, reference.LogicalFileName, StringComparison.OrdinalIgnoreCase) || String.Equals(entry.Name, reference.LogicalFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A file expression is allowed to carry the match on its own, so a mismatch here is not the end of it
            return String.IsNullOrEmpty(reference.FileExpression) is false;
        }

        private static bool MatchesFileExpression(CollectionModEntry entry, CollectionModRuleReference reference)
        {
            if (String.IsNullOrEmpty(reference.FileExpression))
            {
                return true;
            }

            // The comparison is against the installed archive's name. Nothing is downloaded when rules are
            // resolved, so the entry's own expression stands in for it, which is what a rule from the same
            // collection is written against anyway
            if (MatchesExpression(entry.FileExpression, reference.FileExpression))
            {
                return true;
            }

            if (MatchesExpression(SanitizeExpression(entry.SourceArchivePath), reference.FileExpression))
            {
                return true;
            }

            return String.Equals(entry.Name, reference.FileExpression, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesVersion(CollectionModEntry entry, CollectionModRuleReference reference)
        {
            if (String.IsNullOrEmpty(reference.VersionMatch) || reference.VersionMatch == "*" || String.IsNullOrEmpty(entry.Version))
            {
                return true;
            }

            var versionMatch = reference.VersionMatch.Split('+')[0];
            if (String.Equals(entry.Version, reference.VersionMatch, StringComparison.OrdinalIgnoreCase) || String.Equals(entry.Version, versionMatch, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A version that cannot be read leaves only the exact comparisons above
            if (SemVersion.TryParse(entry.Version, SemVersionStyles.Any, out var entryVersion) is false)
            {
                return false;
            }

            if (SemVersionRange.TryParseNpm(versionMatch, out var range) is false)
            {
                return false;
            }

            return entryVersion.Satisfies(range);
        }

        /// <summary>
        /// Whether a version match covers more than one version. A hash is only enforced on an exact reference, as a
        /// reference asking for a range is by definition not pinned to the file it was written against.
        /// </summary>
        private static bool IsFuzzyVersion(string? versionMatch)
        {
            if (String.IsNullOrEmpty(versionMatch))
            {
                return false;
            }

            if (versionMatch == "*" || versionMatch.EndsWith("+prefer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (SemVersion.TryParse(versionMatch, SemVersionStyles.Any, out _))
            {
                return false;
            }

            return SemVersionRange.TryParseNpm(versionMatch, out _);
        }

        private static bool MatchesExpression(string? value, string expression)
        {
            if (String.IsNullOrEmpty(value))
            {
                return false;
            }

            if (String.Equals(value, expression, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // The expression is a glob, of which only the wildcards are ever seen in practice
            if (expression.Contains('*') is false && expression.Contains('?') is false)
            {
                return false;
            }

            var pattern = "^" + Regex.Escape(expression).Replace("\\*", ".*").Replace("\\?", ".") + "$";

            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }

        private static string? SanitizeExpression(string? filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return null;
            }

            return _duplicateSuffixPattern.Replace(Path.GetFileNameWithoutExtension(filePath), String.Empty);
        }
    }
}
