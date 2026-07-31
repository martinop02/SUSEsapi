using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace StructureCompareAutoMatch
{
    /// <summary>One ground-truth vs AI comparison (one CSV row), with its metrics or a skip note.</summary>
    public sealed class ComparisonRow
    {
        public int Group { get; set; }
        public string MatchKey { get; set; }
        public string GtSetId { get; set; }
        public string GtStructureId { get; set; }
        public string AiSetId { get; set; }
        public string AiStructureId { get; set; }
        public MetricResult Metrics { get; set; }   // null when not computed
        public string Note { get; set; }
    }

    /// <summary>
    /// Turns matched groups into ground-truth vs AI comparison rows and computes the metrics. For
    /// each group the ground-truth structure (the set whose name lacks "auto") is compared against
    /// every AI structure in the group. Pairs on different image grids, empty structures, or
    /// oversized structures are recorded with a note instead of metrics.
    /// </summary>
    public static class Comparison
    {
        // Safety cap on the shared bounding box (voxels) so a body/couch match can't blow up memory.
        private const long VoxelCap = 25_000_000L;

        public static List<ComparisonRow> Build(IReadOnlyList<MatchGroup> groups, GatherResult gather,
                                                Action<string> log = null)
        {
            var rows = new List<ComparisonRow>();
            int groupNumber = 0;

            foreach (MatchGroup group in groups)
            {
                groupNumber++;
                List<MatchMember> gt = group.Members.Where(m => m.Role == AutoMatcher.GroundTruthRole).ToList();
                List<MatchMember> ai = group.Members.Where(m => m.Role == AutoMatcher.AiRole).ToList();

                if (gt.Count == 0)
                {
                    foreach (MatchMember m in ai)
                        rows.Add(new ComparisonRow
                        {
                            Group = groupNumber, MatchKey = group.Key,
                            AiSetId = m.SetId, AiStructureId = m.StructureId,
                            Note = "no ground-truth structure in this group",
                        });
                    continue;
                }

                MatchMember gtm = gt[0];   // exactly one ground truth is expected
                foreach (MatchMember aim in ai)
                {
                    var row = new ComparisonRow
                    {
                        Group = groupNumber, MatchKey = group.Key,
                        GtSetId = gtm.SetId, GtStructureId = gtm.StructureId,
                        AiSetId = aim.SetId, AiStructureId = aim.StructureId,
                    };
                    rows.Add(row);

                    try { ComputeInto(row, gather, log); }
                    catch (Exception ex) { row.Note = "error: " + ex.Message; }
                }
            }

            return rows;
        }

        private static void ComputeInto(ComparisonRow row, GatherResult gather, Action<string> log)
        {
            StructureHandle hGt = gather.Handle(row.GtSetId, row.GtStructureId);
            StructureHandle hAi = gather.Handle(row.AiSetId, row.AiStructureId);
            if (hGt == null || hAi == null) { row.Note = "structure handle missing"; return; }

            // Both structures are rasterized onto the ground truth's image grid. That only makes sense
            // when they share a frame of reference (same coordinate system); otherwise a registration
            // would be needed, which is out of scope.
            if (hGt.Image == null)
            {
                row.Note = "ground-truth set has no image to rasterize on";
                return;
            }
            if (hGt.ForUid == null || hAi.ForUid == null || hGt.ForUid != hAi.ForUid)
            {
                row.Note = "ground truth and AI are in different frames of reference";
                return;
            }

            log?.Invoke($"Metrics: {row.MatchKey}  {row.GtStructureId} vs {row.AiStructureId} ...");

            VoxelMask mGt, mAi;
            string note;
            if (!Rasterizer.TryBuild(hGt.Structure, hAi.Structure, hGt.Image, VoxelCap, out mGt, out mAi, out note))
            {
                row.Note = note;
                return;
            }

            MetricResult m = Metrics.Compute(mGt, mAi);
            row.Metrics = m;
            if (!m.Valid) row.Note = m.Note;
        }

        /// <summary>
        /// Writes the comparison rows as a semicolon-delimited CSV. UTF-8 with BOM so Excel (with ';'
        /// as the list separator) opens it cleanly. Returns the number of data rows written.
        /// </summary>
        public static int WriteCsv(string path, IReadOnlyList<ComparisonRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("Group;MatchKey;GroundTruthSet;GroundTruthStructure;AiSet;AiStructure;");
            sb.Append("DICE;Jaccard;Hausdorff_mm;HD95_mm;ASSD_mm;");
            sb.Append("Volume_GT_cc;Volume_AI_cc;VolumeDiff_cc;COMdiff_mm;Note\r\n");

            foreach (ComparisonRow r in rows)
            {
                sb.Append(r.Group.ToString(CultureInfo.InvariantCulture)); sb.Append(';');
                sb.Append(Escape(r.MatchKey)); sb.Append(';');
                sb.Append(Escape(r.GtSetId)); sb.Append(';');
                sb.Append(Escape(r.GtStructureId)); sb.Append(';');
                sb.Append(Escape(r.AiSetId)); sb.Append(';');
                sb.Append(Escape(r.AiStructureId)); sb.Append(';');

                MetricResult m = r.Metrics;
                bool ok = m != null && m.Valid;
                sb.Append(ok ? F(m.Dice) : ""); sb.Append(';');
                sb.Append(ok ? F(m.Jaccard) : ""); sb.Append(';');
                sb.Append(ok ? F(m.HausdorffMm) : ""); sb.Append(';');
                sb.Append(ok ? F(m.Hd95Mm) : ""); sb.Append(';');
                sb.Append(ok ? F(m.AssdMm) : ""); sb.Append(';');
                sb.Append(ok ? F(m.VolumeGtCc) : ""); sb.Append(';');
                sb.Append(ok ? F(m.VolumeAiCc) : ""); sb.Append(';');
                sb.Append(ok ? F(m.VolumeDiffCc) : ""); sb.Append(';');
                sb.Append(ok ? F(m.ComDiffMm) : ""); sb.Append(';');
                sb.Append(Escape(r.Note));
                sb.Append("\r\n");
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
