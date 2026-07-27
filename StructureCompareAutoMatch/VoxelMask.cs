using System.Collections.Generic;

namespace StructureCompareAutoMatch
{
    /// <summary>
    /// A binary 3D mask over an axis-aligned sub-box of an image's voxel grid. Two masks built for
    /// the same comparison share the same box (origin + dims + spacing), so they are index-for-index
    /// aligned and overlap counts are a straight element-wise scan.
    ///
    /// Coordinates: local indices 0..N-1 within the box; the box sits at (Ox,Oy,Oz) in full-image
    /// voxels. Spacing (Sx,Sy,Sz) is mm per voxel along the image's orthonormal X/Y/Z directions,
    /// so a Euclidean distance in spacing-scaled local coordinates equals the true mm distance.
    /// </summary>
    public sealed class VoxelMask
    {
        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }
        public int Ox { get; }
        public int Oy { get; }
        public int Oz { get; }
        public double Sx { get; }
        public double Sy { get; }
        public double Sz { get; }

        private readonly bool[] _d;

        public VoxelMask(int ox, int oy, int oz, int nx, int ny, int nz, double sx, double sy, double sz)
        {
            Ox = ox; Oy = oy; Oz = oz;
            Nx = nx; Ny = ny; Nz = nz;
            Sx = sx; Sy = sy; Sz = sz;
            _d = new bool[nx * ny * nz];
        }

        private int I(int x, int y, int z) { return x + Nx * (y + Ny * z); }

        public void Set(int x, int y, int z) { _d[I(x, y, z)] = true; }
        public bool At(int x, int y, int z) { return _d[I(x, y, z)]; }
        public bool[] Raw { get { return _d; } }

        public double VoxelVolumeCc { get { return Sx * Sy * Sz / 1000.0; } }

        public int Count()
        {
            int c = 0;
            for (int i = 0; i < _d.Length; i++) if (_d[i]) c++;
            return c;
        }

        /// <summary>Centroid in spacing-scaled (mm) local coordinates, or null if the mask is empty.</summary>
        public double[] CentroidScaled()
        {
            long c = 0;
            double sx = 0, sy = 0, sz = 0;
            for (int z = 0; z < Nz; z++)
                for (int y = 0; y < Ny; y++)
                    for (int x = 0; x < Nx; x++)
                        if (_d[I(x, y, z)]) { sx += x; sy += y; sz += z; c++; }
            if (c == 0) return null;
            return new[] { sx / c * Sx, sy / c * Sy, sz / c * Sz };
        }

        /// <summary>
        /// Border voxels (a foreground voxel with at least one background or out-of-box 6-neighbour),
        /// as spacing-scaled (mm) coordinates. Matches scipy/medpy's connectivity-1 surface. The box
        /// is padded by a voxel around the structures, so treating out-of-box as background is safe.
        /// </summary>
        public List<double[]> BorderScaled()
        {
            var pts = new List<double[]>();
            for (int z = 0; z < Nz; z++)
                for (int y = 0; y < Ny; y++)
                    for (int x = 0; x < Nx; x++)
                    {
                        if (!_d[I(x, y, z)]) continue;
                        if (x == 0 || y == 0 || z == 0 || x == Nx - 1 || y == Ny - 1 || z == Nz - 1
                            || !_d[I(x - 1, y, z)] || !_d[I(x + 1, y, z)]
                            || !_d[I(x, y - 1, z)] || !_d[I(x, y + 1, z)]
                            || !_d[I(x, y, z - 1)] || !_d[I(x, y, z + 1)])
                        {
                            pts.Add(new[] { x * Sx, y * Sy, z * Sz });
                        }
                    }
            return pts;
        }
    }
}
