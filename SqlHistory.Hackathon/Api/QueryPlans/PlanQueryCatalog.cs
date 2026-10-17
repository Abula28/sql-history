using SqlHistory.Core.Models;

namespace SqlHistory.Hackathon.Api.QueryPlans;

/// <param name="Routine">The routine whose current definition shapes this plan, if any.</param>
public sealed record PlanQuery(string Key, string Title, string Description, string Sql,
    StoredProcedureIdentity? Routine);

/// <summary>
/// The only statements the Query Plan Explorer will EXPLAIN. Each is a reviewed, read-only SELECT
/// against the demo schema; there is deliberately no way to submit free-form SQL, because even
/// plain EXPLAIN may evaluate functions while planning.
/// </summary>
public static class PlanQueryCatalog
{
    private static StoredProcedureIdentity Function(string schema, string name, string arguments) =>
        new(SourceProviders.PostgreSql, schema, name, RoutineType.Function, arguments);

    public static readonly IReadOnlyList<PlanQuery> Queries =
    [
        new("order-items-by-order",
            "Items of one order (indexed column)",
            "Filter on order_id, which has an index after 05-observability.sql.",
            "SELECT * FROM sales.order_items WHERE order_id = 4242",
            null),
        new("order-items-by-product",
            "Items by product (no index)",
            "Filter on product, which has no index, so PostgreSQL reads the whole table.",
            "SELECT * FROM sales.order_items WHERE product = 'Laptop'",
            null),
        new("monthly-revenue-2026",
            "analytics.monthly_revenue(2026)",
            "A SQL function PostgreSQL inlines, so the plan shows the routine's own query and changes when the routine changes.",
            "SELECT * FROM analytics.monthly_revenue(2026)",
            Function("analytics", "monthly_revenue", "integer")),
        new("top-customers",
            "analytics.top_customers(5)",
            "Joins, aggregation, sort and limit inside an inlined SQL function.",
            "SELECT * FROM analytics.top_customers(5)",
            Function("analytics", "top_customers", "integer")),
        new("get-all-users",
            "public.get_all_users()",
            "A PL/pgSQL function: its body is opaque to EXPLAIN, which only shows a Function Scan.",
            "SELECT * FROM public.get_all_users()",
            Function("public", "get_all_users", "")),
        new("active-users",
            "public.active_users()",
            "A scalar SQL function called in the select list; the plan cannot see inside it either.",
            "SELECT public.active_users()",
            Function("public", "active_users", ""))
    ];

    public static PlanQuery? Find(string key) => Queries.FirstOrDefault(x => x.Key == key);
}
