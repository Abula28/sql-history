namespace SqlHistory.Hackathon.Api.QueryPlans;

public enum PlanNodeChange
{
    Unchanged,
    Changed,
    Added,
    Removed
}

/// <summary>How one tree position differs between two plans.</summary>
/// <param name="Description">What changed, e.g. "Seq Scan → Index Scan".</param>
public sealed record PlanNodeDiff(string Path, PlanNodeChange Change, int? FromNodeId, int? ToNodeId,
    double? CostDelta, string? Description);

public sealed record PlanDiffResult(double FromTotalCost, double ToTotalCost, IReadOnlyList<PlanNodeDiff> Nodes)
{
    public double TotalCostDelta => ToTotalCost - FromTotalCost;

    public bool StructureChanged => Nodes.Any(x => x.Change != PlanNodeChange.Unchanged);
}

/// <summary>
/// Compares two plans by tree position. Nodes at the same position are "changed" when their
/// type, relation, index, join type or conditions differ. Estimated cost differences alone do
/// not count as a structural change.
/// </summary>
public static class PlanDiff
{
    public static PlanDiffResult Compare(ParsedPlan from, ParsedPlan to)
    {
        var fromByPath = from.Nodes.ToDictionary(x => x.Path);
        var toByPath = to.Nodes.ToDictionary(x => x.Path);

        var paths = from.Nodes.Select(x => x.Path)
            .Concat(to.Nodes.Select(x => x.Path).Where(p => !fromByPath.ContainsKey(p)))
            .OrderBy(p => p, PathComparer.Instance);

        var diffs = new List<PlanNodeDiff>();
        foreach (var path in paths)
        {
            fromByPath.TryGetValue(path, out var a);
            toByPath.TryGetValue(path, out var b);

            if (a == null)
                diffs.Add(new PlanNodeDiff(path, PlanNodeChange.Added, null, b!.Id, null, $"added {Label(b)}"));
            else if (b == null)
                diffs.Add(new PlanNodeDiff(path, PlanNodeChange.Removed, a.Id, null, null, $"removed {Label(a)}"));
            else
            {
                var differences = Differences(a, b).ToList();
                diffs.Add(new PlanNodeDiff(path,
                    differences.Count == 0 ? PlanNodeChange.Unchanged : PlanNodeChange.Changed,
                    a.Id, b.Id, b.TotalCost - a.TotalCost,
                    differences.Count == 0 ? null : string.Join("; ", differences)));
            }
        }

        return new PlanDiffResult(from.TotalCost, to.TotalCost, diffs);
    }

    private static IEnumerable<string> Differences(PlanNode a, PlanNode b)
    {
        if (a.NodeType != b.NodeType) yield return $"{a.NodeType} → {b.NodeType}";
        if (a.RelationName != b.RelationName) yield return $"relation {a.RelationName ?? "none"} → {b.RelationName ?? "none"}";
        if (a.IndexName != b.IndexName) yield return $"index {a.IndexName ?? "none"} → {b.IndexName ?? "none"}";
        if (a.JoinType != b.JoinType) yield return $"join {a.JoinType ?? "none"} → {b.JoinType ?? "none"}";
        if (a.Filter != b.Filter) yield return "filter changed";
        if (a.IndexCondition != b.IndexCondition) yield return "index condition changed";
        if (a.JoinCondition != b.JoinCondition) yield return "join condition changed";
    }

    private static string Label(PlanNode node) =>
        node.RelationName == null ? node.NodeType : $"{node.NodeType} on {node.RelationName}";

    /// <summary>Orders "0.10" after "0.9" by comparing path segments numerically.</summary>
    private sealed class PathComparer : IComparer<string>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = x!.Split('.').Select(int.Parse).ToArray();
            var b = y!.Split('.').Select(int.Parse).ToArray();
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var c = a[i].CompareTo(b[i]);
                if (c != 0) return c;
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
