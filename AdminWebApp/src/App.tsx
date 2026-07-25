import { useEffect, useMemo, useRef, useState } from 'react'
import type {
  LogQueryResult,
  LogStatsResult,
  DBMetricsStatsResult,
  ManualIPBanQueryResult,
  CheckpointUnlock,
  CheckpointUnlockQueryResult,
  PlayerData,
  PlayerQueryResult,
  RateLimitLog,
  RateLimitQueryResult,
  RequestLog,
} from './types'
import {
  clearLogs,
  clearPlayers,
  clearRateLimits,
  clearCheckpointUnlocks,
  addPlayerTime,
  banClientIP,
  createCheckpointUnlock,
  deleteManualBan,
  deleteCheckpointUnlock,
  deletePlayer,
  queryCheckpointUnlocks,
  queryManualBans,
  upsertManualBan,
  updatePlayer,
  updateCheckpointUnlock,
  getLogById,
  getPlayerByName,
  queryLogStats,
  queryDBMetricsStats,
  queryLogs,
  queryPlayers,
  queryRateLimits,
} from './api'
import { loadConfig, saveConfig, type AppConfig } from './storage'
import { ConfirmModal } from './components/ConfirmModal'
import { LogsView } from './views/LogsView'
import { GraphsView } from './views/GraphsView'
import { ManualBansView } from './views/ManualBansView'
import { CheckpointUnlocksView } from './views/CheckpointUnlocksView'
import { PlayersView } from './views/PlayersView'
import { RateLimitsView } from './views/RateLimitsView'
import { SettingsView } from './views/SettingsView'
import { PUBLIC_ENDPOINTS } from './endpoints'

type View = 'logs' | 'players' | 'ratelimits' | 'checkpointunlocks' | 'manualbans' | 'graphs' | 'settings'

type GraphsTab = 'network' | 'database'
type GraphsRangeMode = 'period' | 'range'

type NetworkBucket = 'sec' | 'min' | 'hour' | 'day'
type DatabaseBucket = '30min' | 'hour' | 'day'

type ConfirmState =
  | null
  | {
      title: string
      message: string
      confirmText: string
      onConfirm: () => Promise<void>
    }

