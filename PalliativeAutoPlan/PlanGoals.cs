using System.Collections.Generic;

namespace PalliativeAutoPlan
{
    /// <summary>What a goal measures on a structure's DVH.</summary>
    public enum GoalMetric
    {
        Max,   // near-max point dose (dose at VolumePct = 0 %)
        Min,   // near-min / coverage point dose (dose at VolumePct = 100 %)
        Mean,  // mean dose
    }

    /// <summary>
    /// One clinical goal. This is the single source of truth for both the inverse VMAT objectives
    /// (<see cref="OptimizationGoals"/>) and the forward static-field scorer (<see cref="PlanScore"/>),
    /// so the two techniques are judged against identical numbers (LimitGy / Priority).
    /// <para>
    /// Priority doubles as the scoring weight: for VMAT it is the optimization-objective priority,
    /// for the static scorer it weights the squared violation.
    /// </para>
    /// </summary>
    public sealed class PlanGoal
    {
        public string StructureId;   // ignored when IsTarget is true
        public bool IsTarget;        // true => resolve to the plan's PTV at run time
        public GoalMetric Metric;
        public double LimitGy;
        public double VolumePct;     // point-objective volume level for Max (0) / Min (100); unused for Mean
        public double Priority;      // VMAT objective priority AND static scoring weight

        /// <summary>
        /// The fixed 8 Gy single-fraction palliative goals. Mirrors the previously hard-coded
        /// objectives exactly: PTV 8.12 max / 7.8 min (prio 100), spinal_cord 7.8 max (prio 90),
        /// heart / kidneys mean 1.8 and lungs mean 5 (prio 50).
        /// </summary>
        public static readonly IReadOnlyList<PlanGoal> All = new List<PlanGoal>
        {
            new PlanGoal { IsTarget = true,  Metric = GoalMetric.Max, LimitGy = 8.12, VolumePct = 0.0,   Priority = 100.0 },
            new PlanGoal { IsTarget = true,  Metric = GoalMetric.Min, LimitGy = 7.8,  VolumePct = 100.0, Priority = 100.0 },
            new PlanGoal { StructureId = "spinal_cord",  Metric = GoalMetric.Max,  LimitGy = 7.8, VolumePct = 0.0, Priority = 90.0 },
            new PlanGoal { StructureId = "heart",        Metric = GoalMetric.Mean, LimitGy = 1.8, Priority = 50.0 },
            new PlanGoal { StructureId = "kidney_left",  Metric = GoalMetric.Mean, LimitGy = 1.8, Priority = 50.0 },
            new PlanGoal { StructureId = "kidney_right", Metric = GoalMetric.Mean, LimitGy = 1.8, Priority = 50.0 },
            new PlanGoal { StructureId = "lung_left",    Metric = GoalMetric.Mean, LimitGy = 5.0, Priority = 50.0 },
            new PlanGoal { StructureId = "lung_right",   Metric = GoalMetric.Mean, LimitGy = 5.0, Priority = 50.0 },
        };
    }
}
