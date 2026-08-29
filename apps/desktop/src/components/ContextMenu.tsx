import type { ReactNode } from 'react'

export type MenuAction = { label: string; icon: ReactNode; danger?: boolean; disabled?: boolean; onClick: () => void }

export function ContextMenu({ x, y, actions, onClose }: { x: number; y: number; actions: MenuAction[]; onClose: () => void }) {
  return <><button className="context-dismiss" onClick={onClose} aria-label="关闭菜单" /><div className="context-menu" style={{ left: Math.min(x, window.innerWidth - 190), top: Math.min(y, window.innerHeight - actions.length * 38 - 18) }}>
    {actions.map(action => <button key={action.label} className={action.danger ? 'is-danger' : ''} disabled={action.disabled} onClick={() => { action.onClick(); onClose() }}>{action.icon}{action.label}</button>)}
  </div></>
}