function App() {
  const [view, setView] = useState<View>('logs')
  const [config, setConfig] = useState<AppConfig>(() => loadConfig())
  const api = useMemo(() => config, [config])

  const [confirm, setConfirm] = useState<ConfirmState>(null)

  // Logs state
  const [logsQ, setLogsQ] = useState('')
  const [logsEndpoints, setLogsEndpoints] = useState<string[]>(() => [...PUBLIC_ENDPOINTS])
  const [logsStatus, setLogsStatus] = useState('')
  const [logsErrorOnly, setLogsErrorOnly] = useState(false)
  const [logsDateTimeFilterEnabled, setLogsDateTimeFilterEnabled] = useState(false)
  const [logsRangeStartDate, setLogsRangeStartDate] = useState('')
  const [logsRangeStartTime, setLogsRangeStartTime] = useState('')
  const [logsRangeEndDate, setLogsRangeEndDate] = useState('')
  const [logsRangeEndTime, setLogsRangeEndTime] = useState('')
  const [logsPage, setLogsPage] = useState(0)
  const [logsPageSize, setLogsPageSize] = useState(100)
  const [logsResult, setLogsResult] = useState<LogQueryResult | null>(null)
  const [logsSelectedId, setLogsSelectedId] = useState<number | null>(null)
  const [logsExpandedId, setLogsExpandedId] = useState<number | null>(null)

  // Players state
  const [playersQ, setPlayersQ] = useState('')
  const [playersIP, setPlayersIP] = useState('')
  const [playersOrder, setPlayersOrder] = useState('added_time_desc')
  const [playersPage, setPlayersPage] = useState(0)
  const [playersPageSize, setPlayersPageSize] = useState(100)
  const [playersResult, setPlayersResult] = useState<PlayerQueryResult | null>(null)
  const [playerLookupName, setPlayerLookupName] = useState('')
  const [playerLookupResult, setPlayerLookupResult] = useState<PlayerData | null>(null)

  // Rate limit logs state
  const [rlQ, setRlQ] = useState('')
  const [rlIP, setRlIP] = useState('')
  const [rlEndpoint, setRlEndpoint] = useState('')
  const [rlEvent, setRlEvent] = useState('')
  const [rlPage, setRlPage] = useState(0)
  const [rlPageSize, setRlPageSize] = useState(100)
  const [rlResult, setRlResult] = useState<RateLimitQueryResult | null>(null)
  const [rlExpandedId, setRlExpandedId] = useState<number | null>(null)

  // Manual IP bans state
  const [mbQ, setMbQ] = useState('')
  const [mbActiveOnly, setMbActiveOnly] = useState(true)
  const [mbPage, setMbPage] = useState(0)
  const [mbPageSize, setMbPageSize] = useState(100)
  const [mbResult, setMbResult] = useState<ManualIPBanQueryResult | null>(null)

  // Checkpoint unlocks state
  const [cuQ, setCuQ] = useState('')
  const [cuIP, setCuIP] = useState('')
  const [cuCheckpoint, setCuCheckpoint] = useState('')
  const [cuPage, setCuPage] = useState(0)
  const [cuPageSize, setCuPageSize] = useState(100)
  const [cuResult, setCuResult] = useState<CheckpointUnlockQueryResult | null>(null)

  // Graphs state
  const [graphsTab, setGraphsTab] = useState<GraphsTab>('network')
  const [graphsRangeMode, setGraphsRangeMode] = useState<GraphsRangeMode>('period')
  const [graphsNetworkBucket, setGraphsNetworkBucket] = useState<NetworkBucket>('min')
  const [graphsDatabaseBucket, setGraphsDatabaseBucket] = useState<DatabaseBucket>('hour')
  const [graphsPeriodPreset, setGraphsPeriodPreset] = useState<'' | '15m' | '1h' | '6h' | '24h' | '7d'>('1h')
  const [graphsRangeStartDate, setGraphsRangeStartDate] = useState('')
  const [graphsRangeEndDate, setGraphsRangeEndDate] = useState('')
  const [graphsEndpoints, setGraphsEndpoints] = useState<string[]>(() => [...PUBLIC_ENDPOINTS])
  const [graphsErrorOnly, setGraphsErrorOnly] = useState(false)
  const [graphsSplitSuccessErrors, setGraphsSplitSuccessErrors] = useState(false)
  const [graphsResult, setGraphsResult] = useState<LogStatsResult | null>(null)
  const [graphsErrorsResult, setGraphsErrorsResult] = useState<LogStatsResult | null>(null)
  const [graphsDBSizeResult, setGraphsDBSizeResult] = useState<DBMetricsStatsResult | null>(null)
  const [graphsDBRowsResult, setGraphsDBRowsResult] = useState<DBMetricsStatsResult | null>(null)
  const [graphsDiskFreeResult, setGraphsDiskFreeResult] = useState<DBMetricsStatsResult | null>(null)

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const logsReqSeq = useRef(0)
  const playersReqSeq = useRef(0)
  const rlReqSeq = useRef(0)
  const mbReqSeq = useRef(0)
  const cuReqSeq = useRef(0)
  const graphsReqSeq = useRef(0)

  useEffect(() => {
    saveConfig(config)
  }, [config])

  // Auto refresh on load and whenever switching tabs.
  useEffect(() => {
    if (busy) return

    if (view === 'logs') void refreshLogs()
    else if (view === 'players') void refreshPlayers()
    else if (view === 'ratelimits') void refreshRateLimits()
    else if (view === 'checkpointunlocks') void refreshCheckpointUnlocks()
    else if (view === 'manualbans') void refreshManualBans()
    else if (view === 'graphs') void refreshGraphs(graphsTab)
    else {
      // Settings view doesn't require fetching.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view])

  // When switching Graphs tabs, only fetch the active tab's data.
  useEffect(() => {
    if (busy) return
    if (view !== 'graphs') return
    void refreshGraphs(graphsTab)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [graphsTab])

  function setGraphsRangeModeAndClear(next: GraphsRangeMode) {
    setGraphsRangeMode(next)
    setGraphsPeriodPreset('')
    setGraphsRangeStartDate('')
    setGraphsRangeEndDate('')
  }

  function computeGraphsRange() {
    const startDate = graphsRangeStartDate.trim()
    const endDate = graphsRangeEndDate.trim()

    // If either side is provided, use an explicit full-day date range.
    // (We keep it date-only so: start=00:00:00, end=23:59:59.999)
    if (graphsRangeMode === 'range') {
      if (startDate === '' && endDate === '') {
        throw new Error('Pick a start/end date')
      }
      const s = startDate !== '' ? startDate : endDate
      const e = endDate !== '' ? endDate : startDate

      const start = new Date(`${s}T00:00:00`)
      const end = new Date(`${e}T23:59:59.999`)
      if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime())) {
        throw new Error('Invalid date range')
      }
      if (start.getTime() > end.getTime()) {
        throw new Error('Invalid date range (start is after end)')
      }
      return { start: start.toISOString(), end: end.toISOString() }
    }

    // Otherwise fall back to the preset range.
    if (graphsPeriodPreset === '') {
      throw new Error('Pick a period')
    }
    const end = new Date()
    const ms =
      graphsPeriodPreset === '15m'
        ? 15 * 60 * 1000
        : graphsPeriodPreset === '1h'
          ? 60 * 60 * 1000
          : graphsPeriodPreset === '6h'
            ? 6 * 60 * 60 * 1000
            : graphsPeriodPreset === '24h'
              ? 24 * 60 * 60 * 1000
              : 7 * 24 * 60 * 60 * 1000
    const start = new Date(end.getTime() - ms)
    return { start: start.toISOString(), end: end.toISOString() }
  }

  async function refreshGraphs(tab?: GraphsTab) {
    setError(null)
    setBusy(true)
    const reqSeq = ++graphsReqSeq.current
    try {
      const activeTab = tab ?? graphsTab
      const { start, end } = computeGraphsRange()

      if (activeTab === 'database') {
        const [dbBytesRes, dbRowsRes, freeBytesRes] = await Promise.all([
          queryDBMetricsStats(api, { bucket: graphsDatabaseBucket, start, end, metric: 'db_bytes' }),
          queryDBMetricsStats(api, { bucket: graphsDatabaseBucket, start, end, metric: 'rows' }),
          queryDBMetricsStats(api, { bucket: graphsDatabaseBucket, start, end, metric: 'free_bytes' }),
        ])

        if (reqSeq === graphsReqSeq.current) {
          setGraphsDBSizeResult(dbBytesRes)
          setGraphsDBRowsResult(dbRowsRes)
          setGraphsDiskFreeResult(freeBytesRes)
        }
      } else {
        const endpointFilter = graphsEndpoints.length > 0 ? graphsEndpoints.join(',') : undefined

        if (graphsSplitSuccessErrors) {
          const [allRes, errRes] = await Promise.all([
            queryLogStats(api, {
              bucket: graphsNetworkBucket,
              start,
              end,
              endpoint: endpointFilter,
            }),
            queryLogStats(api, {
              bucket: graphsNetworkBucket,
              start,
              end,
              endpoint: endpointFilter,
              errorOnly: true,
            }),
          ])
          if (reqSeq === graphsReqSeq.current) {
            setGraphsResult(allRes)
            setGraphsErrorsResult(errRes)
          }
        } else {
          const res = await queryLogStats(api, {
            bucket: graphsNetworkBucket,
            start,
            end,
            endpoint: endpointFilter,
            errorOnly: graphsErrorOnly || undefined,
          })
          if (reqSeq === graphsReqSeq.current) {
            setGraphsResult(res)
            setGraphsErrorsResult(null)
          }
        }
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === graphsReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function refreshLogs(opts?: { page?: number; pageSize?: number }) {
    setError(null)
    setBusy(true)
    const reqSeq = ++logsReqSeq.current
    try {
      const page = opts?.page ?? logsPage
      const pageSize = opts?.pageSize ?? logsPageSize
      const endpointFilter = logsEndpoints.length > 0 ? logsEndpoints.join(',') : undefined
      const statusNum = logsStatus.trim() === '' ? undefined : Number(logsStatus)

	  let startISO: string | undefined
	  let endISO: string | undefined
      if (logsDateTimeFilterEnabled) {
        const startDateRaw = logsRangeStartDate.trim()
        const endDateRaw = logsRangeEndDate.trim()
        const startTimeRaw = logsRangeStartTime.trim()
        const endTimeRaw = logsRangeEndTime.trim()

        if (startDateRaw === '' || endDateRaw === '') {
          setError('Select both a start date and end date (or disable date/time filter).')
          return
        }

        // If the user doesn't pick a time, interpret Start as start-of-day and End as end-of-day.
        const startLocal = new Date(`${startDateRaw}T${startTimeRaw !== '' ? startTimeRaw : '00:00:00'}`)
        const endLocal = new Date(`${endDateRaw}T${endTimeRaw !== '' ? endTimeRaw : '23:59:59'}`)

        if (!Number.isFinite(startLocal.getTime()) || !Number.isFinite(endLocal.getTime())) {
          setError('Invalid start/end date/time.')
          return
        }
        if (startLocal.getTime() > endLocal.getTime()) {
          setError('Start must be before End.')
          return
        }

        startISO = startLocal.toISOString()
        endISO = endLocal.toISOString()
      }

      const res = await queryLogs(api, {
        endpoint: endpointFilter,
        q: logsQ.trim() || undefined,
        errorOnly: logsErrorOnly || undefined,
        status: Number.isFinite(statusNum) ? statusNum : undefined,
        start: startISO,
        end: endISO,
        page,
        pageSize,
      })
      if (reqSeq === logsReqSeq.current) {
        setLogsResult(res)
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === logsReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function doClearLogs() {
    setError(null)
    setBusy(true)
    try {
      await clearLogs(api)
      await refreshLogs()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doFetchLogById() {
    if (!logsSelectedId || logsSelectedId <= 0) return
    setError(null)
    setBusy(true)
    try {
      const res = await getLogById(api, logsSelectedId)
      setLogsExpandedId(res.id)
      setLogsResult((prev) => {
        if (!prev) {
          return { total: 1, page: 0, pagesize: 1, logs: [res] }
        }
        const existingIdx = prev.logs.findIndex((l) => l.id === res.id)
        const nextLogs = existingIdx >= 0 ? prev.logs : [res, ...prev.logs]
        return { ...prev, logs: nextLogs }
      })
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doBanClientIP(ip: string) {
    const v = ip.trim()
    if (v === '') return
    setError(null)
    setBusy(true)
    try {
      await banClientIP(api, { ip: v })
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function refreshPlayers(opts?: { page?: number; pageSize?: number }) {
    setError(null)
    setBusy(true)
    const reqSeq = ++playersReqSeq.current
    try {
      const page = opts?.page ?? playersPage
      const pageSize = opts?.pageSize ?? playersPageSize
      const res = await queryPlayers(api, {
        q: playersQ.trim() || undefined,
        ip: playersIP.trim() || undefined,
        order: playersOrder,
        page,
        pageSize,
      })
      if (reqSeq === playersReqSeq.current) {
        setPlayersResult(res)
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === playersReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function doClearPlayers() {
    setError(null)
    setBusy(true)
    try {
      await clearPlayers(api)
      setPlayerLookupResult(null)
      await refreshPlayers()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doLookupPlayer() {
    setError(null)
    setBusy(true)
    try {
      const name = playerLookupName.trim()
      if (!name) {
        setPlayerLookupResult(null)
        return
      }
      const res = await getPlayerByName(api, name)
      setPlayerLookupResult(res)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doDeletePlayer(name: string) {
    setError(null)
    setBusy(true)
    try {
      await deletePlayer(api, name)
      if (playerLookupResult?.playername === name) {
        setPlayerLookupResult(null)
      }
      await refreshPlayers()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doUpdatePlayer(oldName: string, nextName: string, nextSeconds: number) {
    setError(null)
    setBusy(true)
    try {
      const updated = await updatePlayer(api, oldName, {
        playername: nextName,
        completionseconds: nextSeconds,
      })
      if (playerLookupResult?.playername === oldName) {
        setPlayerLookupResult(updated)
      }
      if (playerLookupName.trim() === oldName) {
        setPlayerLookupName(updated.playername)
      }
      await refreshPlayers()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doAddPlayerTime(name: string, seconds: number) {
    setError(null)
    setBusy(true)
    try {
      const created = await addPlayerTime(api, { playername: name, completionseconds: seconds })
      setPlayerLookupName(created.playername)
      setPlayerLookupResult(created)
      await refreshPlayers()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function refreshRateLimits(opts?: { page?: number; pageSize?: number }) {
    setError(null)
    setBusy(true)
    const reqSeq = ++rlReqSeq.current
    try {
      const page = opts?.page ?? rlPage
      const pageSize = opts?.pageSize ?? rlPageSize
      const res = await queryRateLimits(api, {
        q: rlQ.trim() || undefined,
        ip: rlIP.trim() || undefined,
        endpoint: rlEndpoint.trim() || undefined,
        event: rlEvent.trim() || undefined,
        page,
        pageSize,
      })
      if (reqSeq === rlReqSeq.current) {
        setRlResult(res)
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === rlReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function refreshManualBans(opts?: { page?: number; pageSize?: number }) {
    setError(null)
    setBusy(true)
    const reqSeq = ++mbReqSeq.current
    try {
      const page = opts?.page ?? mbPage
      const pageSize = opts?.pageSize ?? mbPageSize
      const res = await queryManualBans(api, {
        q: mbQ.trim() || undefined,
        activeOnly: mbActiveOnly || undefined,
        page,
        pageSize,
      })
      if (reqSeq === mbReqSeq.current) {
        setMbResult(res)
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === mbReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function refreshCheckpointUnlocks(opts?: { page?: number; pageSize?: number }) {
    setError(null)
    setBusy(true)
    const reqSeq = ++cuReqSeq.current
    try {
      const page = opts?.page ?? cuPage
      const pageSize = opts?.pageSize ?? cuPageSize
      const res = await queryCheckpointUnlocks(api, {
        q: cuQ.trim() || undefined,
        ip: cuIP.trim() || undefined,
        checkpoint: cuCheckpoint.trim() || undefined,
        page,
        pageSize,
      })
      if (reqSeq === cuReqSeq.current) {
        setCuResult(res)
      }
    } catch (e) {
      setError(String(e))
    } finally {
      if (reqSeq === cuReqSeq.current) {
        setBusy(false)
      }
    }
  }

  async function doClearCheckpointUnlocks() {
    setError(null)
    setBusy(true)
    try {
      await clearCheckpointUnlocks(api)
      await refreshCheckpointUnlocks()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doCreateCheckpointUnlock(req: { clientip: string; checkpoint: string; timeutc?: string }) {
    setError(null)
    setBusy(true)
    try {
      await createCheckpointUnlock(api, req)
      await refreshCheckpointUnlocks()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doUpdateCheckpointUnlock(id: number, req: { clientip: string; checkpoint: string; timeutc: string }) {
    setError(null)
    setBusy(true)
    try {
      await updateCheckpointUnlock(api, id, req)
      await refreshCheckpointUnlocks()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doDeleteCheckpointUnlock(id: number) {
    setError(null)
    setBusy(true)
    try {
      await deleteCheckpointUnlock(api, id)
      await refreshCheckpointUnlocks()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doUpsertManualBan(req: { ip: string; minutes: number; reason: string }) {
    setError(null)
    setBusy(true)
    try {
      await upsertManualBan(api, req)
      await refreshManualBans()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doDeleteManualBan(ip: string) {
    setError(null)
    setBusy(true)
    try {
      await deleteManualBan(api, ip)
      await refreshManualBans()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  async function doClearRateLimits() {
    setError(null)
    setBusy(true)
    try {
      await clearRateLimits(api)
      await refreshRateLimits()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  const logsRows: RequestLog[] = logsResult?.logs ?? []
  const playersRows: PlayerData[] = playersResult?.players ?? []
  const rlRows: RateLimitLog[] = rlResult?.logs ?? []
  const cuRows: CheckpointUnlock[] = cuResult?.unlocks ?? []

  async function handleConfirm() {
    if (!confirm) return
    const action = confirm.onConfirm
    setConfirm(null)
    await action()
  }

  return (
    <div className="container">
      <ConfirmModal
        open={confirm !== null}
        title={confirm?.title ?? ''}
        message={confirm?.message ?? ''}
        confirmText={confirm?.confirmText ?? ''}
        busy={busy}
        onCancel={() => setConfirm(null)}
        onConfirm={() => void handleConfirm()}
      />

      <div className="d-flex flex-wrap align-items-center justify-content-between">
        <div className="h5">AztecTrials Admin</div>
        <ul className="nav nav-pills">
          <li className="nav-item">
            <button
              type="button"
              className={view === 'logs' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('logs')}
            >
              Logs
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'players' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('players')}
            >
              Players
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'ratelimits' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('ratelimits')}
            >
              Rate Limits
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'checkpointunlocks' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('checkpointunlocks')}
            >
              Checkpoint Unlocks
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'manualbans' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('manualbans')}
            >
              Manual Bans
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'graphs' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('graphs')}
            >
              Graphs
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={view === 'settings' ? 'nav-link active' : 'nav-link'}
              onClick={() => setView('settings')}
            >
              Settings
            </button>
          </li>
        </ul>
      </div>

      {error ? (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      ) : null}

      {view === 'logs' ? (
        <LogsView
          busy={busy}
          logsQ={logsQ}
          setLogsQ={setLogsQ}
          logsEndpoints={logsEndpoints}
          setLogsEndpoints={setLogsEndpoints}
          logsStatus={logsStatus}
          setLogsStatus={setLogsStatus}
          logsErrorOnly={logsErrorOnly}
          setLogsErrorOnly={setLogsErrorOnly}
          logsDateTimeFilterEnabled={logsDateTimeFilterEnabled}
          setLogsDateTimeFilterEnabled={setLogsDateTimeFilterEnabled}
          logsRangeStartDate={logsRangeStartDate}
          setLogsRangeStartDate={(v: string) => {
            setLogsRangeStartDate(v)
            if (logsRangeEndDate.trim() === '') setLogsRangeEndDate(v)
          }}
          logsRangeStartTime={logsRangeStartTime}
          setLogsRangeStartTime={(v: string) => {
            setLogsRangeStartTime(v)
            if (logsRangeEndTime.trim() === '') setLogsRangeEndTime(v)
          }}
          logsRangeEndDate={logsRangeEndDate}
          setLogsRangeEndDate={(v: string) => {
            setLogsRangeEndDate(v)
            if (logsRangeStartDate.trim() === '') setLogsRangeStartDate(v)
          }}
          logsRangeEndTime={logsRangeEndTime}
          setLogsRangeEndTime={(v: string) => {
            setLogsRangeEndTime(v)
            if (logsRangeStartTime.trim() === '') setLogsRangeStartTime(v)
          }}
          logsPage={logsPage}
          setLogsPage={setLogsPage}
          logsPageSize={logsPageSize}
          setLogsPageSize={setLogsPageSize}
          logsResult={logsResult}
          logsRows={logsRows}
          logsSelectedId={logsSelectedId}
          setLogsSelectedId={setLogsSelectedId}
          logsExpandedId={logsExpandedId}
          setLogsExpandedId={setLogsExpandedId}
          onClearLogs={() =>
            setConfirm({
              title: 'Clear logs?',
              message:
                'This will permanently delete all request logs (including error and rate limit history).',
              confirmText: 'Clear logs',
              onConfirm: doClearLogs,
            })
          }
          onFetchLogById={doFetchLogById}
          onBanClientIP={doBanClientIP}
          onRefresh={(opts) => void refreshLogs(opts)}
        />
      ) : null}

      {view === 'players' ? (
        <PlayersView
          busy={busy}
          playersQ={playersQ}
          setPlayersQ={setPlayersQ}
          playersIP={playersIP}
          setPlayersIP={setPlayersIP}
          playersOrder={playersOrder}
          setPlayersOrder={setPlayersOrder}
          playersPage={playersPage}
          setPlayersPage={setPlayersPage}
          playersPageSize={playersPageSize}
          setPlayersPageSize={setPlayersPageSize}
          playersResult={playersResult}
          playersRows={playersRows}
          playerLookupName={playerLookupName}
          setPlayerLookupName={setPlayerLookupName}
          playerLookupResult={playerLookupResult}
          onClearPlayers={() =>
            setConfirm({
              title: 'Clear players?',
              message:
                'This will permanently delete all players from the leaderboard and cannot be undone.',
              confirmText: 'Clear players',
              onConfirm: doClearPlayers,
            })
          }
          onLookupPlayer={doLookupPlayer}
          onDeletePlayer={(name) =>
            setConfirm({
              title: 'Delete player?',
              message: `This will permanently delete the player "${name}" from the leaderboard and cannot be undone.`,
              confirmText: 'Delete player',
              onConfirm: () => doDeletePlayer(name),
            })
          }
          onUpdatePlayer={(oldName, nextName, nextSeconds) => doUpdatePlayer(oldName, nextName, nextSeconds)}
          onAddPlayerTime={(name, seconds) => doAddPlayerTime(name, seconds)}
          onRefresh={(opts) => void refreshPlayers(opts)}
        />
      ) : null}

      {view === 'ratelimits' ? (
        <RateLimitsView
          busy={busy}
          rlQ={rlQ}
          setRlQ={setRlQ}
          rlIP={rlIP}
          setRlIP={setRlIP}
          rlEndpoint={rlEndpoint}
          setRlEndpoint={setRlEndpoint}
          rlEvent={rlEvent}
          setRlEvent={setRlEvent}
          rlPage={rlPage}
          setRlPage={setRlPage}
          rlPageSize={rlPageSize}
          setRlPageSize={setRlPageSize}
          rlResult={rlResult}
          rlRows={rlRows}
          rlExpandedId={rlExpandedId}
          setRlExpandedId={setRlExpandedId}
          onClearRateLimits={() =>
            setConfirm({
              title: 'Clear rate limit logs?',
              message:
                'This will permanently delete all rate limit log entries and cannot be undone.',
              confirmText: 'Clear rate limit logs',
              onConfirm: doClearRateLimits,
            })
          }
          onRefresh={(opts) => void refreshRateLimits(opts)}
        />
      ) : null}

      {view === 'checkpointunlocks' ? (
        <CheckpointUnlocksView
          busy={busy}
          q={cuQ}
          setQ={setCuQ}
          ip={cuIP}
          setIP={setCuIP}
          checkpoint={cuCheckpoint}
          setCheckpoint={setCuCheckpoint}
          page={cuPage}
          setPage={setCuPage}
          pageSize={cuPageSize}
          setPageSize={setCuPageSize}
          result={cuResult}
          rows={cuRows}
          onRefresh={(opts) => void refreshCheckpointUnlocks(opts)}
          onCreate={(req) => doCreateCheckpointUnlock(req)}
          onUpdate={(id, req) => doUpdateCheckpointUnlock(id, req)}
          onDelete={(id) =>
            setConfirm({
              title: 'Delete checkpoint unlock?',
              message: `This will permanently delete checkpoint unlock #${id} and cannot be undone.`,
              confirmText: 'Delete',
              onConfirm: () => doDeleteCheckpointUnlock(id),
            })
          }
          onClear={() =>
            setConfirm({
              title: 'Clear checkpoint unlocks?',
              message: 'This will permanently delete all checkpoint unlock events and cannot be undone.',
              confirmText: 'Clear',
              onConfirm: doClearCheckpointUnlocks,
            })
          }
        />
      ) : null}

      {view === 'manualbans' ? (
        <ManualBansView
          busy={busy}
          q={mbQ}
          setQ={setMbQ}
          activeOnly={mbActiveOnly}
          setActiveOnly={setMbActiveOnly}
          page={mbPage}
          setPage={setMbPage}
          pageSize={mbPageSize}
          setPageSize={setMbPageSize}
          result={mbResult}
          rows={mbResult?.bans ?? []}
          onRefresh={(opts) => void refreshManualBans(opts)}
          onUpsert={(req) => doUpsertManualBan(req)}
          onDelete={(ip) =>
            setConfirm({
              title: 'Delete manual ban?',
              message: `This will remove the manual ban for IP "${ip}".`,
              confirmText: 'Delete',
              onConfirm: () => doDeleteManualBan(ip),
            })
          }
        />
      ) : null}

      {view === 'graphs' ? (
        <GraphsView
          busy={busy}
          tab={graphsTab}
          setTab={setGraphsTab}
          rangeMode={graphsRangeMode}
          setRangeMode={setGraphsRangeModeAndClear}
          networkBucket={graphsNetworkBucket}
          setNetworkBucket={setGraphsNetworkBucket}
          databaseBucket={graphsDatabaseBucket}
          setDatabaseBucket={setGraphsDatabaseBucket}
          periodPreset={graphsPeriodPreset}
          setPeriodPreset={setGraphsPeriodPreset}
          rangeStartDate={graphsRangeStartDate}
          setRangeStartDate={(v) => {
            setGraphsRangeStartDate(v)
            if (graphsRangeEndDate.trim() === '') setGraphsRangeEndDate(v)
          }}
          rangeEndDate={graphsRangeEndDate}
          setRangeEndDate={(v) => {
            setGraphsRangeEndDate(v)
            if (graphsRangeStartDate.trim() === '') setGraphsRangeStartDate(v)
          }}
          endpoints={graphsEndpoints}
          setEndpoints={setGraphsEndpoints}
          errorOnly={graphsErrorOnly}
          setErrorOnly={setGraphsErrorOnly}
          splitSuccessErrors={graphsSplitSuccessErrors}
          setSplitSuccessErrors={setGraphsSplitSuccessErrors}
          result={graphsResult}
          errorsResult={graphsErrorsResult}
          dbSizeResult={graphsDBSizeResult}
          dbRowsResult={graphsDBRowsResult}
          diskFreeResult={graphsDiskFreeResult}
          onRefresh={() => void refreshGraphs(graphsTab)}
        />
      ) : null}

      {view === 'settings' ? (
        <SettingsView
          busy={busy}
          adminKey={config.adminKey}
          setAdminKey={(v) => setConfig({ ...config, adminKey: v })}
        />
      ) : null}
    </div>
  )
}

export default App
