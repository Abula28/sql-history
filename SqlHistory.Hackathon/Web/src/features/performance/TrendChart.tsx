import { useState } from 'react'
import { formatTime } from '../../api'
import { count, ms, type TrendPoint } from '../observabilityApi'

type Measure = 'calls' | 'totalTimeMs'

interface TrendChartProps {
  points: TrendPoint[]
  measure: Measure
  title: string
}

const WIDTH = 720
const HEIGHT = 160
const PAD = { top: 10, right: 8, bottom: 22, left: 56 }

/**
 * One series of per-interval deltas as bars. Intervals that contain a statistics reset or
 * evicted entries are marked, because their values cover less than the whole interval.
 */
export function TrendChart({ points, measure, title }: TrendChartProps) {
  const [hover, setHover] = useState<number | null>(null)
  const [asTable, setAsTable] = useState(false)

  if (points.length === 0) {
    return (
      <div className="card">
        <h3>{title}</h3>
        <span className="muted">Not enough samples yet: trends need at least two samples.</span>
      </div>
    )
  }

  const values = points.map((p) => p[measure])
  const max = Math.max(...values, measure === 'calls' ? 1 : 0.001)
  const plotW = WIDTH - PAD.left - PAD.right
  const plotH = HEIGHT - PAD.top - PAD.bottom
  const step = plotW / points.length
  const barW = Math.max(2, Math.min(28, step - 2))
  const format = (v: number) => (measure === 'calls' ? count(Math.round(v)) : ms(v))
  const ticks = [0, max / 2, max]

  return (
    <div className="card">
      <div className="toolbar" style={{ marginBottom: 6 }}>
        <h3 style={{ margin: 0 }}>{title}</h3>
        <div className="spacer" />
        <button onClick={() => setAsTable((x) => !x)}>{asTable ? 'Show chart' : 'Show table'}</button>
      </div>

      {asTable ? (
        <table className="data">
          <thead>
            <tr><th>Interval end</th><th>Calls</th><th>Total time</th><th>Mean</th><th>Note</th></tr>
          </thead>
          <tbody>
            {points.map((p) => (
              <tr key={p.toUtc}>
                <td>{formatTime(p.toUtc)}</td>
                <td>{count(p.calls)}</td>
                <td>{ms(p.totalTimeMs)}</td>
                <td>{ms(p.meanTimeMs)}</td>
                <td>{note(p)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="chart" onMouseLeave={() => setHover(null)}>
          <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="img" aria-label={title}>
            {ticks.map((t) => {
              const y = PAD.top + plotH - (t / max) * plotH
              return (
                <g key={t}>
                  <line x1={PAD.left} x2={WIDTH - PAD.right} y1={y} y2={y} className="grid" />
                  <text x={PAD.left - 6} y={y + 4} textAnchor="end" className="axis">{format(t)}</text>
                </g>
              )
            })}
            {points.map((p, i) => {
              const v = values[i]
              const h = Math.max(v > 0 ? 2 : 0, (v / max) * plotH)
              const x = PAD.left + i * step + (step - barW) / 2
              const y = PAD.top + plotH - h
              return (
                <g key={p.toUtc} onMouseEnter={() => setHover(i)}>
                  {/* Hit target taller and wider than the bar. */}
                  <rect x={PAD.left + i * step} y={PAD.top} width={step} height={plotH} fill="transparent" />
                  <path d={roundedTop(x, y, barW, h)} className={hover === i ? 'bar active' : 'bar'} />
                  {(p.statsReset || p.possiblyIncomplete) && (
                    <text x={x + barW / 2} y={PAD.top + plotH + 14} textAnchor="middle" className="axis marker">
                      {p.statsReset ? 'R' : '!'}
                    </text>
                  )}
                </g>
              )
            })}
            <line x1={PAD.left} x2={WIDTH - PAD.right} y1={PAD.top + plotH} y2={PAD.top + plotH} className="baseline" />
          </svg>
          {hover !== null && (
            <div className="tooltip" style={{ left: `${((PAD.left + (hover + 0.5) * step) / WIDTH) * 100}%` }}>
              <strong>{formatTime(points[hover].fromUtc)} → {formatTime(points[hover].toUtc)}</strong>
              <div>{count(points[hover].calls)} calls · {ms(points[hover].totalTimeMs)} total · {ms(points[hover].meanTimeMs)} mean</div>
              {note(points[hover]) && <div className="muted">{note(points[hover])}</div>}
            </div>
          )}
          <div className="muted" style={{ fontSize: 12 }}>
            Each bar is the activity between two consecutive samples. R = statistics were reset in that interval;
            ! = entries were evicted, so some activity is missing.
          </div>
        </div>
      )}
    </div>
  )
}

function note(p: TrendPoint): string {
  if (p.statsReset) return 'Statistics reset during this interval: activity before the reset is not counted.'
  if (p.possiblyIncomplete) return 'Entries were evicted during this interval: some activity may be missing.'
  return ''
}

/** A bar with 4px rounded top corners and a square base on the axis. */
function roundedTop(x: number, y: number, w: number, h: number): string {
  if (h <= 0) return ''
  const r = Math.min(4, w / 2, h)
  return `M${x},${y + h} V${y + r} Q${x},${y} ${x + r},${y} H${x + w - r} Q${x + w},${y} ${x + w},${y + r} V${y + h} Z`
}
