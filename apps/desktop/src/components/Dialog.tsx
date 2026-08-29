import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'

type DialogProps = {
  title: string
  children: ReactNode
  onClose: () => void
  footer?: ReactNode
  wide?: boolean
  className?: string
}

let activeDialogCount = 0

const updateNativeModalState = (active: boolean) => {
  activeDialogCount = Math.max(0, activeDialogCount + (active ? 1 : -1))
  window.mofangDesktop?.setModalActive(activeDialogCount > 0)
}

export function Dialog({ title, children, onClose, footer, wide = false, className = '' }: DialogProps) {
  useEffect(() => {
    updateNativeModalState(true)
    return () => updateNativeModalState(false)
  }, [])

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}>
      <section className={`dialog ${wide ? 'dialog--wide' : ''} ${className}`.trim()} role="dialog" aria-modal="true" aria-label={title}>
        <header className="dialog__header">
          <h2>{title}</h2>
          <button className="icon-button" onClick={onClose} aria-label="关闭"><X size={18} /></button>
        </header>
        <div className="dialog__body">{children}</div>
        {footer ? <footer className="dialog__footer">{footer}</footer> : null}
      </section>
    </div>
  )
}
