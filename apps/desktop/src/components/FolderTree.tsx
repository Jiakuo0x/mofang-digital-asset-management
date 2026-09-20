import { ChevronDown, ChevronRight, FileClock, Folder as FolderIcon, FolderOpen, MoreHorizontal, Trash2, Users } from 'lucide-react'
import { useState } from 'react'
import type { Folder } from '../types'

type Props = {
  folders: Folder[]
  selectedId: string | null
  revealSequence: number
  trash: boolean
  operations: boolean
  accounts: boolean
  isMasterAdmin: boolean
  onSelect: (id: string | null) => void
  onTrash: () => void
  onOperations: () => void
  onAccounts: () => void
  onFolderMenu: (folder: Folder, anchor: HTMLElement) => void
}

const containsFolder = (folder: Folder, id: string | null): boolean => folder.id === id || Boolean(folder.children?.some(child => containsFolder(child, id)))

function FolderBranch({ folder, level, selectedId, revealSequence, onSelect, onMenu }: { folder: Folder; level: number; selectedId: string | null; revealSequence: number; onSelect: (id: string) => void; onMenu: (folder: Folder, anchor: HTMLElement) => void }) {
  const [expansion, setExpansion] = useState({ open: true, selectedId, revealSequence })
  const containsSelection = containsFolder(folder, selectedId)
  const open = containsSelection && (expansion.selectedId !== selectedId || expansion.revealSequence !== revealSequence) ? true : expansion.open
  const hasChildren = Boolean(folder.children?.length)
  return <>
    <div className={`tree-row ${selectedId === folder.id ? 'is-selected' : ''}`} style={{ '--tree-level': level } as React.CSSProperties} onContextMenu={event => { event.preventDefault(); if (folder.canView) onMenu(folder, event.currentTarget) }}>
      <button className="tree-row__chevron" disabled={!hasChildren} onClick={() => setExpansion({ open: !open, selectedId, revealSequence })} aria-label={open ? '折叠' : '展开'}>
        {hasChildren ? open ? <ChevronDown /> : <ChevronRight /> : null}
      </button>
      <button className="tree-row__label" disabled={!folder.canView} title={folder.navigationOnly ? '仅用于定位已授权的子目录' : undefined} onClick={() => onSelect(folder.id)}>{selectedId === folder.id ? <FolderOpen /> : <FolderIcon />}<span>{folder.name}</span></button>
      {folder.canView ? <button className="tree-row__menu" onClick={event => onMenu(folder, event.currentTarget)} aria-label={`${folder.name} 操作`}><MoreHorizontal /></button> : null}
    </div>
    {open ? folder.children?.map(child => <FolderBranch key={child.id} folder={child} level={level + 1} selectedId={selectedId} revealSequence={revealSequence} onSelect={onSelect} onMenu={onMenu} />) : null}
  </>
}

export function FolderTree({ folders, selectedId, revealSequence, trash, operations, accounts, isMasterAdmin, onSelect, onTrash, onOperations, onAccounts, onFolderMenu }: Props) {
  return (
    <nav className="folder-navigation" aria-label="资产目录">
      <div className="sidebar-label">文件夹</div>
      <button className={`sidebar-item ${!selectedId && !trash && !operations && !accounts ? 'is-selected' : ''}`} onClick={() => onSelect(null)}><FolderOpen /><span>全部素材</span></button>
      <div className="folder-tree">{folders.map(folder => <FolderBranch key={folder.id} folder={folder} level={0} selectedId={selectedId} revealSequence={revealSequence} onSelect={onSelect} onMenu={onFolderMenu} />)}</div>
      <div className="sidebar-divider" />
      <button className={`sidebar-item ${trash ? 'is-selected' : ''}`} onClick={onTrash}><Trash2 /><span>回收站</span></button>
      <button className={`sidebar-item ${operations ? 'is-selected' : ''}`} onClick={onOperations}><FileClock /><span>操作日志</span></button>
      {isMasterAdmin ? <button className={`sidebar-item ${accounts ? 'is-selected' : ''}`} onClick={onAccounts}><Users /><span>账户与权限</span></button> : null}
    </nav>
  )
}
