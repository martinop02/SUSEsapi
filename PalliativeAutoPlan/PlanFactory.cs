using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VMS.TPS.Common.Model.API;

namespace PalliativeAutoPlan
{
    /// <summary>Beam technique used for the created plans.</summary>
    public enum PlanTechnique
    {
        Vmat,        // single full VMAT arc, inverse-optimized (OptimizeVMAT)
        StaticPair,  // two open posterior fields, forward-planned hinge-angle search
        ApPaPair,    // AP/PA parallel-opposed pair (gantry 0 + 180), forward-planned weight search
    }

    /// <summary>
    /// Creates the plan + CTV + PTV for a matched prescription, and owns the MVx
    /// plan-numbering logic.
    /// </summary>
    public static class PlanFactory
    {
        private const int MaxIdLength = 16;       // Eclipse limit for plan and structure ids
        private const double PtvMarginMm = 5.0;   // 0.5 cm grown in all directions
        private const bool RunOptimizationAndDose = true;  // set false to build plans without optimizing/dosing
        private const double DefaultHingeDeg = 90.0;       // static pair angle when RunOptimizationAndDose is off
        private const string VmatOptResolution = "Normal"; // Photon Optimizer resolution: "Normal" ~2.5 mm (not "High"/fine)
        private static readonly Regex MvRegex = new Regex(@"^MV(\d+)_", RegexOptions.IgnoreCase);

        /// <summary>Highest existing MVx number across every plan in every course of the patient (0 if none).</summary>
        public static int GetHighestMvNumber(Patient patient)
        {
            int max = 0;
            foreach (Course course in patient.Courses)
                foreach (PlanSetup plan in course.PlanSetups)
                {
                    Match m = MvRegex.Match(plan.Id ?? string.Empty);
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n > max)
                        max = n;
                }
            return max;
        }

        /// <summary>
        /// Creates plan MV{mvNumber}_{name}_8 in the prescription's course, builds
        /// CTV_{name}_8 (union of the vertebrae) and PTV_{name}_8 (CTV + 5 mm), and sets
        /// the PTV as the plan target. Returns the created plan, or null if it was skipped
        /// (id too long, or no vertebra structures available). The plan is associated with
        /// the prescription by name + course only (ESAPI 18.0 cannot set a database link).
        /// </summary>
        public static PlanSetup CreatePlan(PrescriptionMatch match, StructureSet set, int mvNumber, PlanTechnique technique, Action<string> log)
        {
            string planId = $"MV{mvNumber}_{match.Name}_8";
            string ctvId = $"CTV_{match.Name}_8";
            string ptvId = $"PTV_{match.Name}_8";

            foreach (string id in new[] { planId, ctvId, ptvId })
                if (id.Length > MaxIdLength)
                {
                    log?.Invoke($"SKIPPED '{match.Name}': id '{id}' is {id.Length} chars (max {MaxIdLength}).");
                    return null;
                }

            // Collect the segmented vertebra structures for this prescription.
            var vertebrae = new List<Structure>();
            foreach (string segId in match.SegmentIds)
            {
                Structure s = set.Structures.FirstOrDefault(x => x.Id == segId);
                if (s == null || s.IsEmpty)
                {
                    log?.Invoke($"  WARNING: vertebra '{segId}' missing or empty in structure set.");
                    continue;
                }
                vertebrae.Add(s);
            }
            if (vertebrae.Count == 0)
            {
                log?.Invoke($"SKIPPED '{match.Name}': no vertebra structures available.");
                return null;
            }

            // CTV = union of all vertebrae in the prescription.
            Structure ctv = set.AddStructure("CTV", ctvId);
            SegmentVolume union = vertebrae[0].SegmentVolume;
            for (int i = 1; i < vertebrae.Count; i++)
                union = union.Or(vertebrae[i].SegmentVolume);
            ctv.SegmentVolume = union;

            // PTV = CTV grown 5 mm (0.5 cm) in all directions.
            Structure ptv = set.AddStructure("PTV", ptvId);
            ptv.SegmentVolume = ctv.SegmentVolume.Margin(PtvMarginMm);

            // Create the plan in the prescription's course and set the PTV as target.
            ExternalPlanSetup plan = match.Course.AddExternalPlanSetup(set);
            plan.Id = planId;

            var sb = new StringBuilder();
            if (!plan.SetTargetStructureIfNoDose(ptv, sb))
                log?.Invoke($"  NOTE: could not set target on '{planId}': {sb}");

            // Equivalent prescription dose, copied from the matched RTPrescription. There is no
            // database link in ESAPI 18.0, so this just gives the plan the same fractionation/dose.
            ApplyPrescriptionDose(plan, match.Prescription, log);

            // Beams + dose for the chosen technique. Wrapped so that a beam/optimization failure
            // still returns the (already-created) plan, keeping the MVx numbering in sync.
            AddBeamAndOptimize(plan, ptv, set, technique, log);

            log?.Invoke($"Created plan '{planId}' (course '{match.Course.Id}') with {ctvId} + {ptvId}.");
            return plan;
        }

