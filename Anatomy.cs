using System.Collections.Generic;
using System.Linq;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Organs-at-risk that are always segmented (in addition to the prescription vertebrae),
    /// plus the lobe→lung merge rules. TotalSegmentator does not output whole lungs directly,
    /// so lung_left / lung_right are combined from their lobes with totalseg_combine_masks.
    /// </summary>
    public static class Anatomy
    {
        public static readonly List<string> Oars = new List<string>
        {
            "heart",
            "lung_left",
            "lung_right",
            "spinal_cord",
            "kidney_left",
            "kidney_right",
        };

        // final id -> the sub-structures that must be present in --roi_subset and then merged.
        public static readonly Dictionary<string, List<string>> MergeArgs =
            new Dictionary<string, List<string>>
            {
                { "lung_left",  new List<string> { "lung_upper_lobe_left", "lung_lower_lobe_left" } },
                { "lung_right", new List<string> { "lung_upper_lobe_right", "lung_middle_lobe_right", "lung_lower_lobe_right" } },
            };

        /// <summary>
        /// Expands a list of final structure ids into the --roi_subset TotalSegmentator must run
        /// on: mergeable structures are replaced by their sub-structures, everything else passes
        /// through unchanged. Duplicates are removed.
        /// </summary>
        public static List<string> ExpandRoiSubset(IEnumerable<string> finalIds)
        {
            var roi = new List<string>();
            foreach (string id in finalIds)
            {
                List<string> parts;
                if (MergeArgs.TryGetValue(id, out parts)) roi.AddRange(parts);
                else roi.Add(id);
            }
            return roi.Distinct().ToList();
        }
    }
}
