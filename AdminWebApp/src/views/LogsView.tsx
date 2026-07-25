import { Fragment, useEffect, useMemo, useState } from 'react'
import type { ComponentType } from 'react'
import type { LogQueryResult, RequestLog } from '../types'
import { PaginationControls } from '../components/PaginationControls'
import { VALID_ENDPOINTS } from '../endpoints'
import { EndpointMultiSelect } from '../components/EndpointMultiSelect'
import * as Flags from 'country-flag-icons/react/3x2'
import DatePicker from 'react-datepicker'

function pickPayload(l: RequestLog): string {
  const json = (l.payloadjson ?? '').trim()
  if (json !== '') return json
  const unescaped = (l.payloadunescaped ?? '').trim()
  if (unescaped !== '') return unescaped
  const stripped = (l.payloadstripped ?? '').trim()
  if (stripped !== '') return stripped
  return (l.payloadraw ?? '').trim()
}

function timeStringToDate(value: string): Date | null {
  const v = value.trim()
  if (v === '') return null
  const m = /^([01]\d|2[0-3]):([0-5]\d)(?::([0-5]\d))?$/.exec(v)
  if (!m) return null
  const hours = Number(m[1])
  const minutes = Number(m[2])
  const seconds = m[3] ? Number(m[3]) : 0
  const d = new Date(0)
  d.setHours(hours, minutes, seconds, 0)
  return d
}

function dateToTimeString(value: Date | null): string {
  if (!value) return ''
  const hh = String(value.getHours()).padStart(2, '0')
  const mm = String(value.getMinutes()).padStart(2, '0')
  return `${hh}:${mm}`
}

function GeoLocationCell(props: { country?: string; city?: string }) {
  const country = (props.country ?? '').trim().toUpperCase()
  const city = (props.city ?? '').trim()

  const hasAnything = country !== '' || city !== ''
  if (!hasAnything) return null

  const Flag = (Flags as unknown as Record<
    string,
    ComponentType<{ title?: string; className?: string; style?: React.CSSProperties }>
  >)[country]
  const locationText = [country, city].filter((x) => x && x.trim() !== '').join(' ')

  return (
    <span className="d-inline-flex align-items-center gap-2">
      {Flag ? (
        <Flag
          title={country}
          className="flex-shrink-0"
          style={{ width: '1.1em', height: '1.1em' }}
        />
      ) : null}
      <span className="font-monospace">{locationText}</span>
    </span>
  )
}

