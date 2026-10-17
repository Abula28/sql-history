// Types and calls for the performance, query-plan and tracing features (Api/Performance,
// Api/QueryPlans, Api/Tracing). Mirrors the C# records.
import { post, request } from '../api'

// ---------- performance ----------

export interface PerformanceStatus {
  statementsAvailable: boolean
  statementsSetupHint: string | null
  functionsTracked: boolean
  trackFunctions: string
  functionsSetupHint: string | null
  statementsResetAtUtc: string | null
  lastSampleAtUtc: string | null
  sampleCount: number
  sampleIntervalSeconds: number
}

export interface RoutineRef {
  id: string
  displayName: string
}

export interface Statement {
  queryId: number
  userId: number
  role: string
  topLevel: boolean
  query: string
  calls: number
  totalExecTimeMs: number
  meanExecTimeMs: number
  minExecTimeMs: number
  maxExecTimeMs: number
  rows: number
  mentionsRoutines: RoutineRef[]
}

export interface Statements {
  available: boolean
  setupHint: string | null
  readAtUtc: string
  statsResetAtUtc: string | null
  summary: {
    statementCount: number
    totalCalls: number
    totalExecTimeMs: number
    meanExecTimeMs: number
    minExecTimeMs: number
    maxExecTimeMs: number
    totalRows: number
  }
  statements: Statement[]
}

export interface FunctionStat {
  routineId: string | null
  displayName: string
  routineType: string
  calls: number
  totalTimeMs: number
  selfTimeMs: number
  meanTimeMs: number
}

export interface Functions {
  tracked: boolean
  trackFunctions: string
  setupHint: string | null
  statsResetAtUtc: string | null
  functions: FunctionStat[]
}

export interface TrendPoint {
  fromUtc: string
  toUtc: string
  calls: number
  totalTimeMs: number
  meanTimeMs: number
  statsReset: boolean
  possiblyIncomplete: boolean
}

export interface Trend {
  sampleCount: number
  points: TrendPoint[]
  topStatements: { queryId: number; userId: number; topLevel: boolean; query: string; calls: number; totalTimeMs: number; meanTimeMs: number }[]
}

export interface RoutinePerformance {
  functionsTracked: boolean
  functionsSetupHint: string | null
  functionStats: FunctionStat | null
  functionTrend: Trend
  statementsAvailable: boolean
  statementsCallingRoutine: Statement[]
}

export type StatementSort = 'total' | 'mean' | 'calls'

// ---------- query plans ----------

export type PlanNodeCategory = 'SequentialScan' | 'IndexScan' | 'Join' | 'Sort' | 'Aggregate' | 'Function' | 'Other'

export interface PlanNode {
  id: number
  parentId: number | null
  path: string
  nodeType: string
  category: PlanNodeCategory
  parentRelationship: string | null
  relationName: string | null
  schema: string | null
  alias: string | null
  indexName: string | null
  functionName: string | null
  joinType: string | null
  strategy: string | null
  startupCost: number
  totalCost: number
  planRows: number
  planWidth: number
  filter: string | null
  indexCondition: string | null
  joinCondition: string | null
  sortKey: string[]
  groupKey: string[]
  exclusiveCost: number
  costShare: number
  isCostHotspot: boolean
}

export interface ParsedPlan {
  totalCost: number
  planRows: number
  nodes: PlanNode[]
}

export interface PlanQuery {
  key: string
  title: string
  description: string
  sql: string
  routineId: string | null
  routineDisplayName: string | null
}

export interface LivePlan {
  query: PlanQuery
  explainedAtUtc: string
  database: string
  serverVersion: string
  plan: ParsedPlan
}

export interface PlanSnapshot {
  id: number
  queryKey: string
  capturedAtUtc: string
  database: string
  serverVersion: string
  routineDisplayName: string | null
  routineVersion: number | null
  totalCost: number
}

export type PlanNodeChange = 'Unchanged' | 'Changed' | 'Added' | 'Removed'

export interface PlanCompare {
  from: { snapshot: PlanSnapshot; plan: ParsedPlan }
  to: { snapshot: PlanSnapshot; plan: ParsedPlan }
  diff: {
    fromTotalCost: number
    toTotalCost: number
    totalCostDelta: number
    structureChanged: boolean
    nodes: { path: string; change: PlanNodeChange; fromNodeId: number | null; toNodeId: number | null; costDelta: number | null; description: string | null }[]
  }
}

