import type { VersionSummary } from '../api'

interface VersionPickerProps {
  versions: VersionSummary[]
  from: number
  to: number
  onChange: (from: number, to: number) => void
}

export function VersionPicker({ versions, from, to, onChange }: VersionPickerProps) {
  const options = versions.map((v) => (
    <option key={v.versionNumber} value={v.versionNumber}>
      Version {v.versionNumber}
    </option>
  ))

  return (
    <>
      <select value={from} onChange={(e) => onChange(Number(e.target.value), to)} aria-label="From version">
        {options}
      </select>
      <span className="muted">→</span>
      <select value={to} onChange={(e) => onChange(from, Number(e.target.value))} aria-label="To version">
        {options}
      </select>
      <button onClick={() => onChange(to, from)} title="Swap">
        ⇄
      </button>
    </>
  )
}
