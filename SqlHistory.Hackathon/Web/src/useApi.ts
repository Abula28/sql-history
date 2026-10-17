import { useCallback, useEffect, useState } from 'react'

export interface ApiState<T> {
  data: T | null
  error: string | null
  loading: boolean
  reload: () => void
}

/** Runs `load` whenever `deps` change and tracks loading/error state. Stale responses are ignored. */
export function useApi<T>(load: (() => Promise<T>) | null, deps: unknown[]): ApiState<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [reloadCount, setReloadCount] = useState(0)

  useEffect(() => {
    if (!load) {
      setData(null)
      setError(null)
      return
    }

    let cancelled = false
    setLoading(true)
    load()
      .then((result) => {
        if (cancelled) return
        setData(result)
        setError(null)
      })
      .catch((e: Error) => {
        if (cancelled) return
        setData(null)
        setError(e.message)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reloadCount])

  const reload = useCallback(() => setReloadCount((x) => x + 1), [])
  return { data, error, loading, reload }
}
