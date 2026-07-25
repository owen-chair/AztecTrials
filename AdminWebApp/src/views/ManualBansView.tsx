import { useEffect, useMemo, useState } from 'react'
import type { ManualIPBan, ManualIPBanQueryResult } from '../types'
import { PaginationControls } from '../components/PaginationControls'

function minutesFromUntilUTC(untilUTC: string): number {
  const until = new Date(untilUTC)
  if (!Number.isFinite(until.getTime())) return 0
  const ms = until.getTime() - Date.now()
  return Math.max(0, Math.round(ms / 60000))
}

export function ManualBansView(props: {
  busy: boolean
  q: string
  setQ: (v: string) => void
  activeOnly: boolean
  setActiveOnly: (v: boolean) => void
  page: number
  setPage: (v: number) => void
  pageSize: number
  setPageSize: (v: number) => void
  result: ManualIPBanQueryResult | null
  rows: ManualIPBan[]
  onRefresh: (opts?: { page?: number; pageSize?: number }) => void
  onUpsert: (req: { ip: string; minutes: number; reason: string }) => Promise<void>
  onDelete: (ip: string) => void
}) {
  const [editOpen, setEditOpen] = useState(false)
  const [editIp, setEditIp] = useState('')
  const [editMinutes, setEditMinutes] = useState('1440')
  const [editReason, setEditReason] = useState('')

  const minutesNum = useMemo(() => {
    const n = Number(editMinutes)
    return Number.isFinite(n) ? n : NaN
  }, [editMinutes])

  const valid = useMemo(() => {
    const ipOk = editIp.trim() !== ''
    const minOk = Number.isFinite(minutesNum) && minutesNum > 0 && minutesNum <= 60 * 24 * 365
    return ipOk && minOk
  }, [editIp, minutesNum])

  useEffect(() => {
    if (!editOpen) return
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setEditOpen(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [editOpen])

  function openAdd() {
    setEditIp('')
    setEditMinutes('1440')
    setEditReason('')
    setEditOpen(true)
  }

  function openEdit(b: ManualIPBan) {
    setEditIp(b.ip)
    setEditMinutes(String(Math.max(1, minutesFromUntilUTC(b.banneduntilutc))))
    setEditReason(b.reason ?? '')
    setEditOpen(true)
  }

  async function handleSave() {
    if (props.busy || !valid) return
    await props.onUpsert({ ip: editIp.trim(), minutes: minutesNum, reason: editReason })
    setEditOpen(false)
  }

  return (
    <div className="card">
      {editOpen ? (
        <>
          <div className="modal fade show" role="dialog" aria-modal="true" style={{ display: 'block' }}>
            <div className="modal-dialog">
              <div className="modal-content">
                <div className="modal-header">
                  <h5 className="modal-title">Manual IP ban</h5>
                  <button
                    type="button"
                    className="btn-close"
                    aria-label="Close"
                    disabled={props.busy}
                    onClick={() => setEditOpen(false)}
                  />
                </div>
                <div className="modal-body">
                  <div className="mb-3">
                    <label className="form-label">IP</label>
                    <input
                      className="form-control font-monospace"
                      value={editIp}
                      disabled={props.busy}
                      onChange={(e) => setEditIp(e.target.value)}
                      placeholder="1.2.3.4"
                    />
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Duration (minutes)</label>
                    <input
                      className="form-control"
                      type="number"
                      value={editMinutes}
                      disabled={props.busy}
                      onChange={(e) => setEditMinutes(e.target.value)}
                      min={1}
                      step={1}
                    />
                    <div className="form-text">Example: 1440 = 24 hours</div>
                  </div>
                  <div className="mb-0">
                    <label className="form-label">Reason</label>
                    <input
                      className="form-control"
                      value={editReason}
                      disabled={props.busy}
                      onChange={(e) => setEditReason(e.target.value)}
                      placeholder="optional"
                    />
                  </div>
                </div>
                <div className="modal-footer">
                  <button type="button" className="btn btn-secondary" disabled={props.busy} onClick={() => setEditOpen(false)}>
                    Close
                  </button>
                  <button type="button" className="btn btn-primary" disabled={props.busy || !valid} onClick={() => void handleSave()}>
                    Save
                  </button>
                </div>
              </div>
            </div>
          </div>
          <div className="modal-backdrop fade show" onClick={props.busy ? undefined : () => setEditOpen(false)} />
        </>
      ) : null}

      <div className="card-body">
        <div className="row align-items-end">
          <div className="col-12 col-lg-5">
            <label className="form-label">Search (q)</label>
            <input className="form-control" value={props.q} onChange={(e) => props.setQ(e.target.value)} placeholder="ip or reason" />
          </div>
          <div className="col-12 col-lg-3">
            <label className="form-label">Filters</label>
            <div className="form-check">
              <input
                className="form-check-input"
                type="checkbox"
                checked={props.activeOnly}
                onChange={(e) => props.setActiveOnly(e.target.checked)}
                id="manualBansActiveOnly"
              />
              <label className="form-check-label" htmlFor="manualBansActiveOnly">
                Active only
              </label>
            </div>
          </div>
          <div className="col-12 col-lg-4">
            <div className="btn-group" role="group" aria-label="Manual ban actions">
              <button type="button" className="btn btn-outline-secondary" disabled={props.busy} onClick={openAdd}>
                Add ban
              </button>
              <button type="button" className="btn btn-outline-secondary" disabled={props.busy} onClick={() => props.onRefresh()}>
                Refresh
              </button>
            </div>
          </div>
        </div>

        <hr />

        <div className="row align-items-end">
          <div className="col-12 col-lg-12">
            <PaginationControls
              busy={props.busy}
              page={props.page}
              pageSize={props.pageSize}
              total={props.result?.total ?? 0}
              onPageChange={(p) => {
                props.setPage(p)
                props.onRefresh({ page: p })
              }}
              onPageSizeChange={(n) => {
                props.setPageSize(n)
                props.setPage(0)
                props.onRefresh({ page: 0, pageSize: n })
              }}
              onRefresh={() => props.onRefresh()}
            />
          </div>
        </div>
      </div>

      <div className="card-body">
        <div className="text-body-secondary small">
          Total <span className="fw-semibold">{props.result?.total ?? 0}</span> • Showing{' '}
          <span className="fw-semibold">{props.rows.length}</span>
        </div>

        <div className="table-responsive">
          <table className="table table-sm table-hover align-middle manualbans-table">
            <thead>
              <tr>
                <th>IP</th>
                <th>Banned until (UTC)</th>
                <th>Reason</th>
                <th>Created (UTC)</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {props.rows.map((b) => (
                <tr key={b.id || b.ip}>
                  <td className="font-monospace">{b.ip}</td>
                  <td className="font-monospace">{b.banneduntilutc}</td>
                  <td>{b.reason}</td>
                  <td className="font-monospace">{b.createdutc}</td>
                  <td className="text-end">
                    <div className="btn-group manualbans-row-actions" role="group" aria-label="Row actions">
                      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={props.busy} onClick={() => openEdit(b)}>
                        Edit
                      </button>
                      <button type="button" className="btn btn-outline-danger btn-sm" disabled={props.busy} onClick={() => props.onDelete(b.ip)}>
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
