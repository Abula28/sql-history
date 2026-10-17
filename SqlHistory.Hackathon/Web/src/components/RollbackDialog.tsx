import { useEffect, useState } from 'react'
import { api, type RollbackResult } from '../api'
import { useApi } from '../useApi'
import { SqlDiff } from './SqlEditors'

interface RollbackDialogProps {
  routineId: string
  displayName: string
  databaseName: string | null
  version: number
  onClose: () => void
  onRestored: () => void
}

/**
 * Restore flow: the API dry-runs the restore inside a rolled-back transaction, we show the
 * exact SQL, and only after explicit confirmation is it executed. The confirmed request carries
 * the hash of the definition shown here, so a concurrent change makes it fail instead of overwriting.
 */
export function RollbackDialog({ routineId, displayName, databaseName, version, onClose, onRestored }: RollbackDialogProps) {
  const preview = useApi(() => api.previewRollback(routineId, version), [routineId, version])
  const [confirmed, setConfirmed] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [result, setResult] = useState<RollbackResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !restoring && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, restoring])

  async function restore() {
    if (!preview.data?.currentDefinitionHash) return
    setRestoring(true)
    setError(null)
    try {
      const restored = await api.rollback(routineId, version, preview.data.currentDefinitionHash)
      setResult(restored)
      onRestored()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setRestoring(false)
    }
  }

  const data = preview.data
  const done = result?.status === 'Succeeded'

  return (
    <div className="backdrop" onMouseDown={(e) => e.target === e.currentTarget && !restoring && onClose()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-label={`Restore version ${version}`}>
        <h2>Restore version {version}</h2>
        <p className="muted mono" style={{ marginTop: 0 }}>{displayName}</p>

        {preview.loading && <div className="empty">Validating the restore (dry run)…</div>}
        {preview.error && <div className="message error">{preview.error}</div>}

        {data && !done && (
          <>
            <div className={`message ${data.canRestore ? 'success' : 'error'}`}>
              {data.canRestore ? '✓ ' : '✕ '}
              {data.message}
            </div>

            <SqlDiff
              original={data.currentDefinition ?? ''}
              modified={data.sqlToExecute}
              originalLabel="Current definition in the database"
              modifiedLabel={`SQL that will be executed (version ${version})`}
              height="48vh"
            />

            {data.canRestore && (
              <label className="confirm">
                <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
                I reviewed this SQL and want to execute it against <strong>{databaseName ?? 'the database'}</strong>.
              </label>
            )}
          </>
        )}

        {error && <div className="message error" style={{ marginTop: 12 }}>{error}</div>}
        {done && (
          <div className="message success">
            {result.message}
            {result.recordedVersion !== null && ` Recorded as version ${result.recordedVersion}.`}
          </div>
        )}

        <div className="actions">
          <button onClick={onClose} disabled={restoring}>
            {done ? 'Close' : 'Cancel'}
          </button>
          {!done && (
            <button className="danger" onClick={restore} disabled={!data?.canRestore || !confirmed || restoring}>
              {restoring ? 'Restoring…' : `Restore version ${version}`}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
