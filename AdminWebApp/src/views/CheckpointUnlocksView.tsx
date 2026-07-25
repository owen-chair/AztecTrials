import { useEffect, useMemo, useState } from 'react'
import type { ComponentType } from 'react'
import type { CheckpointUnlock, CheckpointUnlockQueryResult } from '../types'
import { PaginationControls } from '../components/PaginationControls'
import * as Flags from 'country-flag-icons/react/3x2'

const CHECKPOINTS = [
  'ZiplineCheckpointUnlocked',
  'BoulderTunnelCheckpointUnlocked',
  'crushingWallsCheckpointUnlocked',
  'jumpRoomCheckpointUnlocked'
] as const

type CheckpointName = (typeof CHECKPOINTS)[number]

function isValidDateString(v: string): boolean {
  const d = new Date(v)
  return Number.isFinite(d.getTime())
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
      {Flag ? <Flag title={country} className="flex-shrink-0" style={{ width: '1.1em', height: '1.1em' }} /> : null}
      <span className="font-monospace">{locationText}</span>
    </span>
  )
}

export function CheckpointUnlocksView(props: {
  busy: boolean
  q: string
  setQ: (v: string) => void
  ip: string
  setIP: (v: string) => void
  checkpoint: string
  setCheckpoint: (v: string) => void
  page: number
  setPage: (v: number) => void
  pageSize: number
  setPageSize: (v: number) => void
  result: CheckpointUnlockQueryResult | null
  rows: CheckpointUnlock[]
  onRefresh: (opts?: { page?: number; pageSize?: number }) => void
  onCreate: (req: { clientip: string; checkpoint: CheckpointName; timeutc?: string }) => Promise<void>
  onUpdate: (id: number, req: { clientip: string; checkpoint: CheckpointName; timeutc: string }) => Promise<void>
  onDelete: (id: number) => void
  onClear: () => void
}) {
  const [editOpen, setEditOpen] = useState(false)
  const [editMode, setEditMode] = useState<'add' | 'edit'>('add')
  const [editId, setEditId] = useState<number | null>(null)
  const [editIP, setEditIP] = useState('')
  const [editCheckpoint, setEditCheckpoint] = useState<CheckpointName>('ZiplineCheckpointUnlocked')
  const [editTimeUTC, setEditTimeUTC] = useState('')

  const timeValid = useMemo(() => {
    const v = editTimeUTC.trim()
    if (editMode === 'add') {
      // Optional for add.
      if (v === '') return true
      return isValidDateString(v)
    }
    // Required for edit.
    if (v === '') return false
    return isValidDateString(v)
  }, [editTimeUTC, editMode])

  const valid = useMemo(() => {
    return editIP.trim() !== '' && editCheckpoint.trim() !== '' && timeValid
  }, [editIP, editCheckpoint, timeValid])

  useEffect(() => {
    if (!editOpen) return
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setEditOpen(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [editOpen])

  function openAdd() {
    setEditMode('add')
    setEditId(null)
    setEditIP('')
    setEditCheckpoint('ZiplineCheckpointUnlocked')
    setEditTimeUTC('')
    setEditOpen(true)
  }

  function openEditRow(r: CheckpointUnlock) {
    setEditMode('edit')
    setEditId(r.id)
    setEditIP(r.clientip)
    setEditCheckpoint(r.checkpoint as CheckpointName)
    setEditTimeUTC(r.timeutc)
    setEditOpen(true)
  }

  async function handleSave() {
    if (props.busy || !valid) return

    const clientip = editIP.trim()
    const checkpoint = editCheckpoint
    const timeutc = editTimeUTC.trim()

    if (editMode === 'add') {
      await props.onCreate({ clientip, checkpoint, timeutc: timeutc === '' ? undefined : timeutc })
      setEditOpen(false)
      return
    }

    if (!editId) return
    await props.onUpdate(editId, { clientip, checkpoint, timeutc })
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
                  <h5 className="modal-title">
                    {editMode === 'add' ? 'Add checkpoint unlock' : `Edit checkpoint unlock #${editId ?? ''}`}
                  </h5>
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
                    <label className="form-label">Client IP</label>
                    <input
                      className="form-control font-monospace"
                      value={editIP}
                      disabled={props.busy}
                      onChange={(e) => setEditIP(e.target.value)}
                      placeholder="1.2.3.4"
                    />
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Checkpoint</label>
                    <select
                      className="form-select"
                      value={editCheckpoint}
                      disabled={props.busy}
                      onChange={(e) => setEditCheckpoint(e.target.value as CheckpointName)}
                    >
                      {CHECKPOINTS.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="mb-0">
                    <label className="form-label">Time (UTC, RFC3339)</label>
                    <input
                      className="form-control font-monospace"
                      value={editTimeUTC}
                      disabled={props.busy}
                      onChange={(e) => setEditTimeUTC(e.target.value)}
                      placeholder={editMode === 'add' ? '(optional) 2026-01-28T12:34:56Z' : '2026-01-28T12:34:56Z'}
                    />
                    {!timeValid ? <div className="form-text text-danger">Invalid time format</div> : null}
                  </div>
                </div>
                <div className="modal-footer">
                  <button type="button" className="btn btn-secondary" disabled={props.busy} onClick={() => setEditOpen(false)}>
                    Cancel
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
          <div className="col-12 col-md-6 col-lg-4">
            <label className="form-label">Search (q)</label>
            <input className="form-control" value={props.q} onChange={(e) => props.setQ(e.target.value)} placeholder="ip or checkpoint" />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">IP (exact)</label>
            <input className="form-control font-monospace" value={props.ip} onChange={(e) => props.setIP(e.target.value)} placeholder="1.2.3.4" />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Checkpoint</label>
            <select className="form-select" value={props.checkpoint} onChange={(e) => props.setCheckpoint(e.target.value)}>
              <option value="">(any)</option>
              {CHECKPOINTS.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
          <div className="col-12 col-lg-2">
            <div className="btn-group" role="group" aria-label="Checkpoint unlock actions">
              <button type="button" className="btn btn-outline-secondary" disabled={props.busy} onClick={openAdd}>
                Add
              </button>
              <button type="button" className="btn btn-outline-secondary" disabled={props.busy} onClick={() => props.onRefresh()}>
                Refresh
              </button>
              <button type="button" className="btn btn-outline-danger" disabled={props.busy} onClick={props.onClear}>
                Clear
              </button>
            </div>
          </div>
        </div>

        <hr />

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

      <div className="card-body">
        <div className="text-body-secondary small">
          Total <span className="fw-semibold">{props.result?.total ?? 0}</span> • Showing{' '}
          <span className="fw-semibold">{props.rows.length}</span>
        </div>

        <div className="table-responsive">
          <table className="table table-sm table-hover align-middle">
            <thead>
              <tr>
                <th>ID</th>
                <th>Time (UTC)</th>
                <th>IP</th>
                <th>Location</th>
                <th>Checkpoint</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {props.rows.map((r) => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td className="font-monospace">{r.timeutc}</td>
                  <td className="font-monospace">{r.clientip}</td>
                  <td>
                    <GeoLocationCell country={r.geocountry} city={r.geocity} />
                  </td>
                  <td>{r.checkpoint}</td>
                  <td className="text-end">
                    <div className="btn-group" role="group" aria-label="Row actions">
                      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={props.busy} onClick={() => openEditRow(r)}>
                        Edit
                      </button>
                      <button type="button" className="btn btn-outline-danger btn-sm" disabled={props.busy} onClick={() => props.onDelete(r.id)}>
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
