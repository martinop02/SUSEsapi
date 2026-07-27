using System;
using System.Collections.Generic;

namespace StructureComparePick
{
    /// <summary>The geometry metrics for one ground-truth vs AI structure pair.</summary>
    public sealed class MetricResult
    {
        public bool Valid { get; set; }
        public string Note { get; set; }

        public double Dice { get; set; }
        public double Jaccard { get; set; }
        public double HausdorffMm { get; set; }
        public double Hd95Mm { get; set; }
        public double AssdMm { get; set; }
        public double VolumeGtCc { get; set; }
        public double VolumeAiCc { get; set; }
        public double VolumeDiffCc { get; set; }   // AI - GT
        public double ComDiffMm { get; set; }
    }

    /// <summary>
    /// Computes the comparison metrics from two aligned voxel masks (ground truth vs AI). Overlap
    /// metrics (DICE, Jaccard, volumes, centre-of-mass) come from voxel counts; the surface-distance
    /// metrics (Hausdorff, HD95, ASSD) use nearest-neighbour distances between the masks' border
    /// voxels. The definitions match medpy.metric.binary (validated to ~1e-15 against a scipy EDT
    /// reference): hd = max of the two directed maxima; hd95 = 95th percentile (linear) of the two
    /// directed distance sets concatenated; assd = mean of the two directed means.
    /// </summary>
    public static class Metrics
    {
        public static MetricResult Compute(VoxelMask gt, VoxelMask ai)
        {
            var r = new MetricResult();

            bool[] g = gt.Raw, a = ai.Raw;
            int cg = 0, ca = 0, inter = 0, uni = 0;
            for (int i = 0; i < g.Length; i++)
            {
                bool bg = g[i], ba = a[i];
                if (bg) cg++;
                if (ba) ca++;
                if (bg && ba) inter++;
                if (bg || ba) uni++;
            }

            if (cg == 0 || ca == 0)
            {
                r.Valid = false;
                r.Note = "empty structure (no voxels)";
                return r;
            }

            double voxelVol = gt.VoxelVolumeCc;
            r.Dice = 2.0 * inter / (cg + ca);
            r.Jaccard = (double)inter / uni;
            r.VolumeGtCc = cg * voxelVol;
            r.VolumeAiCc = ca * voxelVol;
            r.VolumeDiffCc = r.VolumeAiCc - r.VolumeGtCc;

            double[] comG = gt.CentroidScaled();
            double[] comA = ai.CentroidScaled();
            double dx = comA[0] - comG[0], dy = comA[1] - comG[1], dz = comA[2] - comG[2];
            r.ComDiffMm = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            List<double[]> borderG = gt.BorderScaled();
            List<double[]> borderA = ai.BorderScaled();

            // Directed surface distances: each border voxel of one to the nearest of the other.
            var treeA = new KdTree3D(borderA);
            var treeG = new KdTree3D(borderG);

            double maxGA = 0, sumGA = 0;
            var distsGA = new List<double>(borderG.Count);
            foreach (double[] p in borderG)
            {
                double d = Math.Sqrt(treeA.NearestSquared(p[0], p[1], p[2]));
                distsGA.Add(d);
                sumGA += d;
                if (d > maxGA) maxGA = d;
            }

            double maxAG = 0, sumAG = 0;
            var distsAG = new List<double>(borderA.Count);
            foreach (double[] p in borderA)
            {
                double d = Math.Sqrt(treeG.NearestSquared(p[0], p[1], p[2]));
                distsAG.Add(d);
                sumAG += d;
                if (d > maxAG) maxAG = d;
            }

            r.HausdorffMm = Math.Max(maxGA, maxAG);

            var all = new List<double>(distsGA.Count + distsAG.Count);
            all.AddRange(distsGA);
            all.AddRange(distsAG);
            r.Hd95Mm = PercentileLinear(all, 95.0);

            r.AssdMm = (sumGA / distsGA.Count + sumAG / distsAG.Count) / 2.0;

            r.Valid = true;
            return r;
        }

        // numpy.percentile default ("linear") interpolation.
        private static double PercentileLinear(List<double> values, double p)
        {
            if (values.Count == 0) return double.NaN;
            values.Sort();
            if (values.Count == 1) return values[0];
            double rank = p / 100.0 * (values.Count - 1);
            int lo = (int)Math.Floor(rank);
            double frac = rank - lo;
            if (lo + 1 >= values.Count) return values[values.Count - 1];
            return values[lo] + frac * (values[lo + 1] - values[lo]);
        }
    }
}
