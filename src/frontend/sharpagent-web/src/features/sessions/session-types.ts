import type { RunSummary, Session, SessionStatus } from '@/shared/api/client'

export interface SessionEvent {
  sequence: number
  type: string
  occurredAtUtc: string
  sessionId: string | null
  runId: string | null
  eventId: string | null
  payload: unknown
}

export interface SessionEventState {
  events: SessionEvent[]
  lastSequence: number
  hasGap: boolean
}

export type SessionEventAction = SessionEvent | { kind: 'reset' }

export const emptySessionEventState: SessionEventState = {
  events: [],
  lastSequence: 0,
  hasGap: false,
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function sessionEventReducer(state: SessionEventState, action: SessionEventAction): SessionEventState {
  if ('kind' in action && action.kind === 'reset') {
    return emptySessionEventState
  }

  const event = action as SessionEvent
  if (event.sequence <= state.lastSequence || state.events.some((candidate) => candidate.sequence === event.sequence)) {
    return state
  }

  return {
    events: [...state.events, event].sort((left, right) => left.sequence - right.sequence),
    lastSequence: Math.max(state.lastSequence, event.sequence),
    hasGap: state.hasGap || event.sequence > state.lastSequence + 1,
  }
}

export function parsePayload(payload: unknown): Record<string, unknown> {
  if (isRecord(payload)) {
    return payload
  }

  if (typeof payload === 'string') {
    try {
      const parsed: unknown = JSON.parse(payload)
      return isRecord(parsed) ? parsed : {}
    } catch {
      return {}
    }
  }

  return {}
}

export function statusLabel(value: SessionStatus | RunSummary['status']): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/(^|\s)\S/g, (letter) => letter.toUpperCase())
}

export function sessionIsActive(session: Pick<Session, 'status' | 'activeRunId'>): boolean {
  return session.activeRunId !== null || ['planning', 'executing', 'awaitingApproval', 'reviewing'].includes(session.status)
}

export function eventSummary(event: SessionEvent): string {
  const payload = parsePayload(event.payload)
  const value = ['message', 'summary', 'text', 'detail', 'output'].map((key) => payload[key]).find(
    (candidate): candidate is string => typeof candidate === 'string' && candidate.trim().length > 0,
  )

  if (value) {
    return value
  }

  const labels: Record<string, string> = {
    session_created: 'Session created',
    run_started: 'Run started',
    run_completed: 'Run completed',
    run_failed: 'Run failed',
    run_cancelled: 'Run cancelled',
    tool_proposed: 'A tool action was proposed',
    tool_started: 'A controlled tool started',
    tool_completed: 'A controlled tool completed',
    approval_requested: 'Approval requested',
    approval_resolved: 'Approval decision recorded',
    context_compacted: 'Context was compacted safely',
    change_detected: 'Workspace changes detected',
    usage_updated: 'Usage updated',
  }

  return labels[event.type] ?? 'Informational activity received'
}