export function LogsView(props: {
  busy: boolean
  logsQ: string
  setLogsQ: (v: string) => void
  logsEndpoints: string[]
  setLogsEndpoints: (v: string[] | ((cur: string[]) => string[])) => void
  logsStatus: string
  setLogsStatus: (v: string) => void
  logsErrorOnly: boolean
  setLogsErrorOnly: (v: boolean) => void
  logsDateTimeFilterEnabled: boolean
  setLogsDateTimeFilterEnabled: (v: boolean) => void
  logsRangeStartDate: string
  setLogsRangeStartDate: (v: string) => void
  logsRangeStartTime: string
  setLogsRangeStartTime: (v: string) => void
  logsRangeEndDate: string
  setLogsRangeEndDate: (v: string) => void
  logsRangeEndTime: string
  setLogsRangeEndTime: (v: string) => void
  logsPage: number
  setLogsPage: (v: number) => void
  logsPageSize: number
  setLogsPageSize: (v: number) => void
  logsResult: LogQueryResult | null
  logsRows: RequestLog[]
  logsSelectedId: number | null
  setLogsSelectedId: (v: number | null) => void
  logsExpandedId: number | null
  setLogsExpandedId: (v: number | null | ((cur: number | null) => number | null)) => void
  onClearLogs: () => void
  onFetchLogById: () => void
  onBanClientIP: (ip: string) => Promise<void>
  onRefresh: (opts?: { page?: number; pageSize?: number }) => void
}) {
  const [actionOpen, setActionOpen] = useState(false)
  const [actionLog, setActionLog] = useState<RequestLog | null>(null)
  const payload = useMemo(() => (actionLog ? pickPayload(actionLog) : ''), [actionLog])

  useEffect(() => {
    if (!actionOpen) return
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setActionOpen(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [actionOpen])

  function openAction(l: RequestLog) {
    setActionLog(l)
    setActionOpen(true)
  }

  async function handleBan() {
    if (!actionLog || props.busy) return
    await props.onBanClientIP(actionLog.clientip)
    setActionOpen(false)
  }

  return (
    <div className="card">
      {actionOpen && actionLog ? (
        <>
          <div className="modal fade show" role="dialog" aria-modal="true" style={{ display: 'block' }}>
            <div className="modal-dialog modal-lg">
              <div className="modal-content">
                <div className="modal-header">
                  <h5 className="modal-title">Log action</h5>
                  <button
                    type="button"
                    className="btn-close"
                    aria-label="Close"
                    disabled={props.busy}
                    onClick={() => setActionOpen(false)}
                  />
                </div>
                <div className="modal-body">
                  <div className="mb-3">
                    <div className="text-body-secondary small">Time (UTC)</div>
                    <div className="font-monospace">{actionLog.timeutc}</div>
                  </div>

                  <div className="mb-3">
                    <div className="text-body-secondary small">IP addresses</div>
                    <div className="font-monospace">Client IP: {actionLog.clientip || '(none)'}</div>
                    <div className="font-monospace">Remote addr: {actionLog.remoteaddr || '(none)'}</div>
                  </div>

                  <div className="mb-0">
                    <div className="text-body-secondary small">Payload</div>
                    <pre className="mb-0 font-monospace" style={{ maxHeight: 320, overflow: 'auto' }}>
                      {payload || '(none)'}
                    </pre>
                  </div>
                </div>
                <div className="modal-footer">
                  <button
                    type="button"
                    className="btn btn-outline-danger"
                    disabled={props.busy || actionLog.clientip.trim() === ''}
                    onClick={() => void handleBan()}
                  >
                    Ban client IP
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={props.busy}
                    onClick={() => setActionOpen(false)}
                  >
                    Close
                  </button>
                </div>
              </div>
            </div>
          </div>
          <div className="modal-backdrop fade show" onClick={props.busy ? undefined : () => setActionOpen(false)} />
        </>
      ) : null}

      <div className="card-body">
        <div className="row align-items-end">
          <div className="col-12 col-lg-4">
            <label className="form-label">Search (q)</label>
            <input className="form-control" value={props.logsQ} onChange={(e) => props.setLogsQ(e.target.value)} />
          </div>
          <div className="col-12 col-lg-4">
            <EndpointMultiSelect
              label="Endpoints"
              groups={[
                { label: 'Public', endpoints: VALID_ENDPOINTS.public },
                { label: 'Admin', endpoints: VALID_ENDPOINTS.admin },
              ]}
              selected={props.logsEndpoints}
              setSelected={props.setLogsEndpoints}
              disabled={props.busy}
            />
          </div>

          <div className="col-12 col-lg-2">
            <label className="form-label">Status</label>
            <input
              className="form-control"
              value={props.logsStatus}
              onChange={(e) => props.setLogsStatus(e.target.value)}
              placeholder="200"
            />
          </div>

          <div className="col-12 col-lg-2">
            <button type="button" className="btn btn-outline-danger" disabled={props.busy} onClick={props.onClearLogs}>
              Clear logs
            </button>
          </div>
        </div>

        <div className="row align-items-end mt-2">
          <div className="col-12 col-lg-2">
            <label className="form-label">Error only</label>
            <div className="form-check">
              <input
                className="form-check-input"
                type="checkbox"
                checked={props.logsErrorOnly}
                onChange={(e) => props.setLogsErrorOnly(e.target.checked)}
                id="logsErrorOnly"
              />
              <label className="form-check-label" htmlFor="logsErrorOnly">
                Only errors
              </label>
            </div>
          </div>
          <div className="col-12 col-lg-4">
            <label className="form-label">Date/time filter</label>
            <div className="form-check">
              <input
                className="form-check-input"
                type="checkbox"
                checked={props.logsDateTimeFilterEnabled}
                onChange={(e) => props.setLogsDateTimeFilterEnabled(e.target.checked)}
                id="logsDateTimeFilterEnabled"
              />
              <label className="form-check-label" htmlFor="logsDateTimeFilterEnabled">
                Enable date/time range
              </label>
            </div>
          </div>
        </div>

        {props.logsDateTimeFilterEnabled ? (
          <div className="row align-items-end mt-2">
            <div className="col-12 col-lg-3">
              <label className="form-label">Start date</label>
              <input
                type="date"
                className="form-control"
                disabled={props.busy}
                value={props.logsRangeStartDate}
                onChange={(e) => props.setLogsRangeStartDate(e.target.value)}
              />
            </div>
            <div className="col-12 col-lg-3">
              <label className="form-label">Start time</label>
              <DatePicker
                selected={timeStringToDate(props.logsRangeStartTime)}
                onChange={(d: Date | null) => props.setLogsRangeStartTime(dateToTimeString(d))}
                showTimeSelect
                showTimeSelectOnly
                timeIntervals={5}
                timeCaption="Time"
                dateFormat="HH:mm"
                placeholderText="--:--"
                className="form-control"
                wrapperClassName="w-100"
                disabled={props.busy}
              />
            </div>
            <div className="col-12 col-lg-3">
              <label className="form-label">End date</label>
              <input
                type="date"
                className="form-control"
                disabled={props.busy}
                value={props.logsRangeEndDate}
                onChange={(e) => props.setLogsRangeEndDate(e.target.value)}
              />
            </div>
            <div className="col-12 col-lg-3">
              <label className="form-label">End time</label>
              <DatePicker
                selected={timeStringToDate(props.logsRangeEndTime)}
                onChange={(d: Date | null) => props.setLogsRangeEndTime(dateToTimeString(d))}
                showTimeSelect
                showTimeSelectOnly
                timeIntervals={5}
                timeCaption="Time"
                dateFormat="HH:mm"
                placeholderText="--:--"
                className="form-control"
                wrapperClassName="w-100"
                disabled={props.busy}
              />
            </div>
          </div>
        ) : null}

        <hr />

        <div className="row align-items-end">
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Fetch log by id</label>
            <input
              className="form-control"
              type="number"
              value={props.logsSelectedId ?? ''}
              onChange={(e) => {
                const v = e.target.value
                props.setLogsSelectedId(v === '' ? null : Number(v))
              }}
            />
          </div>
          <div className="col-12 col-md-6 col-lg-2">
            <button
              type="button"
              className="btn btn-outline-secondary"
              disabled={props.busy}
              onClick={props.onFetchLogById}
            >
              Fetch
            </button>
          </div>
          <div className="col-12 col-lg-7">
            <PaginationControls
              busy={props.busy}
              page={props.logsPage}
              pageSize={props.logsPageSize}
              total={props.logsResult?.total ?? 0}
              onPageChange={(p) => {
                props.setLogsPage(p)
                props.onRefresh({ page: p })
              }}
              onPageSizeChange={(n) => {
                props.setLogsPageSize(n)
                props.setLogsPage(0)
                props.onRefresh({ page: 0, pageSize: n })
              }}
              onRefresh={() => props.onRefresh()}
            />
          </div>
        </div>
      </div>

      <div className="card-body">
        <div className="text-body-secondary small">
          Total <span className="fw-semibold">{props.logsResult?.total ?? 0}</span> • Showing{' '}
          <span className="fw-semibold">{props.logsRows.length}</span>
        </div>

        <div className="table-responsive">
          <table className="table table-sm table-hover align-middle logs-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Time</th>
                <th>Endpoint</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Client IP</th>
                <th>Location</th>
                <th>Error</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {props.logsRows.map((l) => (
                <Fragment key={l.id}>
                  <tr
                    style={{ cursor: 'pointer' }}
                    onClick={() =>
                      props.setLogsExpandedId((cur) => (cur === l.id ? null : l.id))
                    }
                  >
                    <td>{l.id}</td>
                    <td>{l.timeutc}</td>
                    <td>{l.endpoint}</td>
                    <td>{l.status}</td>
                    <td>{l.durationms}ms</td>
                    <td>{l.clientip}</td>
                    <td>
                      <GeoLocationCell country={l.geocountry} city={l.geocity} />
                    </td>
                    <td className="font-monospace">{l.error}</td>
                    <td className="text-end">
                      <div className="btn-group logs-row-actions" role="group" aria-label="Row actions">
                        <button
                          type="button"
                          className="btn btn-outline-secondary btn-sm"
                          disabled={props.busy}
                          onClick={(e) => {
                            e.stopPropagation()
                            openAction(l)
                          }}
                        >
                          Action
                        </button>
                      </div>
                    </td>
                  </tr>
                  {props.logsExpandedId === l.id ? (
                    <tr>
                      <td colSpan={9}>
                        <pre className="mb-0">{JSON.stringify(l, null, 2)}</pre>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
