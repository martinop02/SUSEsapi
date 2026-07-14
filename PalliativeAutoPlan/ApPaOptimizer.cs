using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Forward-planned weight optimizer for the AP/PA parallel-opposed pair. Geometry is fixed
    /// (gantry 0 + 180), so the only variable is the PA weight fraction f (AP gets 1-f). The pair is
    /// built ONCE; each candidate just reweights, recomputes dose, normalizes to target mean, and
    /// scores with <see cref="PlanScore"/>.
    /// <para>
    /// The search is biased PA-heavy: the posterior spine target is far from the AP entrance, so the
    /// AP beam irradiates a large volume of anterior normal tissue (kidneys/bowel/heart) to reach it;
    /// heavier PA spares those OARs. The upper bound guards against a posterior hot spot / cold
    /// anterior target edge, which PlanScore's PTV max/min terms also penalize. Note a parallel pair
    /// cannot spare the cord (it lies on the axis, inside the target) — weighting only trades
    /// anterior-vs-posterior normal-tissue dose. Search on a coarse AAA grid, final on the fine grid.
    /// </para>
    /// </summary>
    public static class ApPaOptimizer
    {
        // PA weight fraction search range (PA-heavy). f = PA / (PA + AP).
        private const double MinPaFraction = 0.50;
        private const double MaxPaFraction = 0.85;
        private const double CoarseStep = 0.05;
        private const double RefineHalfWindow = 0.05;
        private const double RefineStep = 0.025;

        // Cheap grid for the search, fine grid for the final dose (same AAA model).
        private const string SearchGridCm = "0.5";
        private const string FinalGridCm = "0.25";

        /// <summary>
        /// Builds the AP/PA pair, searches the PA weight fraction, leaves the plan holding the best
        /// split with a final fine-grid dose (normalized to target mean), and returns that fraction.
        /// </summary>
        public static double Run(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            // Iso at the PTV center: it sits on the AP/PA axis, so moving it along that axis would not
            // change the parallel-opposed distribution (unlike the hinged pair's posterior shift).
            VVector iso = ptv.CenterPoint;

            StaticFieldBuilder.RemoveAllBeams(plan, log);
            List<Beam> beams = StaticFieldBuilder.AddApPaPair(plan, ptv, iso, log);
            Beam pa = beams[0], ap = beams[1];

            log?.Invoke($"  AP/PA weight search on {SearchGridCm} cm grid: PA fraction {MinPaFraction:0.##}–{MaxPaFraction:0.##} step {CoarseStep:0.###}.");
            Calculation.SetDoseGridSize(plan, SearchGridCm, log);

            var scores = new Dictionary<double, double>();   // f (rounded) -> cost, avoids recomputation
            double bestF = double.NaN;
            double bestScore = double.PositiveInfinity;

            // Coarse sweep.
            foreach (double f in Grid(MinPaFraction, MaxPaFraction, CoarseStep))
                Consider(plan, ptv, set, pa, ap, f, scores, ref bestF, ref bestScore, log);

            if (double.IsNaN(bestF))
            {
                log?.Invoke("  WARNING: no AP/PA candidate could be evaluated; leaving pair at equal weight.");
                return double.NaN;
            }

            // Local refine around the coarse winner.
            double lo = Math.Max(MinPaFraction, bestF - RefineHalfWindow);
            double hi = Math.Min(MaxPaFraction, bestF + RefineHalfWindow);
            log?.Invoke($"  Coarse best PA {bestF:0.###} (cost {bestScore:0.###}); refining {lo:0.###}–{hi:0.###} step {RefineStep:0.####}.");
            foreach (double f in Grid(lo, hi, RefineStep))
                Consider(plan, ptv, set, pa, ap, f, scores, ref bestF, ref bestScore, log);

            // Apply the winner and compute the final dose on the fine grid.
            log?.Invoke($"  Best PA fraction {bestF:0.###} (search cost {bestScore:0.###}); final dose on {FinalGridCm} cm grid.");
            Calculation.SetDoseGridSize(plan, FinalGridCm, log);
            ApplyWeights(pa, ap, bestF);
            plan.CalculateDose();
            Calculation.NormalizeToTargetMean(plan, ptv, log);
            double finalCost = PlanScore.Evaluate(plan, ptv, set, log);
            log?.Invoke($"  Final AP/PA plan: PA {bestF:0.###} / AP {1.0 - bestF:0.###}, fine-grid cost {finalCost:0.###}.");
            return bestF;
        }

        // Reweights the pair to PA fraction f, calculates + normalizes, scores, updates the best.
        // A failure on one split is logged and treated as +∞ so the sweep continues.
        private static void Consider(ExternalPlanSetup plan, Structure ptv, StructureSet set, Beam pa, Beam ap,
            double f, Dictionary<double, double> scores, ref double bestF, ref double bestScore, Action<string> log)
        {
            double key = Math.Round(f, 3);
            if (scores.ContainsKey(key))
                return;

            double cost;
            try
            {
                ApplyWeights(pa, ap, f);
                plan.CalculateDose();
                Calculation.NormalizeToTargetMean(plan, ptv, log);
                cost = PlanScore.Evaluate(plan, ptv, set, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"    PA fraction {f:0.###} failed ({ex.Message}); skipped.");
                cost = double.PositiveInfinity;
            }

            scores[key] = cost;
            if (cost < bestScore)
            {
                bestScore = cost;
                bestF = f;
            }
        }

        private static void ApplyWeights(Beam pa, Beam ap, double paFraction)
        {
            StaticFieldBuilder.SetWeightFactor(pa, paFraction);
            StaticFieldBuilder.SetWeightFactor(ap, 1.0 - paFraction);
        }

        // Inclusive grid from lo to hi (small epsilon guards the float endpoint).
        private static IEnumerable<double> Grid(double lo, double hi, double step)
        {
            for (double v = lo; v <= hi + 1e-9; v += step)
                yield return v;
        }
    }
}
