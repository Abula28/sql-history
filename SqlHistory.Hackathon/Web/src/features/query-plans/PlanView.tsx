import { useMemo, useState } from 'react'
import { formatTime } from '../../api'
import { useApi } from '../../useApi'
import { observabilityApi, type PlanNode, type PlanNodeChange } from '../observabilityApi'
import { PlanGraph } from './PlanGraph'
import { PlanNodeDetail } from './PlanNodeDetail'

interface PlanViewProps {
  initialKey?: string | null
  onOpenRoutine: (id: string) => void
}

export function PlanView({ initialKey, onOpenRoutine }: PlanViewProps) {
  const queries = useApi(() => observabilityApi.planQueries(), [])
  const [chosenKey, setChosenKey] = useState<string | null>(initialKey ?? null)
  const key = chosenKey ?? queries.data?.[0]?.key ?? null
  const [refresh, setRefresh] = useState(0)
  const [selectedNode, setSelectedNode] = useState<PlanNode | null>(null)
  const [capturing, setCapturing] = useState(false)
  const [captureError, setCaptureError] = useState<string | null>(null)
  const [compare, setCompare] = useState<number[]>([])

  const live = useApi(key ? () => observabilityApi.explain(key) : null, [key, refresh])
  const snapshots = useApi(key ? () => observabilityApi.snapshots(key) : null, [key, refresh])

  const query = queries.data?.find((q) => q.key === key)

  function choose(next: string) {
    setChosenKey(next)
    setSelectedNode(null)
    setCompare([])
  }

  async function capture() {
    if (!key) return
    setCapturing(true)
    setCaptureError(null)
    try {
      await observabilityApi.captureSnapshot(key)
      setRefresh((x) => x + 1)
    } catch (e) {
      setCaptureError((e as Error).message)
    } finally {
      setCapturing(false)
    }
  }

  function toggleCompare(id: number) {
    setCompare((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id].slice(-2)))
  }

  return (
    <div className="view">
      <div className="toolbar">
        <h1 className="view-title">Query Plans</h1>
        <div className="spacer" />
        <span className="muted">Estimated plans only (EXPLAIN, never ANALYZE) · predefined read-only queries</span>
      </div>

      {queries.error && <div className="message error">{queries.error}</div>}

      <div className="plan-layout">
        <ol className="timeline">
          {queries.data?.map((q) => (
            <li key={q.key}>
              <button className={q.key === key ? 'selected' : ''} onClick={() => choose(q.key)}>
                <span className="v">{q.title}</span>
                <span className="when">{q.description}</span>
              </button>
            </li>
          ))}
        </ol>

        <div style={{ minWidth: 0 }}>
          {query && (
            <div className="toolbar">
              <code className="sql-line">{query.sql}</code>
              {query.routineId && (
                <button className="link" onClick={() => onOpenRoutine(query.routineId!)}>open {query.routineDisplayName}</button>
              )}
              <div className="spacer" />
              <button onClick={() => setRefresh((x) => x + 1)}>Re-explain</button>
              <button className="primary" onClick={capture} disabled={capturing || !live.data}>
                {capturing ? 'Capturing…' : 'Capture snapshot'}
              </button>
            </div>
          )}

          {captureError && <div className="message error">{captureError}</div>}
          {live.error && <div className="message error">{live.error}</div>}
          {live.loading && !live.data && <div className="empty">Asking PostgreSQL for the plan…</div>}

          {live.data && (
            <>
              <p className="muted" style={{ marginTop: 0 }}>
                {live.data.database} · PostgreSQL {live.data.serverVersion} · planned {formatTime(live.data.explainedAtUtc)} ·
                total estimated cost {live.data.plan.totalCost.toFixed(1)} (cost units, not ms)
              </p>
              <Legend />
              <div className="plan-split">
                <PlanGraph plan={live.data.plan} selectedId={selectedNode?.id ?? null} onSelect={setSelectedNode} />
                <PlanNodeDetail node={selectedNode} rootCost={live.data.plan.totalCost} />
              </div>
            </>
          )}

          <div className="card">
            <h3>Captured snapshots</h3>
            <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
              A snapshot stores the plan as PostgreSQL produced it at that moment, with the routine version it ran against.
              Old plans cannot be rebuilt from historical definitions, so capture one before and after a change. Tick two
              to compare.
            </p>
            {snapshots.data?.length === 0 && <span className="muted">No snapshots yet.</span>}
            {snapshots.data && snapshots.data.length > 0 && (
              <table className="data">
                <thead>
                  <tr><th></th><th>Captured</th><th>Routine version</th><th>Environment</th><th>Total cost</th></tr>
                </thead>
                <tbody>
                  {snapshots.data.map((s) => (
                    <tr key={s.id}>
                      <td><input type="checkbox" checked={compare.includes(s.id)} onChange={() => toggleCompare(s.id)} aria-label={`Compare snapshot ${s.id}`} /></td>
                      <td>{formatTime(s.capturedAtUtc)}</td>
                      <td>{s.routineDisplayName ? (s.routineVersion ? `${s.routineDisplayName} v${s.routineVersion}` : `${s.routineDisplayName} (unrecorded change)`) : '—'}</td>
                      <td className="muted">{s.database} · PG {s.serverVersion}</td>
                      <td>{s.totalCost.toFixed(1)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {compare.length === 2 && <PlanComparison fromId={Math.min(...compare)} toId={Math.max(...compare)} />}
        </div>
      </div>
    </div>
  )
}

function Legend() {
  return (
    <div className="legend">
      <span><i className="swatch cat-seq" />Sequential scan</span>
      <span><i className="swatch cat-index" />Index scan</span>
      <span><i className="swatch cat-join" />Join</span>
      <span><i className="swatch cat-other" />Other (named on the node)</span>
      <span><i className="swatch hotspot" />Thick border: cost hotspot</span>
    </div>
  )
}

function PlanComparison({ fromId, toId }: { fromId: number; toId: number }) {
  const result = useApi(() => observabilityApi.comparePlans(fromId, toId), [fromId, toId])
  const [selected, setSelected] = useState<PlanNode | null>(null)

  const { fromChanges, toChanges } = useMemo(() => {
    const fromChanges = new Map<number, PlanNodeChange>()
    const toChanges = new Map<number, PlanNodeChange>()
    for (const d of result.data?.diff.nodes ?? []) {
      if (d.fromNodeId !== null) fromChanges.set(d.fromNodeId, d.change)
      if (d.toNodeId !== null) toChanges.set(d.toNodeId, d.change)
    }
    return { fromChanges, toChanges }
  }, [result.data])

  if (result.error) return <div className="message error">{result.error}</div>
  if (!result.data) return <div className="empty">Comparing…</div>

  const { from, to, diff } = result.data
  const label = (s: typeof from.snapshot) =>
    `${formatTime(s.capturedAtUtc)}${s.routineVersion ? ` · v${s.routineVersion}` : ''} · cost ${s.totalCost.toFixed(1)}`
  const changed = diff.nodes.filter((n) => n.change !== 'Unchanged')

  return (
    <div className="card">
      <h3>Plan comparison</h3>
      <p style={{ marginTop: 0 }}>
        {diff.structureChanged ? `${changed.length} plan node(s) differ.` : 'Same plan structure.'} Estimated total cost{' '}
        {diff.fromTotalCost.toFixed(1)} → {diff.toTotalCost.toFixed(1)} ({diff.totalCostDelta >= 0 ? '+' : ''}
        {diff.totalCostDelta.toFixed(1)} cost units). Estimates, not measured latency.
      </p>
      {changed.length > 0 && (
        <ul>
          {changed.map((n) => <li key={n.path}><span className="badge">{n.change.toLowerCase()}</span> {n.description}</li>)}
        </ul>
      )}
      <div className="plan-compare">
        <div>
          <div className="muted">Before: {label(from.snapshot)}</div>
          <PlanGraph plan={from.plan} selectedId={selected?.id ?? null} onSelect={setSelected} changes={fromChanges} height={360} />
        </div>
        <div>
          <div className="muted">After: {label(to.snapshot)}</div>
          <PlanGraph plan={to.plan} selectedId={null} onSelect={setSelected} changes={toChanges} height={360} />
        </div>
      </div>
    </div>
  )
}
