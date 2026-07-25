import type { AppConfig } from './storage'
import type {
  LogQueryResult,
  LogStatsResult,
  DBMetricsStatsResult,
  ManualIPBan,
  ManualIPBanQueryResult,
  CheckpointUnlock,
  CheckpointUnlockQueryResult,
  PlayerData,
  PlayerQueryResult,
  RateLimitQueryResult,
  RequestLog,
} from './types'

async function adminFetch<T>(cfg: AppConfig, path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (cfg.adminKey.trim() !== '') {
    headers.set('X-Admin-Key', cfg.adminKey.trim())
  }

  const res = await fetch(path, {
    ...init,
    headers,
  })

  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(`${res.status} ${res.statusText}${text ? `: ${text}` : ''}`)
  }

  return (await res.json()) as T
}

export async function queryLogs(
  cfg: AppConfig,
  params: {
    endpoint?: string
    q?: string
    status?: number
    errorOnly?: boolean
    start?: string
    end?: string
    page?: number
    pageSize?: number
  },
): Promise<LogQueryResult> {
  const usp = new URLSearchParams()
  if (params.endpoint) usp.set('endpoint', params.endpoint)
  if (params.q) usp.set('q', params.q)
  if (typeof params.status === 'number') usp.set('status', String(params.status))
  if (params.errorOnly) usp.set('errorOnly', 'true')
  if (params.start) usp.set('start', params.start)
  if (params.end) usp.set('end', params.end)
  if (typeof params.page === 'number') usp.set('page', String(params.page))
  if (typeof params.pageSize === 'number') usp.set('pageSize', String(params.pageSize))
  return adminFetch<LogQueryResult>(cfg, `/admin/logs?${usp.toString()}`)
}

export async function getLogById(cfg: AppConfig, id: number): Promise<RequestLog> {
  return adminFetch<RequestLog>(cfg, `/admin/logs/${id}`)
}

export async function clearLogs(cfg: AppConfig): Promise<{ deleted: number }> {
  return adminFetch<{ deleted: number }>(cfg, `/admin/logs/clear`, { method: 'POST' })
}

export async function queryLogStats(
  cfg: AppConfig,
  params: {
    bucket: 'sec' | 'min' | 'hour' | 'day'
    start: string
    end: string
    endpoint?: string
    errorOnly?: boolean
  },
): Promise<LogStatsResult> {
  const usp = new URLSearchParams()
  usp.set('bucket', params.bucket)
  usp.set('start', params.start)
  usp.set('end', params.end)
  if (params.endpoint) usp.set('endpoint', params.endpoint)
  if (params.errorOnly) usp.set('errorOnly', 'true')
  return adminFetch<LogStatsResult>(cfg, `/admin/logs/stats?${usp.toString()}`)
}

export async function queryDBMetricsStats(
  cfg: AppConfig,
  params: {
    bucket: 'sec' | 'min' | '30min' | 'hour' | 'day'
    start: string
    end: string
    metric: 'db_bytes' | 'rows' | 'free_bytes'
  },
): Promise<DBMetricsStatsResult> {
  const usp = new URLSearchParams()
  usp.set('bucket', params.bucket)
  usp.set('start', params.start)
  usp.set('end', params.end)
  usp.set('metric', params.metric)
  return adminFetch<DBMetricsStatsResult>(cfg, `/admin/dbmetrics/stats?${usp.toString()}`)
}

export async function queryPlayers(
  cfg: AppConfig,
  params: { q?: string; ip?: string; order?: string; page?: number; pageSize?: number },
): Promise<PlayerQueryResult> {
  const usp = new URLSearchParams()
  if (params.q) usp.set('q', params.q)
  if (params.ip) usp.set('ip', params.ip)
  if (params.order) usp.set('order', params.order)
  if (typeof params.page === 'number') usp.set('page', String(params.page))
  if (typeof params.pageSize === 'number') usp.set('pageSize', String(params.pageSize))
  return adminFetch<PlayerQueryResult>(cfg, `/admin/players?${usp.toString()}`)
}

export async function addPlayerTime(
  cfg: AppConfig,
  next: { playername: string; completionseconds: number },
): Promise<PlayerData> {
  return adminFetch<PlayerData>(cfg, `/admin/players`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(next),
  })
}

export async function getPlayerByName(cfg: AppConfig, name: string): Promise<PlayerData> {
  const enc = encodeURIComponent(name)
  return adminFetch<PlayerData>(cfg, `/admin/players/${enc}`)
}

export async function deletePlayer(cfg: AppConfig, name: string): Promise<{ deleted: boolean }> {
  const enc = encodeURIComponent(name)
  return adminFetch<{ deleted: boolean }>(cfg, `/admin/players/${enc}`, { method: 'DELETE' })
}

