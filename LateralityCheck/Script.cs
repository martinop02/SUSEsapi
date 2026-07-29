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
    /// reference the same side (both LEFT or both RIGHT).
    ///
    /// A "side token" is matched only at a boundary: the start/end of the string, one of
    /// ('_', '-', ' '), or a CamelCase transition (a lowercase/digit followed by an uppercase
    /// letter). The CamelCase rule lets attached tokens match while keeping single letters safe:
    ///   "Lungs" / "Cord" / "L1_S1"  -> no match (no boundary around the letter)
    ///   "Lung_S" / "PTV-D"          -> match   (delimited)
    ///   "BreastL" / "BreastLeft"    -> match   (CamelCase capital)
    ///
    ///   LEFT  : left, venstre, sin, si, l, s          (case-insensitive)
    ///   RIGHT : right, høyre, hoyre, dxt, dx, r, d     (case-insensitive)
    ///
    /// Note: an all-caps concatenation like "BREASTL" or a leading single capital run like
    /// "LBreast" is intentionally NOT matched — there is no lower->upper transition to anchor on.
    ///
    /// The plan side comes from PlanSetup.Id; the prescription side is pooled from its Site, Id,
    /// Name, and each target's TargetId. If a source contains both a LEFT and a RIGHT token it is
    /// reported as AMBIGUOUS; if it contains neither, NONE.
    /// </summary>
    public class Script
    {
        public Script() { }

        // A token counts only at a boundary. A boundary is: start/end of the string, one of
        // ('_', '-', ' '), OR a CamelCase transition (a lowercase/digit immediately followed by an
        // uppercase letter). The CamelCase option is what lets attached tokens match —
        // "BreastL" / "BreastLeft" / "LeftBreast" -> LEFT — while still rejecting "Lungs"/"Cord"
        // (no case transition around the letter) and spine levels like "L1_S1" (letter is followed
        // by a digit, not a boundary).
        private const string Lead  = @"(?:^|(?<=[ _\-])|(?<=[a-z0-9])(?=[A-Z]))";
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

            // --- Verdict -------------------------------------------------------------------------
            log("");
            log(new string('-', 60));
            if (planSide == Side.None || rxSide == Side.None)
            {
                log("RESULT: INCONCLUSIVE — no side token found on " +
                    (planSide == Side.None && rxSide == Side.None ? "either the plan or the prescription."
                     : planSide == Side.None ? "the plan." : "the prescription."));
            }
            else if (planSide == Side.Ambiguous || rxSide == Side.Ambiguous)
            {
                log("RESULT: AMBIGUOUS — both LEFT and RIGHT tokens found on " +
                    (planSide == Side.Ambiguous ? "the plan" : "the prescription") + ". Review manually.");
            }
            else if (planSide == rxSide)
            {
                log($"RESULT: OK — plan and prescription are both {planSide.ToString().ToUpper()}.");
            }
            else
            {
                log($"RESULT: MISMATCH — plan is {planSide.ToString().ToUpper()} but prescription is " +
                    $"{rxSide.ToString().ToUpper()}. Verify before treating.");
            }
        }

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
