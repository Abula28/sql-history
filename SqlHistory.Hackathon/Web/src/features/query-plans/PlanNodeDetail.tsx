import type { PlanNode } from '../observabilityApi'

const explanations: Record<string, string> = {
  'Seq Scan':
    'Reads every row of the table and checks the filter. Normal and often cheapest for small tables or when most rows match; worth a look on large tables when only a few rows match.',
  'Index Scan': 'Uses an index to find matching rows, then fetches each row from the table.',
  'Index Only Scan': 'Answers the query from the index alone, without visiting the table for most rows.',
  'Bitmap Index Scan': 'Scans an index to build a bitmap of matching row locations for its parent Bitmap Heap Scan.',
  'Bitmap Heap Scan': 'Fetches the rows marked by a bitmap index scan, in physical order, rechecking the condition.',
  'Hash Join': 'Builds a hash table from one input (the Hash child) and probes it with each row of the other.',
  'Merge Join': 'Joins two inputs that are sorted on the join key by walking them in step.',
  'Nested Loop': 'For each row of the outer input, scans the inner input. Cheap when the outer side is small.',
  Hash: 'Builds the in-memory hash table used by its parent Hash Join.',
  Sort: 'Sorts its input. Large sorts may spill to disk.',
  'Incremental Sort': 'Sorts input that is already sorted on a prefix of the keys.',
  Aggregate: 'Computes aggregates (count, sum, ...), sorted or plain, over its input.',
  HashAggregate: 'Groups rows with a hash table and computes aggregates per group.',
  GroupAggregate: 'Computes aggregates per group over input already sorted by the group key.',
  Limit: 'Stops after the requested number of rows.',
  'Function Scan':
    'Calls a set-returning function. EXPLAIN cannot see inside non-inlined (for example PL/pgSQL) functions; their own queries are planned separately at run time.',
  Result: 'Computes a value without reading a table, for example a scalar function call. The function body is not shown.',
}

export function PlanNodeDetail({ node, rootCost }: { node: PlanNode | null; rootCost: number }) {
  if (!node) return <div className="card muted">Select a node to see its details.</div>

  const rows: [string, string | null][] = [
    ['Relation', node.relationName ? `${node.schema ? node.schema + '.' : ''}${node.relationName}${node.alias && node.alias !== node.relationName ? ` (${node.alias})` : ''}` : null],
    ['Index', node.indexName],
    ['Function', node.functionName],
    ['Join type', node.joinType],
    ['Strategy', node.strategy],
    ['Relationship to parent', node.parentRelationship],
    ['Filter', node.filter],
    ['Index condition', node.indexCondition],
    ['Join condition', node.joinCondition],
    ['Sort key', node.sortKey.length ? node.sortKey.join(', ') : null],
    ['Group key', node.groupKey.length ? node.groupKey.join(', ') : null],
  ]

  return (
    <div className="card">
      <h3>{node.nodeType}</h3>
      <p style={{ marginTop: 0 }}>{explanations[node.nodeType] ?? 'A plan step; see the PostgreSQL EXPLAIN documentation.'}</p>

      <table className="data compact">
        <tbody>
          <tr><th>Startup cost</th><td>{node.startupCost.toFixed(2)}</td></tr>
          <tr><th>Total cost</th><td>{node.totalCost.toFixed(2)}</td></tr>
          <tr><th>Own estimated cost</th><td>{node.exclusiveCost.toFixed(2)} ({Math.round(node.costShare * 100)}% of {rootCost.toFixed(1)})</td></tr>
          <tr><th>Estimated rows</th><td>{Math.round(node.planRows).toLocaleString()}</td></tr>
          <tr><th>Estimated row width</th><td>{node.planWidth} bytes</td></tr>
          {rows.filter(([, v]) => v).map(([k, v]) => (
            <tr key={k}><th>{k}</th><td className="mono">{v}</td></tr>
          ))}
        </tbody>
      </table>

      {node.isCostHotspot && (
        <p className="muted">
          Highlighted because this node accounts for {Math.round(node.costShare * 100)}% of the plan's estimated cost.
          That makes it the place to look first, not necessarily a problem.
        </p>
      )}
      <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>
        Costs are the planner's estimates in arbitrary cost units, not milliseconds. Measure real latency separately.
      </p>
    </div>
  )
}