        // Adds beams and produces dose according to the chosen technique. Any failure here is
        // logged but not rethrown, so the plan (already created above) is still returned and the
        // MVx numbering stays in sync.
        private static void AddBeamAndOptimize(ExternalPlanSetup plan, Structure ptv, StructureSet set, PlanTechnique technique, Action<string> log)
        {
            try
            {
                switch (technique)
                {
                    case PlanTechnique.StaticPair: BuildStaticPair(plan, ptv, set, log); break;
                    case PlanTechnique.ApPaPair:   BuildApPaPair(plan, ptv, set, log);   break;
                    default:                        BuildVmatArc(plan, ptv, set, log);    break;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("  WARNING: beam/optimization step failed: " + ex.Message);
            }
        }

        // Single full VMAT arc, inverse-optimized (OptimizeVMAT) then dosed.
        private static void BuildVmatArc(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            if (!Calculation.SetVmatModels(plan, log))
            {
                log?.Invoke("  Skipped beam/optimization (calculation models unavailable).");
                return;
            }

            // Optimizer dose-calculation resolution -> "Normal" (~2.5 mm, not fine). Sets the SRS
            // variant too, which is what governs our "SRS ARC" beams.
            Calculation.SetOptimizationResolution(plan, VmatOptResolution, log);

            // Aperture Shape Controller (after the optimization model is set): Moderate in Fast mode
            // (quicker), else Very High (higher quality).
            Calculation.SetApertureShapeController(plan, RunConfig.Fast ? Calculation.AscModerate : Calculation.AscVeryHigh, log);

            BeamBuilder.AddSingleArc(plan, ptv, log);
            int objectives = OptimizationGoals.Apply(plan, ptv, set, log);

            if (objectives > 0 && RunOptimizationAndDose)
            {
                log?.Invoke(RunConfig.Fast
                    ? "  Optimizing VMAT (fast: fewer cycles, no intermediate dose)..."
                    : "  Optimizing VMAT (this may take several minutes)...");
                Timing.Run("OptimizeVMAT", log, () =>
                {
                    if (RunConfig.Fast)
                        plan.OptimizeVMAT(Calculation.FastVmatOptions());
                    else
                        plan.OptimizeVMAT();
                });
                log?.Invoke("  Calculating final dose...");
                Timing.Run("CalculateDose", log, () => plan.CalculateDose());
                Calculation.NormalizeToTargetMean(plan, ptv, log);   // 100% of target mean
                log?.Invoke("  Optimization + dose calculation complete.");
            }
        }

        // Two open posterior fields; the hinge angle is chosen by StaticFieldOptimizer (forward
        // planning — no inverse optimizer). AAA dose model only; no optimization model.
        private static void BuildStaticPair(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            if (!Calculation.SetDoseModel(plan, log))
            {
                log?.Invoke("  Skipped static beams (dose model unavailable).");
                return;
            }

            if (RunOptimizationAndDose)
            {
                log?.Invoke("  Searching static posterior-pair hinge angle (this may take several minutes)...");
                double hinge = Timing.Run("Static hinge search", log, () => StaticFieldOptimizer.Run(plan, ptv, set, log));
                if (!double.IsNaN(hinge))
                    log?.Invoke($"  Static pair complete (hinge {hinge:0.#}°).");
            }
            else
            {
                // Build-only: a single default pair, no dose.
                VMS.TPS.Common.Model.Types.VVector iso = Isocenter.PosteriorOfCord(set, ptv, log);
                StaticFieldBuilder.AddPosteriorPair(plan, ptv, iso, DefaultHingeDeg, log);
                log?.Invoke($"  Built static pair at default hinge {DefaultHingeDeg:0.#}° (dose skipped).");
            }
        }

        // AP/PA parallel-opposed pair (gantry 0 + 180); the PA:AP weight split is chosen by
        // ApPaOptimizer (forward planning). AAA dose model only; no optimization model.
        private static void BuildApPaPair(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            if (!Calculation.SetDoseModel(plan, log))
            {
                log?.Invoke("  Skipped AP/PA beams (dose model unavailable).");
                return;
            }

            if (RunOptimizationAndDose)
            {
                log?.Invoke("  Searching AP/PA weight split (this may take several minutes)...");
                double paFraction = Timing.Run("AP/PA weight search", log, () => ApPaOptimizer.Run(plan, ptv, set, log));
                if (!double.IsNaN(paFraction))
                    log?.Invoke($"  AP/PA pair complete (PA {paFraction:0.###} / AP {1.0 - paFraction:0.###}).");
            }
            else
            {
                // Build-only: equal-weight AP/PA pair, no dose.
                StaticFieldBuilder.AddApPaPair(plan, ptv, ptv.CenterPoint, log);
                log?.Invoke("  Built AP/PA pair at equal weight (dose skipped).");
            }
        }

        // Copies the prescription's fractionation and dose-per-fraction onto the plan.
        // Uses the prescription's first target (palliative spine prescriptions have one).
        private static void ApplyPrescriptionDose(PlanSetup plan, RTPrescription rx, Action<string> log)
        {
            RTPrescriptionTarget target = rx.Targets?.FirstOrDefault();
            if (target == null)
            {
                log?.Invoke("  WARNING: prescription has no target dose; SetPrescription skipped.");
                return;
            }

            try
            {
                int fractions = Convert.ToInt32(target.NumberOfFractions);
                // treatmentPercentage is a decimal fraction (1.0 == 100%).
                plan.SetPrescription(fractions, target.DosePerFraction, 1.0);
                log?.Invoke($"  Prescription: {fractions} fx x {target.DosePerFraction}.");
            }
            catch (Exception ex)
            {
                log?.Invoke("  WARNING: could not set prescription dose: " + ex.Message);
            }
        }
    }
}
