import { Fragment } from 'react'
import type { RateLimitLog, RateLimitQueryResult } from '../types'
import { PaginationControls } from '../components/PaginationControls'

export function RateLimitsView(props: {
  busy: boolean
  rlQ: string
  setRlQ: (v: string) => void
  rlIP: string
  setRlIP: (v: string) => void
  rlEndpoint: string
  setRlEndpoint: (v: string) => void
  rlEvent: string
  setRlEvent: (v: string) => void
  rlPage: number
  setRlPage: (v: number) => void
  rlPageSize: number
  setRlPageSize: (v: number) => void
  rlResult: RateLimitQueryResult | null
  rlRows: RateLimitLog[]
  rlExpandedId: number | null
  setRlExpandedId: (v: number | null | ((cur: number | null) => number | null)) => void
  onClearRateLimits: () => void
  onRefresh: (opts?: { page?: number; pageSize?: number }) => void
}) {
  return (
    <div className="card">
      <div className="card-body">
        <div className="row align-items-end">
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Search (q)</label>
            <input className="form-control" value={props.rlQ} onChange={(e) => props.setRlQ(e.target.value)} />
          </div>
          <div className="col-12 col-md-6 col-lg-2">
            <label className="form-label">IP (exact)</label>
            <input className="form-control" value={props.rlIP} onChange={(e) => props.setRlIP(e.target.value)} />
          </div>
          <div className="col-12 col-md-6 col-lg-3">
            <label className="form-label">Endpoint</label>
            <input
              className="form-control"
              value={props.rlEndpoint}
              onChange={(e) => props.setRlEndpoint(e.target.value)}
              placeholder="/data/top10/, /time/submit/, etc"
            />
          </div>
          <div className="col-12 col-md-6 col-lg-2">
            <label className="form-label">Event</label>
            <select className="form-select" value={props.rlEvent} onChange={(e) => props.setRlEvent(e.target.value)}>
              <option value="">(any)</option>
              <option value="banned">banned</option>
              <option value="blocked">blocked</option>
            </select>
          </div>
          <div className="col-12 col-lg-2">
            <button
              type="button"
              className="btn btn-outline-danger"
              disabled={props.busy}
              onClick={props.onClearRateLimits}
            >
              Clear rate limit logs
            </button>
          </div>
        </div>

        <hr />

        <PaginationControls
          busy={props.busy}
          page={props.rlPage}
          pageSize={props.rlPageSize}
          total={props.rlResult?.total ?? 0}
          onPageChange={(p) => {
            props.setRlPage(p)
            props.onRefresh({ page: p })
          }}
          onPageSizeChange={(n) => {
            props.setRlPageSize(n)
            props.setRlPage(0)
            props.onRefresh({ page: 0, pageSize: n })
          }}
          onRefresh={() => props.onRefresh()}
        />
      </div>

      <div className="card-body">
        <div className="text-body-secondary small">
          Total <span className="fw-semibold">{props.rlResult?.total ?? 0}</span> • Showing{' '}
          <span className="fw-semibold">{props.rlRows.length}</span>
        </div>

        <div className="table-responsive">
          <table className="table table-sm table-hover align-middle">
            <thead>
              <tr>
                <th>ID</th>
                <th>Time</th>
                <th>IP</th>
                <th>Endpoint</th>
                <th>Event</th>
                <th>Count/sec</th>
                <th>Banned until</th>
              </tr>
            </thead>
            <tbody>
              {props.rlRows.map((l) => (
                <Fragment key={l.id}>
                  <tr
                    style={{ cursor: 'pointer' }}
                    onClick={() =>
                      props.setRlExpandedId((cur) => (cur === l.id ? null : l.id))
                    }
                  >
                    <td>{l.id}</td>
                    <td>{l.timeutc}</td>
                    <td>{l.clientip}</td>
                    <td>{l.endpoint}</td>
                    <td>{l.event}</td>
                    <td>
                      {l.countthissec}/{l.limitpersecond}
                    </td>
                    <td className="font-monospace">{l.banneduntilutc}</td>
                  </tr>
                  {props.rlExpandedId === l.id ? (
                    <tr>
                      <td colSpan={7}>
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
