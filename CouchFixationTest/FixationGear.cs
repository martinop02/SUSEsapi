using System;
using System.Collections.Generic;
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
    /// Pipeline:
    ///   1. Per slice, build a binary mask: threshold the CT, erase the body (fill its own contours),
    ///      and morphologically close small gaps so thin/low-HU fixation is less patchy. Store each
    ///      cleaned slice into a full 3D volume.
    ///   2. Filter the 3D volume by connected-component size: keep only components whose total volume
    ///      is >= MinComponentVolumeCc. This removes noise specks and small couch fragments WITHOUT
    ///      losing the fixation — which is thin on any single slice but large in 3D (a 2D per-slice
    ///      size filter can't tell those apart and wrongly deletes the fixation too).
    ///   3. Per slice, extract the remaining contours with OpenCV (same technique as Segmenter.cs)
    ///      and write them onto the structure.
    ///
    /// The body is excluded in the mask domain (not "threshold everything then SegmentVolume.Sub"):
    /// at -550 HU the whole patient is above threshold, so the naive approach writes thousands of
    /// body/hole contours via the expensive AddContourOnImagePlane and then discards them.
    ///
    /// What remains is dense material outside the patient: fixation devices, masks/straps, and (if
    /// imaged) the couch/table. Tune the threshold and the cleanup parameters from real cases.
    /// </summary>
    public static class FixationGear
    {
        public const string StructureId = "fixation_gear";

        // --- Cleanup parameters, tune from real cases ---
        // Morphological close radius (px, per slice): fills small gaps so fixation is less patchy.
        private const int CloseRadiusPx = 2;
        // Drop 3D connected components smaller than this (cc): removes noise and small couch bits
        // while keeping the fixation, which is large in 3D even though thin per slice.
        private const double MinComponentVolumeCc = 0.2;

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

            BuildFixation(fixation, image, body, rawThreshold, keepAtOrAbove, log);
            log($"  Volume: {SafeVolume(fixation):0.0} cc.");

            return fixation;
        }

        private static void BuildFixation(Structure fixation, Image img, Structure body, double rawThreshold, bool keepAtOrAbove, Action<string> log)
        {
            int nx = img.XSize, ny = img.YSize, nz = img.ZSize;
            int planeSize = nx * ny;
            byte[] vol = new byte[planeSize * nz];   // 0/255 cleaned mask, whole volume
            int[,] plane = new int[nx, ny];
            int progressEvery = Math.Max(1, nz / 5);

            // Pass 1: per-slice threshold -> erase body -> close -> store into vol.
            log($"  Thresholding + cleaning {nz} slices...");
            for (int k = 0; k < nz; k++)
            {
                if (k % progressEvery == 0 && k > 0) log($"    ...slice {k}/{nz}");
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

                    Point[][] bodyPolys = ToPixelPolygons(img, body.GetContoursOnImagePlane(k));
                    if (bodyPolys.Length > 0)
                        Cv2.FillPoly(slice, bodyPolys, Scalar.All(0));

                    if (CloseRadiusPx > 0)
                    {
                        int d = 2 * CloseRadiusPx + 1;
                        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(d, d)))
                            Cv2.MorphologyEx(slice, slice, MorphTypes.Close, kernel);
                        if (bodyPolys.Length > 0)           // close can bleed into the body; re-erase
                            Cv2.FillPoly(slice, bodyPolys, Scalar.All(0));
                    }

                    CopySliceIntoVolume(slice, vol, k * planeSize, nx, ny);
                }
            }

            // Pass 2: 3D connected-component size filter.
            double voxelCc = img.XRes * img.YRes * img.ZRes / 1000.0;   // mm^3 -> cc
            int minVoxels = Math.Max(1, (int)(MinComponentVolumeCc / voxelCc));
            log($"  Filtering 3D components (min {MinComponentVolumeCc:0.##} cc = {minVoxels} vox)...");
            RemoveSmallComponents3D(vol, nx, ny, nz, minVoxels, log);

            // Pass 3: contour each slice from the cleaned volume and write.
            log("  Writing contours...");
            int written = 0;
            for (int k = 0; k < nz; k++)
            {
                using (Mat slice = SliceFromVolume(vol, k * planeSize, nx, ny))
                {
                    if (Cv2.CountNonZero(slice) == 0) continue;

                    Point[][] contours = Cv2.FindContoursAsArray(
                        slice, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);
                    foreach (Point[] contour in contours)
                    {
                        if (contour.Length < 3) continue;
                        VVector[] vv = contour.Select(pt => ToDicom(img, pt.X, pt.Y, k)).ToArray();
                        fixation.AddContourOnImagePlane(vv, k);
                        written++;
                    }
                }
            }

            log($"  Wrote {written} contour(s) across {nz} slices.");
        }

        // 26-connected component labelling over the whole volume; zero any component with fewer than
        // minVoxels voxels. Iterative (explicit stack) so deep components don't blow the call stack.
        private static void RemoveSmallComponents3D(byte[] vol, int nx, int ny, int nz, int minVoxels, Action<string> log)
        {
            int planeSize = nx * ny;
            int total = vol.Length;
            bool[] visited = new bool[total];
            var stack = new Stack<int>();
            var comp = new List<int>();
            int kept = 0, removed = 0;
            long keptVoxels = 0;

            for (int start = 0; start < total; start++)
            {
                if (vol[start] == 0 || visited[start]) continue;

                comp.Clear();
                stack.Push(start);
                visited[start] = true;

                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    comp.Add(idx);

                    int k = idx / planeSize;
                    int rem = idx - k * planeSize;
                    int y = rem / nx;
                    int x = rem - y * nx;

                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nk = k + dz; if (nk < 0 || nk >= nz) continue;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nyy = y + dy; if (nyy < 0 || nyy >= ny) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0 && dz == 0) continue;
                                int nxx = x + dx; if (nxx < 0 || nxx >= nx) continue;
                                int nidx = nxx + nx * (nyy + ny * nk);
                                if (vol[nidx] != 0 && !visited[nidx])
                                {
                                    visited[nidx] = true;
                                    stack.Push(nidx);
                                }
                            }
                        }
                    }
                }

                if (comp.Count < minVoxels)
                {
                    foreach (int i in comp) vol[i] = 0;
                    removed++;
                }
                else
                {
                    kept++;
                    keptVoxels += comp.Count;
                }
            }

            log($"  3D components: kept {kept} ({keptVoxels} vox, >= {minVoxels} each), removed {removed} small.");
        }

        // --- mask <-> volume helpers (handle possible row padding via Mat.Step) ---

        private static void CopySliceIntoVolume(Mat slice, byte[] vol, int offset, int nx, int ny)
        {
            unsafe
            {
                byte* sp = (byte*)slice.DataPointer;
                long step = slice.Step();
                for (int y = 0; y < ny; y++)
                {
                    int rowOff = offset + y * nx;
                    byte* row = sp + y * step;
                    for (int x = 0; x < nx; x++) vol[rowOff + x] = row[x];
                }
            }
        }

        private static Mat SliceFromVolume(byte[] vol, int offset, int nx, int ny)
        {
            Mat slice = new Mat(ny, nx, MatType.CV_8UC1, Scalar.All(0));
            unsafe
            {
                byte* sp = (byte*)slice.DataPointer;
                long step = slice.Step();
                for (int y = 0; y < ny; y++)
                {
                    int rowOff = offset + y * nx;
                    byte* row = sp + y * step;
                    for (int x = 0; x < nx; x++) row[x] = vol[rowOff + x];
                }
            }
            return slice;
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
