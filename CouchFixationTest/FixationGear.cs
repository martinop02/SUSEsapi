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
    /// Segments dense material into a structure by HU threshold, with optional spatial constraints.
    /// One method serves both passes of the couch-fixation workflow:
    ///
    ///   Coarse pass:  threshold at ~-550, erase the body -> rough fixation (later OR'd into body so
    ///                 the couch places correctly).
    ///   Refined pass: threshold much lower, erase the (original) body and the couch, and keep only
    ///                 voxels posterior to (below) the original body -> clean base fixation.
    ///
    /// Per axial slice: build a threshold mask, fill the "erase" structures' contours with 0,
    /// morphologically close small gaps, optionally drop everything not below a reference structure,
    /// and store into a 3D volume. Then a 3D connected-component size filter removes noise, and the
    /// remaining contours are written back (same OpenCV technique as PalliativeAutoPlan/Segmenter.cs).
    /// The size filter is 3D on purpose: fixation is thin per slice but large as a 3D object, so a
    /// 2D per-slice filter would delete it.
    /// </summary>
    public static class FixationGear
    {
        // --- Cleanup parameters, tune from real cases ---
        // Base morphological close radius (px, per slice): fills small gaps / de-patches.
        private const int CloseRadiusPx = 2;
        // Larger close radius used when fillGaps is set: bridges the broken outline of the board so
        // the (External) contour then fills its interior solid.
        private const int FillCloseRadiusPx = 12;

        /// <summary>
        /// Creates (or replaces) a structure <paramref name="id"/> holding every voxel with
        /// HU >= <paramref name="huThreshold"/>, minus the interiors of <paramref name="eraseStructures"/>,
        /// optionally restricted to voxels lying below (posterior to) <paramref name="belowReference"/>
        /// on each axial slice. Requires the patient to be in modifications mode. Returns it, or null.
        /// </summary>
        public static Structure Segment(
            StructureSet set, Image image, string id, System.Windows.Media.Color color,
            double huThreshold, IList<Structure> eraseStructures, Structure belowReference,
            double minComponentCc, bool fillGaps, Action<string> log)
        {
            if (image == null) { log("  No image; cannot threshold."); return null; }

            Structure fixation = ReplaceStructure(set, id, log);
            if (fixation == null) return null;
            fixation.Color = color;

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

            BuildVolume(fixation, image, rawThreshold, keepAtOrAbove,
                        eraseStructures ?? new Structure[0], belowReference, minComponentCc, fillGaps, log);
            log($"  Volume: {SafeVolume(fixation):0.0} cc.");
            return fixation;
        }

        private static Structure ReplaceStructure(StructureSet set, string id, Action<string> log)
        {
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
                    log($"  '{id}' base cannot be removed (approved?); using a new id.");
                }
            }
            return set.AddStructure("CONTROL", id);
        }

        private static void BuildVolume(
            Structure fixation, Image img, double rawThreshold, bool keepAtOrAbove,
            IList<Structure> eraseStructures, Structure belowReference,
            double minComponentCc, bool fillGaps, Action<string> log)
        {
            int nx = img.XSize, ny = img.YSize, nz = img.ZSize;
            int planeSize = nx * ny;
            byte[] vol = new byte[planeSize * nz];
            int[,] plane = new int[nx, ny];
            int progressEvery = Math.Max(1, nz / 5);
            int closeRadius = fillGaps ? FillCloseRadiusPx : CloseRadiusPx;

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

                    // Erase each structure's interior (fill its contours with 0).
                    var erasePolys = new List<Point[][]>();
                    foreach (Structure s in eraseStructures)
                    {
                        Point[][] polys = ToPixelPolygons(img, s.GetContoursOnImagePlane(k));
                        if (polys.Length > 0)
                        {
                            Cv2.FillPoly(slice, polys, Scalar.All(0));
                            erasePolys.Add(polys);
                        }
                    }

                    if (closeRadius > 0)
                    {
                        int d = 2 * closeRadius + 1;
                        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(d, d)))
                            Cv2.MorphologyEx(slice, slice, MorphTypes.Close, kernel);
                        foreach (Point[][] polys in erasePolys)   // undo close bleed into erased regions
                            Cv2.FillPoly(slice, polys, Scalar.All(0));
                    }

                    // Keep only voxels below (posterior to) the reference structure, per column.
                    if (belowReference != null)
                    {
                        Point[][] refPolys = ToPixelPolygons(img, belowReference.GetContoursOnImagePlane(k));
                        KeepBelowReference(slice, refPolys, nx, ny);
                    }

                    CopySliceIntoVolume(slice, vol, k * planeSize, nx, ny);
                }
            }

            if (minComponentCc > 0)
            {
                double voxelCc = img.XRes * img.YRes * img.ZRes / 1000.0;
                int minVoxels = Math.Max(1, (int)(minComponentCc / voxelCc));
                log($"  Filtering 3D components (min {minComponentCc:0.##} cc = {minVoxels} vox)...");
                RemoveSmallComponents3D(vol, nx, ny, nz, minVoxels, log);
            }
            else
            {
                log("  3D component filter disabled (keeping all voxels).");
            }

            log("  Writing contours...");
            int written = 0;
            for (int k = 0; k < nz; k++)
            {
                using (Mat slice = SliceFromVolume(vol, k * planeSize, nx, ny))
                {
                    if (Cv2.CountNonZero(slice) == 0) continue;
                    // External = outer contours only, so interiors stay solid (no holes carved into
                    // the structure from small gaps in the mask).
                    Point[][] contours = Cv2.FindContoursAsArray(
                        slice, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
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

        // Zero any mask pixel that is not strictly below the reference on its column. "Below" = larger
        // pixel-row = posterior for head-first-supine; flip the comparison if your orientation differs.
        private static void KeepBelowReference(Mat slice, Point[][] refPolys, int nx, int ny)
        {
            if (refPolys.Length == 0) { slice.SetTo(Scalar.All(0)); return; } // no reference here -> nothing below

            using (Mat refMask = new Mat(ny, nx, MatType.CV_8UC1, Scalar.All(0)))
            {
                Cv2.FillPoly(refMask, refPolys, Scalar.All(255));
                unsafe
                {
                    byte* rp = (byte*)refMask.DataPointer; long rstep = refMask.Step();
                    byte* sp = (byte*)slice.DataPointer; long sstep = slice.Step();
                    for (int x = 0; x < nx; x++)
                    {
                        int bottom = -1;
                        for (int y = 0; y < ny; y++)
                            if (rp[y * rstep + x] != 0) bottom = y;   // lowest reference row in this column

                        int cut = bottom < 0 ? ny - 1 : bottom;       // no reference -> clear the column
                        for (int y = 0; y <= cut; y++)
                            sp[y * sstep + x] = 0;                     // keep only rows strictly below 'bottom'
                    }
                }
            }
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
