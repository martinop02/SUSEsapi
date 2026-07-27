using System;
using System.Windows.Media.Media3D;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace StructureCompareAutoMatch
{
    /// <summary>
    /// Rasterizes structures to binary voxel masks on a common reference grid using ESAPI's own
    /// point-in-structure test (<see cref="Structure.IsPointInsideSegment"/>) — the reliable method
    /// used elsewhere in this repo (see PalliativeAutoPlan/Isocenter.cs). Because the test takes an
    /// absolute DICOM point, both structures can be sampled onto the same grid as long as they share
    /// a frame of reference, even if they were drawn on different image instances or at high
    /// resolution. Voxel &lt;-&gt; DICOM uses the image direction cosines, exactly as
    /// CouchFixationTest/FixationGear.cs does.
    /// </summary>
    public static class Rasterizer
    {
        /// <summary>
        /// Builds aligned masks for <paramref name="gt"/> and <paramref name="ai"/> over the union of
        /// their bounding boxes on <paramref name="reference"/>'s grid. Returns false (with a reason)
        /// when a structure has no segment / mesh, or the shared box would exceed
        /// <paramref name="voxelCap"/> voxels.
        /// </summary>
        public static bool TryBuild(Structure gt, Structure ai, Image reference, long voxelCap,
                                    out VoxelMask maskGt, out VoxelMask maskAi, out string note)
        {
            maskGt = null; maskAi = null; note = null;

            if (!gt.HasSegment || !ai.HasSegment) { note = "empty structure (no segment)"; return false; }

            int gc0, gr0, gs0, gc1, gr1, gs1;
            int ac0, ar0, as0, ac1, ar1, as1;
            if (!BoundsToIndex(reference, gt, out gc0, out gr0, out gs0, out gc1, out gr1, out gs1) ||
                !BoundsToIndex(reference, ai, out ac0, out ar0, out as0, out ac1, out ar1, out as1))
            {
                note = "no mesh geometry / bounds unavailable";
                return false;
            }

            // Union of the two boxes, padded by a voxel (so every foreground voxel has a background
            // neighbour inside the box), clamped to the image.
            int ox = Math.Max(0, Math.Min(gc0, ac0) - 1);
            int oy = Math.Max(0, Math.Min(gr0, ar0) - 1);
            int oz = Math.Max(0, Math.Min(gs0, as0) - 1);
            int ex = Math.Min(reference.XSize - 1, Math.Max(gc1, ac1) + 1);
            int ey = Math.Min(reference.YSize - 1, Math.Max(gr1, ar1) + 1);
            int ez = Math.Min(reference.ZSize - 1, Math.Max(gs1, as1) + 1);

            int nx = ex - ox + 1, ny = ey - oy + 1, nz = ez - oz + 1;
            if (nx <= 0 || ny <= 0 || nz <= 0) { note = "degenerate bounding box"; return false; }
            if ((long)nx * ny * nz > voxelCap) { note = $"structure too large ({(long)nx * ny * nz:N0} voxels)"; return false; }

            double xr = reference.XRes, yr = reference.YRes, zr = reference.ZRes;
            maskGt = new VoxelMask(ox, oy, oz, nx, ny, nz, xr, yr, zr);
            maskAi = new VoxelMask(ox, oy, oz, nx, ny, nz, xr, yr, zr);

            // Step vectors (mm) for one voxel along each image axis, and the DICOM position of the
            // box's origin voxel. Then each voxel centre is base + x*xStep + y*yStep + z*zStep.
            VVector xStep = xr * reference.XDirection;
            VVector yStep = yr * reference.YDirection;
            VVector zStep = zr * reference.ZDirection;
            VVector baseP = reference.Origin + (double)ox * xStep + (double)oy * yStep + (double)oz * zStep;

            // Local index bounds of each structure (only test a structure where its box covers).
            int lgc0 = gc0 - ox, lgr0 = gr0 - oy, lgs0 = gs0 - oz, lgc1 = gc1 - ox, lgr1 = gr1 - oy, lgs1 = gs1 - oz;
            int lac0 = ac0 - ox, lar0 = ar0 - oy, las0 = as0 - oz, lac1 = ac1 - ox, lar1 = ar1 - oy, las1 = as1 - oz;

            for (int z = 0; z < nz; z++)
            {
                bool zInGt = z >= lgs0 && z <= lgs1;
                bool zInAi = z >= las0 && z <= las1;
                if (!zInGt && !zInAi) continue;

                for (int y = 0; y < ny; y++)
                {
                    bool yInGt = zInGt && y >= lgr0 && y <= lgr1;
                    bool yInAi = zInAi && y >= lar0 && y <= lar1;
                    if (!yInGt && !yInAi) continue;

                    // DICOM position of voxel (0,y,z); advance by xStep along x within the row.
                    double sx = baseP.x + y * yStep.x + z * zStep.x;
                    double syv = baseP.y + y * yStep.y + z * zStep.y;
                    double sz = baseP.z + y * yStep.z + z * zStep.z;

                    for (int x = 0; x < nx; x++)
                    {
                        double qx = sx + x * xStep.x;
                        double qy = syv + x * xStep.y;
                        double qz = sz + x * xStep.z;

                        bool inGtBox = yInGt && x >= lgc0 && x <= lgc1;
                        bool inAiBox = yInAi && x >= lac0 && x <= lac1;
                        if (!inGtBox && !inAiBox) continue;

                        var p = new VVector(qx, qy, qz);
                        if (inGtBox && gt.IsPointInsideSegment(p)) maskGt.Set(x, y, z);
                        if (inAiBox && ai.IsPointInsideSegment(p)) maskAi.Set(x, y, z);
                    }
                }
            }

            return true;
        }

        // Structure bounding box (DICOM mm) -> inclusive voxel-index box on the image grid. Converts
        // all eight corners with the direction cosines so it stays correct for rotated orientations.
        private static bool BoundsToIndex(Image img, Structure s,
            out int c0, out int r0, out int s0, out int c1, out int r1, out int s1)
        {
            c0 = r0 = s0 = int.MaxValue;
            c1 = r1 = s1 = int.MinValue;

            Rect3D b;
            try { b = s.MeshGeometry.Bounds; }
            catch { return false; }
            if (b.IsEmpty) return false;

            VVector origin = img.Origin, xd = img.XDirection, yd = img.YDirection, zd = img.ZDirection;
            double xr = img.XRes, yr = img.YRes, zr = img.ZRes;

            for (int i = 0; i < 8; i++)
            {
                double X = b.X + ((i & 1) != 0 ? b.SizeX : 0);
                double Y = b.Y + ((i & 2) != 0 ? b.SizeY : 0);
                double Z = b.Z + ((i & 4) != 0 ? b.SizeZ : 0);

                double rx = X - origin.x, ry = Y - origin.y, rz = Z - origin.z;
                double col = (rx * xd.x + ry * xd.y + rz * xd.z) / xr;
                double row = (rx * yd.x + ry * yd.y + rz * yd.z) / yr;
                double sl = (rx * zd.x + ry * zd.y + rz * zd.z) / zr;

                int ci = (int)Math.Floor(col), cj = (int)Math.Ceiling(col);
                int ri = (int)Math.Floor(row), rj = (int)Math.Ceiling(row);
                int si = (int)Math.Floor(sl), sj = (int)Math.Ceiling(sl);

                if (ci < c0) c0 = ci; if (cj > c1) c1 = cj;
                if (ri < r0) r0 = ri; if (rj > r1) r1 = rj;
                if (si < s0) s0 = si; if (sj > s1) s1 = sj;
            }

            // Clamp to the image.
            c0 = Math.Max(0, c0); r0 = Math.Max(0, r0); s0 = Math.Max(0, s0);
            c1 = Math.Min(img.XSize - 1, c1); r1 = Math.Min(img.YSize - 1, r1); s1 = Math.Min(img.ZSize - 1, s1);
            return c1 >= c0 && r1 >= r0 && s1 >= s0;
        }
    }
}
