import { api, formatTime, type VersionSummary } from '../api'
import { useApi } from '../useApi'
import { SqlDiff } from './SqlEditors'
import { VersionPicker } from './VersionPicker'

interface CompareTabProps {
  routineId: string
  versions: VersionSummary[]
  from: number
  to: number
  canRestore: boolean
  onChange: (from: number, to: number) => void
  onAnalyze: () => void
  onRestore: (version: number) => void
}

export function CompareTab({ routineId, versions, from, to, canRestore, onChange, onAnalyze, onRestore }: CompareTabProps) {
  const compare = useApi(() => api.compare(routineId, from, to), [routineId, from, to])

  if (versions.length < 2) {
    return <div className="empty">Only one version recorded. Change the routine and scan again to compare.</div>
  }

  const identical = compare.data && compare.data.from.definitionHash === compare.data.to.definitionHash

  return (
    <div>
      <div className="toolbar">
        <VersionPicker versions={versions} from={from} to={to} onChange={onChange} />
        <div className="spacer" />
        <button className="ai" onClick={onAnalyze} disabled={from === to || !!identical}>
          Analyze with AI
        </button>
        <button className="danger" onClick={() => onRestore(from)} disabled={!canRestore}>
          Preview rollback to v{from}…
        </button>
      </div>

      {compare.error && <div className="message error">{compare.error}</div>}
      {identical && <div className="message">These versions have identical definitions.</div>}
      {compare.loading && !compare.data && <div className="empty">Loading versions…</div>}
      {compare.data && (
        <SqlDiff
          original={compare.data.from.definition ?? ''}
          modified={compare.data.to.definition ?? ''}
          originalLabel={`Version ${compare.data.from.versionNumber} · ${formatTime(compare.data.from.capturedAtUtc)}`}
          modifiedLabel={`Version ${compare.data.to.versionNumber} · ${formatTime(compare.data.to.capturedAtUtc)}`}
        />
      )}
    </div>
  )
}
