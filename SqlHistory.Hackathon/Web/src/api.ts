// Typed client for SqlHistory.Hackathon.Api. Types mirror the C# records in Api/Contracts.

export type RoutineType = 'Procedure' | 'Function'
export type RoutineState = 'Untracked' | 'Tracked' | 'ChangedSinceLastScan' | 'Dropped'
export type RiskLevel = 'None' | 'Low' | 'Medium' | 'High'
export type RestoreStatus = 'Succeeded' | 'NotSupported' | 'RoutineNotFound' | 'AlreadyCurrent' | 'Conflict'

export interface RoutineSummary {
  id: string
  schema: string
  name: string
  routineType: RoutineType
  identityArguments: string
  displayName: string
  state: RoutineState
  latestVersion: number | null
  lastChangeDetectedAtUtc: string | null
}

export interface RoutineDetail {
  routine: RoutineSummary
  currentDefinition: string | null
  currentDefinitionHash: string | null
}

export interface VersionSummary {
  versionNumber: number
  capturedAtUtc: string
  definitionHash: string
  identicalToVersion: number | null
}

export interface Version {
  versionNumber: number
  capturedAtUtc: string
  definitionHash: string
  definition: string | null
}

export interface Compare {
  from: Version
  to: Version
}

export interface ScanSummary {
  scannedAtUtc: string
  newCount: number
  modifiedCount: number
  unchangedCount: number
  changes: { id: string; displayName: string; status: string; versionNumber: number }[]
}

export interface Status {
  database: {
    provider: string
    connected: boolean
    database: string | null
    user: string | null
    serverVersion: string | null
    error: string | null
  }
  scan: {
    lastScanAtUtc: string | null
    lastScan: ScanSummary | null
    lastFailureAtUtc: string | null
    lastError: string | null
    autoScanIntervalSeconds: number
  }
  ai: { configured: boolean; model: string; endpointHost: string }
}

export interface Analysis {
  fromVersion: number
  toVersion: number
  summary: string
  changes: string[]
  risks: string[]
  riskLevel: RiskLevel
  recommendations: string[]
  embeddedInstructionsDetected: boolean
  model: string
  sentTo: string
}

export interface RollbackPreview {
  targetVersion: number
  sqlToExecute: string
  currentDefinition: string | null
  currentDefinitionHash: string | null
  canRestore: boolean
  status: RestoreStatus
  message: string
}

export interface RollbackResult {
  status: RestoreStatus
  message: string
  recordedVersion: number | null
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(`/api${path}`, init)
  } catch {
    throw new ApiError(0, 'Cannot reach the SQL Time Machine API. Is it running on localhost:5180?')
  }

  const body = await response.json().catch(() => null)
  if (!response.ok) {
    // Problem details use "detail"; rollback results use "message".
    const message = body?.detail ?? body?.message ?? `Request failed with HTTP ${response.status}.`
    throw new ApiError(response.status, message)
  }
  return body as T
}

function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      // Required by the API for every state-changing request (see RequireClientHeaderFilter).
      'X-SqlHistory-Client': 'web',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

const routine = (id: string) => `/routines/${encodeURIComponent(id)}`

export const api = {
  status: () => request<Status>('/status'),
  routines: () => request<RoutineSummary[]>('/routines'),
  routine: (id: string) => request<RoutineDetail>(routine(id)),
  versions: (id: string) => request<VersionSummary[]>(`${routine(id)}/versions`),
  version: (id: string, version: number) => request<Version>(`${routine(id)}/versions/${version}`),
  compare: (id: string, from: number, to: number) =>
    request<Compare>(`${routine(id)}/compare?from=${from}&to=${to}`),
  scan: () => post<ScanSummary>('/snapshots/scan'),
  analyze: (id: string, from: number, to: number) => post<Analysis>(`${routine(id)}/analyze`, { from, to }),
  previewRollback: (id: string, version: number) =>
    post<RollbackPreview>(`${routine(id)}/rollback/preview`, { version }),
  rollback: (id: string, version: number, expectedCurrentHash: string) =>
    post<RollbackResult>(`${routine(id)}/rollback`, { version, expectedCurrentHash, confirm: true }),
}

export function formatTime(utc: string | null | undefined): string {
  if (!utc) return '—'
  return new Date(utc).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}
