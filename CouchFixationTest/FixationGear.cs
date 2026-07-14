using System;
using System.Linq;
using OpenCvSharp;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using Point = OpenCvSharp.Point;
using Image = VMS.TPS.Common.Model.API.Image;

namespace CouchFixationTest
{
    /// <summary>
    /// Builds a "fixation gear" structure: every voxel with HU >= <paramref name="huThreshold"/>
    /// that is NOT inside the body.
    ///
    /// Both steps happen per axial slice in the (cheap) mask domain, so we only ever write the
    /// contours we actually keep:
    ///   1. Threshold the CT into a binary mask.
    ///   2. Erase the body from that mask, using the body's own stored contours on the slice.
    ///   3. Extract the remaining contours with OpenCV (same technique as Segmenter.cs) and write
    ///      them onto the structure.
    ///
    /// This is deliberately done in the mask domain rather than "threshold everything, then
    /// SegmentVolume.Sub(body)": at -550 HU the whole patient is above threshold, so the naive
    /// approach writes thousands of body/hole contours via the expensive AddContourOnImagePlane and
    /// then throws them away. Erasing the body first drops the write count by 1-2 orders of magnitude.
    ///
    /// What remains is everything denser than the threshold that is outside the patient: fixation
    /// devices, masks/straps, and (if imaged) the couch/table. Tune the threshold from real cases.
    /// </summary>
    public static class FixationGear
    {
        public const string StructureId = "fixation_gear";

        // Ignore contour specks below this pixel area to keep CT noise near the threshold out.
        private const double MinContourPixelArea = 2.0;

        /// <summary>
        /// Creates (or replaces) the fixation-gear structure in <paramref name="set"/>. Requires the
        /// patient to already be in modifications mode. Returns the created structure, or null.
        /// </summary>
        public static Structure Create(StructureSet set, Image image, Structure body, double huThreshold, Action<string> log)
        {
            if (image == null) { log("  No image on the structure set; cannot threshold."); return null; }
            if (body == null) { log("  No body structure; cannot exclude the patient interior."); return null; }

            // Fresh structure each run: remove a previous one if we can, otherwise use a fallback id.
            string id = StructureId;
            Structure existing = set.Structures.FirstOrDefault(s => s.Id == id);
            if (existing != null)
            {
                if (set.CanRemoveStructure(existing))
                {
                    set.RemoveStructure(existing);
                    log($"  Removed previous '{id}'.");
                }
                else
                {
                    id = id + "_ny";
                    log($"  '{StructureId}' cannot be removed (approved?); using '{id}' instead.");
                }
            }

            Structure fixation = set.AddStructure("CONTROL", id);
            fixation.Color = System.Windows.Media.Color.FromRgb(0, 220, 220); // cyan, easy to spot

            // HU is a linear function of the stored voxel value; invert it once instead of calling
            // VoxelToDisplayValue per voxel (millions of calls otherwise).
            double huAt0 = image.VoxelToDisplayValue(0);
            double huAt1000 = image.VoxelToDisplayValue(1000);
            double slope = (huAt1000 - huAt0) / 1000.0;
            if (Math.Abs(slope) < 1e-12)
            {
                log("  Could not derive a HU->voxel scale from the image; aborting.");
                return fixation;
            }
            double rawThreshold = (huThreshold - huAt0) / slope;
            bool keepAtOrAbove = slope > 0; // normal CT: higher stored value = higher HU
            log($"  HU threshold {huThreshold:0.#} -> raw voxel {rawThreshold:0.#} (keep {(keepAtOrAbove ? ">=" : "<=")}).");

            WriteThresholdContours(fixation, image, body, rawThreshold, keepAtOrAbove, log);
            log($"  Volume: {SafeVolume(fixation):0.0} cc.");

            return fixation;
        }

        // Per slice: threshold -> erase body -> write the remaining contours onto the structure.
        private static void WriteThresholdContours(Structure fixation, Image img, Structure body, double rawThreshold, bool keepAtOrAbove, Action<string> log)
        {
            int nx = img.XSize, ny = img.YSize;
            int[,] plane = new int[nx, ny];
            int written = 0;

            for (int k = 0; k < img.ZSize; k++)
            {
                img.GetVoxels(k, plane);

                using (Mat slice = new Mat(ny, nx, MatType.CV_8UC1, Scalar.All(0)))
                {
                    unsafe
                    {
                        byte* ptr = (byte*)slice.DataPointer;
                        long step = slice.Step();
                        for (int y = 0; y < ny; y++)
                            for (int x = 0; x < nx; x++)
                            {
                                int v = plane[x, y];
                                bool keep = keepAtOrAbove ? v >= rawThreshold : v <= rawThreshold;
                                if (keep) ptr[y * step + x] = 255;
                            }
                    }

                    // Erase the patient interior in the mask domain (fill the body polygons with 0).
                    Point[][] bodyPolys = ToPixelPolygons(img, body.GetContoursOnImagePlane(k));
                    if (bodyPolys.Length > 0)
                        Cv2.FillPoly(slice, bodyPolys, Scalar.All(0));

                    Point[][] contours = Cv2.FindContoursAsArray(
                        slice, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                    foreach (Point[] contour in contours)
                    {
                        if (contour.Length < 3) continue;
                        if (Cv2.ContourArea(contour) < MinContourPixelArea) continue;

                        VVector[] vv = contour.Select(pt => ToDicom(img, pt.X, pt.Y, k)).ToArray();
                        fixation.AddContourOnImagePlane(vv, k);
                        written++;
                    }
                }
            }

            log($"  Wrote {written} contour(s) across {img.ZSize} slices.");
        }

        // DICOM contours -> pixel polygons (dropping degenerate ones), for OpenCV fill.
        private static Point[][] ToPixelPolygons(Image img, VVector[][] contours)
        {
            if (contours == null) return new Point[0][];
            return contours
                .Where(c => c != null && c.Length >= 3)
                .Select(c => c.Select(p => DicomToPixel(img, p)).ToArray())
                .ToArray();
        }

        // Voxel index -> DICOM point using the image direction cosines (orientation-safe).
        private static VVector ToDicom(Image img, int x, int y, int z)
        {
            return img.Origin
                 + x * img.XRes * img.XDirection
                 + y * img.YRes * img.YDirection
                 + z * img.ZRes * img.ZDirection;
        }

        // DICOM point -> pixel index: project onto the (unit) direction cosines and divide by spacing.
        private static Point DicomToPixel(Image img, VVector p)
        {
            VVector d = p - img.Origin;
            double x = Dot(d, img.XDirection) / img.XRes;
            double y = Dot(d, img.YDirection) / img.YRes;
            return new Point((int)Math.Round(x), (int)Math.Round(y));
        }

        private static double Dot(VVector a, VVector b) => a.x * b.x + a.y * b.y + a.z * b.z;

        private static double SafeVolume(Structure s)
        {
            try { return s.Volume; } catch { return double.NaN; }
        }
    }
}
