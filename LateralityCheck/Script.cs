using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LateralityCheck;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// Read-only laterality check: never writes to the patient.
[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    /// <summary>
    /// LateralityCheck — a read-only sanity check that the active plan and its prescription
    /// reference the same side. It reports a single outcome plus a short reason:
    ///   OK      : neither has a laterality, or both have the same laterality.
    ///   WARNING : the two disagree, only one has a laterality, or a name is itself ambiguous
    ///             (contains both a left and a right token).
    ///
    /// A "side token" is matched only at a boundary: the start/end of the string, one of
    /// ('_', '-', ' '), or a case transition — lower/digit -> Upper, or Upper -> lower. Those rules
    /// let attached tokens match while keeping single letters safe:
    ///   "Lungs" / "Cord" / "L1_S1"  -> no match (no boundary around the letter)
    ///   "Lung_S" / "PTV-D"          -> match   (delimited)
    ///   "BreastL" / "BreastLeft"    -> match   (lower->Upper: capitalised token)
    ///   "IMNd"                      -> match   (Upper->lower: 'd' after 'N')
    ///
    ///   LEFT  : left, venstre, sin, si, l, s          (case-insensitive)
    ///   RIGHT : right, høyre, hoyre, dxt, dx, r, d     (case-insensitive)
    ///
    /// Trade-off of the Upper->lower rule: an all-caps token trailed by a lowercase side letter,
    /// e.g. "OARs"/"PTVs", resolves to LEFT (the trailing 's'). That is intended and cannot be told
    /// apart structurally from a real appended side such as "IMNd".
    ///
    /// The plan side comes from PlanSetup.Id; the prescription side is pooled from its Site, Id,
    /// Name, and each target's TargetId. If a source contains both a LEFT and a RIGHT token it is
    /// reported as AMBIGUOUS; if it contains neither, NONE.
    /// </summary>
    public class Script
    {
        public Script() { }

        // A token counts only at a boundary. The leading boundary is: start of the string, one of
        // ('_', '-', ' '), OR a case transition in either direction —
        //   lower/digit -> Upper   lets an attached token that starts with a capital match:
        //                          "BreastL" / "BreastLeft" / "LeftBreast" -> LEFT
        //   Upper -> lower         lets a lowercase token appended to an ALL-CAPS abbreviation
        //                          match: "IMNd" -> RIGHT (the 'd' after 'N').
        // The trailing boundary allows end, a delimiter, or lower/digit -> Upper only. The
        // Upper -> lower transition is deliberately NOT a trailing boundary — otherwise a leading
        // capital like the 'R' in "Rectum" would falsely close a token. Together these still reject
        // "Lungs"/"Cord" (no transition around the letter) and spine levels like "L1_S1" (letter
        // followed by a digit, not a boundary).
        //
        // Caveat of the Upper->lower rule: an all-caps token followed by a lowercase side letter is
        // read as a side, so "OARs"/"PTVs" resolve to LEFT (trailing 's' = sinister). That is the
        // intended trade-off for catching "IMNd" and cannot be distinguished structurally.
        private const string Lead  = @"(?:^|(?<=[ _\-])|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[a-z]))";
        private const string Trail = @"(?:$|(?=[ _\-])|(?<=[a-z0-9])(?=[A-Z]))";

        // The token alternation is case-insensitive via the inline (?i:...) group, but the
        // boundaries above stay case-sensitive so [A-Z]/[a-z] keep their literal meaning — hence
        // RegexOptions.IgnoreCase is deliberately NOT set globally.
        private static readonly Regex LeftRx = new Regex(
            Lead + @"(?i:venstre|left|sin|si|l|s)" + Trail,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RightRx = new Regex(
            Lead + "(?i:h\u00f8yre|hoyre|right|dxt|dx|r|d)" + Trail,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private enum Side { None, Left, Right, Ambiguous }
        private enum Status { Ok, Warning }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Laterality check", log => Run(context, log));
        }

        private void Run(ScriptContext context, Action<string> log)
        {
            log("=== LateralityCheck (read-only) ===");

            PlanSetup plan = context.PlanSetup ?? context.ExternalPlanSetup;
            if (plan == null) { log("No plan is open/selected in the context. Aborting."); return; }
            log($"Patient: {context.Patient?.Id}");
            log($"Plan:    {plan.Id}");

            RTPrescription rx = plan.RTPrescription;
            if (rx == null) { log("The plan has no linked RT prescription. Cannot compare. Aborting."); return; }

            // --- Plan side (from the plan id) ----------------------------------------------------
            Side planSide = Classify(new[] { plan.Id }, out var planHits);
            log("");
            log($"Plan side       : {planSide}   [{plan.Id}]{FormatHits(planHits)}");

            // --- Prescription side (pooled across its identifying strings) ------------------------
            var rxSources = new List<string>();
            AddSource(rxSources, "Site", rx.Site);
            AddSource(rxSources, "Id",   rx.Id);
            AddSource(rxSources, "Name", rx.Name);
            try
            {
                foreach (var t in rx.Targets ?? Enumerable.Empty<RTPrescriptionTarget>())
                    AddSource(rxSources, "Target", t?.TargetId);
            }
            catch (Exception ex) { log($"(could not read prescription targets: {ex.Message})"); }

            Side rxSide = Classify(rxSources.Select(StripLabel), out var rxHits);
            log($"Prescription    : {rxSide}   [{string.Join(", ", rxSources)}]{FormatHits(rxHits)}");

            // --- Verdict: OK or WARNING, with a short reason -------------------------------------
            var (status, note) = Evaluate(planSide, rxSide);
            log("");
            log(new string('-', 60));
            log($"{status.ToString().ToUpper()}: {note}");
        }

        // Reduce the two detected sides to a single OK/WARNING outcome plus a short reason.
        //   OK      : neither side has laterality, OR both have the same laterality.
        //   WARNING : the two sides disagree, one has laterality and the other does not, or a
        //             name is itself ambiguous (contains both a left and a right token).
        private static (Status status, string note) Evaluate(Side plan, Side rx)
        {
            if (plan == Side.Ambiguous || rx == Side.Ambiguous)
            {
                string who = plan == Side.Ambiguous && rx == Side.Ambiguous ? "plan and prescription both reference"
                           : plan == Side.Ambiguous ? "plan references"
                           : "prescription references";
                return (Status.Warning, $"{who} both left and right");
            }

            bool planHas = plan != Side.None;
            bool rxHas   = rx   != Side.None;

            if (!planHas && !rxHas)
                return (Status.Ok, "no laterality on plan or prescription");

            if (planHas != rxHas)   // exactly one side carries a laterality
                return planHas
                    ? (Status.Warning, $"plan is {Word(plan)} but prescription has no laterality")
                    : (Status.Warning, $"prescription is {Word(rx)} but plan has no laterality");

            // both carry a laterality
            return plan == rx
                ? (Status.Ok, $"plan and prescription are both {Word(plan)}")
                : (Status.Warning, $"plan is {Word(plan)} but prescription is {Word(rx)}");
        }

        private static string Word(Side s) => s.ToString().ToUpper();

        // ---- helpers ---------------------------------------------------------------------------

        // Classify a set of strings into a single Side. Any LEFT hit + any RIGHT hit => Ambiguous.
        private static Side Classify(IEnumerable<string> texts, out List<string> hits)
        {
            hits = new List<string>();
            bool left = false, right = false;
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                foreach (Match m in LeftRx.Matches(text))  { left = true;  hits.Add($"L:'{m.Value}'"); }
                foreach (Match m in RightRx.Matches(text)) { right = true; hits.Add($"R:'{m.Value}'"); }
            }
            if (left && right) return Side.Ambiguous;
            if (left)  return Side.Left;
            if (right) return Side.Right;
            return Side.None;
        }

        private static void AddSource(List<string> list, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add($"{label}={value}");
        }

        // "Label=value" -> "value" for the actual token scan.
        private static string StripLabel(string labelled)
        {
            int i = labelled.IndexOf('=');
            return i >= 0 ? labelled.Substring(i + 1) : labelled;
        }

        private static string FormatHits(List<string> hits) =>
            hits.Count == 0 ? "" : "  hits: " + string.Join(" ", hits);
    }
}
