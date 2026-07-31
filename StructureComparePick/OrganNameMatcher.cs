using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StructureComparePick
{
    /// <summary>
    /// Reduces a structure name to a canonical "match key" so structures that denote the same organ
    /// match even when the names differ. Two structures are the same organ iff their keys are equal
    /// (and non-null). Designed to be as robust as practical while avoiding false matches.
    ///
    /// Rules (all case-insensitive):
    ///   • Separators &amp; casing — underscores, hyphens, spaces, dots and PascalCase/camelCase all
    ///     tokenize the same way, so <c>SpinalCord</c> = <c>spinal_cord</c> = <c>Spinal-Cord</c>.
    ///   • Laterality — a left/right token is stripped and recorded as a side, so the base organ
    ///     matches only same-side. Left: l, lt, left, sin, sinister, sinistra. Right: r, rt, right,
    ///     dx, dxt, dexter, dextra. So <c>Parotid_L</c> = <c>parotid_left</c> = <c>ParotidL</c> =
    ///     <c>parotid_sin</c>, but never matches <c>Parotid_R</c>.
    ///   • Vertebrae — a region+number token (C/T/Th/L/S + 1–2 digits) becomes a spine label, with
    ///     Th→T and leading zeros dropped, and the filler words vertebra(e)/vert/corpus ignored. So
    ///     <c>Th11</c> = <c>T11</c>, and <c>vertebrae_s1</c> = <c>S1</c>. Crucially <c>L1</c>/<c>L5</c>
    ///     read as lumbar vertebrae, not as a left-side marker.
    ///
    /// Known limits (documented, deliberately conservative): laterality glued on with no separator
    /// or case change (e.g. <c>parotidl</c>) is NOT detected; a leading <c>D</c> (dorsal) is not
    /// treated as thoracic. Both can be added to the tables below if a site needs them.
    /// </summary>
    public static class OrganNameMatcher
    {
        private static readonly HashSet<string> Left = new HashSet<string>(StringComparer.Ordinal)
        { "l", "lt", "left", "sin", "sinister", "sinistra", "sinistr" };

        private static readonly HashSet<string> Right = new HashSet<string>(StringComparer.Ordinal)
        { "r", "rt", "right", "dx", "dxt", "dext", "dexter", "dextra" };

        // Words that carry no anatomical meaning next to a vertebra label.
        private static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.Ordinal)
        { "vertebra", "vertebrae", "vert", "corpus" };

        // A spine label token: region letter(s) + 1–2 digits. "th" collapses to "t" (thoracic).
        private static readonly Regex VertRe = new Regex(@"^(th|c|t|l|s)(\d{1,2})$", RegexOptions.Compiled);
        private static readonly Regex SepRe = new Regex(@"[\s_\-\./]+", RegexOptions.Compiled);
        // Boundary between a lower-case/digit and an upper-case letter (camelCase / PascalCase).
        private static readonly Regex CamelRe = new Regex(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

        /// <summary>
        /// Returns the canonical match key for a structure name, or null when the name yields no
        /// usable token (empty / only filler). Names with equal non-null keys are the same organ.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // Separators -> spaces, then split camelCase, then tokenize + lowercase + drop filler.
            string spaced = CamelRe.Replace(SepRe.Replace(name.Trim(), " "), " ");
            List<string> tokens = spaced
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant())
                .Where(t => !Filler.Contains(t))
                .ToList();
            if (tokens.Count == 0) return null;

            string side = null;
            var baseTokens = new List<string>();
            var vertebrae = new List<string>();

            foreach (string t in tokens)
            {
                Match m = VertRe.Match(t);
                if (m.Success)
                {
                    string region = m.Groups[1].Value;
                    if (region == "th") region = "t";
                    int num = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); // drops leading zeros
                    vertebrae.Add(region + num.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                if (Left.Contains(t)) { side = side ?? "L"; continue; }
                if (Right.Contains(t)) { side = side ?? "R"; continue; }
                baseTokens.Add(t);
            }

            // A pure vertebra label (only a spine token, nothing else) keys on the spine label alone.
            if (vertebrae.Count == 1 && baseTokens.Count == 0)
                return "v:" + vertebrae[0];

            // Otherwise treat any vertebra tokens as ordinary base words (so a decorated name only
            // matches a similarly decorated one, never a bare vertebra).
            baseTokens.AddRange(vertebrae);
            if (baseTokens.Count == 0) return null;

            string key = string.Concat(baseTokens);
            if (side != null) key += "|" + side;
            return key;
        }
    }
}
