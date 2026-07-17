using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Adds VMAT arc(s) isocentred on the PTV — a single full arc, or a dual CW+CCW arc pair. The
    /// treatment-unit configuration and arc geometry are constants so they are easy to find and adjust.
    /// </summary>
    public static class BeamBuilder
    {
        // --- Treatment unit (site-verified working values) ---
        // The machineId is chosen at startup (SBH_2 / SBH3_2021, see RunConfig) — SBH_2 is the
        // beam-data machine id that differs from the Beam.TreatmentUnit.Id the diagnostic shows
        // ("SBH2_2025"). techniqueId is the site's literal "SRS ARC"; fluence mode is null (plain 6X).
        private const string EnergyId = "6X";
        private const int DoseRate = 600;
        private const string Technique = "SRS ARC";
        private const string FluenceMode = null;

        // --- Arc geometry ---
        private const double GantryStart = 181.0;
        private const double GantryStop = 179.0;      // 181 -> 179 clockwise ~ full 360 deg arc
        private const double CollimatorAngle = 30.0;    // single / first (CW) arc
        private const double CollimatorAngle2 = 330.0;  // second (CCW) arc of the dual-arc technique
        private const double CouchAngle = 0.0;
        private const int ControlPoints = 178;        // number of meterset weights = control points

        // --- Jaw fitting (Eclipse otherwise leaves a default field size) ---
        private const double JawMarginMm = 10.0;               // uniform margin around the PTV (mm)
        private const bool OptimizeCollimatorRotation = false; // false = keep the collimator angle
        private const double MaxFieldXMm = 170.0;              // cap on X field width (X1+X2): 17.0 cm

        /// <summary>Single full arc: 181 -> 179 clockwise, collimator 30.</summary>
        public static Beam AddSingleArc(ExternalPlanSetup plan, Structure ptv, Action<string> log)
            => AddArc(plan, ptv, GantryStart, GantryStop, GantryDirection.Clockwise, CollimatorAngle, log);

        /// <summary>
        /// Dual arc: 181 -> 179 clockwise (collimator 30) plus 179 -> 181 counter-clockwise
        /// (collimator 330). Otherwise identical to the single arc; OptimizeVMAT optimizes both arcs
        /// together, so the rest of the VMAT pipeline is unchanged.
        /// </summary>
        public static List<Beam> AddDualArc(ExternalPlanSetup plan, Structure ptv, Action<string> log)
        {
            return new List<Beam>
            {
                AddArc(plan, ptv, GantryStart, GantryStop, GantryDirection.Clockwise, CollimatorAngle, log),
                AddArc(plan, ptv, GantryStop, GantryStart, GantryDirection.CounterClockwise, CollimatorAngle2, log),
            };
        }

        // Adds one VMAT arc with the given gantry sweep, direction and collimator, then fits/clamps
        // its jaws to the PTV (shared by the single- and dual-arc entry points above).
        private static Beam AddArc(ExternalPlanSetup plan, Structure ptv, double gantryStart, double gantryStop,
                                   GantryDirection direction, double collimator, Action<string> log)
        {
            var machine = new ExternalBeamMachineParameters(RunConfig.MachineId, EnergyId, DoseRate, Technique, FluenceMode);

            // Cumulative meterset weights ramping 0 -> 1 across the control points (the optimizer
            // redistributes them). AddVMATBeam requires the first cumulative weight to be zero.
            double[] weights = new double[ControlPoints];
            for (int i = 0; i < ControlPoints; i++)
                weights[i] = (double)i / (ControlPoints - 1);

            Beam arc = plan.AddVMATBeam(
                machine,
                weights,
                collimator,
                gantryStart,
                gantryStop,
                direction,
                CouchAngle,
                ptv.CenterPoint);

            log?.Invoke($"  Added VMAT arc '{arc.Id}' ({RunConfig.MachineId} {EnergyId} {Technique}/{FluenceMode}, gantry {gantryStart}->{gantryStop} {direction}, coll {collimator}, iso at PTV center).");

            // Size the jaws to the PTV's beam's-eye-view projection plus a margin (VMAT keeps the
            // jaws fixed across the arc). Without this the field stays at Eclipse's default size.
            arc.FitCollimatorToStructure(
                new FitToStructureMargins(JawMarginMm),
                ptv,
                useAsymmetricXJaws: true,
                useAsymmetricYJaws: true,
                optimizeCollimatorRotation: OptimizeCollimatorRotation);

            VRect<double> jaws = arc.ControlPoints.First().JawPositions;
            log?.Invoke($"  Fit jaws to '{ptv.Id}' (+{JawMarginMm} mm): X1={jaws.X1:0.#} X2={jaws.X2:0.#} Y1={jaws.Y1:0.#} Y2={jaws.Y2:0.#} mm.");

            // Cap the X field width (X1+X2) at MaxFieldXMm; if the fit went wider, shrink it
            // symmetrically about the field centre.
            double fieldXmm = jaws.X2 - jaws.X1;
            if (fieldXmm > MaxFieldXMm)
            {
                double centerX = (jaws.X1 + jaws.X2) / 2.0;
                double clampedX1 = centerX - MaxFieldXMm / 2.0;
                double clampedX2 = centerX + MaxFieldXMm / 2.0;
                BeamParameters bp = arc.GetEditableParameters();
                bp.SetJawPositions(new VRect<double>(clampedX1, jaws.Y1, clampedX2, jaws.Y2));
                arc.ApplyParameters(bp);
                log?.Invoke($"  X field {fieldXmm / 10.0:0.#} cm > {MaxFieldXMm / 10.0:0.#} cm -> clamped to X1={clampedX1:0.#}, X2={clampedX2:0.#} mm.");
            }
            return arc;
        }
    }
}