// ---------- tracing ----------

export type CallerType = 'HttpRequest' | 'InternalService' | 'BackgroundJob' | 'Unknown'

export interface CallerInfo {
  type: CallerType
  serviceName: string
  label: string
  endpoint: string | null
  source: string
}

export interface RoutineExecution {
  traceId: string
  spanId: string
  startUtc: string
  durationMs: number
  isError: boolean
  routineId: string | null
  routineDisplayName: string
  caller: CallerInfo
  path: string[]
}

export interface TraceSpan {
  spanId: string
  parentSpanId: string | null
  name: string
  kind: string
  serviceName: string
  startUtc: string
  durationMs: number
  isError: boolean
  statusDescription: string | null
  routineId: string | null
  attributes: Record<string, string>
}

export interface TraceDetail {
  traceId: string
  caller: CallerInfo
  spans: TraceSpan[]
}

export interface RoutineExecutions {
  observedCalls: number
  averageDurationMs: number | null
  lastCallUtc: string | null
  errorCount: number
  callers: { type: CallerType; label: string; calls: number; averageDurationMs: number; lastCallUtc: string }[]
  recent: RoutineExecution[]
  retentionHours: number
}

export interface DatabaseSession {
  pid: number
  applicationName: string | null
  role: string | null
  clientAddress: string | null
  state: string | null
  backendStartUtc: string | null
  stateChangeUtc: string | null
  backendType: string | null
}

const enc = encodeURIComponent

export const observabilityApi = {
  performanceStatus: () => request<PerformanceStatus>('/performance/status'),
  statements: (sort: StatementSort, includeCatalog: boolean) =>
    request<Statements>(`/performance/statements?sort=${sort}&limit=50&includeCatalog=${includeCatalog}`),
  functions: () => request<Functions>('/performance/functions'),
  trends: (hours: number, statement?: { queryId: number; userId: number; topLevel: boolean }) =>
    request<Trend>(
      `/performance/trends?hours=${hours}` +
        (statement ? `&queryId=${statement.queryId}&userId=${statement.userId}&topLevel=${statement.topLevel}` : ''),
    ),
  takeSample: () => post<unknown>('/performance/samples'),
  routinePerformance: (id: string) => request<RoutinePerformance>(`/routines/${enc(id)}/performance`),

  planQueries: () => request<PlanQuery[]>('/plans'),
  explain: (key: string) => request<LivePlan>(`/plans/${enc(key)}/explain`),
  captureSnapshot: (key: string) => post<PlanSnapshot>(`/plans/${enc(key)}/snapshots`),
  snapshots: (key: string) => request<PlanSnapshot[]>(`/plans/${enc(key)}/snapshots`),
  comparePlans: (from: number, to: number) => request<PlanCompare>(`/plans/compare?from=${from}&to=${to}`),

  executions: (limit = 50) => request<RoutineExecution[]>(`/traces?limit=${limit}`),
  trace: (traceId: string) => request<TraceDetail>(`/traces/${enc(traceId)}`),
  routineExecutions: (id: string) => request<RoutineExecutions>(`/routines/${enc(id)}/executions`),
  sessions: () => request<DatabaseSession[]>('/database/sessions'),
}

export function ms(value: number | null | undefined): string {
  if (value == null) return '—'
  if (value >= 1000) return `${(value / 1000).toFixed(2)} s`
  if (value >= 10) return `${value.toFixed(0)} ms`
  return `${value.toFixed(2)} ms`
}

export function count(value: number): string {
  return value.toLocaleString()
}

export function ago(utc: string | null | undefined): string {
  if (!utc) return '—'
  const seconds = Math.max(0, (Date.now() - new Date(utc).getTime()) / 1000)
  if (seconds < 60) return `${Math.round(seconds)} s ago`
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`
  return `${(seconds / 3600).toFixed(1)} h ago`
}

export const callerLabels: Record<CallerType, string> = {
  HttpRequest: 'HTTP request',
  InternalService: 'Internal service',
  BackgroundJob: 'Background job',
  Unknown: 'Unknown',
}
