using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace StructureCompareLink
{
    /// <summary>
    /// A single structure inside a single set — the atom that links connect. Identity is
    /// (structure-set id, structure id); the same anatomical structure appears once per set.
    /// </summary>
    public sealed class LinkNode
    {
        public StructureSetInfo Set { get; }
        public StructureInfo Structure { get; }

        public LinkNode(StructureSetInfo set, StructureInfo structure)
        {
            Set = set;
            Structure = structure;
        }
    }

    /// <summary>An undirected link ("these are the same structure") between two nodes.</summary>
    public sealed class LinkEdge
    {
        public LinkNode A { get; }
        public LinkNode B { get; }

        /// <summary>True when the user drew it; false when auto-created from a name match.</summary>
        public bool Manual { get; }

        public LinkEdge(LinkNode a, LinkNode b, bool manual)
        {
            A = a;
            B = b;
            Manual = manual;
        }

        public bool Connects(LinkNode x, LinkNode y)
        {
            return (A == x && B == y) || (A == y && B == x);
        }
    }

    /// <summary>
    /// Holds the link edges and turns them into connected-component "groups" for export. A group is
    /// one anatomical structure represented across several sets/methods. Grouping is transitive:
    /// linking A–B and B–C puts A, B and C in the same group, so a name that is auto-linked across
    /// three sets, or a manual link that bridges two auto-linked pairs, all collapse correctly.
    /// </summary>
    public sealed class LinkGraph
    {
        private readonly List<LinkEdge> _edges = new List<LinkEdge>();

        public IReadOnlyList<LinkEdge> Edges { get { return _edges; } }

        public LinkEdge Find(LinkNode a, LinkNode b)
        {
            return _edges.FirstOrDefault(e => e.Connects(a, b));
        }

        /// <summary>Adds a link if the two nodes are in different sets and not already linked.</summary>
        public LinkEdge Add(LinkNode a, LinkNode b, bool manual)
        {
            if (a == null || b == null || a == b) return null;
            if (a.Set == b.Set) return null;          // a set can't link a structure to itself
            LinkEdge existing = Find(a, b);
            if (existing != null) return existing;

            var edge = new LinkEdge(a, b, manual);
            _edges.Add(edge);
            return edge;
        }

        public void Remove(LinkEdge edge)
        {
            if (edge != null) _edges.Remove(edge);
        }

        public void Clear()
        {
            _edges.Clear();
        }

        /// <summary>
        /// Auto-links, across every pair of sets, structures whose normalized names match. Nodes
        /// that share a name are chained (n0–n1, n1–n2, …) so they all land in one group. Existing
        /// links (including manual ones) are preserved and never duplicated. Returns the number of
        /// new links created.
        /// </summary>
        public int AutoLinkByName(IReadOnlyList<LinkNode> nodes)
        {
            int added = 0;
            IEnumerable<IGrouping<string, LinkNode>> byName = nodes
                .GroupBy(n => n.Structure.NormalizedName)
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (IGrouping<string, LinkNode> group in byName)
            {
                // One node per set for this name (if a set had the name twice we just take the first).
                List<LinkNode> perSet = group
                    .GroupBy(n => n.Set)
                    .Select(g => g.First())
                    .ToList();

                for (int i = 1; i < perSet.Count; i++)
                {
                    // Only count genuinely new links; skip pairs already linked (manual or auto).
                    if (Find(perSet[i - 1], perSet[i]) == null &&
                        Add(perSet[i - 1], perSet[i], manual: false) != null)
                    {
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// Union-find over the current edges, producing groups. When
        /// <paramref name="includeUnlinked"/> is false only multi-member groups (real
        /// correspondences) are returned; when true every node appears, singletons included.
        /// Groups are ordered largest first, and members within a group by set id.
        /// </summary>
        public List<List<LinkNode>> BuildGroups(IReadOnlyList<LinkNode> allNodes, bool includeUnlinked)
        {
            var parent = new Dictionary<LinkNode, LinkNode>();
            foreach (LinkNode n in allNodes) parent[n] = n;

            Func<LinkNode, LinkNode> find = null;
            find = node =>
            {
                LinkNode root = node;
                while (parent[root] != root) root = parent[root];
                while (parent[node] != root) { LinkNode next = parent[node]; parent[node] = root; node = next; }
                return root;
            };

            foreach (LinkEdge e in _edges)
            {
                if (!parent.ContainsKey(e.A) || !parent.ContainsKey(e.B)) continue;
                parent[find(e.A)] = find(e.B);
            }

            var groups = new Dictionary<LinkNode, List<LinkNode>>();
            foreach (LinkNode n in allNodes)
            {
                LinkNode root = find(n);
                if (!groups.TryGetValue(root, out List<LinkNode> members))
                {
                    members = new List<LinkNode>();
                    groups[root] = members;
                }
                members.Add(n);
            }

            return groups.Values
                .Where(m => includeUnlinked || m.Count > 1)
                .Select(m => m.OrderBy(n => n.Set.Id, StringComparer.OrdinalIgnoreCase).ToList())
                .OrderByDescending(m => m.Count)
                .ThenBy(m => m[0].Set.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m[0].Structure.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Writes the groups as a semicolon-delimited CSV with columns
        /// <c>Group;StructureSetId;StructureId</c>. Rows that share a Group are the same structure
        /// by different methods. UTF-8 with BOM so Excel (with ';' as the list separator) opens it
        /// cleanly. Returns the number of data rows written.
        /// </summary>
        public int WriteCsv(string path, IReadOnlyList<LinkNode> allNodes, bool includeUnlinked)
        {
            List<List<LinkNode>> groups = BuildGroups(allNodes, includeUnlinked);

            var sb = new StringBuilder();
            sb.Append("Group;StructureSetId;StructureId\r\n");

            int rows = 0;
            int groupNumber = 0;
            foreach (List<LinkNode> group in groups)
            {
                groupNumber++;
                foreach (LinkNode node in group)
                {
                    sb.Append(groupNumber.ToString(CultureInfo.InvariantCulture));
                    sb.Append(';');
                    sb.Append(Escape(node.Set.Id));
                    sb.Append(';');
                    sb.Append(Escape(node.Structure.Id));
                    sb.Append("\r\n");
                    rows++;
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return rows;
        }

        // Keep the delimiter unambiguous: any stray ';' in an id becomes ',' (ESAPI ids should
        // never contain one, but a malformed set could).
        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace(';', ',');
        }
    }
}
