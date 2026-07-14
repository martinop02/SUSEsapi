using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
using CouchFixationTest;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.*"), ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// CouchFixationTest — an isolated harness for the "couch is placed wrong when the patient
    /// has fixation gear" problem.
    ///
    /// When fixation gear rolls the patient slightly, the body's posterior (back) surface is no
    /// longer parallel to the treatment couch. AddCouchStructures drops an axis-aligned couch, so
    /// it ends up tilted relative to the patient. This script does NOT modify the plan; it only
    /// measures and reports the geometry so the correction can be developed and verified quickly:
    ///
    ///   1. Report patient/image orientation and the structures involved.
    ///   2. Fit the body's posterior surface across the left-right axis -> body roll angle.
    ///   3. If a couch (SUPPORT) structure exists, fit its top plane the same way.
    ///   4. Report the difference (body roll - couch roll) = the mismatch to correct.
    ///
    /// Coordinates are ESAPI/DICOM patient (LPS): +x = patient left, +y = posterior, +z = cranial.
    /// The +y = posterior convention matches Isocenter.cs in PalliativeAutoPlan. If a measured
    /// angle comes out with the wrong sign on real data, flip it here after checking one case.
    /// </summary>
    public class Script
    {
        public Script() { }

        // Left-right sampling: use the central fraction of the body width so arms / immobilization
        // at the edges don't corrupt the posterior-surface fit.
        private const double CentralFraction = 0.6;
        private const int Samples = 25;      // x positions across the central band
        private const double StepMm = 1.0;   // y ray-march resolution

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            LogWindow.Run("Couch fixation test", log => Inspect(context, log));
        }

        private void Inspect(ScriptContext context, Action<string> log)
        {
            log("=== CouchFixationTest (read-only inspection) ===");

            Patient patient = context.Patient;
            if (patient == null) { log("No patient is open. Aborting."); return; }
            log($"Patient: {patient.Id}");

            StructureSet set = context.StructureSet ?? context.PlanSetup?.StructureSet;
            if (set == null)
            {
                log("No structure set in context (open a plan or structure set). Aborting.");
                return;
            }
            log($"Structure set: {set.Id}");

            Image img = set.Image;
            if (img != null)
                log($"Image: {img.Id}  orientation: {img.ImagingOrientation}");
            if (context.PlanSetup != null)
                log($"Plan: {context.PlanSetup.Id}  treatment orientation: {context.PlanSetup.TreatmentOrientation}");
            log("");

            // --- Body (EXTERNAL) ---
            Structure body = set.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty);
            if (body == null)
            {
                log("No non-empty EXTERNAL (BODY) structure found. Aborting.");
                return;
            }
            LogBounds(body, log);

            // --- Couch (SUPPORT) ---
            List<Structure> couch = set.Structures
                .Where(s => s.DicomType == "SUPPORT" && !s.IsEmpty)
                .ToList();
            if (couch.Count == 0)
                log("No SUPPORT (couch) structure present in this set.");
            foreach (Structure c in couch)
                LogBounds(c, log);
            log("");

            // --- Body posterior-surface roll ---
            log("--- Body posterior surface ---");
            Fit bodyFit = FitSurface(body, PosteriorSurfaceY, log);
            if (bodyFit == null)
            {
                log("Could not fit the body posterior surface. Aborting.");
                return;
            }
            log($"Body roll: {bodyFit.AngleDeg:0.00} deg  (slope {bodyFit.Slope:0.0000} mm/mm, {bodyFit.Count} pts, residual {bodyFit.Rms:0.0} mm)");
            log("");

            // --- Couch top plane, if any ---
            if (couch.Count > 0)
            {
                log("--- Couch top surface ---");
                Structure top = couch.OrderBy(c => c.CenterPoint.y).First(); // most anterior support = the top the patient rests on
                Fit couchFit = FitSurface(top, CouchTopY, log);
                if (couchFit != null)
                {
                    log($"Couch top roll: {couchFit.AngleDeg:0.00} deg  (using support '{top.Id}')");
                    double mismatch = bodyFit.AngleDeg - couchFit.AngleDeg;
                    log("");
                    log($">>> MISMATCH (body - couch): {mismatch:0.00} deg. This is the tilt to correct.");
                }
            }
            else
            {
                log($">>> Body roll relative to an axis-aligned couch would be {bodyFit.AngleDeg:0.00} deg.");
                log("    Add the couch (as PalliativeAutoPlan does) to compare against the real support plane.");
            }

            // =====================================================================================
            // TODO (next iteration): turn the measured mismatch into a correction. Options to try:
            //   - Re-place / rotate the couch structures to match the body roll, or
            //   - Compensate at the isocenter/beam level, or
            //   - Adjust the structure set before AddCouchStructures is called.
            // Keep this script read-only until the correction approach is chosen; add a guarded
            // patient.BeginModifications() block here when ready to write.
            // =====================================================================================
        }

        // Fit posteriorY = slope*x + b across the central band of the structure's width, then
        // report the roll angle (atan of the slope) about the cranio-caudal axis.
        private static Fit FitSurface(Structure s, Func<Structure, double, double, double, double, double, double?> surfaceY, Action<string> log)
        {
            Rect3D b = s.MeshGeometry.Bounds;
            double z = s.CenterPoint.z;
            double xMin = b.X, xMax = b.X + b.SizeX;
            double yMin = b.Y, yMax = b.Y + b.SizeY;

            double margin = (1.0 - CentralFraction) / 2.0 * (xMax - xMin);
            double x0 = xMin + margin, x1 = xMax - margin;

            var pts = new List<Point>();
            for (int i = 0; i < Samples; i++)
            {
                double x = x0 + (x1 - x0) * i / (Samples - 1);
                double? y = surfaceY(s, x, z, yMin, yMax);
                if (y.HasValue) pts.Add(new Point(x, y.Value));
            }

            if (pts.Count < 3)
            {
                log($"  '{s.Id}': only {pts.Count} surface point(s) found — not enough to fit.");
                return null;
            }

            // Ordinary least squares.
            double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
            double sxx = pts.Sum(p => (p.X - mx) * (p.X - mx));
            double sxy = pts.Sum(p => (p.X - mx) * (p.Y - my));
            double slope = Math.Abs(sxx) < 1e-9 ? 0 : sxy / sxx;
            double intercept = my - slope * mx;
            double rms = Math.Sqrt(pts.Average(p => Math.Pow(p.Y - (slope * p.X + intercept), 2)));

            return new Fit
            {
                Slope = slope,
                AngleDeg = Math.Atan(slope) * 180.0 / Math.PI,
                Rms = rms,
                Count = pts.Count
            };
        }

        // Posterior (back) surface: the largest y still inside the body at (x, z).
        private static double? PosteriorSurfaceY(Structure s, double x, double z, double yMin, double yMax)
        {
            double found = double.NaN;
            for (double y = yMin; y <= yMax + 1e-6; y += StepMm)
                if (s.IsPointInsideSegment(new VVector(x, y, z)))
                    found = y;
            return double.IsNaN(found) ? (double?)null : found;
        }

        // Couch top: the smallest y still inside the support at (x, z) — the surface facing the patient.
        private static double? CouchTopY(Structure s, double x, double z, double yMin, double yMax)
        {
            for (double y = yMin; y <= yMax + 1e-6; y += StepMm)
                if (s.IsPointInsideSegment(new VVector(x, y, z)))
                    return y;
            return null;
        }

        private static void LogBounds(Structure s, Action<string> log)
        {
            try
            {
                Rect3D b = s.MeshGeometry.Bounds;
                log($"  {s.DicomType,-8} '{s.Id}'  x[{b.X:0} .. {b.X + b.SizeX:0}]  y[{b.Y:0} .. {b.Y + b.SizeY:0}]  z[{b.Z:0} .. {b.Z + b.SizeZ:0}] (mm)");
            }
            catch (Exception ex)
            {
                log($"  {s.DicomType} '{s.Id}': bounds unavailable ({ex.Message}).");
            }
        }

        private sealed class Fit
        {
            public double Slope;
            public double AngleDeg;
            public double Rms;
            public int Count;
        }

        private struct Point
        {
            public double X, Y;
            public Point(double x, double y) { X = x; Y = y; }
        }
    }
}
