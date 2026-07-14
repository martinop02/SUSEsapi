using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using DoseUnit = VMS.TPS.Common.Model.Types.DoseValue.DoseUnit;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Adds the fixed palliative optimization objectives (PTV coverage plus OAR mean/max
    /// constraints) to the VMAT optimizer. The goals themselves live in <see cref="PlanGoal.All"/>
    /// so the static-field scorer uses the identical numbers. OAR objectives are looked up by
    /// structure id; missing/empty OARs are skipped with a warning. Returns the number added.
    /// </summary>
    public static class OptimizationGoals
    {
        public static int Apply(ExternalPlanSetup plan, Structure ptv, StructureSet set, Action<string> log)
        {
            OptimizationSetup opt = plan.OptimizationSetup;

            // Wipe any existing objectives first so a re-run (or goals already set in Eclipse) does
            // not stack duplicates — the plan always ends with exactly the PlanGoal.All set.
            var stale = opt.Objectives.ToList();
            if (stale.Count > 0)
            {
                foreach (OptimizationObjective o in stale)
                    opt.RemoveObjective(o);
                log?.Invoke($"  Cleared {stale.Count} existing optimization objective(s) before adding.");
            }

            int count = 0;

            foreach (PlanGoal g in PlanGoal.All)
            {
                Structure s = g.IsTarget ? ptv : Find(set, g.StructureId);
                if (s == null)
                {
                    if (!g.IsTarget)
                        log?.Invoke($"  WARNING: structure '{g.StructureId}' not found/empty; objective skipped.");
                    continue;
                }

                var dose = new DoseValue(g.LimitGy, DoseUnit.Gy);
                switch (g.Metric)
                {
                    case GoalMetric.Max:
                        opt.AddPointObjective(s, OptimizationObjectiveOperator.Upper, dose, g.VolumePct, g.Priority);
                        break;
                    case GoalMetric.Min:
                        opt.AddPointObjective(s, OptimizationObjectiveOperator.Lower, dose, g.VolumePct, g.Priority);
                        break;
                    case GoalMetric.Mean:
                        opt.AddMeanDoseObjective(s, dose, g.Priority);
                        break;
                }
                count++;
            }

            log?.Invoke($"  Added {count} optimization objective(s).");
            return count;
        }

        private static Structure Find(StructureSet set, string id)
        {
            return set.Structures.FirstOrDefault(x => x.Id == id && !x.IsEmpty);
        }
    }
}
