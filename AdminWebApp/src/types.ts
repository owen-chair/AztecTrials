export type RequestLog = {
  id: number
  timeutc: string
  durationms: number
  endpoint: string
  method: string
  path: string
  remoteaddr: string
  clientip: string
  geocountry: string
  geocity: string
  useragent: string
  headers?: Record<string, string[]>
  payloadraw: string
  payloadunescaped: string
  payloadstripped: string
  payloadjson: string
  status: number
  error: string
}

export type LogQueryResult = {
  total: number
  page: number
  pagesize: number
  logs: RequestLog[]
}

export type LogStatsPoint = {
  timeutc: string
  count: number
}

export type LogStatsResult = {
  bucket: string
  startutc: string
  endutc: string
  points: LogStatsPoint[]
}

export type DBMetricsPoint = {
  timeutc: string
  value: number
}

export type DBMetricsStatsResult = {
  bucket: string
  startutc: string
  endutc: string
  points: DBMetricsPoint[]
}

export type PlayerData = {
  playername: string
  completionseconds: number
  addedtime: string

  // Private/admin-only metadata
  clientip: string
  geocountry: string
  geocity: string
}

export type PlayerQueryResult = {
  total: number
  page: number
  pagesize: number
  players: PlayerData[]
}

export type RateLimitLog = {
  id: number
  timeutc: string
  endpoint: string
  method: string
  path: string
  remoteaddr: string
  clientip: string
  useragent: string
  event: string
  limitpersecond: number
  countthissec: number
  windowstartutc: string
  banneduntilutc: string
}

export type RateLimitQueryResult = {
  total: number
  page: number
  pagesize: number
  logs: RateLimitLog[]
}

export type ManualIPBan = {
  id: number
  createdutc: string
  ip: string
  banneduntilutc: string
  reason: string
}

export type ManualIPBanQueryResult = {
  total: number
  page: number
  pagesize: number
  bans: ManualIPBan[]
}

export type CheckpointUnlock = {
  id: number
  timeutc: string
  clientip: string
  geocountry: string
  geocity: string
  checkpoint: string
}

export type CheckpointUnlockQueryResult = {
  total: number
  page: number
  pagesize: number
  unlocks: CheckpointUnlock[]
}
