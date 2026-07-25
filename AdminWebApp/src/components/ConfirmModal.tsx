import { useEffect } from 'react'

export function ConfirmModal(props: {
  open: boolean
  title: string
  message: string
  confirmText: string
  busy: boolean
  onCancel: () => void
  onConfirm: () => void
}) {
  useEffect(() => {
    if (!props.open) return

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') props.onCancel()
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [props.open, props.onCancel])

  if (!props.open) return null

  return (
    <>
      <div className="modal fade show" role="dialog" aria-modal="true" style={{ display: 'block' }}>
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{props.title}</h5>
              <button
                type="button"
                className="btn-close"
                aria-label="Close"
                disabled={props.busy}
                onClick={props.onCancel}
              />
            </div>
            <div className="modal-body">
              <p className="mb-0">{props.message}</p>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" disabled={props.busy} onClick={props.onCancel}>
                Cancel
              </button>
              <button type="button" className="btn btn-danger" disabled={props.busy} onClick={props.onConfirm}>
                {props.confirmText}
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" onClick={props.busy ? undefined : props.onCancel} />
    </>
  )
}
