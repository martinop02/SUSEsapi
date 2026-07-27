using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StructureCompareAutoMatch
{
    /// <summary>One structure that belongs to a matched group.</summary>
    public sealed class MatchMember
    {
        public string SetId { get; set; }
        public string StructureId { get; set; }
        public bool IsEmpty { get; set; }

        /// <summary><see cref="AutoMatcher.GroundTruthRole"/> or <see cref="AutoMatcher.AiRole"/>.</summary>
        public string Role { get; set; }
    }

    /// <summary>
    /// A set of structures across the patient that normalize to the same organ (its
    /// <see cref="Key"/>). Only groups spanning two or more structure sets are kept — a structure
    /// with no counterpart in another set is not a match and is dropped.
    /// </summary>
    public sealed class MatchGroup
    {
        public string Key { get; set; }
        public List<MatchMember> Members { get; set; } = new List<MatchMember>();

        public int SetCount
        {
            get { return Members.Select(m => m.SetId).Distinct().Count(); }
        }
    }

    /// <summary>
    /// Groups every structure on the patient by <see cref="OrganNameMatcher"/> key and keeps the
    /// groups that appear in at least two structure sets. Unmatched structures are ignored.
    /// </summary>
    public static class AutoMatcher
    {
        public const string GroundTruthRole = "GroundTruth";
        public const string AiRole = "AI";

        // Site convention: the manual/reference set's name does NOT contain "auto"; the AI-segmented
        // sets do. So the ground truth is simply the set(s) without "auto" in the id.
        public static bool IsAuto(string setId)
        {
            return (setId ?? string.Empty).IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string RoleOf(string setId)
        {
            return IsAuto(setId) ? AiRole : GroundTruthRole;
        }

        public static List<MatchGroup> Match(IEnumerable<StructureSetInfo> sets)
        {
            var byKey = new Dictionary<string, MatchGroup>(StringComparer.Ordinal);

            foreach (StructureSetInfo set in sets)
            {
                foreach (StructureInfo s in set.Structures)
                {
                    string key = OrganNameMatcher.Normalize(s.Id);
                    if (key == null) continue;

                    if (!byKey.TryGetValue(key, out MatchGroup group))
                    {
                        group = new MatchGroup { Key = key };
                        byKey[key] = group;
                    }
                    group.Members.Add(new MatchMember
                    {
                        SetId = set.Id,
                        StructureId = s.Id,
                        IsEmpty = s.IsEmpty,
                        Role = RoleOf(set.Id),
                    });
                }
            }

            // A real match spans >= 2 different structure sets. Order groups by key; members by set.
            return byKey.Values
                .Where(g => g.SetCount >= 2)
                .Select(g =>
                {
                    g.Members = g.Members
                        .OrderByDescending(m => m.Role == GroundTruthRole)   // ground truth first
                        .ThenBy(m => m.SetId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => m.StructureId, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return g;
                })
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Writes the matched groups as a semicolon-delimited CSV with columns
        /// <c>Group;MatchKey;StructureSetId;StructureId</c>. Rows that share a Group are the same
        /// organ matched across sets. UTF-8 with BOM so Excel (with ';' as the list separator) opens
        /// it cleanly. Returns the number of data rows written.
        /// </summary>
        public static int WriteCsv(string path, IReadOnlyList<MatchGroup> groups)
        {
            var sb = new StringBuilder();
            sb.Append("Group;MatchKey;Role;StructureSetId;StructureId\r\n");

            int rows = 0;
            int groupNumber = 0;
            foreach (MatchGroup group in groups)
            {
                groupNumber++;
                foreach (MatchMember m in group.Members)
                {
                    sb.Append(groupNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(';');
                    sb.Append(Escape(group.Key));
                    sb.Append(';');
                    sb.Append(m.Role);
                    sb.Append(';');
                    sb.Append(Escape(m.SetId));
                    sb.Append(';');
                    sb.Append(Escape(m.StructureId));
                    sb.Append("\r\n");
                    rows++;
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return rows;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace(';', ',');
        }
    }
}
