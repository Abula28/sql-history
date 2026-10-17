import { useMemo, useState } from 'react'
import type { RoutineState, RoutineSummary } from '../api'

const stateLabels: Record<RoutineState, string> = {
  Untracked: 'not scanned',
  Tracked: 'tracked',
  ChangedSinceLastScan: 'changed',
  Dropped: 'dropped',
}

interface RoutineListProps {
  routines: RoutineSummary[] | null
  loading: boolean
  error: string | null
  selectedId: string | null
  onSelect: (id: string) => void
}

export function RoutineList({ routines, loading, error, selectedId, onSelect }: RoutineListProps) {
  const [search, setSearch] = useState('')

  const bySchema = useMemo(() => {
    const term = search.trim().toLowerCase()
    const groups = new Map<string, RoutineSummary[]>()
    for (const routine of routines ?? []) {
      if (term && !routine.displayName.toLowerCase().includes(term)) continue
      const list = groups.get(routine.schema) ?? []
      list.push(routine)
      groups.set(routine.schema, list)
    }
    return [...groups.entries()]
  }, [routines, search])

  return (
    <aside className="sidebar">
      <div className="sidebar-head">
        <h2>ROUTINES {routines && <span>({routines.length})</span>}</h2>
        <input placeholder="Search…" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>

      <div className="routine-list">
        {error && <div className="message error" style={{ margin: 12 }}>{error}</div>}
        {!error && loading && !routines && <div className="empty">Loading routines…</div>}
        {!error && routines && routines.length === 0 && (
          <div className="empty">No functions or procedures found.</div>
        )}
        {routines && routines.length > 0 && bySchema.length === 0 && (
          <div className="empty">No routine matches “{search}”.</div>
        )}

        {bySchema.map(([schema, items]) => (
          <div key={schema}>
            <div className="schema-name">{schema}</div>
            {items.map((routine) => (
              <button
                key={routine.id}
                className={[
                  'routine-item',
                  routine.id === selectedId ? 'selected' : '',
                  routine.state === 'Dropped' ? 'dropped' : '',
                ].join(' ')}
                onClick={() => onSelect(routine.id)}
                title={routine.displayName}
              >
                <div className="title">
                  {routine.name}({routine.identityArguments})
                </div>
                <div className="sub">
                  <span className="badge kind">{routine.routineType === 'Function' ? 'fn' : 'proc'}</span>
                  {routine.latestVersion !== null && <span>v{routine.latestVersion}</span>}
                  <span className={`badge ${routine.state}`}>{stateLabels[routine.state]}</span>
                </div>
              </button>
            ))}
          </div>
        ))}
      </div>
    </aside>
  )
}
