using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Canonical anatomy knowledge for the workflow: the cranio-caudal order of the
    /// vertebrae that can appear in a prescription, how to parse a prescription label
    /// into a vertebra range, and how to convert a prescription token to the
    /// TotalSegmentator structure id.
    ///
    /// Note the naming difference: prescriptions use "Th" for thoracic vertebrae,
    /// while TotalSegmentator uses "T" (e.g. prescription "Th9" -> "vertebrae_T9").
    /// </summary>
    public static class VertebraOrder
    {
        // Cranio-caudal order. Anything outside this list is not a recognised vertebra.
        private static readonly List<string> _order = BuildOrder();

        private static List<string> BuildOrder()
        {
            var order = new List<string>();
            for (int i = 1; i <= 7; i++) order.Add("C" + i);    // cervical
            for (int i = 1; i <= 12; i++) order.Add("Th" + i);  // thoracic (Th on prescriptions)
            for (int i = 1; i <= 5; i++) order.Add("L" + i);    // lumbar
            order.Add("S1");                                    // sacral S1
            return order;
        }

        // Leading vertebra or vertebra range at the START of the label. Internal whitespace is
        // tolerated ("L1 - L4"), and any trailing text after the vertebra part is ignored
        // ("Th9 8Gy x1" -> Th9). Real prescriptions use the full form ("C1-C7", never "C1-7"),
        // but the second region letter is accepted as optional defensively.
        //   group1/2 = first region/number,  group3/4 = optional second region/number
        private static readonly Regex _rangeRegex = new Regex(
            @"^\s*(C|Th|L|S)\s*(\d+)(?:\s*-\s*(C|Th|L|S)?\s*(\d+))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _tokenRegex = new Regex(
            @"^(C|Th|L|S)(\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Tries to interpret a prescription label as a vertebra or vertebra range.
        /// On success, <paramref name="name"/> is the canonical compact label
        /// (e.g. "L1-L4" or "Th9") and <paramref name="tokens"/> is the ordered list
        /// of vertebrae it covers (e.g. L1, L2, L3, L4). Match is anchored at the start, so
        /// trailing text in the prescription name (e.g. "L1-L4 8Gy x1") is ignored, while
        /// whitespace inside the vertebra part (e.g. "L1 - L4") is tolerated.
        /// </summary>
        public static bool TryParse(string label, out string name, out List<string> tokens)
        {
            name = null;
            tokens = null;
            if (string.IsNullOrWhiteSpace(label)) return false;

            Match m = _rangeRegex.Match(label);
            if (!m.Success) return false;

            string startToken = Normalize(m.Groups[1].Value, m.Groups[2].Value);
            if (startToken == null) return false;

            string endToken;
            if (m.Groups[4].Success)
            {
                // Second number present; if the region letter is omitted ("C1-7") reuse the first.
                string endRegion = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[1].Value;
                endToken = Normalize(endRegion, m.Groups[4].Value);
                if (endToken == null) return false;
            }
            else
            {
                endToken = startToken;
            }

            int startIdx = _order.IndexOf(startToken);
            int endIdx = _order.IndexOf(endToken);
            if (startIdx < 0 || endIdx < 0) return false;
            if (startIdx > endIdx) { int t = startIdx; startIdx = endIdx; endIdx = t; }

            tokens = _order.GetRange(startIdx, endIdx - startIdx + 1);
            name = (startToken == endToken) ? startToken : startToken + "-" + endToken;
            return true;
        }

        /// <summary>Maps a canonical token ("Th9") to its TotalSegmentator structure id ("vertebrae_T9").</summary>
        public static string ToSegmentId(string token)
        {
            Match m = _tokenRegex.Match(token);
            if (!m.Success) throw new ArgumentException("Not a vertebra token: " + token);
            string region = m.Groups[1].Value;
            string segRegion = (region == "Th") ? "T" : region;   // Th -> T for TotalSegmentator
            return "vertebrae_" + segRegion + m.Groups[2].Value;
        }

        // Normalizes region casing to canonical form and validates the token exists.
        private static string Normalize(string region, string number)
        {
            string r;
            switch (region.ToUpperInvariant())
            {
                case "C":  r = "C";  break;
                case "TH": r = "Th"; break;
                case "L":  r = "L";  break;
                case "S":  r = "S";  break;
                default: return null;
            }
            string token = r + number;
            return _order.Contains(token) ? token : null;
        }
    }
}
