// Bundle Monaco locally instead of letting @monaco-editor/react load it from a CDN,
// so the app works offline. Only the editor core and PostgreSQL highlighting are included.
import { loader } from '@monaco-editor/react'
import * as monaco from 'monaco-editor/editor/editor.api'
import 'monaco-editor/languages/definitions/pgsql/register'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'

self.MonacoEnvironment = {
  getWorker: () => new EditorWorker(),
}

loader.config({ monaco })
