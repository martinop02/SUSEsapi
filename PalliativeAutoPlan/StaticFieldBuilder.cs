using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Builds the static techniques (both wedge-free — ESAPI cannot add wedges, Beam.Wedges is
    /// read-only). Sibling of <see cref="BeamBuilder"/>; jaws are fit to the PTV like the arc.
    ///   • <see cref="AddPosteriorPair"/>: two OPEN fields symmetric about the posterior-anterior
    ///     (PA) axis, hinged by θ (gantry = 180 ± θ/2), PTV + 10 mm jaws.
    ///   • <see cref="AddApPaPair"/>: a straight AP/PA parallel-opposed pair (gantry 0 + 180),
    ///     PTV + 5 mm jaws; the PA:AP weight split is optimized separately (see ApPaOptimizer).
    /// </summary>
    public static class StaticFieldBuilder
    {
        // --- Treatment unit -------------------------------------------------------------------
        // Machine id is chosen at startup (SBH_2 / SBH3_2021, see RunConfig). Two differences
        // from the arc: (1) the static posterior pair uses 15X (higher energy, deeper penetration
        // for the posterior spine), NOT the arc's 6X; (2) technique is the base "STATIC", not the
        // arc's "SRS ARC". If AddStaticBeam throws "External beam configuration not found", the
        // energy id ("15X") and/or Technique.Id may differ in this beam data — read them off an
        // existing 15X static beam via BeamConfigDiagnostic (mirrors the arc techniqueId saga).
        private const string EnergyId = "15X";
        private const int DoseRate = 600;
        private const string StaticTechnique = "STATIC";   // <-- site-verify if beam creation fails
        private const string FluenceMode = null;

        // --- Geometry -------------------------------------------------------------------------
        private const double PosteriorGantry = 180.0;   // PA direction (head-first supine)
        private const double AnteriorGantry = 0.0;      // AP direction (opposed to PA)
        private const double CollimatorAngle = 0.0;
        private const double CouchAngle = 0.0;

        // --- Jaw fitting (identical policy to BeamBuilder's arc) -------------------------------
        private const double HingedJawMarginMm = 10.0;  // hinged posterior pair: PTV + 10 mm
        private const double ApPaJawMarginMm = 5.0;     // AP/PA pair: PTV + 5 mm (per requirement)
        private const bool OptimizeCollimatorRotation = false;
        private const double MaxFieldXMm = 170.0;        // cap X field width (X2-X1) at 17 cm
        // AddStaticBeam needs starting jaws; they are immediately overwritten by FitCollimatorToStructure.
        private static readonly VRect<double> InitialJaws = new VRect<double>(-50.0, -50.0, 50.0, 50.0);

        /// <summary>
        /// Adds the posterior pair for hinge angle <paramref name="hingeDeg"/> at the given
        /// <paramref name="isocenter"/> and fits each field's jaws to the PTV. Returns the two beams
        /// (equal default weight).
        /// </summary>
        public static List<Beam> AddPosteriorPair(ExternalPlanSetup plan, Structure ptv, VVector isocenter, double hingeDeg, Action<string> log)
        {
            var machine = new ExternalBeamMachineParameters(RunConfig.MachineId, EnergyId, DoseRate, StaticTechnique, FluenceMode);
            double half = hingeDeg / 2.0;
            double gantryA = Norm360(PosteriorGantry - half);
            double gantryB = Norm360(PosteriorGantry + half);

            var beams = new List<Beam>();
            foreach (double gantry in new[] { gantryA, gantryB })
            {
                Beam beam = plan.AddStaticBeam(
                    machine,
                    InitialJaws,
                    CollimatorAngle,
                    gantry,
                    CouchAngle,
                    isocenter);
                FitAndClamp(beam, ptv, HingedJawMarginMm, log);
                beams.Add(beam);
            }

            log?.Invoke($"  Added posterior pair (hinge {hingeDeg:0.#}°, gantry {gantryA:0.#}° / {gantryB:0.#}°).");
            return beams;
        }

        /// <summary>
        /// Adds a straight AP/PA parallel-opposed pair at the given <paramref name="isocenter"/>:
        /// index [0] = PA (gantry 180, from the back), index [1] = AP (gantry 0, from the front),
        /// each with jaws fit to PTV + 5 mm. Beams start at equal weight; the PA:AP split is set
        /// afterwards (see <see cref="SetWeightFactor"/> / ApPaOptimizer).
        /// </summary>
        public static List<Beam> AddApPaPair(ExternalPlanSetup plan, Structure ptv, VVector isocenter, Action<string> log)
        {
            var machine = new ExternalBeamMachineParameters(RunConfig.MachineId, EnergyId, DoseRate, StaticTechnique, FluenceMode);

            var beams = new List<Beam>();
            foreach (double gantry in new[] { PosteriorGantry, AnteriorGantry })   // PA first, then AP
            {
                Beam beam = plan.AddStaticBeam(machine, InitialJaws, CollimatorAngle, gantry, CouchAngle, isocenter);
                FitAndClamp(beam, ptv, ApPaJawMarginMm, log);
                beams.Add(beam);
            }

            log?.Invoke($"  Added AP/PA pair (PA '{beams[0].Id}' gantry {PosteriorGantry:0.#}°, AP '{beams[1].Id}' gantry {AnteriorGantry:0.#}°, jaws PTV + {ApPaJawMarginMm:0.#} mm).");
            return beams;
        }

        /// <summary>Sets a beam's relative weight (WeightFactor); the plan is normalized afterwards.</summary>
        public static void SetWeightFactor(Beam beam, double weightFactor)
        {
            BeamParameters bp = beam.GetEditableParameters();
            bp.WeightFactor = weightFactor;
            beam.ApplyParameters(bp);
        }

        /// <summary>Removes every beam from the plan (used between optimizer candidates).</summary>
        public static void RemoveAllBeams(ExternalPlanSetup plan, Action<string> log)
        {
            foreach (Beam b in plan.Beams.ToList())
                plan.RemoveBeam(b);
        }

        // Fit jaws to the PTV BEV + margin, then cap X width symmetrically — same policy as the arc.
        private static void FitAndClamp(Beam beam, Structure ptv, double marginMm, Action<string> log)
        {
            beam.FitCollimatorToStructure(
                new FitToStructureMargins(marginMm),
                ptv,
                useAsymmetricXJaws: true,
                useAsymmetricYJaws: true,
                optimizeCollimatorRotation: OptimizeCollimatorRotation);

            VRect<double> jaws = beam.ControlPoints.First().JawPositions;
            double fieldXmm = jaws.X2 - jaws.X1;
            if (fieldXmm > MaxFieldXMm)
            {
                double centerX = (jaws.X1 + jaws.X2) / 2.0;
                double clampedX1 = centerX - MaxFieldXMm / 2.0;
                double clampedX2 = centerX + MaxFieldXMm / 2.0;
                BeamParameters bp = beam.GetEditableParameters();
                bp.SetJawPositions(new VRect<double>(clampedX1, jaws.Y1, clampedX2, jaws.Y2));
                beam.ApplyParameters(bp);
                log?.Invoke($"    '{beam.Id}' X field {fieldXmm / 10.0:0.#} cm > {MaxFieldXMm / 10.0:0.#} cm -> clamped.");
            }
        }

        // Wrap a gantry angle into [0, 360).
        private static double Norm360(double deg)
        {
            double d = deg % 360.0;
            return d < 0 ? d + 360.0 : d;
        }
    }
}
