using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// Read-only: this script never modifies the patient.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// CouchShiftCheck — one self-contained, read-only ESAPI script that:
    ///   1. reads the patient orientation from the image series (Image.ImagingOrientation),
    ///   2. determines the treated side from the plan id and the RT prescription (laterality),
    ///   3. parses the Lng / Lat / Vrt couch shift out of each field's free-text setup note,
    ///   4. checks that the lateral shift moves toward the treated side, given the orientation, and
    ///   5. for a breast plan (name/prescription contains breast / mamma / mam / br / bryst), checks
    ///      that the vertical shift is positive.
    ///
    /// Everything lives in this single file (the tiny log window is at the bottom). Nothing on the
    /// patient is modified.
    /// </summary>
    public class Script
    {
        public Script() { }

        // ===================================================================================
        //  Geometry convention (READ THIS before trusting the lateral check)
        // ===================================================================================
        // For Head-First-Supine, a POSITIVE lateral value in the setup note moves the patient
        // toward their RIGHT (dexter); a negative value toward their LEFT (sinister). Confirmed
        // correct against the clinic's convention (matches the sample notes' "+ (dxt)" / "- (sin)").
        // If a different machine ever uses the opposite sign, flip this one constant and the whole
        // check inverts consistently.
        private const bool PositiveLatIsPatientRightHFS = true;

        // ===================================================================================
        //  Laterality tokens & boundaries
        // ===================================================================================
        // A side token counts only at a boundary: start/end, one of ('_','-',' '), or a case
        // transition (lower/digit->Upper, e.g. "BreastL"/"BreastLeft"; or Upper->lower, e.g. the
        // 'd' in "IMNd"). Upper->lower is a LEADING boundary only, so the leading capital of
        // "Rectum" cannot close a token. This keeps "Lungs"/"Cord"/"L1_S1" as no-match.
        private const string Lead  = @"(?:^|(?<=[ _\-])|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[a-z]))";
        private const string Trail = @"(?:$|(?=[ _\-])|(?<=[a-z0-9])(?=[A-Z]))";
        private static readonly Regex LeftRx = new Regex(
            Lead + @"(?i:venstre|left|sin|si|l|s)" + Trail,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RightRx = new Regex(
            Lead + "(?i:høyre|hoyre|right|dxt|dx|r|d)" + Trail,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Breast-site detection. Distinctive full words (breast / mamma / Norwegian bryst) are safe
        // to match as substrings; the short abbreviations "br"/"mam" require a token boundary so
        // "Brain"/"Bronchus"/"Vertebra"/"Cerebrum" are not mistaken for breast.
        private static readonly Regex BreastWord = new Regex(
            @"(?i:breast|mamma|bryst)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BreastAbbr = new Regex(
            Lead + @"(?i:br|mam)" + Trail, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private enum Side { None, Left, Right, Ambiguous }
        private enum Status { Ok, Warning, Inconclusive }

        // ===================================================================================
        //  Setup-note parsing
        // ===================================================================================
        private static readonly Regex LngLabel =
            new Regex(@"^\s*(?:longitudinal|longitud|long|lng|lon)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LatLabel =
            new Regex(@"^\s*(?:lateral|lat)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VrtLabel =
            new Regex(@"^\s*(?:vertical|vert|vrt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberRx =
            new Regex(@"(?<num>[+\-−]?\s*\d+(?:[.,]\d+)?)\s*(?<unit>mm|cm)?",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ParenRx = new Regex(@"\(([^)]*)\)", RegexOptions.Compiled);

        private struct NoteShift
        {
            public double? LngCm, LatCm, VrtCm;
            public Side LatAnnotation;   // side named in the Lat line's parenthesis, if any
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Couch shift check", log => Run(context, log));
        }

        private void Run(ScriptContext context, Action<string> log)
        {
            log("=== CouchShiftCheck (read-only) ===");

            PlanSetup plan = context.PlanSetup ?? context.ExternalPlanSetup;
            if (plan == null) { log("No plan is open/selected in the context. Aborting."); return; }
            log($"Patient: {context.Patient?.Id}");
            log($"Plan:    {plan.Id}");

            // --- 1) Patient orientation from the image series -----------------------------------
            Image image = context.Image ?? plan.StructureSet?.Image;
            PatientOrientation? orient = null;
            if (image != null)
            {
                orient = Try(() => image.ImagingOrientation);
                log($"Image:   {image.Id}   (series {Try(() => image.Series?.UID)})");
                log($"Patient orientation (image): {orient?.ToString() ?? "unknown"}");
                var txOrient = Try(() => (PatientOrientation?)plan.TreatmentOrientation);
                if (txOrient.HasValue && orient.HasValue && txOrient.Value != orient.Value)
                    log($"  NOTE: plan treatment orientation ({txOrient}) differs from the image orientation.");
            }
            else
            {
                log("Patient orientation (image): no image available.");
            }

            // --- 2) Laterality: plan vs prescription --------------------------------------------
            Side planSide = Classify(new[] { plan.Id });
            RTPrescription rx = plan.RTPrescription;
            var rxSources = new List<string>();
            if (rx != null)
            {
                Add(rxSources, rx.Site); Add(rxSources, rx.Id); Add(rxSources, rx.Name);
                foreach (var t in Try(() => rx.Targets) ?? Enumerable.Empty<RTPrescriptionTarget>())
                    Add(rxSources, Try(() => t?.TargetId));
            }
            Side rxSide = Classify(rxSources);

            log("");
            log($"Plan side       : {planSide}   [{plan.Id}]");
            log($"Prescription    : {(rx == null ? "no prescription" : rxSide.ToString())}   [{string.Join(", ", rxSources)}]");
            var (latStatus, latNote) = EvaluateLaterality(planSide, rxSide);
            log($"Laterality       -> {latStatus.ToString().ToUpper()}: {latNote}");

            // Side actually treated, for the shift check: prefer the plan, fall back to the rx.
            Side treatedSide = planSide != Side.None ? planSide : rxSide;

            // Breast site? (from plan id + prescription strings). If so we can also require Vrt > 0.
            var siteTexts = new List<string> { plan.Id };
            siteTexts.AddRange(rxSources);
            bool breast = IsBreast(siteTexts);
            log($"Site            : {(breast ? "BREAST detected (vertical shift expected positive)" : "breast not detected")}");

            // --- 3 & 4) Setup notes: parse shifts and check the lateral direction ---------------
            var beams = (plan.Beams ?? Enumerable.Empty<Beam>())
                        .OrderByDescending(b => b.IsSetupField).ToList();
            int notes = 0;
            foreach (var beam in beams)
            {
                string note = Try(() => beam.SetupNote);
                if (string.IsNullOrWhiteSpace(note)) continue;
                notes++;

                log("");
                log(new string('=', 72));
                log($"Field [{beam.Id}]   (setup field: {Try(() => beam.IsSetupField.ToString())})");
                log(new string('-', 72));
                foreach (var line in note.Replace("\r", "").Split('\n')) log("   | " + line);

                NoteShift s = ParseNote(note);
                log("parsed shift:  " + Describe(s));

                var (shiftStatus, shiftNote) = CheckLateralDirection(s, treatedSide, orient);
                log($"lateral check  -> {shiftStatus.ToString().ToUpper()}: {shiftNote}");

                var (vertStatus, vertNote) = CheckVertical(s, breast);
                log($"vertical check -> {vertStatus.ToString().ToUpper()}: {vertNote}");
            }

            log("");
            if (notes == 0) log("No setup notes found on any field; shift check skipped.");
            log(new string('-', 72));
            log("Reminder: the lateral check assumes a near-midline setup reference and the sign");
            log("convention documented at the top of the script. Verify both for your workflow.");
        }

        // ===================================================================================
        //  Lateral-direction check (the core new logic)
        // ===================================================================================
        private static (Status, string) CheckLateralDirection(NoteShift s, Side treated, PatientOrientation? orient)
        {
            if (treated == Side.None)      return (Status.Ok, "plan is not lateralised — lateral direction not checked.");
            if (treated == Side.Ambiguous) return (Status.Inconclusive, "treated side is ambiguous — cannot check.");
            if (!s.LatCm.HasValue)         return (Status.Inconclusive, "no lateral (Lat) value found in the note.");
            if (!orient.HasValue)          return (Status.Inconclusive, "patient orientation unknown — cannot map sign to side.");
            if (!IsLeftRightSupported(orient.Value))
                return (Status.Inconclusive, $"orientation {orient} has no simple left/right couch axis — not checked.");

            double lat = s.LatCm.Value;
            if (Math.Abs(lat) < 1e-9)
                return (Status.Warning, $"lateral shift is 0 cm but the plan is {treated.ToString().ToUpper()}.");

            Side implied = ImpliedSide(lat, orient.Value);
            string dir = $"{(lat > 0 ? "+" : "")}{lat.ToString("0.0", CultureInfo.InvariantCulture)} cm";
            string move = $"Lat {dir} moves toward patient {implied.ToString().ToUpper()} ({orient})";

            // The note may also state the intended side in parentheses, e.g. "(sin)"/"(dxt)". If that
            // disagrees with what sign+orientation imply, flag it — either the sign was mistyped or
            // the documented convention does not match this note.
            bool annotationDisagrees = (s.LatAnnotation == Side.Left || s.LatAnnotation == Side.Right)
                                       && s.LatAnnotation != implied;
            string annNote = annotationDisagrees
                ? $" The note annotates '{s.LatAnnotation.ToString().ToLower()}', which disagrees — check the sign convention."
                : "";

            if (implied == treated && !annotationDisagrees)
                return (Status.Ok, $"{move}, matching the {treated.ToString().ToUpper()} plan.");

            if (implied != treated)
                return (Status.Warning, $"{move} but the plan is {treated.ToString().ToUpper()}.{annNote}");

            // implied == treated but the note's own annotation contradicts it.
            return (Status.Warning, $"{move}, matching the {treated.ToString().ToUpper()} plan, but{annNote}");
        }

        // For a breast plan the vertical couch shift is expected to be positive (the sample notes
        // all read "Vrt fra dpl: <positive> cm"). Only checked when the site looks like breast.
        private static (Status, string) CheckVertical(NoteShift s, bool breast)
        {
            if (!breast)           return (Status.Ok, "not a breast plan — vertical sign not checked.");
            if (!s.VrtCm.HasValue) return (Status.Inconclusive, "no vertical (Vrt) value found in the note.");
            double v = s.VrtCm.Value;
            string val = $"{(v > 0 ? "+" : "")}{v.ToString("0.0", CultureInfo.InvariantCulture)} cm";
            return v > 0
                ? (Status.Ok, $"Vrt {val} is positive, as expected for a breast plan.")
                : (Status.Warning, $"Vrt {val} is not positive; a breast plan is expected to be positive.");
        }

        private static bool IsBreast(IEnumerable<string> texts)
        {
            foreach (var t in texts)
                if (!string.IsNullOrWhiteSpace(t) && (BreastWord.IsMatch(t) || BreastAbbr.IsMatch(t)))
                    return true;
            return false;
        }

        // Left/right only makes sense as a couch lateral axis for supine/prone head/feet-first.
        private static bool IsLeftRightSupported(PatientOrientation o)
        {
            string n = o.ToString();
            return !(n.Contains("Decubitus") || o == PatientOrientation.Sitting || o == PatientOrientation.NoOrientation);
        }

        // Which anatomical side a signed lateral value points to, for a supported orientation.
        private static Side ImpliedSide(double latCm, PatientOrientation o)
        {
            bool flipped = LeftRightFlippedVsHFS(o);
            bool positiveIsRight = PositiveLatIsPatientRightHFS ^ flipped;
            bool isRight = (latCm > 0) == positiveIsRight;
            return isRight ? Side.Right : Side.Left;
        }

        // Each of {prone, feet-first} inverts the patient's left/right against the fixed couch
        // lateral axis relative to Head-First-Supine; two inversions cancel.
        private static bool LeftRightFlippedVsHFS(PatientOrientation o)
        {
            string n = o.ToString();
            bool prone = n.Contains("Prone");
            bool feetFirst = n.StartsWith("FeetFirst");
            return prone ^ feetFirst;
        }

        // ===================================================================================
        //  Setup-note parsing
        // ===================================================================================
        private static NoteShift ParseNote(string note)
        {
            var r = new NoteShift { LatAnnotation = Side.None };
            if (string.IsNullOrWhiteSpace(note)) return r;

            foreach (var raw in note.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (!r.LngCm.HasValue) r.LngCm = ValueOnLabelledLine(line, LngLabel);
                if (!r.VrtCm.HasValue) r.VrtCm = ValueOnLabelledLine(line, VrtLabel);
                if (!r.LatCm.HasValue)
                {
                    Match lm = LatLabel.Match(line);
                    if (lm.Success)
                    {
                        r.LatCm = ValueAfter(line, lm.Index + lm.Length);
                        // The anatomical hint typically sits in parentheses, e.g. "(sin)"/"(dxt)".
                        Match p = ParenRx.Match(line);
                        if (p.Success) r.LatAnnotation = Classify(new[] { p.Groups[1].Value });
                    }
                }
            }
            return r;
        }

        private static double? ValueOnLabelledLine(string line, Regex label)
        {
            Match lm = label.Match(line);
            return lm.Success ? ValueAfter(line, lm.Index + lm.Length) : (double?)null;
        }

        private static double? ValueAfter(string line, int start)
        {
            Match nm = NumberRx.Match(line, start);
            if (!nm.Success) return null;
            string t = nm.Groups["num"].Value.Replace('−', '-').Replace(" ", string.Empty).Replace(',', '.');
            if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out double v)) return null;
            if (string.Equals(nm.Groups["unit"].Value, "mm", StringComparison.OrdinalIgnoreCase)) v /= 10.0;
            return v;
        }

        private static string Describe(NoteShift s)
        {
            string F(double? v) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + " cm" : "n/a";
            string ann = s.LatAnnotation == Side.None ? "" : $" (Lat note: {s.LatAnnotation.ToString().ToLower()})";
            return $"Lng = {F(s.LngCm)}   Lat = {F(s.LatCm)}   Vrt = {F(s.VrtCm)}{ann}";
        }

        // ===================================================================================
        //  Laterality helpers
        // ===================================================================================
        private static Side Classify(IEnumerable<string> texts)
        {
            bool left = false, right = false;
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (LeftRx.IsMatch(text)) left = true;
                if (RightRx.IsMatch(text)) right = true;
            }
            if (left && right) return Side.Ambiguous;
            if (left) return Side.Left;
            if (right) return Side.Right;
            return Side.None;
        }

        private static (Status, string) EvaluateLaterality(Side plan, Side rx)
        {
            if (plan == Side.Ambiguous || rx == Side.Ambiguous)
            {
                string who = plan == Side.Ambiguous && rx == Side.Ambiguous ? "plan and prescription both reference"
                           : plan == Side.Ambiguous ? "plan references" : "prescription references";
                return (Status.Warning, $"{who} both left and right.");
            }
            bool ph = plan != Side.None, rh = rx != Side.None;
            if (!ph && !rh) return (Status.Ok, "no laterality on plan or prescription.");
            if (ph != rh)
                return ph ? (Status.Warning, $"plan is {plan.ToString().ToUpper()} but prescription has no laterality.")
                          : (Status.Warning, $"prescription is {rx.ToString().ToUpper()} but plan has no laterality.");
            return plan == rx ? (Status.Ok, $"plan and prescription are both {plan.ToString().ToUpper()}.")
                              : (Status.Warning, $"plan is {plan.ToString().ToUpper()} but prescription is {rx.ToString().ToUpper()}.");
        }

        private static void Add(List<string> list, string v) { if (!string.IsNullOrWhiteSpace(v)) list.Add(v); }

        private static T Try<T>(Func<T> f) { try { return f(); } catch { return default(T); } }
    }

    /// <summary>Minimal, code-only log window shown when the script finishes.</summary>
    public static class LogWindow
    {
        public static void Run(string title, Action<Action<string>> body)
        {
            var sb = new StringBuilder();
            Action<string> log = line => { sb.AppendLine(line ?? string.Empty); System.Diagnostics.Trace.WriteLine(line); };
            try { body(log); }
            catch (Exception ex) { log(""); log("UNHANDLED ERROR:"); log(ex.ToString()); }
            Show(title, sb.ToString());
        }

        private static void Show(string title, string text)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, BorderThickness = new Thickness(0), Padding = new Thickness(8)
            };
            new Window { Title = title, Width = 860, Height = 600, Content = box,
                         WindowStartupLocation = WindowStartupLocation.CenterScreen }.ShowDialog();
        }
    }
}
