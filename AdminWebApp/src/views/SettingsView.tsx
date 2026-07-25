export function SettingsView(props: {
  busy: boolean
  adminKey: string
  setAdminKey: (v: string) => void
}) {
  return (
    <div className="card">
      <div className="card-body">
        <div className="row">
          <div className="col-12 col-lg-6">
            <label className="form-label">Admin Key</label>
            <input
              className="form-control"
              type="password"
              value={props.adminKey}
              disabled={props.busy}
              onChange={(e) => props.setAdminKey(e.target.value)}
              placeholder="X-Admin-Key"
              autoComplete="off"
              spellCheck={false}
            />
            <div className="form-text">
              Stored locally in your browser (localStorage). Required for /admin/* requests.
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