export async function updatePlayer(
  cfg: AppConfig,
  oldName: string,
  next: { playername: string; completionseconds: number },
): Promise<PlayerData> {
  const enc = encodeURIComponent(oldName)
  return adminFetch<PlayerData>(cfg, `/admin/players/${enc}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(next),
  })
}

export async function clearPlayers(cfg: AppConfig): Promise<{ deleted: number }> {
  return adminFetch<{ deleted: number }>(cfg, `/admin/players/clear`, { method: 'POST' })
}

export async function queryRateLimits(
  cfg: AppConfig,
  params: {
    ip?: string
    endpoint?: string
    event?: string
    q?: string
    page?: number
    pageSize?: number
  },
): Promise<RateLimitQueryResult> {
  const usp = new URLSearchParams()
  if (params.ip) usp.set('ip', params.ip)
  if (params.endpoint) usp.set('endpoint', params.endpoint)
  if (params.event) usp.set('event', params.event)
  if (params.q) usp.set('q', params.q)
  if (typeof params.page === 'number') usp.set('page', String(params.page))
  if (typeof params.pageSize === 'number') usp.set('pageSize', String(params.pageSize))
  return adminFetch<RateLimitQueryResult>(cfg, `/admin/ratelimits?${usp.toString()}`)
}

export async function clearRateLimits(cfg: AppConfig): Promise<{ deleted: number }> {
  return adminFetch<{ deleted: number }>(cfg, `/admin/ratelimits/clear`, { method: 'POST' })
}

export async function banClientIP(
  cfg: AppConfig,
  req: { ip: string },
): Promise<{ ip: string; banneduntilutc: string }> {
  return adminFetch<{ ip: string; banneduntilutc: string }>(cfg, `/admin/ratelimits/ban`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
}

export async function queryManualBans(
  cfg: AppConfig,
  params: { q?: string; activeOnly?: boolean; page?: number; pageSize?: number },
): Promise<ManualIPBanQueryResult> {
  const usp = new URLSearchParams()
  if (params.q) usp.set('q', params.q)
  if (params.activeOnly) usp.set('activeOnly', 'true')
  if (typeof params.page === 'number') usp.set('page', String(params.page))
  if (typeof params.pageSize === 'number') usp.set('pageSize', String(params.pageSize))
  return adminFetch<ManualIPBanQueryResult>(cfg, `/admin/manualbans?${usp.toString()}`)
}

export async function upsertManualBan(
  cfg: AppConfig,
  req: { ip: string; minutes: number; reason: string },
): Promise<ManualIPBan> {
  return adminFetch<ManualIPBan>(cfg, `/admin/manualbans`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
}

export async function deleteManualBan(cfg: AppConfig, ip: string): Promise<{ deleted: boolean }> {
  const enc = encodeURIComponent(ip)
  return adminFetch<{ deleted: boolean }>(cfg, `/admin/manualbans/${enc}`, { method: 'DELETE' })
}

export async function queryCheckpointUnlocks(
  cfg: AppConfig,
  params: {
    ip?: string
    checkpoint?: string
    q?: string
    start?: string
    end?: string
    page?: number
    pageSize?: number
  },
): Promise<CheckpointUnlockQueryResult> {
  const usp = new URLSearchParams()
  if (params.ip) usp.set('ip', params.ip)
  if (params.checkpoint) usp.set('checkpoint', params.checkpoint)
  if (params.q) usp.set('q', params.q)
  if (params.start) usp.set('start', params.start)
  if (params.end) usp.set('end', params.end)
  if (typeof params.page === 'number') usp.set('page', String(params.page))
  if (typeof params.pageSize === 'number') usp.set('pageSize', String(params.pageSize))
  return adminFetch<CheckpointUnlockQueryResult>(cfg, `/admin/checkpointunlocks?${usp.toString()}`)
}

export async function getCheckpointUnlockById(cfg: AppConfig, id: number): Promise<CheckpointUnlock> {
  return adminFetch<CheckpointUnlock>(cfg, `/admin/checkpointunlocks/${id}`)
}

export async function createCheckpointUnlock(
  cfg: AppConfig,
  next: { clientip: string; checkpoint: string; timeutc?: string },
): Promise<CheckpointUnlock> {
  return adminFetch<CheckpointUnlock>(cfg, `/admin/checkpointunlocks`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(next),
  })
}

export async function updateCheckpointUnlock(
  cfg: AppConfig,
  id: number,
  next: { clientip: string; checkpoint: string; timeutc: string },
): Promise<CheckpointUnlock> {
  return adminFetch<CheckpointUnlock>(cfg, `/admin/checkpointunlocks/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(next),
  })
}

export async function deleteCheckpointUnlock(cfg: AppConfig, id: number): Promise<{ deleted: boolean }> {
  return adminFetch<{ deleted: boolean }>(cfg, `/admin/checkpointunlocks/${id}`, { method: 'DELETE' })
}

export async function clearCheckpointUnlocks(cfg: AppConfig): Promise<{ deleted: number }> {
  return adminFetch<{ deleted: number }>(cfg, `/admin/checkpointunlocks/clear`, { method: 'POST' })
}
