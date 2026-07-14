using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Custom forward-planned optimizer for the static posterior pair. ESAPI has no static-field
    /// optimizer (only inverse IMRT/VMAT), so this searches the single geometric variable — the
    /// hinge angle θ — with a coarse grid plus a local refine, scoring each candidate with
    /// <see cref="PlanScore"/>. The search runs on a coarse AAA dose grid (cheap); the winning angle
    /// is rebuilt and recalculated on the fine grid. Grid, not gradient: the objective is a
    /// multimodal, expensive black box.
    /// </summary>
    public static class StaticFieldOptimizer
    {
        // Coarse sweep, then a local ±window refine around the coarse winner.
        private const double CoarseMinDeg = 30.0;
        private const double CoarseMaxDeg = 140.0;
        private const double CoarseStepDeg = 20.0;
        private const double RefineHalfWindowDeg = 20.0;
        private const double RefineStepDeg = 5.0;
        // Absolute clamp so the refine cannot wander into unphysical hinge angles.
        private const double MinHingeDeg = 20.0;
        private const double MaxHingeDeg = 160.0;

        // Cheap grid for the search, fine grid for the final dose (same AAA model — no faster
        // algorithm exists). Point/max metrics are grid-sensitive, so the winner is re-checked fine.
        private const string SearchGridCm = "0.5";
        private const string FinalGridCm = "0.25";

        /// <summary>
        /// Searches for the best hinge angle, leaves the plan holding that pair with a final
        /// fine-grid dose (normalized to target mean), and returns the chosen angle.
        /// </summary>
        public static double Run(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            var scores = new Dictionary<double, double>();   // θ (rounded) -> cost, avoids recomputation

            // Iso is θ-independent: compute it once (1 cm posterior of the cord) and reuse it.
            VVector iso = Isocenter.PosteriorOfCord(set, ptv, log);

            log?.Invoke($"  Static search on {SearchGridCm} cm grid: coarse {CoarseMinDeg}–{CoarseMaxDeg}° step {CoarseStepDeg}°.");
            Calculation.SetDoseGridSize(plan, SearchGridCm, log);

            double bestTheta = double.NaN;
            double bestScore = double.PositiveInfinity;

            // Coarse sweep.
            foreach (double theta in Grid(CoarseMinDeg, CoarseMaxDeg, CoarseStepDeg))
                Consider(plan, ptv, set, iso, theta, scores, ref bestTheta, ref bestScore, log);

            if (double.IsNaN(bestTheta))
            {
                log?.Invoke("  WARNING: no static candidate could be evaluated; leaving plan without beams.");
                return double.NaN;
            }

            // Local refine around the coarse winner.
            double lo = Math.Max(MinHingeDeg, bestTheta - RefineHalfWindowDeg);
            double hi = Math.Min(MaxHingeDeg, bestTheta + RefineHalfWindowDeg);
            log?.Invoke($"  Coarse best {bestTheta:0.#}° (cost {bestScore:0.###}); refining {lo:0.#}–{hi:0.#}° step {RefineStepDeg}°.");
            foreach (double theta in Grid(lo, hi, RefineStepDeg))
                Consider(plan, ptv, set, iso, theta, scores, ref bestTheta, ref bestScore, log);

            // Rebuild the winner and compute the final dose on the fine grid.
            log?.Invoke($"  Best hinge {bestTheta:0.#}° (search cost {bestScore:0.###}); final dose on {FinalGridCm} cm grid.");
            Calculation.SetDoseGridSize(plan, FinalGridCm, log);
            StaticFieldBuilder.RemoveAllBeams(plan, log);
            StaticFieldBuilder.AddPosteriorPair(plan, ptv, iso, bestTheta, log);
            plan.CalculateDose();
            Calculation.NormalizeToTargetMean(plan, ptv, log);
            double finalCost = PlanScore.Evaluate(plan, ptv, set, log);
            log?.Invoke($"  Final static plan: hinge {bestTheta:0.#}°, fine-grid cost {finalCost:0.###}.");
            return bestTheta;
        }

        // Builds the pair at θ, calculates + normalizes, scores, and updates the running best.
        // A failure on one angle is logged and treated as +∞ so the sweep continues.
        private static void Consider(ExternalPlanSetup plan, Structure ptv, StructureSet set, VVector iso, double theta,
            Dictionary<double, double> scores, ref double bestTheta, ref double bestScore, Action<string> log)
        {
            double key = Math.Round(theta, 1);
            if (scores.ContainsKey(key))
                return;

            double cost;
            try
            {
                StaticFieldBuilder.RemoveAllBeams(plan, log);
                StaticFieldBuilder.AddPosteriorPair(plan, ptv, iso, theta, log);
                plan.CalculateDose();
                Calculation.NormalizeToTargetMean(plan, ptv, log);
                cost = PlanScore.Evaluate(plan, ptv, set, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"    hinge {theta:0.#}° failed ({ex.Message}); skipped.");
                cost = double.PositiveInfinity;
            }

            scores[key] = cost;
            if (cost < bestScore)
            {
                bestScore = cost;
                bestTheta = theta;
            }
        }

        // Inclusive grid from lo to hi (small epsilon guards the float endpoint).
        private static IEnumerable<double> Grid(double lo, double hi, double step)
        {
            for (double v = lo; v <= hi + 1e-6; v += step)
                yield return v;
        }
    }
}
