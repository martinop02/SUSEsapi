using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SetupNoteParser
{
    /// <summary>
    /// The longitudinal / lateral / vertical shift values parsed out of a (human-written) field
    /// setup note. Each value is a signed magnitude in centimetres, or null when that axis was not
    /// found in the note.
    /// </summary>
    public struct NoteShift
    {
        public double? LngCm;
        public double? LatCm;
        public double? VrtCm;

        public bool Any => LngCm.HasValue || LatCm.HasValue || VrtCm.HasValue;
    }

    /// <summary>
    /// Extracts the Lng / Lat / Vrt couch-shift values from a free-text setup note.
    ///
    /// The notes are typed by hand, so the parser is deliberately forgiving. It assumes only that:
    ///   * each value sits on its own line, and
    ///   * the line starts with an axis label (Lng / Lat / Vrt and common spellings), and
    ///   * the value is the first signed number on that line.
    ///
    /// It copes with the variations seen in real notes:
    ///   "Lng: -7cm (cran)"      -> -7.0
    ///   "Lat: +7 cm (dxt)"      -> +7.0
    ///   "Vrt fra dpl: 25.1cm"   -> 25.1   (extra words between the label and the number)
    ///   "Vrt fra dpl 25.5cm"    -> 25.5   (no colon)
    ///   "Lat: -9cm (sin)"       -> -9.0
    /// Sign may be '+', '-' or the Unicode minus (U+2212); the decimal separator may be '.' or ','
    /// (Norwegian). If a value is written in millimetres ("mm") it is converted to centimetres.
    /// </summary>
    public static class NoteShiftParser
    {
        // Axis label alternations, longest spelling first so e.g. "longitudinal" wins over "long".
        // Anchored to the start of the (trimmed) line and terminated by a word boundary.
        private static readonly Regex LngLabel =
            new Regex(@"^\s*(?:longitudinal|longitud|long|lng|lon)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LatLabel =
            new Regex(@"^\s*(?:lateral|lat)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VrtLabel =
            new Regex(@"^\s*(?:vertical|vert|vrt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // First signed number on the remainder of a labelled line, with an optional cm/mm unit.
        private static readonly Regex Number =
            new Regex(@"(?<num>[+\-−]?\s*\d+(?:[.,]\d+)?)\s*(?<unit>mm|cm)?",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static NoteShift Parse(string note)
        {
            var result = new NoteShift();
            if (string.IsNullOrWhiteSpace(note)) return result;

            foreach (var rawLine in note.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (!result.LngCm.HasValue) TryAxis(line, LngLabel, ref result.LngCm);
                if (!result.LatCm.HasValue) TryAxis(line, LatLabel, ref result.LatCm);
                if (!result.VrtCm.HasValue) TryAxis(line, VrtLabel, ref result.VrtCm);
            }
            return result;
        }

        private static void TryAxis(string line, Regex label, ref double? slot)
        {
            Match lm = label.Match(line);
            if (!lm.Success) return;

            Match nm = Number.Match(line, lm.Index + lm.Length);
            if (!nm.Success) return;

            if (TryParseValue(nm.Groups["num"].Value, nm.Groups["unit"].Value, out double cm))
                slot = cm;
        }

        // Normalise a matched number token into a signed value in centimetres.
        private static bool TryParseValue(string numToken, string unit, out double cm)
        {
            cm = 0;
            string t = numToken.Replace('−', '-')   // Unicode minus -> ASCII hyphen
                               .Replace(" ", string.Empty) // drop any space between sign and digits
                               .Replace(',', '.');         // Norwegian decimal comma -> dot
            if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out double value))
                return false;

            if (string.Equals(unit, "mm", StringComparison.OrdinalIgnoreCase))
                value /= 10.0;

            cm = value;
            return true;
        }

        // Convenience for logging: "Lng = -7.0 cm   Lat = -9.0 cm   Vrt = 25.1 cm".
        public static string Describe(NoteShift s)
        {
            string F(double? v) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + " cm" : "n/a";
            return $"Lng = {F(s.LngCm)}   Lat = {F(s.LatCm)}   Vrt = {F(s.VrtCm)}";
        }
    }
}
