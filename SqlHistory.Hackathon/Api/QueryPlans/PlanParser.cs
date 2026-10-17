using System.Text.Json;

namespace SqlHistory.Hackathon.Api.QueryPlans;

public enum PlanNodeCategory
{
    SequentialScan,
    IndexScan,
    Join,
    Sort,
    Aggregate,
    Function,
    Other
}

/// <summary>
/// One node of an estimated plan. Costs are planner cost units (relative estimates), not milliseconds.
/// </summary>
/// <param name="Path">Position in the tree ("0", "0.1", ...), used to align two plans.</param>
/// <param name="ExclusiveCost">Total cost minus the children's total cost: roughly the node's own estimated work.</param>
/// <param name="CostShare">ExclusiveCost as a fraction of the root's total cost.</param>
/// <param name="IsCostHotspot">The node accounts for a large share of a non-trivial plan's cost.</param>
public sealed record PlanNode(
    int Id,
    int? ParentId,
    string Path,
    string NodeType,
    PlanNodeCategory Category,
    string? ParentRelationship,
    string? RelationName,
    string? Schema,
    string? Alias,
    string? IndexName,
    string? FunctionName,
    string? JoinType,
    string? Strategy,
    double StartupCost,
    double TotalCost,
    double PlanRows,
    int PlanWidth,
    string? Filter,
    string? IndexCondition,
    string? JoinCondition,
    IReadOnlyList<string> SortKey,
    IReadOnlyList<string> GroupKey,
    double ExclusiveCost,
    double CostShare,
    bool IsCostHotspot);

public sealed record ParsedPlan(double TotalCost, double PlanRows, IReadOnlyList<PlanNode> Nodes);

/// <summary>Parses EXPLAIN (FORMAT JSON) output into a flat list of nodes with parent links.</summary>
public static class PlanParser
{
    /// <summary>Plans cheaper than this are too small for "hotspot" highlighting to mean anything.</summary>
    public const double HotspotMinimumTotalCost = 100;

    /// <summary>Share of the plan's cost a single node needs to be highlighted.</summary>
    public const double HotspotShare = 0.3;

    public static ParsedPlan Parse(string explainJson)
    {
        using var document = JsonDocument.Parse(explainJson);

        var root = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement[0]
            : document.RootElement;
        if (!root.TryGetProperty("Plan", out var plan))
            throw new FormatException("EXPLAIN output has no \"Plan\" element.");

        var raw = new List<(JsonElement Element, int? ParentId, string Path)>();
        Collect(plan, null, "0", raw);

        var rootTotal = Double(plan, "Total Cost");
        var nodes = new List<PlanNode>(raw.Count);

        for (var id = 0; id < raw.Count; id++)
        {
            var (element, parentId, path) = raw[id];
            var total = Double(element, "Total Cost");
            var childrenTotal = element.TryGetProperty("Plans", out var children)
                ? children.EnumerateArray().Sum(x => Double(x, "Total Cost"))
                : 0;
            var exclusive = Math.Max(0, total - childrenTotal);
            var share = rootTotal > 0 ? exclusive / rootTotal : 0;
            var nodeType = String(element, "Node Type") ?? "Unknown";

            nodes.Add(new PlanNode(
                id,
                parentId,
                path,
                nodeType,
                Categorize(nodeType),
                String(element, "Parent Relationship"),
                String(element, "Relation Name"),
                String(element, "Schema"),
                String(element, "Alias"),
                String(element, "Index Name"),
                String(element, "Function Name"),
                String(element, "Join Type"),
                String(element, "Strategy"),
                Double(element, "Startup Cost"),
                total,
                Double(element, "Plan Rows"),
                (int)Double(element, "Plan Width"),
                String(element, "Filter"),
                String(element, "Index Cond") ?? String(element, "Recheck Cond"),
                String(element, "Hash Cond") ?? String(element, "Merge Cond") ?? String(element, "Join Filter"),
                Strings(element, "Sort Key"),
                Strings(element, "Group Key"),
                exclusive,
                share,
                rootTotal >= HotspotMinimumTotalCost && share >= HotspotShare));
        }

        return new ParsedPlan(rootTotal, Double(plan, "Plan Rows"), nodes);
    }

    public static PlanNodeCategory Categorize(string nodeType) => nodeType switch
    {
        "Seq Scan" or "Parallel Seq Scan" => PlanNodeCategory.SequentialScan,
        "Index Scan" or "Index Only Scan" or "Bitmap Index Scan" or "Bitmap Heap Scan" => PlanNodeCategory.IndexScan,
        "Nested Loop" or "Hash Join" or "Merge Join" => PlanNodeCategory.Join,
        "Sort" or "Incremental Sort" => PlanNodeCategory.Sort,
        "Aggregate" or "HashAggregate" or "GroupAggregate" or "WindowAgg" => PlanNodeCategory.Aggregate,
        "Function Scan" or "ProjectSet" => PlanNodeCategory.Function,
        _ => PlanNodeCategory.Other
    };

    private static void Collect(JsonElement node, int? parentId, string path,
        List<(JsonElement, int?, string)> into)
    {
        var id = into.Count;
        into.Add((node, parentId, path));

        if (!node.TryGetProperty("Plans", out var children)) return;

        var index = 0;
        foreach (var child in children.EnumerateArray())
            Collect(child, id, $"{path}.{index++}", into);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
