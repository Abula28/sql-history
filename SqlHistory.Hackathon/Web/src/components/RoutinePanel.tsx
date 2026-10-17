import { useState } from 'react'
import { api, formatTime, type Status } from '../api'
import { useApi } from '../useApi'
import { AiReviewTab, type AnalysisState } from './AiReviewTab'
import { CompareTab } from './CompareTab'
import { HistoryTab } from './HistoryTab'
import { RollbackDialog } from './RollbackDialog'
import { SqlView } from './SqlEditors'
import { RoutineObservabilityTab } from '../features/tracing/RoutineObservabilityTab'

type Tab = 'definition' | 'history' | 'compare' | 'ai' | 'observed'

const stateText = {
  Untracked: 'Not scanned yet',
  Tracked: 'Matches latest version',
  ChangedSinceLastScan: 'Changed since last scan',
  Dropped: 'No longer in the database',
}

interface RoutinePanelProps {
  routineId: string
  refreshKey: number
  status: Status | null
  onRestored: () => void
  onOpenRoutine: (id: string) => void
  onOpenTraces: () => void
  onOpenPlan: (key: string) => void
}

export function RoutinePanel({ routineId, refreshKey, status, onRestored, onOpenRoutine, onOpenTraces, onOpenPlan }: RoutinePanelProps) {
  const detail = useApi(() => api.routine(routineId), [routineId, refreshKey])
  const versions = useApi(() => api.versions(routineId), [routineId, refreshKey])

  // The parent remounts this panel per routine (key={routineId}), so state starts fresh for each one.
  const [tab, setTab] = useState<Tab>('definition')
  const [chosenPair, setChosenPair] = useState<{ from: number; to: number; latest: number } | null>(null)
  const [analyses, setAnalyses] = useState<Record<string, AnalysisState>>({})
  const [restoreVersion, setRestoreVersion] = useState<number | null>(null)

  // Default comparison: previous version → latest version. A user's choice is dropped
  // once a scan adds a newer version, so the newest change is shown.
  const latest = versions.data?.[0]?.versionNumber ?? 0
  const pair =
    chosenPair && chosenPair.latest === latest
      ? chosenPair
      : latest >= 2
        ? { from: latest - 1, to: latest }
        : null
  const setPair = (p: { from: number; to: number }) => setChosenPair({ ...p, latest })

  const routine = detail.data?.routine
  const versionList = versions.data ?? []
  const canRestore = routine !== undefined && routine.state !== 'Dropped'
  const pairKey = pair ? `${pair.from}:${pair.to}` : ''

  async function analyze() {
    if (!pair) return
    const key = pairKey
    setTab('ai')
    setAnalyses((a) => ({ ...a, [key]: { loading: true, error: null, result: null } }))
    try {
      const result = await api.analyze(routineId, pair.from, pair.to)
      setAnalyses((a) => ({ ...a, [key]: { loading: false, error: null, result } }))
    } catch (e) {
      setAnalyses((a) => ({ ...a, [key]: { loading: false, error: (e as Error).message, result: null } }))
    }
  }

  if (detail.error) return <div className="message error">{detail.error}</div>
  if (!routine) return <div className="empty">Loading routine…</div>

  return (
    <div>
      <div className="routine-header">
        <h1>{routine.displayName}</h1>
        <div className="facts">
          <span className="badge kind">{routine.routineType.toLowerCase()}</span>
          <span className={`badge ${routine.state}`}>{stateText[routine.state]}</span>
          <span>
            {routine.latestVersion !== null ? `Latest: version ${routine.latestVersion}` : 'No versions recorded'}
          </span>
          {routine.lastChangeDetectedAtUtc && (
            <span>· last change detected {formatTime(routine.lastChangeDetectedAtUtc)}</span>
          )}
        </div>
      </div>

      <nav className="tabs">
        <TabButton tab="definition" current={tab} onClick={setTab}>Definition</TabButton>
        <TabButton tab="history" current={tab} onClick={setTab}>History ({versionList.length})</TabButton>
        <TabButton tab="compare" current={tab} onClick={setTab}>Compare</TabButton>
        <TabButton tab="ai" current={tab} onClick={setTab}>AI Review</TabButton>
        <TabButton tab="observed" current={tab} onClick={setTab}>Observed</TabButton>
      </nav>

      {versions.error && <div className="message error">{versions.error}</div>}

      {tab === 'definition' &&
        (detail.data?.currentDefinition != null ? (
          <>
            {routine.state === 'ChangedSinceLastScan' && (
              <div className="message warn">
                The database definition differs from version {routine.latestVersion}. Scan to record it.
              </div>
            )}
            <SqlView value={detail.data.currentDefinition} />
          </>
        ) : (
          <div className="message warn">This routine no longer exists in the database. Its history is still available.</div>
        ))}

      {tab === 'history' && (
        <HistoryTab
          routineId={routineId}
          versions={versionList}
          canRestore={canRestore}
          onCompare={(from, to) => {
            setPair({ from, to })
            setTab('compare')
          }}
          onRestore={setRestoreVersion}
        />
      )}

      {tab === 'compare' &&
        (pair ? (
          <CompareTab
            routineId={routineId}
            versions={versionList}
            from={pair.from}
            to={pair.to}
            canRestore={canRestore}
            onChange={(from, to) => setPair({ from, to })}
            onAnalyze={analyze}
            onRestore={setRestoreVersion}
          />
        ) : (
          <div className="empty">Only one version recorded. Change the routine and scan again to compare.</div>
        ))}

      {tab === 'ai' &&
        (pair ? (
          <AiReviewTab
            versions={versionList}
            from={pair.from}
            to={pair.to}
            ai={status?.ai ?? null}
            analysis={analyses[pairKey]}
            onChange={(from, to) => setPair({ from, to })}
            onAnalyze={analyze}
          />
        ) : (
          <div className="empty">AI review needs two versions to compare.</div>
        ))}

      {tab === 'observed' && (
        <RoutineObservabilityTab
          routineId={routineId}
          refreshKey={refreshKey}
          onOpenTraces={onOpenTraces}
          onOpenRoutine={onOpenRoutine}
          onOpenPlan={onOpenPlan}
        />
      )}

      {restoreVersion !== null && (
        <RollbackDialog
          routineId={routineId}
          displayName={routine.displayName}
          databaseName={status?.database.database ?? null}
          version={restoreVersion}
          onClose={() => setRestoreVersion(null)}
          onRestored={onRestored}
        />
      )}
    </div>
  )
}

function TabButton({ tab, current, onClick, children }: {
  tab: Tab
  current: Tab
  onClick: (tab: Tab) => void
  children: React.ReactNode
}) {
  return (
    <button className={tab === current ? 'active' : ''} onClick={() => onClick(tab)}>
      {children}
    </button>
  )
}
