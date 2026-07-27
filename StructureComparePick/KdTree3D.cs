using System;
using System.Collections.Generic;

namespace StructureComparePick
{
    /// <summary>
    /// Minimal static 3D k-d tree for nearest-neighbour queries. Built once over a point set, then
    /// queried for the squared distance to the closest point. Used for the surface-distance metrics
    /// (Hausdorff / HD95 / ASSD): the nearest border voxel of one structure to the border of the
    /// other. Exact nearest neighbour — identical results to a brute-force scan, just far faster.
    /// </summary>
    public sealed class KdTree3D
    {
        private readonly List<double> _x = new List<double>();
        private readonly List<double> _y = new List<double>();
        private readonly List<double> _z = new List<double>();
        private readonly List<int> _axis = new List<int>();
        private readonly List<int> _left = new List<int>();
        private readonly List<int> _right = new List<int>();
        private readonly int _root;

        public KdTree3D(IReadOnlyList<double[]> points)
        {
            int n = points.Count;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            _root = Build(points, idx, 0, n, 0);
        }

        private int Build(IReadOnlyList<double[]> pts, int[] idx, int lo, int hi, int depth)
        {
            if (lo >= hi) return -1;
            int axis = depth % 3;
            int count = hi - lo;
            // Sort this sub-range by the split axis and take the median as the node.
            Array.Sort(idx, lo, count, Comparer<int>.Create((a, b) => pts[a][axis].CompareTo(pts[b][axis])));
            int mid = lo + count / 2;
            int p = idx[mid];

            int node = _x.Count;
            _x.Add(pts[p][0]); _y.Add(pts[p][1]); _z.Add(pts[p][2]);
            _axis.Add(axis); _left.Add(-1); _right.Add(-1);

            int l = Build(pts, idx, lo, mid, depth + 1);
            int r = Build(pts, idx, mid + 1, hi, depth + 1);
            _left[node] = l;
            _right[node] = r;
            return node;
        }

        /// <summary>Squared distance from (qx,qy,qz) to the nearest stored point.</summary>
        public double NearestSquared(double qx, double qy, double qz)
        {
            double best = double.PositiveInfinity;
            Search(_root, qx, qy, qz, ref best);
            return best;
        }

        private void Search(int node, double qx, double qy, double qz, ref double best)
        {
            while (node >= 0)
            {
                double dx = qx - _x[node], dy = qy - _y[node], dz = qz - _z[node];
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < best) best = d2;

                int axis = _axis[node];
                double diff = axis == 0 ? dx : (axis == 1 ? dy : dz);
                int near, far;
                if (diff < 0) { near = _left[node]; far = _right[node]; }
                else { near = _right[node]; far = _left[node]; }

                // Descend the near side iteratively; only recurse into the far side if the splitting
                // plane is closer than the best distance found so far.
                if (diff * diff < best) Search(far, qx, qy, qz, ref best);
                node = near;
            }
        }
    }
}
