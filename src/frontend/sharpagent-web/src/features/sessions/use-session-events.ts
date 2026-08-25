import { useEffect, useReducer, useRef, useState } from 'react'
import { ApiProblemError } from '@/shared/api/client'
import {
  emptySessionEventState,
  sessionEventReducer,
  type SessionEvent,
  type SessionEventState,
} from './session-types'

export type SessionConnection = 'connecting' | 'live' | 'reconnecting' | 'error'

export interface ParsedSseBlock {
  kind: 'event' | 'gap'
  event?: SessionEvent
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function parseSseBlock(block: string): ParsedSseBlock | null {
  let id = ''
  let type = 'status'
  const data: string[] = []

  for (const line of block.split(/\r?\n/)) {
    if (line.startsWith(':')) {
      continue
    }

    const separator = line.indexOf(':')
    const field = separator >= 0 ? line.slice(0, separator) : line
    const value = separator >= 0 ? line.slice(separator + 1).trimStart() : ''

    if (field === 'id') {
      id = value
    } else if (field === 'event') {
      type = value || 'status'
    } else if (field === 'data') {
      data.push(value)
    }
  }

  if (data.length === 0) {
    return null
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(data.join('\n'))
  } catch {
    return null
  }

  if (!isRecord(parsed)) {
    return null
  }

  const payload = parsed.payload
  if (payload && isRecord(payload) && payload.code === 'replay_gap') {
    return { kind: 'gap' }
  }

  const sequence = typeof parsed.sequence === 'number' ? parsed.sequence : Number(id)
  if (!Number.isSafeInteger(sequence) || sequence < 1) {
    return null
  }

  return {
    kind: 'event',
    event: {
      sequence,
      type: typeof parsed.type === 'string' ? parsed.type : type,
      occurredAtUtc: typeof parsed.occurredAtUtc === 'string' ? parsed.occurredAtUtc : new Date().toISOString(),
      sessionId: typeof parsed.sessionId === 'string' ? parsed.sessionId : null,
      runId: typeof parsed.runId === 'string' ? parsed.runId : null,
      eventId: typeof parsed.eventId === 'string' ? parsed.eventId : id || null,
      payload,
    },
  }
}

async function consumeStream(
  sessionId: string,
  lastSequence: number,
  signal: AbortSignal,
  onEvent: (event: SessionEvent) => void,
  onGap: () => void,
): Promise<void> {
  const headers = new Headers({ Accept: 'text/event-stream' })
  if (lastSequence > 0) {
    headers.set('Last-Event-ID', String(lastSequence))
  }

  const response = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/events`, { headers, signal })
  if (!response.ok) {
    throw new ApiProblemError(response.status, 'The session activity stream could not be opened.', null)
  }

  if (!response.body) {
    throw new Error('The session activity stream returned no body.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (!signal.aborted) {
    const chunk = await reader.read()
    if (chunk.done) {
      break
    }

    buffer += decoder.decode(chunk.value, { stream: true })
    let boundary = buffer.indexOf('\n\n')
    while (boundary >= 0) {
      const block = buffer.slice(0, boundary)
      buffer = buffer.slice(boundary + 2)
      const parsed = parseSseBlock(block)
      if (parsed?.kind === 'gap') {
        onGap()
      } else if (parsed?.event) {
        onEvent(parsed.event)
      }
      boundary = buffer.indexOf('\n\n')
    }
  }

  const trailing = parseSseBlock(buffer)
  if (trailing?.kind === 'gap') {
    onGap()
  } else if (trailing?.event) {
    onEvent(trailing.event)
  }
}

export interface SessionEventsState extends SessionEventState {
  connection: SessionConnection
  error: string | null
}

export function useSessionEvents(sessionId: string | undefined, onGap?: () => void): SessionEventsState {
  const [events, dispatch] = useReducer(sessionEventReducer, emptySessionEventState)
  const [connection, setConnection] = useState<SessionConnection>('connecting')
  const [error, setError] = useState<string | null>(null)
  const gapRef = useRef(onGap)
  const lastSequenceRef = useRef(0)

  useEffect(() => {
    gapRef.current = onGap
  }, [onGap])

  useEffect(() => {
    dispatch({ kind: 'reset' })
    lastSequenceRef.current = 0
  }, [sessionId])

  useEffect(() => {
    if (!sessionId) {
      return
    }

    const controller = new AbortController()
    let retryTimer: number | undefined
    let retry = 0

    const connect = async (): Promise<void> => {
      if (controller.signal.aborted) {
        return
      }

      setConnection(retry === 0 ? 'connecting' : 'reconnecting')
      try {
        await consumeStream(
          sessionId,
          lastSequenceRef.current,
          controller.signal,
          (event) => {
            lastSequenceRef.current = Math.max(lastSequenceRef.current, event.sequence)
            dispatch(event)
            retry = 0
            setConnection('live')
            setError(null)
          },
          () => gapRef.current?.(),
        )

        if (!controller.signal.aborted) {
          retry += 1
          setConnection('reconnecting')
          retryTimer = window.setTimeout(() => void connect(), Math.min(1000 * 2 ** Math.min(retry, 3), 8000))
        }
      } catch (cause: unknown) {
        if (controller.signal.aborted) {
          return
        }

        retry += 1
        setConnection('error')
        setError(cause instanceof Error ? cause.message : 'The session activity stream was interrupted.')
        retryTimer = window.setTimeout(() => void connect(), Math.min(1000 * 2 ** Math.min(retry, 3), 8000))
      }
    }

    void connect()
    return () => {
      controller.abort()
      if (retryTimer !== undefined) {
        window.clearTimeout(retryTimer)
      }
    }
  }, [sessionId])

  return {
    ...events,
    connection: sessionId ? connection : 'error',
    error: sessionId ? error : 'A session identifier is required.',
  }
}
