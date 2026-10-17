import { useEffect, useRef, useState } from 'react'
import { api, type ScanSummary } from './api'
import { Header } from './components/Header'
import { RoutineList } from './components/RoutineList'
import { RoutinePanel } from './components/RoutinePanel'
import { PerformanceView } from './features/performance/PerformanceView'
import { PlanView } from './features/query-plans/PlanView'
import { TracesView } from './features/tracing/TracesView'
import { useApi } from './useApi'

const STATUS_POLL_MS = 10_000

type View = 'routines' | 'performance' | 'plans' | 'traces'

const views: { id: View; label: string }[] = [
  { id: 'routines', label: 'Routines' },
  { id: 'performance', label: 'Performance' },
  { id: 'plans', label: 'Query Plans' },
  { id: 'traces', label: 'Traces' },
]

function readSelectedFromHash(): string | null {
  const match = window.location.hash.match(/^#routine=(.+)$/)
  return match ? decodeURIComponent(match[1]) : null
}

export default function App() {
  // Bumped after scans and restores so every panel reloads from the API.
  const [refreshKey, setRefreshKey] = useState(0)
  const refresh = () => setRefreshKey((x) => x + 1)

  const [statusTick, setStatusTick] = useState(0)
  const status = useApi(() => api.status(), [statusTick, refreshKey])
  const routines = useApi(() => api.routines(), [refreshKey])

  const [selectedId, setSelectedId] = useState<string | null>(readSelectedFromHash)
  const [view, setView] = useState<View>('routines')
  const [planKey, setPlanKey] = useState<string | null>(null)

  const openRoutine = (id: string) => {
    setSelectedId(id)
    setView('routines')
  }
  const openPlan = (key: string) => {
    setPlanKey(key)
    setView('plans')
  }
  const [scanning, setScanning] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)
  const [lastScanResult, setLastScanResult] = useState<ScanSummary | null>(null)

  useEffect(() => {
    const timer = window.setInterval(() => setStatusTick((x) => x + 1), STATUS_POLL_MS)
    return () => window.clearInterval(timer)
  }, [])

  // When auto-scan is enabled on the API, refresh the views after each scan it runs.
  const lastSeenScan = useRef<string | null | undefined>(undefined)
  const lastScanAt = status.data?.scan.lastScanAtUtc
  useEffect(() => {
    if (lastScanAt === undefined) return
    if (lastSeenScan.current !== undefined && lastSeenScan.current !== lastScanAt) refresh()
    lastSeenScan.current = lastScanAt
  }, [lastScanAt])

  useEffect(() => {
    if (selectedId) window.history.replaceState(null, '', `#routine=${encodeURIComponent(selectedId)}`)
  }, [selectedId])

  async function scan() {
    setScanning(true)
    setScanError(null)
    try {
      setLastScanResult(await api.scan())
      refresh()
    } catch (e) {
      setScanError((e as Error).message)
    } finally {
      setScanning(false)
    }
  }

  return (
    <div className="app">
      <Header
        status={status.data}
        statusError={status.error}
        scanning={scanning}
        scanError={scanError}
        lastScanResult={lastScanResult}
        onScan={scan}
        nav={
          <nav className="views">
            {views.map((v) => (
              <button key={v.id} className={v.id === view ? 'active' : ''} onClick={() => setView(v.id)}>
                {v.label}
              </button>
            ))}
          </nav>
        }
      />

      {view === 'performance' && <main className="main"><PerformanceView onOpenRoutine={openRoutine} /></main>}
      {view === 'plans' && <main className="main"><PlanView key={planKey ?? ''} initialKey={planKey} onOpenRoutine={openRoutine} /></main>}
      {view === 'traces' && <main className="main"><TracesView onOpenRoutine={openRoutine} /></main>}

      <div className="body" hidden={view !== 'routines'}>
        <RoutineList
          routines={routines.data}
          loading={routines.loading}
          error={routines.error}
          selectedId={selectedId}
          onSelect={setSelectedId}
        />

        <main className="main">
          {selectedId ? (
            <RoutinePanel
              key={selectedId}
              routineId={selectedId}
              refreshKey={refreshKey}
              status={status.data}
              onRestored={refresh}
              onOpenRoutine={openRoutine}
              onOpenTraces={() => setView('traces')}
              onOpenPlan={openPlan}
            />
          ) : (
            <div className="empty">
              Select a routine to see its definition and history.
              <br />
              Changes are detected when a scan runs: click “Scan now” after changing a routine.
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
