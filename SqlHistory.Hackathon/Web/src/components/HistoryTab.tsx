import { useState } from 'react'
import { api, formatTime, type VersionSummary } from '../api'
import { useApi } from '../useApi'
import { SqlView } from './SqlEditors'

interface HistoryTabProps {
  routineId: string
  versions: VersionSummary[]
  canRestore: boolean
  onCompare: (from: number, to: number) => void
  onRestore: (version: number) => void
}

export function HistoryTab({ routineId, versions, canRestore, onCompare, onRestore }: HistoryTabProps) {
  const latest = versions[0]?.versionNumber ?? null

  // Show the newest version by default, and again whenever a scan adds one.
  const [choice, setChoice] = useState<{ version: number; latest: number | null } | null>(null)
  const selected = choice && choice.latest === latest ? choice.version : latest
  const setSelected = (version: number) => setChoice({ version, latest })

  const version = useApi(
    selected === null ? null : () => api.version(routineId, selected),
    [routineId, selected],
  )

  if (versions.length === 0) {
    return <div className="empty">No versions recorded yet. Click “Scan now” to record version 1.</div>
  }

  return (
    <div className="history">
      <ol className="timeline">
        {versions.map((v) => (
          <li key={v.versionNumber}>
            <button
              className={v.versionNumber === selected ? 'selected' : ''}
              onClick={() => setSelected(v.versionNumber)}
            >
              <span className="v">Version {v.versionNumber}</span>
              {v.versionNumber === latest && <span className="badge" style={{ marginLeft: 6 }}>latest</span>}
              <span className="when">{formatTime(v.capturedAtUtc)}</span>
              {v.identicalToVersion !== null && (
                <span className="when">same content as v{v.identicalToVersion}</span>
              )}
            </button>
          </li>
        ))}
      </ol>

      <div>
        <div className="toolbar">
          <strong>Version {selected}</strong>
          {version.data && (
            <span className="muted mono" title="SHA-256 of the normalized definition">
              {version.data.definitionHash.slice(0, 12)}
            </span>
          )}
          <div className="spacer" />
          <button
            disabled={selected === null || selected <= 1}
            onClick={() => selected !== null && onCompare(selected - 1, selected)}
          >
            Compare with v{selected !== null ? selected - 1 : ''}
          </button>
          <button
            className="danger"
            disabled={!canRestore || selected === null}
            title={canRestore ? undefined : 'The routine no longer exists in the database.'}
            onClick={() => selected !== null && onRestore(selected)}
          >
            Restore this version…
          </button>
        </div>

        {version.error && <div className="message error">{version.error}</div>}
        {version.loading && !version.data && <div className="empty">Loading version…</div>}
        {version.data && <SqlView value={version.data.definition ?? ''} />}
      </div>
    </div>
  )
}
