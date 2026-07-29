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
    /// A "side token" is matched only when it appears as a delimited segment — i.e. preceded by
    /// the start of the string or one of ('_', '-', ' '), and followed by the end of the string or
    /// one of those same delimiters. That boundary rule is what makes the single-letter tokens safe:
    ///   "Lungs" / "Cord"   -> no match (no delimiter around the letter)
    ///   "Lung_S" / "PTV-D" -> match   (delimited segment)
    ///
    ///   LEFT  : left, sin, si, l, s        (case-insensitive)
    ///   RIGHT : right, dxt, dx, r, d       (case-insensitive)
    ///
    /// The plan side comes from PlanSetup.Id; the prescription side is pooled from its Site, Id,
    /// Name, and each target's TargetId. If a source contains both a LEFT and a RIGHT token it is
    /// reported as AMBIGUOUS; if it contains neither, NONE.
    /// </summary>
    public class Script
    {
        public Script() { }

        // Delimiter class used for both the leading and trailing boundary of a side token.
        private const string Delim = @"[ _\-]";

        // Longest tokens first is not required (boundaries make them unambiguous) but keeps the
        // regex readable. The trailing look-ahead / leading look-behind enforce the segment rule.
        private static readonly Regex LeftRx =
            new Regex($@"(?<=^|{Delim})(left|sin|si|l|s)(?=$|{Delim})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RightRx =
            new Regex($@"(?<=^|{Delim})(right|dxt|dx|r|d)(?=$|{Delim})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
