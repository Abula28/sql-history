import { Background, Controls, Handle, Position, ReactFlow, type Edge, type Node, type NodeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo } from 'react'
import type { ParsedPlan, PlanNode, PlanNodeChange } from '../observabilityApi'

const NODE_W = 220
const NODE_H = 96
const GAP_X = 24
const GAP_Y = 46

type PlanNodeData = {
  node: PlanNode
  selected: boolean
  change?: PlanNodeChange
}

/** Only three categories get a hue (validated as distinguishable); the rest are neutral and named in text. */
const categoryClass: Record<string, string> = {
  SequentialScan: 'cat-seq',
  IndexScan: 'cat-index',
  Join: 'cat-join',
}

function PlanNodeCard({ data }: NodeProps<Node<PlanNodeData>>) {
  const n = data.node
  const target = n.relationName ?? n.functionName ?? n.indexName
  return (
    <div
      className={[
        'plan-node',
        categoryClass[n.category] ?? 'cat-other',
        data.selected ? 'selected' : '',
        n.isCostHotspot ? 'hotspot' : '',
        data.change ? `change-${data.change}` : '',
      ].join(' ')}
    >
      <Handle type="target" position={Position.Top} />
      <div className="plan-node-type">{n.nodeType}</div>
      {target && <div className="plan-node-target mono">{target}</div>}
      <div className="plan-node-meta">
        cost {n.totalCost.toFixed(1)} · rows {Math.round(n.planRows).toLocaleString()}
      </div>
      <div className="plan-node-badges">
        {n.isCostHotspot && <span className="badge">cost hotspot {Math.round(n.costShare * 100)}%</span>}
        {data.change && data.change !== 'Unchanged' && <span className="badge">{data.change.toLowerCase()}</span>}
      </div>
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}

const nodeTypes = { plan: PlanNodeCard }

interface PlanGraphProps {
  plan: ParsedPlan
  selectedId: number | null
  onSelect: (node: PlanNode) => void
  changes?: Map<number, PlanNodeChange>
  height?: number
}

export function PlanGraph({ plan, selectedId, onSelect, changes, height = 460 }: PlanGraphProps) {
  const { nodes, edges } = useMemo(() => layout(plan, selectedId, changes), [plan, selectedId, changes])

  return (
    <div className="graph-frame" style={{ height }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        colorMode="dark"
        fitView
        fitViewOptions={{ padding: 0.15 }}
        nodesDraggable={false}
        nodesConnectable={false}
        onNodeClick={(_, node) => onSelect((node.data as PlanNodeData).node)}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={20} size={1} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  )
}

/** Tidy top-down tree: each leaf gets its own column and a parent is centered over its children. */
function layout(plan: ParsedPlan, selectedId: number | null, changes?: Map<number, PlanNodeChange>) {
  const children = new Map<number, PlanNode[]>()
  for (const n of plan.nodes) {
    if (n.parentId !== null) children.set(n.parentId, [...(children.get(n.parentId) ?? []), n])
  }

  const positions = new Map<number, { x: number; y: number }>()
  let nextColumn = 0
  const place = (n: PlanNode, depth: number): number => {
    const kids = children.get(n.id) ?? []
    let x: number
    if (kids.length === 0) {
      x = nextColumn++ * (NODE_W + GAP_X)
    } else {
      const xs = kids.map((k) => place(k, depth + 1))
      x = (xs[0] + xs[xs.length - 1]) / 2
    }
    positions.set(n.id, { x, y: depth * (NODE_H + GAP_Y) })
    return x
  }
  const root = plan.nodes.find((n) => n.parentId === null)
  if (root) place(root, 0)

  const nodes: Node<PlanNodeData>[] = plan.nodes.map((n) => ({
    id: String(n.id),
    type: 'plan',
    position: positions.get(n.id) ?? { x: 0, y: 0 },
    data: { node: n, selected: n.id === selectedId, change: changes?.get(n.id) },
  }))

  // Edges point from child to parent: rows flow upward into the node that consumes them.
  const edges: Edge[] = plan.nodes
    .filter((n) => n.parentId !== null)
    .map((n) => ({
      id: `${n.parentId}-${n.id}`,
      source: String(n.parentId),
      target: String(n.id),
      label: n.parentRelationship ?? undefined,
      animated: false,
    }))

  return { nodes, edges }
}
