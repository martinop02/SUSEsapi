using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace StructureCompareAutoMatch
{
    /// <summary>
    /// Rasterizes structures to binary voxel masks on their shared image grid. Both structures of a
    /// comparison must be drawn on the same image (same voxel grid); their per-plane DICOM contours
    /// are filled with an even-odd scanline fill, which handles holes and multiple islands.
    /// </summary>
    public static class Rasterizer
    {
        // One planar contour, converted to fractional (column,row) voxel coordinates on the image.
        private sealed class PlaneContour
        {
            public int Z;               // image plane index
            public double[][] Pts;      // each: { col, row }
        }

        /// <summary>
        /// Builds aligned masks for <paramref name="gt"/> and <paramref name="ai"/> over the union of
        /// their bounding boxes on <paramref name="image"/>. Returns false (with a reason) when a
        /// structure is empty or the shared box would exceed <paramref name="voxelCap"/> voxels.
        /// </summary>
        public static bool TryBuild(Structure gt, Structure ai, Image image, long voxelCap,
                                    out VoxelMask maskGt, out VoxelMask maskAi, out string note)
        {
            maskGt = null; maskAi = null; note = null;

            int minC = int.MaxValue, minR = int.MaxValue, minZ = int.MaxValue;
            int maxC = int.MinValue, maxR = int.MinValue, maxZ = int.MinValue;

            List<PlaneContour> cg = Collect(gt, image, ref minC, ref minR, ref minZ, ref maxC, ref maxR, ref maxZ);
            List<PlaneContour> ca = Collect(ai, image, ref minC, ref minR, ref minZ, ref maxC, ref maxR, ref maxZ);

            if (cg.Count == 0 || ca.Count == 0)
            {
                note = "empty structure (no contours)";
                return false;
            }

            // Pad by one voxel so every foreground voxel has an in-box background neighbour, then clamp.
            int ox = Math.Max(0, minC - 1);
            int oy = Math.Max(0, minR - 1);
            int oz = Math.Max(0, minZ - 1);
            int ex = Math.Min(image.XSize - 1, maxC + 1);
            int ey = Math.Min(image.YSize - 1, maxR + 1);
            int ez = Math.Min(image.ZSize - 1, maxZ + 1);

            int nx = ex - ox + 1, ny = ey - oy + 1, nz = ez - oz + 1;
            if (nx <= 0 || ny <= 0 || nz <= 0) { note = "degenerate bounding box"; return false; }
            if ((long)nx * ny * nz > voxelCap) { note = $"structure too large ({(long)nx * ny * nz:N0} voxels)"; return false; }

            double sx = image.XRes, sy = image.YRes, sz = image.ZRes;
            maskGt = new VoxelMask(ox, oy, oz, nx, ny, nz, sx, sy, sz);
            maskAi = new VoxelMask(ox, oy, oz, nx, ny, nz, sx, sy, sz);

            Fill(cg, maskGt);
            Fill(ca, maskAi);
            return true;
        }

        // Reads a structure's contours per image plane and converts each vertex from DICOM mm to
        // fractional (column,row) voxel coordinates, tracking the overall bounding box.
        private static List<PlaneContour> Collect(Structure s, Image image,
            ref int minC, ref int minR, ref int minZ, ref int maxC, ref int maxR, ref int maxZ)
        {
            var result = new List<PlaneContour>();
            VVector origin = image.Origin;
            VVector xdir = image.XDirection;
            VVector ydir = image.YDirection;
            double xres = image.XRes, yres = image.YRes;

            for (int z = 0; z < image.ZSize; z++)
            {
                VVector[][] contours;
                try { contours = s.GetContoursOnImagePlane(z); }
                catch { continue; }
                if (contours == null) continue;

                foreach (VVector[] poly in contours)
                {
                    if (poly == null || poly.Length < 3) continue;
                    var pts = new double[poly.Length][];
                    for (int i = 0; i < poly.Length; i++)
                    {
                        double rx = poly[i].x - origin.x;
                        double ry = poly[i].y - origin.y;
                        double rz = poly[i].z - origin.z;
                        double col = (rx * xdir.x + ry * xdir.y + rz * xdir.z) / xres;
                        double row = (rx * ydir.x + ry * ydir.y + rz * ydir.z) / yres;
                        pts[i] = new[] { col, row };

                        int ci = (int)Math.Floor(col), ri = (int)Math.Floor(row);
                        if (ci < minC) minC = ci;
                        if (ri < minR) minR = ri;
                        if (ci + 1 > maxC) maxC = ci + 1;
                        if (ri + 1 > maxR) maxR = ri + 1;
                    }
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                    result.Add(new PlaneContour { Z = z, Pts = pts });
                }
            }
            return result;
        }

        // Even-odd scanline fill of all contours on each plane into the mask (local coordinates).
        private static void Fill(List<PlaneContour> contours, VoxelMask mask)
        {
            // Group contours by plane.
            var byPlane = new Dictionary<int, List<double[][]>>();
            foreach (PlaneContour c in contours)
            {
                if (!byPlane.TryGetValue(c.Z, out List<double[][]> list))
                {
                    list = new List<double[][]>();
                    byPlane[c.Z] = list;
                }
                list.Add(c.Pts);
            }

            foreach (KeyValuePair<int, List<double[][]>> kv in byPlane)
            {
                int lz = kv.Key - mask.Oz;
                if (lz < 0 || lz >= mask.Nz) continue;

                var xs = new List<double>();
                for (int yy = 0; yy < mask.Ny; yy++)
                {
                    double Y = yy;
                    xs.Clear();

                    foreach (double[][] poly in kv.Value)
                    {
                        int n = poly.Length;
                        for (int i = 0; i < n; i++)
                        {
                            double[] a = poly[i];
                            double[] b = poly[(i + 1) % n];
                            double x0 = a[0] - mask.Ox, y0 = a[1] - mask.Oy;
                            double x1 = b[0] - mask.Ox, y1 = b[1] - mask.Oy;

                            // Half-open crossing rule: include the lower endpoint, exclude the upper.
                            bool crosses = (y0 <= Y && Y < y1) || (y1 <= Y && Y < y0);
                            if (!crosses) continue;
                            double t = (Y - y0) / (y1 - y0);
                            xs.Add(x0 + t * (x1 - x0));
                        }
                    }

                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int i = 0; i + 1 < xs.Count; i += 2)
                    {
                        int xStart = (int)Math.Ceiling(xs[i]);
                        int xEnd = (int)Math.Floor(xs[i + 1]);
                        if (xStart < 0) xStart = 0;
                        if (xEnd > mask.Nx - 1) xEnd = mask.Nx - 1;
                        for (int x = xStart; x <= xEnd; x++) mask.Set(x, yy, lz);
                    }
                }
            }
        }
    }
}
