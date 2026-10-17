import { useApi } from '../../useApi'
import { StatementTable } from '../performance/StatementTable'
import { ago, callerLabels, count, ms, observabilityApi } from '../observabilityApi'

interface Props {
  routineId: string
  refreshKey: number
  onOpenTraces: () => void
  onOpenRoutine: (id: string) => void
  onOpenPlan: (key: string) => void
}

/**
 * Everything observed about one routine, kept in three separately labelled sources because they
 * cover different executions: instrumented application traces, PostgreSQL function statistics,
 * and statement statistics whose text calls the routine.
 */
export function RoutineObservabilityTab({ routineId, refreshKey, onOpenTraces, onOpenRoutine, onOpenPlan }: Props) {
  const executions = useApi(() => observabilityApi.routineExecutions(routineId), [routineId, refreshKey])
  const performance = useApi(() => observabilityApi.routinePerformance(routineId), [routineId, refreshKey])
  const plans = useApi(() => observabilityApi.planQueries(), [])
  const relatedPlans = (plans.data ?? []).filter((p) => p.routineId === routineId)

  const e = executions.data
  const p = performance.data

  return (
    <div>
      <div className="card">
        <h3>Instrumented executions (application traces)</h3>
        {executions.error && <div className="message error">{executions.error}</div>}
        {e && e.observedCalls === 0 && (
          <span className="muted">
            No instrumented executions recorded in the last {e.retentionHours} h. That does not mean the routine was
            never called: uninstrumented callers are invisible here.
          </span>
        )}
        {e && e.observedCalls > 0 && (
          <>
            <div className="tiles">
              <div className="tile"><div className="tile-value">{count(e.observedCalls)}</div><div className="tile-label">observed calls</div></div>
              <div className="tile"><div className="tile-value">{ms(e.averageDurationMs)}</div><div className="tile-label">average observed duration</div></div>
              <div className="tile"><div className="tile-value">{ago(e.lastCallUtc)}</div><div className="tile-label">last observed call</div></div>
              <div className="tile"><div className="tile-value">{count(e.errorCount)}</div><div className="tile-label">errors</div></div>
            </div>
            <table className="data">
              <thead><tr><th>Caller</th><th>Type</th><th>Calls</th><th>Average</th><th>Last</th></tr></thead>
              <tbody>
                {e.callers.map((c) => (
                  <tr key={c.type + c.label}>
                    <td className="mono">{c.label}</td>
                    <td><span className="badge">{callerLabels[c.type]}</span></td>
                    <td>{count(c.calls)}</td>
                    <td>{ms(c.averageDurationMs)}</td>
                    <td>{ago(c.lastCallUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="muted" style={{ fontSize: 12 }}>
              Durations are measured by the calling application (including network time).{' '}
              <button className="link" onClick={onOpenTraces}>Open Traces</button>
            </p>
          </>
        )}
      </div>

      <div className="card">
        <h3>PostgreSQL function statistics (pg_stat_user_functions)</h3>
        {performance.error && <div className="message error">{performance.error}</div>}
        {p && !p.functionsTracked && <div className="message warn">{p.functionsSetupHint}</div>}
        {p?.functionsTracked && !p.functionStats && (
          <span className="muted">
            No calls counted. Either the routine has not run in a tracked session, or PostgreSQL inlined it into the
            calling query (simple SQL functions), in which case it is never counted separately.
          </span>
        )}
        {p?.functionStats && (
          <div className="tiles">
            <div className="tile"><div className="tile-value">{count(p.functionStats.calls)}</div><div className="tile-label">calls (all sessions)</div></div>
            <div className="tile"><div className="tile-value">{ms(p.functionStats.meanTimeMs)}</div><div className="tile-label">mean time in PostgreSQL</div></div>
            <div className="tile"><div className="tile-value">{ms(p.functionStats.totalTimeMs)}</div><div className="tile-label">total time</div></div>
            <div className="tile"><div className="tile-value">{ms(p.functionStats.selfTimeMs)}</div><div className="tile-label">self time</div></div>
          </div>
        )}
        <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>
          Counts every call in tracked sessions, instrumented or not, so it can differ from the traced call count above.
          PostgreSQL publishes function statistics with a delay (sessions flush them in batches, idle ones after up to about 10 s), so counts over windows of a few seconds can be slightly off; statement statistics update immediately.
        </p>
      </div>

      <div className="card">
        <h3>Statements whose text calls this routine (pg_stat_statements)</h3>
        {p && !p.statementsAvailable && <span className="muted">Statement statistics are not enabled.</span>}
        {p?.statementsAvailable && (
          <>
            <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
              Text match on the schema-qualified name. These are statement metrics: they include the calling statement's
              own work and miss calls written without the schema.
            </p>
            <StatementTable statements={p.statementsCallingRoutine} onOpenRoutine={onOpenRoutine} />
          </>
        )}
      </div>

      {relatedPlans.length > 0 && (
        <div className="card">
          <h3>Query plans</h3>
          {relatedPlans.map((q) => (
            <div key={q.key}>
              <button className="link" onClick={() => onOpenPlan(q.key)}>{q.title}</button>{' '}
              <span className="muted">{q.description}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
