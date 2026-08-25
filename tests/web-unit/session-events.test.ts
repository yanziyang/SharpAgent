import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { parseSseBlock, useSessionEvents } from '@/features/sessions/use-session-events'

describe('session activity stream', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('parses comments, event metadata, JSON payloads, and replay gaps', () => {
    expect(parseSseBlock(': heartbeat\n\n')).toBeNull()
    expect(parseSseBlock('id: 4\nevent: run_started\ndata: {"sequence":4,"payload":{"message":"started"}}\n')).toEqual({
      kind: 'event',
      event: expect.objectContaining({ sequence: 4, type: 'run_started', eventId: '4' }),
    })
    expect(parseSseBlock('id: 5\ndata: {"sequence":5,\ndata: "ignored"}\n')).toBeNull()
    expect(parseSseBlock('data: {"payload":{"code":"replay_gap"}}\n')).toEqual({ kind: 'gap' })
    expect(parseSseBlock('data: [1]\n')).toBeNull()
    expect(parseSseBlock('data: null\n')).toBeNull()
    expect(parseSseBlock('event:\ndata: {"payload":{},"sequence":6}\n')).toEqual({
      kind: 'event',
      event: expect.objectContaining({ sequence: 6, type: 'status', sessionId: null, runId: null }),
    })
    expect(parseSseBlock('id: 0\ndata: {"payload":{}}\n')).toBeNull()
    expect(parseSseBlock('data: {not-json}\n')).toBeNull()
    expect(parseSseBlock('event: status\n')).toBeNull()
  })

  it('consumes events, reports gaps, and keeps the latest sequence for replay', async () => {
    let controller: ReadableStreamDefaultController<Uint8Array> | undefined
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(new ReadableStream({
      start(streamController) {
        controller = streamController
      },
    }), { headers: { 'Content-Type': 'text/event-stream' } })))
    vi.stubGlobal('fetch', fetchMock)
    const onGap = vi.fn()

    const { result, unmount } = renderHook(() => useSessionEvents('ses_stream', onGap))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_stream/events', expect.anything()))

    act(() => {
      controller?.enqueue(new TextEncoder().encode('id: 2\nevent: tool_completed\ndata: {"sequence":2,"payload":{"summary":"done"}}\n\n'))
      controller?.enqueue(new TextEncoder().encode('data: {"payload":{"code":"replay_gap"}}\n\n'))
    })

    await waitFor(() => expect(result.current.events).toHaveLength(1))
    expect(result.current.events[0]?.type).toBe('tool_completed')
    expect(result.current.lastSequence).toBe(2)
    expect(result.current.connection).toBe('live')
    expect(onGap).toHaveBeenCalledOnce()
    const headers = (fetchMock.mock.calls[0]?.[1] as RequestInit).headers as Headers
    expect(headers.get('Accept')).toBe('text/event-stream')

    unmount()
  })

  it('exposes safe error state for missing identifiers and failed streams', async () => {
    const missing = renderHook(() => useSessionEvents(undefined))
    expect(missing.result.current.connection).toBe('error')
    expect(missing.result.current.error).toMatch(/identifier is required/i)
    missing.unmount()

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('stream unavailable')))
    const failed = renderHook(() => useSessionEvents('ses_failed'))
    await waitFor(() => expect(failed.result.current.connection).toBe('error'))
    expect(failed.result.current.error).toBe('stream unavailable')
    failed.unmount()

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))
    const noBody = renderHook(() => useSessionEvents('ses_no_body'))
    await waitFor(() => expect(noBody.result.current.error).toMatch(/no body/i))
    noBody.unmount()

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('forbidden', { status: 403 })))
    const rejected = renderHook(() => useSessionEvents('ses_rejected'))
    await waitFor(() => expect(rejected.result.current.error).toMatch(/could not be opened/i))
    rejected.unmount()
  })
})
