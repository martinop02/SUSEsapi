using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Computes the static-plan isocenter. Instead of sitting at the PTV center (which lands
    /// essentially in the spinal cord), the iso is pushed straight posterior until it clears the
    /// cord's posterior surface by a fixed margin. Only the anterior-posterior coordinate moves;
    /// the left-right (x) and cranio-caudal (z) coordinates stay at the PTV center.
    /// </summary>
    public static class Isocenter
    {
        private const string CordId = "spinal_cord";
        private const double ClearanceMm = 10.0;    // place the iso 1 cm posterior of the cord surface
        private const double SearchSpanMm = 50.0;   // how far ± along y (from PTV center) to look for the cord
        private const double StepMm = 1.0;          // ray-march resolution
        private const double MaxShiftMm = 60.0;     // sanity cap on the posterior move

        // In the DICOM/ESAPI patient coordinate system (LPS) +y is posterior — the same convention
        // that makes gantry 180 the posterior beam in StaticFieldBuilder. If the log shows the iso
        // moving the wrong way, flip the sign of the search/clearance here.

        /// <summary>
        /// Returns an isocenter 1 cm posterior of the spinal cord surface, along the PTV center's
        /// AP line. Falls back to the PTV center (with a logged reason) if the cord is missing, the
        /// ray misses it, or the shift would be anterior.
        /// </summary>
        public static VVector PosteriorOfCord(StructureSet set, Structure ptv, Action<string> log)
        {
            VVector c = ptv.CenterPoint;

            Structure cord = set.Structures.FirstOrDefault(s => s.Id == CordId && !s.IsEmpty);
            if (cord == null)
            {
                log?.Invoke($"  Iso: '{CordId}' not found/empty — using PTV center (no posterior shift).");
                return c;
            }

            // March along y at the PTV center's (x, z); the largest y still inside the cord is its
            // posterior surface at the target's center level.
            double posteriorSurfaceY = double.NaN;
            for (double y = c.y - SearchSpanMm; y <= c.y + SearchSpanMm + 1e-6; y += StepMm)
                if (cord.IsPointInsideSegment(new VVector(c.x, y, c.z)))
                    posteriorSurfaceY = y;

            if (double.IsNaN(posteriorSurfaceY))
            {
                log?.Invoke($"  Iso: AP ray at PTV center missed '{CordId}' — using PTV center (no shift).");
                return c;
            }

            double isoY = posteriorSurfaceY + ClearanceMm;
            double shift = isoY - c.y;

            if (shift <= 0)
            {
                log?.Invoke($"  Iso: cord posterior surface is anterior to PTV center (would shift {shift:0.#} mm) — using PTV center.");
                return c;
            }
            if (shift > MaxShiftMm)
            {
                log?.Invoke($"  Iso: computed posterior shift {shift:0.#} mm exceeds cap {MaxShiftMm:0.#} mm — clamping.");
                isoY = c.y + MaxShiftMm;
                shift = MaxShiftMm;
            }

            var iso = new VVector(c.x, isoY, c.z);
            log?.Invoke($"  Iso: PTV center y={c.y:0.#} -> cord posterior surface y={posteriorSurfaceY:0.#} +{ClearanceMm:0.#} mm = y={isoY:0.#} (moved {shift:0.#} mm posterior).");
            return iso;
        }
    }
}
