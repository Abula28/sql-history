import type { ReactNode } from 'react'
import { formatTime, type ScanSummary, type Status } from '../api'

interface HeaderProps {
  status: Status | null
  statusError: string | null
  scanning: boolean
  scanError: string | null
  lastScanResult: ScanSummary | null
  onScan: () => void
  nav: ReactNode
}

export function Header({ status, statusError, scanning, scanError, lastScanResult, onScan, nav }: HeaderProps) {
  const db = status?.database
  const scan = status?.scan
  const connected = db?.connected ?? false

  let connection: string
  if (statusError) connection = statusError
  else if (!db) connection = 'Checking connection…'
  else if (connected) connection = `PostgreSQL ${db.serverVersion} · ${db.database} as ${db.user}`
  else connection = `Cannot connect to ${db.database ?? 'database'}: ${db.error}`

  const summary = lastScanResult ?? scan?.lastScan ?? null

  return (
    <header className="header">
      <div className="brand">
        SQL <span>TIME MACHINE</span>
      </div>

      {nav}

      <div className="meta" title={connection}>
        <span className={`dot ${connected && !statusError ? 'ok' : 'bad'}`} />
        {connection.length > 90 ? connection.slice(0, 90) + '…' : connection}
      </div>

      <div className="spacer" />

      <div className="meta">
        {scanError ? (
          <span style={{ color: 'var(--red)' }}>Scan failed: {scanError}</span>
        ) : scan?.lastScanAtUtc ? (
          <>
            Last scan {formatTime(scan.lastScanAtUtc)}
            {summary && ` · ${summary.newCount} new, ${summary.modifiedCount} changed`}
          </>
        ) : (
          'Not scanned since the API started'
        )}
        {scan && scan.autoScanIntervalSeconds > 0 && ` · auto-scan every ${scan.autoScanIntervalSeconds}s`}
      </div>

      <button className="primary" onClick={onScan} disabled={scanning || !connected}>
        {scanning ? 'Scanning…' : 'Scan now'}
      </button>
    </header>
  )
}
