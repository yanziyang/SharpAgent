import { describe, expect, it } from 'vitest'
import {
  emptySessionEventState,
  eventSummary,
  parsePayload,
  sessionEventReducer,
  sessionIsActive,
  statusLabel,
  type SessionEvent,
} from '@/features/sessions/session-types'

function event(sequence: number, type = 'status', payload: unknown = {}): SessionEvent {
  return {
    sequence,
    type,
    occurredAtUtc: '2026-08-25T00:00:00Z',
    sessionId: 'ses_test',
    runId: 'run_test',
    eventId: `evt_${sequence}`,
    payload,
  }
}

describe('session event projections', () => {
  it('keeps events ordered, detects gaps, and ignores duplicate or stale events', () => {
    const first = sessionEventReducer(emptySessionEventState, event(2))
    expect(first.events.map((item) => item.sequence)).toEqual([2])
    expect(first.lastSequence).toBe(2)
    expect(first.hasGap).toBe(true)

    expect(sessionEventReducer(first, event(1))).toBe(first)
    expect(sessionEventReducer(first, event(2))).toBe(first)

    const continued = sessionEventReducer(first, event(3, 'run_started'))
    expect(continued.events.map((item) => item.sequence)).toEqual([2, 3])
    expect(continued.hasGap).toBe(true)
    expect(sessionEventReducer(continued, { kind: 'reset' })).toEqual(emptySessionEventState)
  })

  it('parses object, JSON-string, invalid, and primitive payloads safely', () => {
    const objectPayload = { message: 'hello' }
    expect(parsePayload(objectPayload)).toBe(objectPayload)
    expect(parsePayload('{"summary":"done"}')).toEqual({ summary: 'done' })
    expect(parsePayload('{not-json}')).toEqual({})
    expect(parsePayload('[]')).toEqual([])
    expect(parsePayload(42)).toEqual({})
    expect(statusLabel('awaitingApproval')).toBe('Awaiting Approval')
    expect(statusLabel('completed')).toBe('Completed')
  })

  it('recognizes active session states and provides safe event summaries', () => {
    expect(sessionIsActive({ status: 'draft', activeRunId: 'run_1' })).toBe(true)
    expect(sessionIsActive({ status: 'executing', activeRunId: null })).toBe(true)
    expect(sessionIsActive({ status: 'completed', activeRunId: null })).toBe(false)

    expect(eventSummary(event(1, 'status', { message: 'Primary message' }))).toBe('Primary message')
    expect(eventSummary(event(2, 'status', { summary: 'Summary' }))).toBe('Summary')
    expect(eventSummary(event(3, 'status', { text: 'Text' }))).toBe('Text')
    expect(eventSummary(event(4, 'status', { detail: 'Detail' }))).toBe('Detail')
    expect(eventSummary(event(5, 'status', { output: 'Output' }))).toBe('Output')
    expect(eventSummary(event(6, 'run_started'))).toBe('Run started')
    expect(eventSummary(event(7, 'unknown_event'))).toBe('Informational activity received')
  })
})
