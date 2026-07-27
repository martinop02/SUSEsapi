using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace StructureComparePick
{
    /// <summary>One matched organ: the ground-truth structure vs the compare structure, with metrics.</summary>
    public sealed class PairRow
    {
        public string MatchKey { get; set; }
        public string GtStructureId { get; set; }
        public string CmpStructureId { get; set; }
        public MetricResult Metrics { get; set; }   // null when not computed
        public string Note { get; set; }
    }

    /// <summary>
    /// Compares two chosen structure sets: matches their organs by name (<see cref="OrganNameMatcher"/>)
    /// and computes the metrics for each organ present in both. Organs in only one set are ignored.
    /// The first set is the reference (ground truth); volume difference and COM are measured against it.
    /// </summary>
    public static class PairCompare
    {
        private const long VoxelCap = 25_000_000L;

        public static List<PairRow> Build(StructureSetInfo gtSet, StructureSetInfo cmpSet,
                                          GatherResult gather, Action<string> log = null)
        {
            Dictionary<string, StructureInfo> gtByKey = KeyByOrgan(gtSet);
            Dictionary<string, StructureInfo> cmpByKey = KeyByOrgan(cmpSet);

            var rows = new List<PairRow>();
            foreach (KeyValuePair<string, StructureInfo> kv in gtByKey
                         .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!cmpByKey.TryGetValue(kv.Key, out StructureInfo cmp)) continue;   // no match -> ignore

                var row = new PairRow
                {
                    MatchKey = kv.Key,
                    GtStructureId = kv.Value.Id,
                    CmpStructureId = cmp.Id,
                };
                rows.Add(row);

                try { ComputeInto(row, gtSet, cmpSet, gather, log); }
                catch (Exception ex) { row.Note = "error: " + ex.Message; }
            }
            return rows;
        }

        private static Dictionary<string, StructureInfo> KeyByOrgan(StructureSetInfo set)
        {
            var d = new Dictionary<string, StructureInfo>(StringComparer.Ordinal);
            foreach (StructureInfo s in set.Structures)
            {
                string key = OrganNameMatcher.Normalize(s.Id);
                if (key == null) continue;
                if (!d.ContainsKey(key)) d[key] = s;   // first occurrence wins
            }
            return d;
        }

        private static void ComputeInto(PairRow row, StructureSetInfo gtSet, StructureSetInfo cmpSet,
                                        GatherResult gather, Action<string> log)
        {
            StructureHandle hGt = gather.Handle(gtSet.Id, row.GtStructureId);
            StructureHandle hCmp = gather.Handle(cmpSet.Id, row.CmpStructureId);
            if (hGt == null || hCmp == null) { row.Note = "structure handle missing"; return; }
            if (hGt.Image == null) { row.Note = "ground-truth set has no image to rasterize on"; return; }
            if (hGt.ForUid == null || hCmp.ForUid == null || hGt.ForUid != hCmp.ForUid)
            {
                row.Note = "sets are in different frames of reference";
                return;
            }

            log?.Invoke($"Metrics: {row.MatchKey}  {row.GtStructureId} vs {row.CmpStructureId} ...");

            VoxelMask mGt, mCmp;
            string note;
            if (!Rasterizer.TryBuild(hGt.Structure, hCmp.Structure, hGt.Image, VoxelCap, out mGt, out mCmp, out note))
            {
                row.Note = note;
                return;
            }

            MetricResult m = Metrics.Compute(mGt, mCmp);
            row.Metrics = m;
            if (!m.Valid) row.Note = m.Note;
        }

        /// <summary>
        /// Writes the rows as a semicolon-delimited CSV (UTF-8 with BOM). Returns the row count.
        /// </summary>
        public static int WriteCsv(string path, StructureSetInfo gtSet, StructureSetInfo cmpSet,
                                   IReadOnlyList<PairRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("MatchKey;GroundTruthSet;GroundTruthStructure;CompareSet;CompareStructure;");
            sb.Append("DICE;Jaccard;Hausdorff_mm;HD95_mm;ASSD_mm;Volume_GT_cc;Volume_Cmp_cc;VolumeDiff_cc;COMdiff_mm;Note\r\n");

            foreach (PairRow r in rows)
            {
                MetricResult m = r.Metrics;
                bool ok = m != null && m.Valid;
                sb.Append(Escape(r.MatchKey)).Append(';');
                sb.Append(Escape(gtSet.Id)).Append(';');
                sb.Append(Escape(r.GtStructureId)).Append(';');
                sb.Append(Escape(cmpSet.Id)).Append(';');
                sb.Append(Escape(r.CmpStructureId)).Append(';');
                sb.Append(ok ? F(m.Dice) : "").Append(';');
                sb.Append(ok ? F(m.Jaccard) : "").Append(';');
                sb.Append(ok ? F(m.HausdorffMm) : "").Append(';');
                sb.Append(ok ? F(m.Hd95Mm) : "").Append(';');
                sb.Append(ok ? F(m.AssdMm) : "").Append(';');
                sb.Append(ok ? F(m.VolumeGtCc) : "").Append(';');
                sb.Append(ok ? F(m.VolumeAiCc) : "").Append(';');   // compare-set volume
                sb.Append(ok ? F(m.VolumeDiffCc) : "").Append(';');
                sb.Append(ok ? F(m.ComDiffMm) : "").Append(';');
                sb.Append(Escape(r.Note)).Append("\r\n");
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return rows.Count;
        }

        private static string F(double v)
        {
            return double.IsNaN(v) ? "" : v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace(';', ',');
        }
    }
}
