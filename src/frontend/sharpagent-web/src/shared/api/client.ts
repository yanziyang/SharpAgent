/**
 * Typed browser-to-API boundary. The browser only ever sends provider-neutral
 * identifiers; credentials and raw provider shapes never cross this file.
 */

export type HealthStatusValue = 'healthy' | 'degraded' | 'unready'

export interface HealthCheck {
  name: string
  status: HealthStatusValue
  detail: string | null
}

export interface HealthSnapshot {
  overall: HealthStatusValue
  checks: HealthCheck[]
  generatedAtUtc: string
}

/** Maps a failed API call to stable, user-safe problem information. */
export class ApiProblemError extends Error {
  readonly status: number

  /** Stable problem code from the API when present; UI maps it to copy. */
  readonly code: string | null

  constructor(status: number, message: string, code: string | null, options?: { cause?: unknown }) {
    super(message, options)
    this.name = 'ApiProblemError'
    this.status = status
    this.code = code
  }
}

function readStringField(body: unknown, field: string): string | null {
  if (typeof body === 'object' && body !== null && field in body) {
    const value = (body as Record<string, unknown>)[field]
    if (typeof value === 'string' && value.trim().length > 0) {
      return value
    }
  }

  return null
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      headers: { Accept: 'application/json', ...init?.headers },
    })
  } catch (cause) {
    throw new ApiProblemError(0, 'The SharpAgent service is unreachable.', 'network_error', { cause })
  }

  if (!response.ok) {
    let problem: unknown = null
    try {
      problem = await response.json()
    } catch {
      problem = null
    }

    const message =
      readStringField(problem, 'detail') ?? readStringField(problem, 'title') ?? `Request failed (${response.status}).`

    throw new ApiProblemError(response.status, message, readStringField(problem, 'code'))
  }

  return (await response.json()) as T
}

export function fetchHealthSnapshot(signal?: AbortSignal): Promise<HealthSnapshot> {
  return apiFetch<HealthSnapshot>('/api/health', { signal })
}
