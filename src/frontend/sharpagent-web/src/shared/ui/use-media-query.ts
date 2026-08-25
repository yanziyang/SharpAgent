import { useEffect, useState } from 'react'

export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(false)

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }

    const mediaQuery = window.matchMedia(query)
    const update = () => setMatches(mediaQuery.matches)
    update()
    mediaQuery.addEventListener?.('change', update)
    mediaQuery.addListener?.(update)

    return () => {
      mediaQuery.removeEventListener?.('change', update)
      mediaQuery.removeListener?.(update)
    }
  }, [query])

  return matches
}
