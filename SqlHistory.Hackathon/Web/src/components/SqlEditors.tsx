import { DiffEditor, Editor } from '@monaco-editor/react'

const baseOptions = {
  readOnly: true,
  domReadOnly: true,
  minimap: { enabled: false },
  fontSize: 13,
  scrollBeyondLastLine: false,
  automaticLayout: true,
  renderWhitespace: 'boundary' as const,
}

export function SqlView({ value, height = '62vh' }: { value: string; height?: string }) {
  return (
    <div className="editor-frame">
      <Editor
        height={height}
        language="pgsql"
        theme="vs-dark"
        value={value}
        options={baseOptions}
        loading={<div className="empty">Loading editor…</div>}
      />
    </div>
  )
}

interface SqlDiffProps {
  original: string
  modified: string
  originalLabel: string
  modifiedLabel: string
  height?: string
}

/** Side-by-side, read-only diff. Whitespace changes are shown because they change the stored hash. */
export function SqlDiff({ original, modified, originalLabel, modifiedLabel, height = '58vh' }: SqlDiffProps) {
  return (
    <div className="editor-frame">
      <div className="diff-labels">
        <span>{originalLabel}</span>
        <span>{modifiedLabel}</span>
      </div>
      <DiffEditor
        height={height}
        language="pgsql"
        theme="vs-dark"
        original={original}
        modified={modified}
        // Let the component own its models; avoids "model disposed" errors when switching versions.
        keepCurrentOriginalModel
        keepCurrentModifiedModel
        options={{
          ...baseOptions,
          originalEditable: false,
          renderSideBySide: true,
          ignoreTrimWhitespace: false,
          renderOverviewRuler: true,
        }}
        loading={<div className="empty">Loading diff…</div>}
      />
    </div>
  )
}
