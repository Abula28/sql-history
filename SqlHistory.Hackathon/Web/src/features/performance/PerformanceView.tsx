import { useState } from 'react'
import { formatTime } from '../../api'
import { useApi } from '../../useApi'
import { count, ms, observabilityApi, type Statement, type StatementSort } from '../observabilityApi'
import { StatementTable } from './StatementTable'
import { TrendChart } from './TrendChart'

interface PerformanceViewProps {
  onOpenRoutine: (id: string) => void
}

const HOURS = 1

export function PerformanceView({ onOpenRoutine }: PerformanceViewProps) {
  const [refresh, setRefresh] = useState(0)
  const [sort, setSort] = useState<StatementSort>('total')
  const [includeCatalog, setIncludeCatalog] = useState(false)
  const [selected, setSelected] = useState<Statement | null>(null)
  const [sampling, setSampling] = useState(false)
  const [sampleError, setSampleError] = useState<string | null>(null)

  const status = useApi(() => observabilityApi.performanceStatus(), [refresh])
  const statements = useApi(() => observabilityApi.statements(sort, includeCatalog), [sort, includeCatalog, refresh])
  const functions = useApi(() => observabilityApi.functions(), [refresh])
  const overall = useApi(() => observabilityApi.trends(HOURS), [refresh])
  const statementTrend = useApi(
    selected ? () => observabilityApi.trends(HOURS, selected) : null,
    [selected?.queryId, selected?.userId, selected?.topLevel, refresh],
  )

  async function sample() {
    setSampling(true)
    setSampleError(null)
    try {
      await observabilityApi.takeSample()
      setRefresh((x) => x + 1)
    } catch (e) {
      setSampleError((e as Error).message)
    } finally {
      setSampling(false)
    }
  }

  const s = status.data
  const data = statements.data

  return (
    <div className="view">
      <div className="toolbar">
        <h1 className="view-title">Performance</h1>
        <div className="spacer" />
        {s && (
          <span className="muted">
            {s.sampleCount} samples · last {formatTime(s.lastSampleAtUtc)}
            {s.sampleIntervalSeconds > 0 ? ` · every ${s.sampleIntervalSeconds}s` : ' · automatic sampling off'}
          </span>
        )}
        <button onClick={() => setRefresh((x) => x + 1)}>Refresh</button>
        <button className="primary" onClick={sample} disabled={sampling}>
          {sampling ? 'Sampling…' : 'Take sample'}
        </button>
      </div>

      {status.error && <div className="message error">{status.error}</div>}
      {sampleError && <div className="message error">Sample failed: {sampleError}</div>}

      <p className="muted" style={{ marginTop: 0 }}>
        Two different sources: <strong>statement statistics</strong> (pg_stat_statements) count normalized SQL
        statements, not routines. <strong>Function statistics</strong> (pg_stat_user_functions) count calls per
        function. Values are cumulative since the last statistics reset
        {data?.statsResetAtUtc ? ` (${formatTime(data.statsResetAtUtc)})` : ''}.
      </p>

      {s && !s.statementsAvailable && (
        <div className="message warn">
          <strong>Statement statistics are not enabled.</strong> {s.statementsSetupHint}
        </div>
      )}

      {data?.available && (
        <>
          <div className="tiles">
            <Tile label="Statements" value={count(data.summary.statementCount)} />
            <Tile label="Executions" value={count(data.summary.totalCalls)} />
            <Tile label="Total execution time" value={ms(data.summary.totalExecTimeMs)} />
            <Tile label="Mean per execution" value={ms(data.summary.meanExecTimeMs)} />
            <Tile label="Fastest / slowest" value={`${ms(data.summary.minExecTimeMs)} / ${ms(data.summary.maxExecTimeMs)}`} />
            <Tile label="Rows processed" value={count(data.summary.totalRows)} />
          </div>

          {overall.data && (
            <TrendChart points={overall.data.points} measure="totalTimeMs"
              title={`Total statement execution time per interval (last ${HOURS} h)`} />
          )}

          <div className="card">
            <div className="toolbar">
              <h3 style={{ margin: 0 }}>Most expensive statements</h3>
              <div className="spacer" />
              <label className="muted">
                Sort by{' '}
                <select value={sort} onChange={(e) => setSort(e.target.value as StatementSort)}>
                  <option value="total">total time</option>
                  <option value="mean">mean time</option>
                  <option value="calls">executions</option>
                </select>
              </label>
              <label className="muted">
                <input type="checkbox" checked={includeCatalog} onChange={(e) => setIncludeCatalog(e.target.checked)} />{' '}
                include system-catalog queries
              </label>
            </div>
            {!includeCatalog && (
              <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
                Hiding statements that read system catalogs; nearly all of them are SQL Time Machine's own queries.
              </p>
            )}
            <StatementTable statements={data.statements} selected={selected} onSelect={setSelected}
              onOpenRoutine={onOpenRoutine} />
          </div>

          {selected && (
            <>
              {statementTrend.data && (
                <TrendChart points={statementTrend.data.points} measure="calls"
                  title={`Executions per interval for statement ${selected.queryId}`} />
              )}
            </>
          )}
        </>
      )}

      <div className="card">
        <h3>Function statistics (pg_stat_user_functions)</h3>
        {functions.data && !functions.data.tracked && (
          <div className="message warn">{functions.data.setupHint}</div>
        )}
        {functions.data?.tracked && (
          <>
            <p className="muted" style={{ marginTop: 0, fontSize: 12 }}>
              track_functions = {functions.data.trackFunctions}. Counted per function, so these numbers do belong to the
              routine. Simple SQL functions that PostgreSQL inlines into the calling query are not counted, and only
              sessions started after track_functions was enabled are tracked. PostgreSQL publishes function statistics with a delay (sessions flush them in batches, idle ones after up to about 10 s), so counts over windows of a few seconds can be slightly off; statement statistics update immediately.
            </p>
            {functions.data.functions.length === 0 ? (
              <span className="muted">No tracked function has been called since statistics were enabled.</span>
            ) : (
              <table className="data">
                <thead>
                  <tr><th>Routine</th><th>Calls</th><th>Total time</th><th>Self time</th><th>Mean</th></tr>
                </thead>
                <tbody>
                  {functions.data.functions.map((f) => (
                    <tr key={f.displayName}>
                      <td>
                        {f.routineId ? (
                          <button className="link mono" onClick={() => onOpenRoutine(f.routineId!)}>{f.displayName}</button>
                        ) : (
                          <span className="mono">{f.displayName}</span>
                        )}
                      </td>
                      <td>{count(f.calls)}</td>
                      <td>{ms(f.totalTimeMs)}</td>
                      <td>{ms(f.selfTimeMs)}</td>
                      <td>{ms(f.meanTimeMs)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </div>
  )
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="tile">
      <div className="tile-value">{value}</div>
      <div className="tile-label">{label}</div>
    </div>
  )
}
