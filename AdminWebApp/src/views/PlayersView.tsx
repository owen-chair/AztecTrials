import { useEffect, useMemo, useState } from 'react'
import type { ComponentType } from 'react'
import type { PlayerData, PlayerQueryResult } from '../types'
import { PaginationControls } from '../components/PaginationControls'
import * as Flags from 'country-flag-icons/react/3x2'

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
        <Flag title={country} className="flex-shrink-0" style={{ width: '1.1em', height: '1.1em' }} />
      ) : null}
      <span className="font-monospace">{locationText}</span>
    </span>
  )
}

export function PlayersView(props: {
  busy: boolean
  playersQ: string
  setPlayersQ: (v: string) => void
  playersIP: string
  setPlayersIP: (v: string) => void
  playersOrder: string
  setPlayersOrder: (v: string) => void
  playersPage: number
  setPlayersPage: (v: number) => void
  playersPageSize: number
  setPlayersPageSize: (v: number) => void
  playersResult: PlayerQueryResult | null
  playersRows: PlayerData[]
  playerLookupName: string
  setPlayerLookupName: (v: string) => void
  playerLookupResult: PlayerData | null
  onClearPlayers: () => void
  onLookupPlayer: () => void
  onDeletePlayer: (name: string) => void
  onUpdatePlayer: (oldName: string, nextName: string, nextSeconds: number) => Promise<void>
  onAddPlayerTime: (name: string, seconds: number) => Promise<void>
  onRefresh: (opts?: { page?: number; pageSize?: number }) => void
}) {
  const [addOpen, setAddOpen] = useState(false)
  const [addName, setAddName] = useState('')
  const [addSeconds, setAddSeconds] = useState('')

  const [editOpen, setEditOpen] = useState(false)
  const [editOldName, setEditOldName] = useState('')
  const [editName, setEditName] = useState('')
  const [editSeconds, setEditSeconds] = useState('')

  const addSecondsNumber = useMemo(() => {
    const n = Number(addSeconds)
    return Number.isFinite(n) ? n : NaN
  }, [addSeconds])

  const addValid = addName.trim() !== '' && Number.isFinite(addSecondsNumber) && addSecondsNumber >= 0

  const editSecondsNumber = useMemo(() => {
    const n = Number(editSeconds)
    return Number.isFinite(n) ? n : NaN
  }, [editSeconds])

  const editValid = editName.trim() !== '' && Number.isFinite(editSecondsNumber) && editSecondsNumber >= 0

  useEffect(() => {
    if (!editOpen && !addOpen) return

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setEditOpen(false)
        setAddOpen(false)
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [editOpen, addOpen])

  function openAdd() {
    setAddName('')
    setAddSeconds('')
    setAddOpen(true)
  }

  async function handleSaveAdd() {
    if (!addValid || props.busy) return
    const name = addName.trim()
    const seconds = addSecondsNumber
    await props.onAddPlayerTime(name, seconds)
    setAddOpen(false)
  }

  function openEdit(p: PlayerData) {
    setEditOldName(p.playername)
    setEditName(p.playername)
    setEditSeconds(String(p.completionseconds))
    setEditOpen(true)
  }

  async function handleSaveEdit() {
    if (!editValid || props.busy) return
    const oldName = editOldName
    const nextName = editName.trim()
    const nextSeconds = editSecondsNumber
    await props.onUpdatePlayer(oldName, nextName, nextSeconds)
    setEditOpen(false)
  }

  return (
    <div className="card">
      {addOpen ? (
        <>
          <div className="modal fade show" role="dialog" aria-modal="true" style={{ display: 'block' }}>
            <div className="modal-dialog">
              <div className="modal-content">
                <div className="modal-header">
                  <h5 className="modal-title">Add player time</h5>
                  <button
                    type="button"
                    className="btn-close"
                    aria-label="Close"
                    disabled={props.busy}
                    onClick={() => setAddOpen(false)}
                  />
                </div>
                <div className="modal-body">
                  <div className="mb-3">
                    <label className="form-label">Player name</label>
                    <input
                      className="form-control"
                      value={addName}
                      disabled={props.busy}
                      onChange={(e) => setAddName(e.target.value)}
                    />
                  </div>
                  <div className="mb-0">
                    <label className="form-label">Completion time (seconds)</label>
                    <input
                      className="form-control"
                      type="number"
                      step="0.001"
                      value={addSeconds}
                      disabled={props.busy}
                      onChange={(e) => setAddSeconds(e.target.value)}
                    />
                  </div>
                </div>
                <div className="modal-footer">
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={props.busy}
                    onClick={() => setAddOpen(false)}
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={props.busy || !addValid}
                    onClick={() => void handleSaveAdd()}
                  >
                    Add
                  </button>
                </div>
              </div>
            </div>
          </div>
          <div className="modal-backdrop fade show" onClick={props.busy ? undefined : () => setAddOpen(false)} />
        </>
      ) : null}

      {editOpen ? (
        <>
          <div className="modal fade show" role="dialog" aria-modal="true" style={{ display: 'block' }}>
            <div className="modal-dialog">
              <div className="modal-content">
                <div className="modal-header">
                  <h5 className="modal-title">Edit player time</h5>
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
                    <label className="form-label">Player name</label>
                    <input
                      className="form-control"
                      value={editName}
                      disabled={props.busy}
                      onChange={(e) => setEditName(e.target.value)}
                    />
                  </div>
                  <div className="mb-0">
                    <label className="form-label">Completion time (seconds)</label>
                    <input
                      className="form-control"
                      type="number"
                      step="0.001"
                      value={editSeconds}
                      disabled={props.busy}
                      onChange={(e) => setEditSeconds(e.target.value)}
                    />
                  </div>
                </div>
                <div className="modal-footer">
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={props.busy}
                    onClick={() => setEditOpen(false)}
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={props.busy || !editValid}
                    onClick={() => void handleSaveEdit()}
                  >
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
            <input
              className="form-control"
              value={props.playersQ}
              onChange={(e) => props.setPlayersQ(e.target.value)}
              placeholder="substring of playername"
            />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Client IP (ip, exact)</label>
            <input
              className="form-control"
              value={props.playersIP}
              onChange={(e) => props.setPlayersIP(e.target.value)}
              placeholder="exact client IP (e.g. 1.2.3.4)"
            />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Order</label>
            <select
              className="form-select"
              value={props.playersOrder}
              onChange={(e) => props.setPlayersOrder(e.target.value)}
            >
              <option value="added_time_desc">added_time_desc</option>
              <option value="added_time_asc">added_time_asc</option>
              <option value="leaderboard">leaderboard</option>
              <option value="time_asc">time_asc</option>
              <option value="time_desc">time_desc</option>
              <option value="name_asc">name_asc</option>
              <option value="name_desc">name_desc</option>
            </select>
          </div>
          <div className="col-12 col-lg-2">
            <div className="btn-group" role="group" aria-label="Players actions">
              <button type="button" className="btn btn-outline-secondary" disabled={props.busy} onClick={openAdd}>
                Add player time
              </button>
              <button
                type="button"
                className="btn btn-outline-danger"
                disabled={props.busy}
                onClick={props.onClearPlayers}
              >
                Clear players
              </button>
            </div>
          </div>
        </div>

        <hr />

        <div className="row align-items-end">
          <div className="col-12 col-md-6 col-lg-4">
            <label className="form-label">Get player by name</label>
            <input
              className="form-control"
              value={props.playerLookupName}
              onChange={(e) => props.setPlayerLookupName(e.target.value)}
            />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <div className="btn-group" role="group" aria-label="Player actions">
              <button
                type="button"
                className="btn btn-outline-secondary"
                disabled={props.busy}
                onClick={props.onLookupPlayer}
              >
                Fetch
              </button>
              <button
                type="button"
                className="btn btn-outline-secondary"
                disabled={props.busy || !props.playerLookupResult}
                onClick={() => props.playerLookupResult && openEdit(props.playerLookupResult)}
              >
                Edit
              </button>
              <button
                type="button"
                className="btn btn-outline-danger"
                disabled={props.busy || props.playerLookupName.trim() === ''}
                onClick={() => props.onDeletePlayer(props.playerLookupName.trim())}
              >
                Delete
              </button>
            </div>
          </div>
          <div className="col-12 col-lg-5">
            <PaginationControls
              busy={props.busy}
              page={props.playersPage}
              pageSize={props.playersPageSize}
              total={props.playersResult?.total ?? 0}
              onPageChange={(p) => {
                props.setPlayersPage(p)
                props.onRefresh({ page: p })
              }}
              onPageSizeChange={(n) => {
                props.setPlayersPageSize(n)
                props.setPlayersPage(0)
                props.onRefresh({ page: 0, pageSize: n })
              }}
              onRefresh={() => props.onRefresh()}
            />
          </div>
        </div>

        {props.playerLookupResult ? (
          <pre className="mb-0">{JSON.stringify(props.playerLookupResult, null, 2)}</pre>
        ) : null}
      </div>

      <div className="card-body">
        <div className="text-body-secondary small">
          Total <span className="fw-semibold">{props.playersResult?.total ?? 0}</span> • Showing{' '}
          <span className="fw-semibold">{props.playersRows.length}</span>
        </div>

        <div className="table-responsive">
          <table className="table table-sm table-hover align-middle players-table">
            <thead>
              <tr>
                <th>Player</th>
                <th>Seconds</th>
                <th>Added</th>
                <th>Client IP</th>
                <th>Location</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {props.playersRows.map((p) => (
                <tr key={p.playername}>
                  <td className="font-monospace">{p.playername}</td>
                  <td>{p.completionseconds}</td>
                  <td className="font-monospace">{p.addedtime}</td>
                  <td className="font-monospace">{(p.clientip ?? '').trim()}</td>
                  <td>
                    <GeoLocationCell country={p.geocountry} city={p.geocity} />
                  </td>
                  <td className="text-end">
                    <div className="btn-group players-row-actions" role="group" aria-label="Row actions">
                      <button
                        type="button"
                        className="btn btn-outline-secondary btn-sm"
                        disabled={props.busy}
                        onClick={() => openEdit(p)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline-danger btn-sm"
                        disabled={props.busy}
                        onClick={() => props.onDeletePlayer(p.playername)}
                      >
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
