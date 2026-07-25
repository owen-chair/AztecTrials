import { useEffect, useRef, useState } from 'react'

export function EndpointMultiSelect(props: {
  label: string
  groups: Array<{ label: string; endpoints: string[] }>
  selected: string[]
  setSelected: (v: string[] | ((cur: string[]) => string[])) => void
  disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const dropdownRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open) return

    function onDocClick(e: MouseEvent) {
      const el = dropdownRef.current
      if (!el) return
      if (e.target instanceof Node && el.contains(e.target)) return
      setOpen(false)
    }

    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [open])

  function isSelected(ep: string) {
    return props.selected.includes(ep)
  }

  function toggle(ep: string) {
    props.setSelected((cur) => {
      const has = cur.includes(ep)
      if (has) {
        if (cur.length === 1) return cur
        return cur.filter((x) => x !== ep)
      }
      return [...cur, ep]
    })
  }

  return (
    <div className="dropdown" ref={dropdownRef}>
      <label className="form-label">{props.label}</label>
      <button
        type="button"
        className="btn btn-outline-secondary dropdown-toggle"
        aria-expanded={open}
        disabled={props.disabled}
        onClick={() => setOpen((v) => !v)}
      >
        {props.selected.length} selected
      </button>
      <div className={open ? 'dropdown-menu show' : 'dropdown-menu'}>
        {props.groups.flatMap((g, gi) => {
          const items = [
            <h6 key={`h-${g.label}`} className="dropdown-header">
              {g.label}
            </h6>,
            ...g.endpoints.map((ep) => (
              <label key={ep} className="dropdown-item">
                <div className="form-check">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={isSelected(ep)}
                    onChange={() => toggle(ep)}
                    disabled={props.disabled}
                  />
                  <span className="form-check-label font-monospace">{ep}</span>
                </div>
              </label>
            )),
          ]

          if (gi < props.groups.length - 1) {
            items.push(<div key={`d-${g.label}`} className="dropdown-divider" />)
          }

          return items
        })}
      </div>
    </div>
  )
}
