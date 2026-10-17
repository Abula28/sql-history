import { count, ms, type Statement } from '../observabilityApi'

export function StatementTable({ statements, selected, onSelect, onOpenRoutine }: {
  statements: Statement[]
  selected?: Statement | null
  onSelect?: (statement: Statement) => void
  onOpenRoutine: (id: string) => void
}) {
  if (statements.length === 0) return <span className="muted">No statements recorded.</span>

  return (
    <table className="data">
      <thead>
        <tr>
          <th>Statement</th><th>Calls</th><th>Total</th><th>Mean</th><th>Min</th><th>Max</th><th>Rows</th>
        </tr>
      </thead>
      <tbody>
        {statements.map((st) => (
          <tr
            key={`${st.queryId}:${st.userId}:${st.topLevel}`}
            className={selected && selected.queryId === st.queryId && selected.userId === st.userId ? 'selected' : ''}
            onClick={() => onSelect?.(st)}
            style={{ cursor: onSelect ? 'pointer' : undefined }}
          >
            <td className="query-cell">
              <code title={st.query}>{st.query.length > 140 ? st.query.slice(0, 140) + '…' : st.query}</code>
              <div className="sub-badges">
                <span className="badge">{st.role}</span>
                {!st.topLevel && <span className="badge">nested</span>}
                {st.mentionsRoutines.map((r) => (
                  <button key={r.id} className="badge link" title="The statement text calls this routine (text match, not attribution)"
                    onClick={(e) => { e.stopPropagation(); onOpenRoutine(r.id) }}>
                    calls {r.displayName} · text match
                  </button>
                ))}
              </div>
            </td>
            <td>{count(st.calls)}</td>
            <td>{ms(st.totalExecTimeMs)}</td>
            <td>{ms(st.meanExecTimeMs)}</td>
            <td>{ms(st.minExecTimeMs)}</td>
            <td>{ms(st.maxExecTimeMs)}</td>
            <td>{count(st.rows)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
