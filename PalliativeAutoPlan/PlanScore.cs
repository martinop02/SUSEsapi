using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using DoseUnit = VMS.TPS.Common.Model.Types.DoseValue.DoseUnit;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Forward-planning objective for the static-field optimizer. Scores an already-calculated plan
    /// against <see cref="PlanGoal.All"/> as a single scalar cost = Σ priority · max(0, violation)²,
    /// where violation is the amount a goal is exceeded (Gy). Lower is better; 0 means every goal met.
    /// Missing/empty structures contribute nothing. The plan should be normalized before scoring so
    /// PTV coverage is fixed and the score discriminates on hot-spot, cold-spot and OAR sparing.
    /// </summary>
    public static class PlanScore
    {
        public static double Evaluate(PlanningItem plan, Structure ptv, StructureSet set, Action<string> log = null)
        {
            double cost = 0.0;

            foreach (PlanGoal g in PlanGoal.All)
            {
                Structure s = g.IsTarget ? ptv : Find(set, g.StructureId);
                if (s == null)
                    continue;   // OAR absent from this patient's set — silently skip (already warned at objective time).

                double valueGy;
                switch (g.Metric)
                {
                    case GoalMetric.Mean:
                        DVHData dvh = plan.GetDVHCumulativeData(s, DoseValuePresentation.Absolute, VolumePresentation.Relative, 0.1);
                        if (dvh == null) continue;
                        valueGy = ToGy(dvh.MeanDose);
                        break;
                    default: // Max / Min are point doses at a volume level
                        valueGy = ToGy(plan.GetDoseAtVolume(s, g.VolumePct, VolumePresentation.Relative, DoseValuePresentation.Absolute));
                        break;
                }

                if (double.IsNaN(valueGy))
                    continue;

                // Min goals penalize being BELOW the limit (cold coverage); everything else penalizes
                // being ABOVE the limit (hot spot / OAR overdose).
                double violation = g.Metric == GoalMetric.Min ? (g.LimitGy - valueGy) : (valueGy - g.LimitGy);
                if (violation > 0.0)
                    cost += g.Priority * violation * violation;
            }

            log?.Invoke($"    PlanScore = {cost:0.###}");
            return cost;
        }

        // GetDoseAtVolume / DVH return absolute dose in the plan's dose unit; convert cGy -> Gy so the
        // comparison against the goals' Gy limits is unit-safe regardless of the plan's display unit.
        private static double ToGy(DoseValue d)
        {
            if (d.Unit == DoseUnit.cGy) return d.Dose / 100.0;
            if (d.Unit == DoseUnit.Gy) return d.Dose;
            return double.NaN;   // Percent / Unknown — not usable as an absolute Gy value.
        }

        private static Structure Find(StructureSet set, string id)
        {
            return set.Structures.FirstOrDefault(x => x.Id == id && !x.IsEmpty);
        }
    }
}
