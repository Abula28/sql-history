import { useState } from 'react'
import { formatTime } from '../../api'
import { useApi } from '../../useApi'
import { ago, callerLabels, ms, observabilityApi, type CallerType, type TraceSpan } from '../observabilityApi'
import { TraceGraph } from './TraceGraph'

interface TracesViewProps {
  onOpenRoutine: (id: string) => void
}

export function TracesView({ onOpenRoutine }: TracesViewProps) {
  const [refresh, setRefresh] = useState(0)
  const [filter, setFilter] = useState<CallerType | 'all'>('all')
  const [traceId, setTraceId] = useState<string | null>(null)
  const [selectedSpan, setSelectedSpan] = useState<TraceSpan | null>(null)

  const executions = useApi(() => observabilityApi.executions(100), [refresh])
  const trace = useApi(traceId ? () => observabilityApi.trace(traceId) : null, [traceId])
  const sessions = useApi(() => observabilityApi.sessions(), [refresh])

  const rows = (executions.data ?? []).filter((e) => filter === 'all' || e.caller.type === filter)

  return (
    <div className="view">
      <div className="toolbar">
        <h1 className="view-title">Traces</h1>
        <div className="spacer" />
        <select value={filter} onChange={(e) => setFilter(e.target.value as CallerType | 'all')} aria-label="Caller type">
          <option value="all">All callers</option>
          {Object.entries(callerLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <button onClick={() => setRefresh((x) => x + 1)}>Refresh</button>
      </div>

      <p className="muted" style={{ marginTop: 0 }}>
        Routine executions reported by <strong>instrumented applications</strong> (OpenTelemetry spans). PostgreSQL
        itself cannot tell which request or user ran a query; calls from uninstrumented clients do not appear here.
      </p>

      {executions.error && <div className="message error">{executions.error}</div>}
      {executions.data?.length === 0 && (
        <div className="message">No instrumented executions recorded yet. Start the DemoApi (see README) to send traces.</div>
      )}

      {rows.length > 0 && (
        <div className="card">
          <table className="data">
            <thead>
              <tr><th>When</th><th>Caller</th><th>Endpoint / job</th><th>Routine</th><th>Duration</th><th>Result</th><th>Trace</th></tr>
            </thead>
            <tbody>
              {rows.map((e) => (
                <tr key={e.spanId} className={e.traceId === traceId ? 'selected' : ''}
                  onClick={() => { setTraceId(e.traceId); setSelectedSpan(null) }} style={{ cursor: 'pointer' }}>
                  <td title={formatTime(e.startUtc)}>{ago(e.startUtc)}</td>
                  <td><span className="badge">{callerLabels[e.caller.type]}</span></td>
                  <td className="mono">{e.caller.label}</td>
                  <td>
                    {e.routineId ? (
                      <button className="link mono" onClick={(ev) => { ev.stopPropagation(); onOpenRoutine(e.routineId!) }}>
                        {e.routineDisplayName}
                      </button>
                    ) : <span className="mono">{e.routineDisplayName}</span>}
                  </td>
                  <td>{ms(e.durationMs)}</td>
                  <td>{e.isError ? '✕ error' : '✓ ok'}</td>
                  <td className="mono muted">{e.traceId.slice(0, 12)}…</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {traceId && trace.data && (
        <div className="card">
          <h3>Trace {trace.data.traceId}</h3>
          <p style={{ marginTop: 0 }}>
            <span className="badge">{callerLabels[trace.data.caller.type]}</span> {trace.data.caller.label}{' '}
            <span className="muted">· classified from: {trace.data.caller.source}</span>
          </p>
          <div className="plan-split">
            <TraceGraph spans={trace.data.spans} selectedId={selectedSpan?.spanId ?? null} onSelect={setSelectedSpan} />
            <SpanDetail span={selectedSpan} />
          </div>
        </div>
      )}

      <div className="card">
        <h3>Current database sessions (pg_stat_activity)</h3>
        <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
          Observed session metadata reported by PostgreSQL: who is connected right now. It is not a history of requests,
          and application_name is whatever the client chose to send. Query text is not shown.
        </p>
        {sessions.error && <div className="message error">{sessions.error}</div>}
        {sessions.data && (
          <table className="data">
            <thead>
              <tr><th>PID</th><th>Application name</th><th>Role</th><th>Client</th><th>State</th><th>Connected</th></tr>
            </thead>
            <tbody>
              {sessions.data.map((s) => (
                <tr key={s.pid}>
                  <td>{s.pid}</td>
                  <td>{s.applicationName ?? <span className="muted">(not set)</span>}</td>
                  <td>{s.role ?? '—'}</td>
                  <td>{s.clientAddress ?? 'local socket'}</td>
                  <td>{s.state ?? s.backendType ?? '—'}</td>
                  <td>{ago(s.backendStartUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}

function SpanDetail({ span }: { span: TraceSpan | null }) {
  if (!span) return <div className="card muted">Select a span to see its details.</div>
  return (
    <div className="card">
      <h3>{span.name}</h3>
      <table className="data compact">
        <tbody>
          <tr><th>Service</th><td>{span.serviceName}</td></tr>
          <tr><th>Kind</th><td>{span.kind}</td></tr>
          <tr><th>Started</th><td>{formatTime(span.startUtc)}</td></tr>
          <tr><th>Duration</th><td>{ms(span.durationMs)} (measured by the application)</td></tr>
          <tr><th>Status</th><td>{span.isError ? `✕ error ${span.statusDescription ?? ''}` : '✓ ok'}</td></tr>
          <tr><th>Span id</th><td className="mono">{span.spanId}</td></tr>
          <tr><th>Parent span id</th><td className="mono">{span.parentSpanId ?? '(root)'}</td></tr>
          {Object.entries(span.attributes).map(([k, v]) => (
            <tr key={k}><th className="mono">{k}</th><td className="mono">{v}</td></tr>
          ))}
        </tbody>
      </table>
      <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>Application-provided trace metadata; only allow-listed attributes are stored.</p>
    </div>
  )
}
