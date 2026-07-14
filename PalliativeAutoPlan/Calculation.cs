using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>Sets the VMAT optimization and final-dose calculation models on a plan.</summary>
    public static class Calculation
    {
        // Site-configured algorithm models. The "16.1.0" is the configured algorithm/beam-data
        // version and is independent of the (18.0) Eclipse version.
        private const string OptimizationModel = "PO_16.1.0";    // Photon Optimizer (VMAT)
        private const string DoseModel = "AAA_16.1.0";           // Anisotropic Analytical Algorithm

        // AAA calculation-grid-size option (cm) — controls the FINAL dose grid. Used by the static /
        // AP-PA optimizers (coarse search grid, fine final grid). Separate from the OPTIMIZER's
        // dose-calculation resolution below.
        private const string DoseGridSizeOption = "CalculationGridSizeInCM";

        // --- Photon Optimizer dose-calculation resolution (exact keys from CalcOptionInspector) ---
        // "Normal" ~ 2.5 mm, "High" ~ fine. IMPORTANT: our beams use the "SRS ARC" technique, so the
        // optimizer honours the SRS/HyperArc variant — the regular key alone has no effect on these
        // plans. Both are set so the behaviour is correct regardless of how Eclipse classifies the arc.
        private const string OptResolutionOption = "General/OptimizerSettings/DoseCalculationResolution";
        private const string OptResolutionSrsOption = "General/OptimizerSettings/DoseCalculationResolutionForSRSAndHyperarc";

        /// <summary>
        /// Sets the photon VMAT optimization model and the photon volume-dose model. Returns
        /// false (and logs the available models) if either named model is not configured, so the
        /// caller can skip optimization instead of throwing.
        /// </summary>
        public static bool SetVmatModels(ExternalPlanSetup plan, Action<string> log)
        {
            // Non-short-circuit '&' so both are checked and both report problems.
            bool ok = SetModel(plan, CalculationType.PhotonVMATOptimization, OptimizationModel, log)
                    & SetModel(plan, CalculationType.PhotonVolumeDose, DoseModel, log);
            return ok;
        }

        /// <summary>
        /// Sets only the photon volume-dose (AAA) model — the static-field technique is forward
        /// planned, so it needs no optimization model. Returns false (and logs) if AAA is not
        /// configured, so the caller can skip dose calculation instead of throwing.
        /// </summary>
        public static bool SetDoseModel(ExternalPlanSetup plan, Action<string> log)
        {
            return SetModel(plan, CalculationType.PhotonVolumeDose, DoseModel, log);
        }

        /// <summary>
        /// Sets the AAA dose-calculation grid size (cm) via SetCalculationOption, verifying the
        /// change by reading it back. The static optimizer searches on a coarse grid and recomputes
        /// the winner on a fine grid. No-op with a warning if the option is rejected.
        /// </summary>
        public static void SetDoseGridSize(ExternalPlanSetup plan, string gridSizeCm, Action<string> log)
        {
            try
            {
                plan.SetCalculationOption(DoseModel, DoseGridSizeOption, gridSizeCm);
            }
            catch (Exception ex)
            {
                log?.Invoke($"  WARNING: could not set dose grid to {gridSizeCm} cm ({ex.Message}).");
                return;
            }

            string current;
            if (plan.GetCalculationOption(DoseModel, DoseGridSizeOption, out current))
                log?.Invoke($"  Dose grid size = {current} cm.");
        }

        /// <summary>
        /// Sets the Photon Optimizer dose-calculation resolution to <paramref name="value"/>
        /// ("Normal" ~ 2.5 mm, "High" ~ fine) for BOTH the regular and the SRS/HyperArc setting,
        /// verifying each by reading it back. The SRS variant is the one that actually governs our
        /// "SRS ARC" beams. No-op (with a log) for any key the config rejects.
        /// </summary>
        public static void SetOptimizationResolution(ExternalPlanSetup plan, string value, Action<string> log)
        {
            foreach (string opt in new[] { OptResolutionOption, OptResolutionSrsOption })
            {
                try
                {
                    plan.SetCalculationOption(OptimizationModel, opt, value);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"  Resolution: '{opt}' = '{value}' REJECTED ({ex.Message}).");
                    continue;
                }

                // Read back and verify it actually changed — a value the config doesn't accept can be
                // silently ignored (leaving it at e.g. 'High'/fine), so flag any mismatch loudly.
                string current;
                if (plan.GetCalculationOption(OptimizationModel, opt, out current))
                {
                    if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                        log?.Invoke($"  {opt} = {current}.");
                    else
                        log?.Invoke($"  WARNING: {opt} is '{current}', NOT '{value}' — value not accepted "
                                  + "(check the valid spelling for this key with CalcOptionInspector).");
                }
                else
                {
                    log?.Invoke($"  WARNING: could not read back '{opt}' to confirm it is '{value}'.");
                }
            }
        }

        // --- Fast VMAT optimization ---
        private const int FastOptimizationCycles = 2;   // fewer cycles than the default = faster

        /// <summary>
        /// OptimizationOptionsVMAT for a FAST run: a small number of cycles plus no intermediate
        /// dose (the intermediate-dose calc between cycles is usually the biggest time sink). Trades
        /// plan quality for speed — acceptable for palliative. NOTE: if the build reports
        /// IntermediateDoseOption is read-only in this ESAPI version, delete that line (you still get
        /// the fewer-cycles speedup) or switch to the single-cycle ctor
        /// new OptimizationOptionsVMAT(OptimizationIntermediateDoseOption.NoIntermediateDose, "").
        /// </summary>
        public static OptimizationOptionsVMAT FastVmatOptions()
        {
            var opts = new OptimizationOptionsVMAT(FastOptimizationCycles, "");   // "" = the single configured MLC
            return opts;
        }

        // --- Aperture Shape Controller (ASC) ---
        // Full option key from CalcOptionInspector (config default is "Off").
        private const string ApertureShapeControllerOption = "VMAT/ApertureShapeController";

        // Candidate value spellings per level (the exact stored spelling is not documented, so we
        // try each until one is accepted, verified by reading the option back).
        public static readonly string[] AscVeryHigh = { "Very High", "VeryHigh", "very high", "veryhigh" };
        public static readonly string[] AscModerate = { "Moderate", "moderate" };

        /// <summary>
        /// Sets the Aperture Shape Controller on the VMAT optimization model to the first accepted
        /// spelling in <paramref name="candidateValues"/> (e.g. <see cref="AscVeryHigh"/> or
        /// <see cref="AscModerate"/>). The optimization model must already be set (call SetVmatModels
        /// first). Verifies the change by reading it back (a rejected value leaves it 'Off') and logs.
        /// </summary>
        public static void SetApertureShapeController(ExternalPlanSetup plan, string[] candidateValues, Action<string> log)
        {
            foreach (string value in candidateValues)
            {
                try
                {
                    plan.SetCalculationOption(OptimizationModel, ApertureShapeControllerOption, value);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"  ASC: value '{value}' rejected ({ex.Message}).");
                    continue;
                }

                // Confirm it actually took (an invalid value typically leaves it at 'Off').
                string current;
                bool read = plan.GetCalculationOption(OptimizationModel, ApertureShapeControllerOption, out current);
                if (read && !string.IsNullOrEmpty(current) && !current.Equals("Off", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke($"  Aperture Shape Controller = '{current}' (set via '{value}').");
                    return;
                }

                log?.Invoke($"  ASC: value '{value}' did not take (now '{current}').");
            }

            log?.Invoke("  WARNING: Aperture Shape Controller not set — no candidate value was "
                      + "accepted. Verify the option name/value with CalcOptionInspector.");
        }

        /// <summary>
        /// Normalizes the plan so the target mean dose becomes 100% of the prescription. ESAPI
        /// cannot select the "Target Mean" normalization MODE (setting PlanNormalizationValue forces
        /// the "Plan Normalization Value" method), but the value computed here gives the identical
        /// dosimetric result. Requires the dose to be calculated and a prescription to be set.
        /// </summary>
        public static void NormalizeToTargetMean(ExternalPlanSetup plan, Structure ptv, Action<string> log)
        {
            DVHData dvh = plan.GetDVHCumulativeData(ptv, DoseValuePresentation.Absolute, VolumePresentation.Relative, 0.1);
            if (dvh == null)
            {
                log?.Invoke("  Normalization skipped: no DVH (is the dose calculated?).");
                return;
            }

            double meanDose = dvh.MeanDose.Dose;
            double prescDose = plan.TotalDose.Dose;
            if (meanDose <= 0 || prescDose <= 0)
            {
                log?.Invoke("  Normalization skipped: target mean or prescription dose unavailable.");
                return;
            }

            // Dose scales as 1 / PlanNormalizationValue, so value *= mean/presc makes mean == presc (100%).
            double current = double.IsNaN(plan.PlanNormalizationValue) ? 100.0 : plan.PlanNormalizationValue;
            plan.PlanNormalizationValue = current * meanDose / prescDose;
            log?.Invoke($"  Normalized to 100% of target mean (PlanNormalizationValue={plan.PlanNormalizationValue:0.##}; mean {meanDose:0.##} / presc {prescDose:0.##}).");
        }

        private static bool SetModel(ExternalPlanSetup plan, CalculationType type, string model, Action<string> log)
        {
            var available = plan.GetModelsForCalculationType(type).ToList();
            if (!available.Contains(model))
            {
                log?.Invoke($"  ERROR: model '{model}' not available for {type}. Available: {string.Join(", ", available)}");
                return false;
            }
            plan.SetCalculationModel(type, model);
            log?.Invoke($"  {type} model: {model}");
            return true;
        }
    }
}
