using SqlHistory.Hackathon.Api.QueryPlans;

namespace SqlHistory.Hackathon.Tests;

/// <summary>Fixtures are real EXPLAIN (FORMAT JSON) output from PostgreSQL 16 on the demo database.</summary>
public class PlanParserTests
{
    private static ParsedPlan Fixture(string name) =>
        PlanParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void Parse_ShouldBuildParentChildTree_WithPaths()
    {
        var plan = Fixture("plan-monthly-revenue.json");

        Assert.Equal(["Aggregate", "Sort", "Hash Join", "Seq Scan", "Hash", "Seq Scan"],
            plan.Nodes.Select(x => x.NodeType));
        Assert.Equal(["0", "0.0", "0.0.0", "0.0.0.0", "0.0.0.1", "0.0.0.1.0"], plan.Nodes.Select(x => x.Path));
        Assert.Equal([null, 0, 1, 2, 2, 4], plan.Nodes.Select(x => x.ParentId));
        Assert.Equal(plan.Nodes[0].TotalCost, plan.TotalCost);
    }

    [Fact]
    public void Parse_ShouldReadRelationsConditionsAndCategories()
    {
        var plan = Fixture("plan-monthly-revenue.json");

        var join = plan.Nodes.Single(x => x.NodeType == "Hash Join");
        Assert.Equal(PlanNodeCategory.Join, join.Category);
        Assert.Equal("Inner", join.JoinType);
        Assert.Equal("(i.order_id = o.id)", join.JoinCondition);

        var orders = plan.Nodes.Single(x => x.RelationName == "orders");
        Assert.Equal(PlanNodeCategory.SequentialScan, orders.Category);
        Assert.Contains("status", orders.Filter);
        Assert.Contains(plan.Nodes, x => x.Category == PlanNodeCategory.Sort);
        Assert.Contains(plan.Nodes, x => x.Category == PlanNodeCategory.Aggregate);
    }

    [Fact]
    public void Parse_ShouldReadIndexScans()
    {
        var plan = Fixture("plan-indexed.json");

        var indexNode = plan.Nodes.Single(x => x.IndexName != null);
        Assert.Equal("order_items_order_id_idx", indexNode.IndexName);
        Assert.Equal(PlanNodeCategory.IndexScan, indexNode.Category);
        Assert.Equal("(order_id = 4242)", indexNode.IndexCondition);
    }

    [Fact]
    public void ExclusiveCost_ShouldSubtractChildren_AndNeverBeNegative()
    {
        var plan = Fixture("plan-monthly-revenue.json");

        foreach (var node in plan.Nodes)
        {
            var children = plan.Nodes.Where(x => x.ParentId == node.Id).Sum(x => x.TotalCost);
            Assert.Equal(Math.Max(0, node.TotalCost - children), node.ExclusiveCost, 6);
            Assert.True(node.ExclusiveCost >= 0);
        }
    }

    [Fact]
    public void Hotspots_ShouldNotFlagCheapPlans_EvenForSequentialScans()
    {
        var cheap = PlanParser.Parse("""
            [{"Plan": {"Node Type": "Seq Scan", "Relation Name": "users", "Startup Cost": 0, "Total Cost": 1.08, "Plan Rows": 8, "Plan Width": 37}}]
            """);

        Assert.False(Assert.Single(cheap.Nodes).IsCostHotspot);
    }

    [Fact]
    public void Hotspots_ShouldFlagNodesDominatingAnExpensivePlan()
    {
        var plan = Fixture("plan-seqscan.json");

        var scan = Assert.Single(plan.Nodes);
        Assert.True(scan.TotalCost >= PlanParser.HotspotMinimumTotalCost);
        Assert.True(scan.IsCostHotspot);
    }

    [Fact]
    public void Parse_ShouldRejectOutputWithoutPlan()
    {
        Assert.Throws<FormatException>(() => PlanParser.Parse("""[{"Planning Time": 1}]"""));
    }

    [Fact]
    public void Diff_ShouldReportChangedAndRemovedNodes()
    {
        var indexed = Fixture("plan-indexed.json");
        var sequential = Fixture("plan-seqscan.json");

        var diff = PlanDiff.Compare(indexed, sequential);

        Assert.True(diff.StructureChanged);
        var root = diff.Nodes.Single(x => x.Path == "0");
        Assert.Equal(PlanNodeChange.Changed, root.Change);
        Assert.Contains("Bitmap Heap Scan → Seq Scan", root.Description);
        Assert.Equal(PlanNodeChange.Removed, diff.Nodes.Single(x => x.Path == "0.0").Change);
        Assert.Equal(sequential.TotalCost - indexed.TotalCost, diff.TotalCostDelta, 6);
    }

    [Fact]
    public void Diff_ShouldReportNoStructuralChange_ForIdenticalPlans()
    {
        var plan = Fixture("plan-monthly-revenue.json");

        var diff = PlanDiff.Compare(plan, plan);

        Assert.False(diff.StructureChanged);
        Assert.All(diff.Nodes, x => Assert.Equal(0, x.CostDelta));
    }
}
