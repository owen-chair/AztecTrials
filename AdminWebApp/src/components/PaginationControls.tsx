import { useMemo } from 'react'

function clamp(n: number, min: number, max: number) {
  return Math.max(min, Math.min(max, n))
}

export function PaginationControls(props: {
  busy: boolean
  page: number
  pageSize: number
  total?: number
  onPageChange: (nextPage: number) => void
  onPageSizeChange: (nextPageSize: number) => void
  onRefresh: () => void
}) {
  const total = props.total ?? 0
  const totalPages = useMemo(() => {
    return props.pageSize > 0 ? Math.max(1, Math.ceil(total / props.pageSize)) : 1
  }, [props.pageSize, total])

  const curPage = clamp(props.page, 0, totalPages - 1)
  const canPrev = curPage > 0
  const canNext = curPage < totalPages - 1

  return (
    <div className="d-flex flex-wrap align-items-center justify-content-between">
      <div className="d-flex flex-wrap align-items-center">
        <div className="btn-group" role="group" aria-label="Pagination">
          <button
            type="button"
            className="btn btn-outline-secondary"
            disabled={props.busy || !canPrev}
            onClick={() => props.onPageChange(0)}
          >
            First
          </button>
          <button
            type="button"
            className="btn btn-outline-secondary"
            disabled={props.busy || !canPrev}
            onClick={() => props.onPageChange(curPage - 1)}
          >
            Prev
          </button>
          <button
            type="button"
            className="btn btn-outline-secondary"
            disabled={props.busy || !canNext}
            onClick={() => props.onPageChange(curPage + 1)}
          >
            Next
          </button>
          <button
            type="button"
            className="btn btn-outline-secondary"
            disabled={props.busy || !canNext}
            onClick={() => props.onPageChange(totalPages - 1)}
          >
            Last
          </button>
        </div>

        <div className="text-body-secondary small ms-2">
          Page <span className="fw-semibold">{curPage + 1}</span> of{' '}
          <span className="fw-semibold">{totalPages}</span>
          {total ? (
            <>
              {' '}
              • Total <span className="fw-semibold">{total}</span>
            </>
          ) : null}
        </div>
      </div>

      <div className="d-flex flex-wrap align-items-center">
        <label className="d-flex align-items-center mb-0">
          <span className="text-body-secondary small me-2">Page size</span>
          <select
            className="form-select form-select-sm"
            value={props.pageSize}
            onChange={(e) => props.onPageSizeChange(Number(e.target.value))}
            disabled={props.busy}
          >
            {[25, 50, 100, 200, 500].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          className="btn btn-outline-primary btn-sm ms-2"
          disabled={props.busy}
          onClick={props.onRefresh}
        >
          Refresh
        </button>
      </div>
    </div>
  )
}
