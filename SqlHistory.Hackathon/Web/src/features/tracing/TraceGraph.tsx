import { Background, Controls, Handle, Position, ReactFlow, type Edge, type Node, type NodeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo } from 'react'
import type { TraceSpan } from '../observabilityApi'
import { ms } from '../observabilityApi'

const NODE_W = 230
const NODE_H = 78

type SpanData = { span: TraceSpan; selected: boolean; orphan: boolean }

function SpanCard({ data }: NodeProps<Node<SpanData>>) {
  const s = data.span
  const isRoutine = s.routineId !== null
  return (
    <div className={['span-node', isRoutine ? 'routine' : '', s.isError ? 'error' : '', data.selected ? 'selected' : ''].join(' ')}>
      <Handle type="target" position={Position.Top} />
      <div className="span-node-kind">{isRoutine ? 'PostgreSQL routine' : `${s.kind} · ${s.serviceName}`}</div>
      <div className="span-node-name mono">{s.name}</div>
      <div className="span-node-meta">
        {ms(s.durationMs)}
        {s.isError && <> · ✕ error</>}
        {data.orphan && <> · parent not received</>}
      </div>
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}

const nodeTypes = { span: SpanCard }

/** Draws the received spans of one trace using their parent span ids; nothing is inferred. */
export function TraceGraph({ spans, selectedId, onSelect }: {
  spans: TraceSpan[]
  selectedId: string | null
  onSelect: (span: TraceSpan) => void
}) {
  const { nodes, edges } = useMemo(() => {
    const ids = new Set(spans.map((s) => s.spanId))
    const children = new Map<string, TraceSpan[]>()
    const roots: TraceSpan[] = []
    for (const s of [...spans].sort((a, b) => a.startUtc.localeCompare(b.startUtc))) {
      if (s.parentSpanId && ids.has(s.parentSpanId)) {
        children.set(s.parentSpanId, [...(children.get(s.parentSpanId) ?? []), s])
      } else {
        roots.push(s)
      }
    }

    const positions = new Map<string, { x: number; y: number }>()
    let column = 0
    const place = (s: TraceSpan, depth: number, seen: Set<string>): number => {
      seen.add(s.spanId)
      const kids = (children.get(s.spanId) ?? []).filter((k) => !seen.has(k.spanId))
      const x = kids.length === 0
        ? column++ * (NODE_W + 30)
        : (() => { const xs = kids.map((k) => place(k, depth + 1, seen)); return (xs[0] + xs[xs.length - 1]) / 2 })()
      positions.set(s.spanId, { x, y: depth * (NODE_H + 44) })
      return x
    }
    const seen = new Set<string>()
    roots.forEach((r) => place(r, 0, seen))

    const nodes: Node<SpanData>[] = spans.map((s) => ({
      id: s.spanId,
      type: 'span',
      position: positions.get(s.spanId) ?? { x: 0, y: 0 },
      data: { span: s, selected: s.spanId === selectedId, orphan: s.parentSpanId !== null && !ids.has(s.parentSpanId) },
    }))
    const edges: Edge[] = spans
      .filter((s) => s.parentSpanId && ids.has(s.parentSpanId))
      .map((s) => ({ id: `${s.parentSpanId}-${s.spanId}`, source: s.parentSpanId!, target: s.spanId }))
    return { nodes, edges }
  }, [spans, selectedId])

  return (
    <div className="graph-frame" style={{ height: 420 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        colorMode="dark"
        fitView
        fitViewOptions={{ padding: 0.15 }}
        nodesDraggable={false}
        nodesConnectable={false}
        onNodeClick={(_, node) => onSelect((node.data as SpanData).span)}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={20} size={1} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  )
}
