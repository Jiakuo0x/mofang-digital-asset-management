import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from 'react'
import type { DragEvent, MouseEvent as ReactMouseEvent } from 'react'
import { Ban, Check, ChevronRight, Copy, Download, FilePlus2, Folder as FolderIcon, FolderInput, FolderOpen, FolderPlus, Grid2X2, HardDrive, KeyRound, List, LockKeyhole, LogOut, Pencil, RefreshCw, Search, Settings, Trash2, Undo2, Upload, UploadCloud, UserRound, X } from 'lucide-react'
import { openExternal, openFile, type MofangApi } from '../api/client'
import { formatBytes } from '../format'
import type { AccountSession, Asset, AssetDetail, Folder, LibraryConfig, StorageSummary, UploadItem } from '../types'
import { AccountsPanel } from './AccountsPanel'
import { AssetItem, FolderItem } from './AssetItems'
import { Brand } from './Brand'
import { ContextMenu, type MenuAction } from './ContextMenu'
import { DetailPanel } from './DetailPanel'
import { Dialog } from './Dialog'
import { FolderTree } from './FolderTree'
import { OperationsPanel } from './OperationsPanel'
import { UploadQueue } from './UploadQueue'

type DialogState =
  | { kind: 'new-folder' }
  | { kind: 'rename-folder'; folder: Folder }
  | { kind: 'move-folder'; folder: Folder }
  | { kind: 'delete-folder'; folder: Folder }
  | { kind: 'permanently-delete-folder'; folder: Folder }
  | { kind: 'restore-folder'; folder: Folder }
  | { kind: 'rename-asset'; asset: Asset }
  | { kind: 'move-asset'; asset: Asset }
  | { kind: 'copy-asset'; asset: Asset }
  | { kind: 'delete-asset'; asset: Asset }
  | { kind: 'permanently-delete-asset'; asset: Asset }
  | { kind: 'restore-asset'; asset: Asset }

type WorkspacePage = 'library' | 'trash' | 'operations' | 'accounts'

const flattenFolders = (folders: Folder[]): Folder[] => folders.flatMap(folder => [folder, ...flattenFolders(folder.children ?? [])])
const findFolder = (folders: Folder[], id: string | null): Folder | null => id ? flattenFolders(folders).find(folder => folder.id === id) ?? null : null
const isFileDrag = (dataTransfer: DataTransfer) => Array.from(dataTransfer.types).includes('Files')
const getDraggedFileCount = (dataTransfer: DataTransfer) => {
  const itemCount = Array.from(dataTransfer.items).filter(item => item.kind === 'file').length
  return itemCount || dataTransfer.files.length
}
const findBreadcrumb = (folders: Folder[], id: string | null) => {
  const flat = flattenFolders(folders)
  const byId = new Map(flat.map(folder => [folder.id, folder]))
  const result: Folder[] = []
  let current = id ? byId.get(id) : undefined
  while (current) { result.unshift(current); current = current.parentId ? byId.get(current.parentId) : undefined }
  return result
}

type DestinationOption = {
  id: string
  name: string
  path: string
  level: number
  canSelect: boolean
  disabledReason?: string
}

function DestinationPicker({ folders, canOperateRoot, value, unavailableReasons, onChange }: { folders: Folder[]; canOperateRoot: boolean; value: string; unavailableReasons: Map<string, string>; onChange: (value: string) => void }) {
  const [query, setQuery] = useState('')
  const options = useMemo(() => {
    const result: DestinationOption[] = []
    const visit = (items: Folder[], ancestors: string[], level: number) => {
      items.forEach(folder => {
        const disabledReason = unavailableReasons.get(folder.id) ?? (!folder.canOperate || folder.navigationOnly ? '无操作权限' : undefined)
        result.push({
          id: folder.id,
          name: folder.name,
          path: ['资产库', ...ancestors, folder.name].join(' / '),
          level,
          canSelect: !disabledReason,
          disabledReason,
        })
        visit(folder.children ?? [], [...ancestors, folder.name], level + 1)
      })
    }
    visit(folders, [], 0)
    return result
  }, [folders, unavailableReasons])
  const normalizedQuery = query.trim().toLocaleLowerCase()
  const visibleOptions = normalizedQuery ? options.filter(option => option.path.toLocaleLowerCase().includes(normalizedQuery)) : options
  const rootVisible = !normalizedQuery || '资产库根目录'.includes(normalizedQuery)
  const selectableCount = options.filter(option => option.canSelect).length + (canOperateRoot ? 1 : 0)

  return <section className="destination-picker" aria-labelledby="destination-picker-title">
    <div className="destination-picker__heading">
      <div><strong id="destination-picker-title">选择目标文件夹</strong><small>按目录层级浏览，完整路径用于区分同名文件夹</small></div>
      <span>{selectableCount} 个可选位置</span>
    </div>
    <label className="destination-search">
      <Search aria-hidden="true" />
      <input autoFocus value={query} onChange={event => setQuery(event.target.value)} placeholder="搜索文件夹或路径" aria-label="搜索目标文件夹" />
      {query ? <button type="button" onClick={() => setQuery('')} aria-label="清空目标文件夹搜索"><X /></button> : null}
    </label>
    <div className="destination-list" role="radiogroup" aria-label="目标文件夹">
      {rootVisible ? <button type="button" role="radio" aria-checked={value === ''} className={`destination-row destination-row--root ${value === '' ? 'is-selected' : ''}`} disabled={!canOperateRoot} onClick={() => onChange('')}>
        <span className="destination-row__icon"><HardDrive /></span>
        <span className="destination-row__copy"><strong>资产库根目录</strong><small>所有顶层文件夹的上一级</small></span>
        {!canOperateRoot ? <span className="destination-row__reason"><LockKeyhole />无操作权限</span> : <span className="destination-row__indicator">{value === '' ? <Check /> : null}</span>}
      </button> : null}
      {visibleOptions.map(option => <button type="button" role="radio" aria-checked={value === option.id} key={option.id} className={`destination-row ${value === option.id ? 'is-selected' : ''} ${option.canSelect ? '' : 'is-disabled'}`} style={{ '--destination-level': normalizedQuery ? 0 : option.level } as React.CSSProperties} disabled={!option.canSelect} title={option.path} onClick={() => onChange(option.id)}>
        <span className="destination-row__icon">{value === option.id ? <FolderOpen /> : <FolderIcon />}</span>
        <span className="destination-row__copy"><strong>{option.name}</strong><small>{option.path}</small></span>
        {option.disabledReason ? <span className="destination-row__reason"><LockKeyhole />{option.disabledReason}</span> : <span className="destination-row__indicator">{value === option.id ? <Check /> : null}</span>}
      </button>)}
      {!rootVisible && visibleOptions.length === 0 ? <div className="destination-list__empty"><Search /><span>没有匹配的文件夹</span><small>试试搜索目录名称中的其他文字</small></div> : null}
    </div>
  </section>
}

function ActionDialog({ state, folders, canOperateRoot, onClose, onSubmit, busy }: { state: DialogState; folders: Folder[]; canOperateRoot: boolean; onClose: () => void; onSubmit: (value?: string | null) => void; busy: boolean }) {
  const targetName = 'folder' in state ? state.folder.name : 'asset' in state ? state.asset.fileName : ''
  const [value, setValue] = useState(state.kind.startsWith('rename') ? targetName : '')
  const hasDestination = state.kind.includes('move') || state.kind.includes('copy') || state.kind.includes('restore')
  const isPermanentDelete = state.kind.startsWith('permanently-delete')
  const isSoftDelete = state.kind.startsWith('delete')
  const isDelete = isSoftDelete || isPermanentDelete
  const title = state.kind === 'new-folder' ? '新建文件夹' : state.kind.startsWith('rename') ? '重命名' : state.kind.startsWith('move') ? '移动到' : state.kind.startsWith('copy') ? '复制到' : isPermanentDelete ? '彻底删除' : isSoftDelete ? '移入回收站' : '恢复到'
  const submitLabel = state.kind === 'new-folder' ? '创建' : state.kind.startsWith('rename') ? '保存' : state.kind.startsWith('copy') ? '复制' : isPermanentDelete ? '彻底删除' : isSoftDelete ? '确认删除' : state.kind.startsWith('restore') ? '恢复' : '移动'
  const unavailableReasons = useMemo(() => {
    const reasons = new Map<string, string>()
    if (state.kind === 'move-folder') {
      reasons.set(state.folder.id, '当前文件夹')
      flattenFolders(state.folder.children ?? []).forEach(folder => reasons.set(folder.id, '当前文件夹的子目录'))
    }
    return reasons
  }, [state])
  const destinationPath = value ? ['资产库', ...findBreadcrumb(folders, value).map(folder => folder.name)].join(' / ') : '资产库根目录'
  const actionHint = state.kind.startsWith('copy') ? '正在复制' : state.kind.startsWith('restore') ? '正在恢复' : '正在移动'
  const disabled = busy || (!hasDestination && !isDelete && !value.trim()) || (hasDestination && !value && !canOperateRoot)
  const actions = <div className="dialog__footer-actions"><button className="button button--ghost" onClick={onClose}>取消</button><button className={`button ${isDelete ? 'button--danger' : 'button--primary'}`} disabled={disabled} onClick={() => onSubmit(hasDestination ? value || null : value.trim() || null)}>{busy ? '处理中…' : submitLabel}</button></div>
  const footer = hasDestination ? <><div className="destination-footer-path"><span>目标位置</span><strong title={destinationPath}>{destinationPath}</strong></div>{actions}</> : actions
  return <Dialog title={title} onClose={onClose} className={hasDestination ? 'dialog--destination' : ''} footer={footer}>
    {isPermanentDelete ? <div className="permanent-delete-warning" role="alert"><Trash2 /><p><strong>此操作无法撤销</strong><span>“{targetName}”{state.kind === 'permanently-delete-folder' ? '及其中的所有文件夹和资产' : ''}将从回收站和对象存储中永久移除。</span></p></div> : isSoftDelete ? <p className="confirm-copy">“{targetName}”将进入回收站，存储文件不会被立即清除。</p> : hasDestination ? <div className="destination-dialog-content">
      <div className="destination-target"><span><FolderInput /></span><div><small>{actionHint}</small><strong title={targetName}>{targetName}</strong></div></div>
      <DestinationPicker folders={folders} canOperateRoot={canOperateRoot} value={value} unavailableReasons={unavailableReasons} onChange={setValue} />
    </div> : <label className="form-field"><span>{state.kind === 'new-folder' ? '文件夹名称' : '新名称'}</span><input autoFocus value={value} onChange={event => setValue(event.target.value)} onKeyDown={event => event.key === 'Enter' && value.trim() && onSubmit(value.trim())} /></label>}
  </Dialog>
}

function ChangePasswordDialog({ api, onClose, onChanged }: { api: MofangApi; onClose: () => void; onChanged: () => void }) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const valid = currentPassword.length > 0 && newPassword.length >= 8 && newPassword === confirmPassword && currentPassword !== newPassword
  const submit = async () => {
    if (!valid) return
    setBusy(true); setError('')
    try { await api.changePassword(currentPassword, newPassword); onChanged() }
    catch (reason) { setError(reason instanceof Error ? reason.message : '密码修改失败。') }
    finally { setBusy(false) }
  }
  return <Dialog title="修改自己的密码" onClose={onClose} footer={<><button className="button button--ghost" onClick={onClose}>取消</button><button className="button button--primary" disabled={busy || !valid} onClick={() => void submit()}>{busy ? '修改中…' : '确认修改'}</button></>}>
    <div className="change-password-form">
      <label className="form-field"><span>当前密码</span><input type="password" autoFocus autoComplete="current-password" value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} /></label>
      <label className="form-field"><span>新密码</span><input type="password" autoComplete="new-password" value={newPassword} onChange={event => setNewPassword(event.target.value)} placeholder="至少 8 个字符" /></label>
      <label className="form-field"><span>确认新密码</span><input type="password" autoComplete="new-password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} /></label>
      {newPassword && confirmPassword && newPassword !== confirmPassword ? <p className="form-error">两次输入的新密码不一致。</p> : null}
      {currentPassword && newPassword && currentPassword === newPassword ? <p className="form-error">新密码不能与当前密码相同。</p> : null}
      {error ? <p className="form-error" role="alert">{error}</p> : null}
    </div>
  </Dialog>
}

type Props = {
  config: LibraryConfig
  api: MofangApi
  account: AccountSession
  appVersion: string
  onConfigure: () => void
  onLogout: () => void
}

export function LibraryScreen({ config, api, account, appVersion, onConfigure, onLogout }: Props) {
  const [folders, setFolders] = useState<Folder[]>([])
  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null)
  const [assets, setAssets] = useState<Asset[]>([])
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const [detail, setDetail] = useState<AssetDetail | null>(null)
  const [summary, setSummary] = useState<StorageSummary>({ assetCount: 0, totalBytes: 0, deletedCount: 0 })
  const [page, setPage] = useState<WorkspacePage>('library')
  const [query, setQuery] = useState('')
  const deferredQuery = useDeferredValue(query)
  const [assetType, setAssetType] = useState('')
  const [sort, setSort] = useState('updated')
  const [view, setView] = useState<'grid' | 'list'>('grid')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [toast, setToast] = useState('')
  const [dialog, setDialog] = useState<DialogState | null>(null)
  const [busy, setBusy] = useState(false)
  const [menu, setMenu] = useState<{ x: number; y: number; actions: MenuAction[] } | null>(null)
  const [uploads, setUploads] = useState<UploadItem[]>([])
  const [draggedFileCount, setDraggedFileCount] = useState<number | null>(null)
  const [changingPassword, setChangingPassword] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const dragDepth = useRef(0)
  const trash = page === 'trash'
  const managementPage = page === 'operations' || page === 'accounts'

  const refresh = useCallback(async () => {
    setLoading(true); setError('')
    const params = new URLSearchParams({ trash: String(trash), sort, pageSize: '200' })
    if (selectedFolderId) params.set('folderId', selectedFolderId)
    if (deferredQuery.trim()) params.set('query', deferredQuery.trim())
    if (assetType) params.set('type', assetType)
    try {
      const [nextFolders, assetPage, nextSummary] = await Promise.all([api.folders(trash), api.assets(params), api.storageSummary()])
      setFolders(nextFolders); setAssets(assetPage.items); setSummary(nextSummary)
    } catch (reason) { setError(reason instanceof Error ? reason.message : '无法读取资产库。') }
    finally { setLoading(false) }
  }, [api, assetType, deferredQuery, selectedFolderId, sort, trash])

  useEffect(() => { const handle = window.setTimeout(() => void refresh(), 0); return () => window.clearTimeout(handle) }, [refresh])
  useEffect(() => {
    if (!selectedAssetId) return
    let ignore = false
    void api.asset(selectedAssetId).then(value => { if (!ignore) setDetail(value) }).catch(() => { if (!ignore) setDetail(null) })
    return () => { ignore = true }
  }, [api, selectedAssetId])

  const notify = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2400) }
  const selectFolder = (id: string | null) => { setSelectedFolderId(id); setPage('library'); setSelectedAssetId(null) }
  const selectPage = (next: WorkspacePage) => { setPage(next); setSelectedFolderId(null); setSelectedAssetId(null) }
  const currentFolder = findFolder(folders, selectedFolderId)
  const currentCanOperate = selectedFolderId ? Boolean(currentFolder?.canOperate) : account.canOperateRoot
  const canDropFiles = page === 'library' && currentCanOperate
  const childFolders = selectedFolderId ? currentFolder?.children ?? [] : folders
  const visibleChildFolders = (deferredQuery.trim() ? childFolders.filter(folder => folder.name.toLocaleLowerCase().includes(deferredQuery.trim().toLocaleLowerCase())) : childFolders)
  const breadcrumbs = findBreadcrumb(folders, selectedFolderId)
  const selectedDetail = detail?.asset.id === selectedAssetId ? detail : null

  const openMenu = (anchor: HTMLElement, actions: MenuAction[]) => { const rect = anchor.getBoundingClientRect(); setMenu({ x: rect.right - 8, y: rect.bottom + 4, actions }) }
  const folderMenu = (folder: Folder, anchor: HTMLElement) => {
    if (!folder.canOperate) return
    openMenu(anchor, trash ? [
      { label: '恢复', icon: <Undo2 />, onClick: () => setDialog({ kind: 'restore-folder', folder }) },
      { label: '彻底删除', icon: <Trash2 />, danger: true, onClick: () => setDialog({ kind: 'permanently-delete-folder', folder }) },
    ] : [
      { label: '重命名', icon: <Pencil />, onClick: () => setDialog({ kind: 'rename-folder', folder }) },
      { label: '移动', icon: <FolderInput />, onClick: () => setDialog({ kind: 'move-folder', folder }) },
      { label: '移入回收站', icon: <Trash2 />, danger: true, onClick: () => setDialog({ kind: 'delete-folder', folder }) },
    ])
  }

  const downloadAsset = async (asset: Asset) => {
    try { const link = await api.downloadLink(asset.id); await openExternal(link.url); notify(`正在下载 ${link.fileName}`) }
    catch (reason) { notify(reason instanceof Error ? reason.message : '无法下载文件。') }
  }

  const openAsset = async (asset: Asset) => {
    try {
      notify(`正在打开 ${asset.fileName}…`)
      const link = await api.downloadLink(asset.id)
      await openFile(link.url, link.fileName)
      notify(`已打开 ${link.fileName}`)
    } catch (reason) { notify(reason instanceof Error ? reason.message : '无法打开文件。') }
  }

  const assetMenu = (asset: Asset, anchor: HTMLElement) => {
    const actions: MenuAction[] = trash ? [] : [{ label: '下载', icon: <Download />, onClick: () => void downloadAsset(asset) }]
    if (asset.canOperate)
    {
      if (trash) actions.push(
        { label: '恢复', icon: <Undo2 />, onClick: () => setDialog({ kind: 'restore-asset', asset }) },
        { label: '彻底删除', icon: <Trash2 />, danger: true, onClick: () => setDialog({ kind: 'permanently-delete-asset', asset }) },
      )
      else actions.push(
        { label: '重命名', icon: <Pencil />, onClick: () => setDialog({ kind: 'rename-asset', asset }) },
        { label: '移动', icon: <FolderInput />, onClick: () => setDialog({ kind: 'move-asset', asset }) },
        { label: '复制', icon: <Copy />, onClick: () => setDialog({ kind: 'copy-asset', asset }) },
        { label: '移入回收站', icon: <Trash2 />, danger: true, onClick: () => setDialog({ kind: 'delete-asset', asset }) },
      )
    }
    openMenu(anchor, actions)
  }

  const workspaceMenu = (event: ReactMouseEvent<HTMLElement>) => {
    if (event.target instanceof Element && event.target.closest('.asset-item')) return
    event.preventDefault()
    const actions: MenuAction[] = []
    if (page === 'library' && currentCanOperate) actions.push({ label: '新建文件夹', icon: <FolderPlus />, onClick: () => setDialog({ kind: 'new-folder' }) })
    actions.push({ label: '刷新', icon: <RefreshCw />, onClick: () => void refresh() })
    setMenu({ x: event.clientX, y: event.clientY, actions })
  }

  const submitDialog = async (value?: string | null) => {
    if (!dialog) return
    setBusy(true)
    try {
      if (dialog.kind === 'new-folder') await api.createFolder(selectedFolderId, value ?? '')
      if (dialog.kind === 'rename-folder') await api.renameFolder(dialog.folder.id, value ?? '')
      if (dialog.kind === 'move-folder') await api.moveFolder(dialog.folder.id, value ?? null)
      if (dialog.kind === 'delete-folder') await api.deleteFolder(dialog.folder.id)
      if (dialog.kind === 'permanently-delete-folder') await api.permanentlyDeleteFolder(dialog.folder.id)
      if (dialog.kind === 'restore-folder') await api.restoreFolder(dialog.folder.id, value ?? null)
      if (dialog.kind === 'rename-asset') await api.renameAsset(dialog.asset.id, value ?? '')
      if (dialog.kind === 'move-asset') await api.moveAsset(dialog.asset.id, value ?? null)
      if (dialog.kind === 'copy-asset') await api.copyAsset(dialog.asset.id, value ?? null)
      if (dialog.kind === 'delete-asset') await api.deleteAsset(dialog.asset.id)
      if (dialog.kind === 'permanently-delete-asset') await api.permanentlyDeleteAsset(dialog.asset.id)
      if (dialog.kind === 'restore-asset') await api.restoreAsset(dialog.asset.id, value ?? null)
      notify(dialog.kind.startsWith('permanently-delete') ? '已彻底删除' : '操作已完成'); setDialog(null); setSelectedAssetId(null); await refresh()
    } catch (reason) { notify(reason instanceof Error ? reason.message : '操作失败') }
    finally { setBusy(false) }
  }

  const uploadFiles = async (files: FileList | File[]) => {
    if (!currentCanOperate) { notify('当前账号没有该目录的操作权限。'); return }
    const list = Array.from(files)
    if (!list.length) return
    const queueItems = list.map(file => ({ id: crypto.randomUUID(), name: file.name, progress: 0, state: 'uploading' as const }))
    setUploads(previous => [...previous.filter(item => item.state === 'uploading'), ...queueItems])
    for (let index = 0; index < list.length; index++) {
      const file = list[index]; const item = queueItems[index]
      try { await api.upload(selectedFolderId, file, progress => setUploads(previous => previous.map(row => row.id === item.id ? { ...row, progress } : row))); setUploads(previous => previous.map(row => row.id === item.id ? { ...row, progress: 100, state: 'done' } : row)) }
      catch (reason) { setUploads(previous => previous.map(row => row.id === item.id ? { ...row, state: 'failed', error: reason instanceof Error ? reason.message : '上传失败' } : row)) }
    }
    await refresh()
  }

  const onDragEnter = (event: DragEvent) => {
    if (!isFileDrag(event.dataTransfer)) return
    event.preventDefault()
    dragDepth.current += 1
    setDraggedFileCount(getDraggedFileCount(event.dataTransfer))
  }
  const onDragOver = (event: DragEvent) => {
    if (!isFileDrag(event.dataTransfer)) return
    event.preventDefault()
    event.dataTransfer.dropEffect = canDropFiles ? 'copy' : 'none'
  }
  const onDragLeave = (event: DragEvent) => {
    if (!isFileDrag(event.dataTransfer)) return
    event.preventDefault()
    dragDepth.current = Math.max(0, dragDepth.current - 1)
    if (dragDepth.current === 0) setDraggedFileCount(null)
  }
  const onDrop = (event: DragEvent) => {
    if (!isFileDrag(event.dataTransfer)) return
    event.preventDefault()
    dragDepth.current = 0
    setDraggedFileCount(null)
    if (canDropFiles) void uploadFiles(event.dataTransfer.files)
  }
  const title = trash ? '回收站' : currentFolder?.name ?? '全部素材'
  const uploadDestination = currentFolder?.name ?? '资产库根目录'
  const managementTitle = page === 'operations' ? '操作日志' : page === 'accounts' ? '账号与权限' : ''

  return <main className={`library-screen ${managementPage ? 'library-screen--management' : ''}`} onDragEnter={onDragEnter} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}>
    <aside className="sidebar">
      <div className="sidebar-brand"><Brand compact /></div>
      <div className="sidebar-label">资产库</div>
      <button className="library-selector"><HardDrive /><span>{config.name}</span><ChevronRight /></button>
      <FolderTree folders={folders} selectedId={selectedFolderId} trash={trash} operations={page === 'operations'} accounts={page === 'accounts'} isMasterAdmin={account.isMasterAdmin} onSelect={selectFolder} onTrash={() => selectPage('trash')} onOperations={() => selectPage('operations')} onAccounts={() => selectPage('accounts')} onFolderMenu={folderMenu} />
      <div className="sidebar-footer">
        <div className="sidebar-account">
          <span className="account-avatar"><UserRound /></span>
          <span className="sidebar-account__identity"><strong>{account.displayName}</strong><small>{account.userName}{account.isMasterAdmin ? ' · 主账号' : ''}</small></span>
          <span className="sidebar-account__actions">
            <button onClick={() => setChangingPassword(true)} aria-label="修改自己的密码" title="修改自己的密码"><KeyRound /></button>
            <button onClick={onLogout} aria-label="退出登录" title="退出登录"><LogOut /></button>
          </span>
        </div>
        <div className="sidebar-meta">
          <span className="connection-status" title={`已连接 · ${config.host}:${config.port}`}><i />服务已连接</span>
          <button className="sidebar-settings" onClick={onConfigure} aria-label="连接设置" title={`连接设置 · ${config.host}:${config.port}`}><Settings /></button>
          <span className="sidebar-version">v{appVersion}</span>
        </div>
      </div>
    </aside>

    <section className="content-shell">
      <header className="topbar">
        <div className="breadcrumbs" aria-label="当前位置">
          {managementPage ? <><span className="breadcrumb-root">系统管理</span><span><ChevronRight /><strong>{managementTitle}</strong></span></> : <><button onClick={() => selectFolder(null)}>资产库</button>{trash ? <span><ChevronRight /><strong>回收站</strong></span> : breadcrumbs.map(folder => <span key={folder.id}><ChevronRight /><button onClick={() => selectFolder(folder.id)}>{folder.name}</button></span>)}</>}
        </div>
        {!managementPage ? <div className="search-box"><Search /><input aria-label="搜索文件和文件夹" value={query} onChange={event => setQuery(event.target.value)} placeholder="搜索文件和文件夹" />{query ? <button onClick={() => setQuery('')} aria-label="清空搜索"><X /></button> : null}</div> : <div className="topbar-spacer" />}
        {!managementPage ? <div className="view-toggle"><button className={view === 'grid' ? 'is-selected' : ''} onClick={() => setView('grid')} aria-label="网格视图"><Grid2X2 /></button><button className={view === 'list' ? 'is-selected' : ''} onClick={() => setView('list')} aria-label="列表视图"><List /></button></div> : null}
        {page === 'library' && currentCanOperate ? <div className="topbar-actions" aria-label="文件操作"><button className="button button--secondary topbar-action" onClick={() => setDialog({ kind: 'new-folder' })}><FolderPlus />新建文件夹</button><button className="button button--primary topbar-action topbar-action--primary" onClick={() => fileInput.current?.click()}><Upload />上传</button></div> : null}
        <input ref={fileInput} type="file" multiple hidden onChange={event => { if (event.target.files) void uploadFiles(event.target.files); event.target.value = '' }} />
        {!managementPage ? <span className="topbar-drag-space" aria-hidden="true" /> : null}
      </header>

      {page === 'operations' ? <OperationsPanel api={api} folders={folders} /> : page === 'accounts' ? <AccountsPanel api={api} folders={folders} currentAccountId={account.id} /> : <>
        <div className="workspace-header"><div><h1>{title}</h1>{draggedFileCount !== null ? <div className={`file-drop-inline ${canDropFiles ? 'is-ready' : 'is-blocked'}`} role="status" aria-live="polite"><span>{canDropFiles ? <UploadCloud /> : <Ban />}</span><strong>{canDropFiles ? '松开以上传' : '当前目录不可上传'}</strong>{canDropFiles ? <small>{draggedFileCount > 0 ? `${draggedFileCount} 个文件 · ` : ''}{uploadDestination}</small> : null}</div> : <span>{assets.length + visibleChildFolders.length} 个项目</span>}</div><div className="filters"><select value={assetType} onChange={event => setAssetType(event.target.value)}><option value="">全部类型</option><option value="Image">图片</option><option value="Video">视频</option><option value="Audio">音频</option><option value="Document">文档</option><option value="Model3D">3D 模型</option><option value="ProjectFile">工程文件</option><option value="Archive">压缩包</option><option value="Other">其他</option></select><select value={sort} onChange={event => setSort(event.target.value)}><option value="updated">最近修改</option><option value="created">创建时间</option><option value="name">名称</option><option value="size">文件大小</option></select><button className="icon-button" onClick={() => void refresh()} aria-label="刷新"><RefreshCw /></button></div></div>
        <section className={`asset-workspace view-${view} ${draggedFileCount !== null ? canDropFiles ? 'is-file-dragging' : 'is-file-dragging is-drop-blocked' : ''}`} onContextMenu={workspaceMenu}>
          {loading ? <div className="empty-state"><RefreshCw className="spin" /><p>正在读取资产库…</p></div> : error ? <div className="empty-state empty-state--error"><HardDrive /><p>{error}</p><button className="button button--secondary" onClick={() => void refresh()}>重试</button></div> : visibleChildFolders.length === 0 && assets.length === 0 ? <div className="empty-state"><FilePlus2 /><p>{trash ? '回收站是空的' : deferredQuery.trim() ? '没有匹配的资产或文件夹' : '这里还没有资产'}</p>{page === 'library' && currentCanOperate && !deferredQuery.trim() ? <span>拖入文件，或使用右上角“上传”开始</span> : null}</div> : <div className="asset-collection">
            {visibleChildFolders.map(folder => <FolderItem key={folder.id} folder={folder} view={view} onOpen={() => { if (folder.canView) selectFolder(folder.id) }} onMenu={anchor => folderMenu(folder, anchor)} />)}
            {assets.map(asset => <AssetItem key={asset.id} asset={asset} api={api} view={view} selected={selectedAssetId === asset.id} onSelect={() => setSelectedAssetId(asset.id)} onOpen={trash ? undefined : () => void openAsset(asset)} onMenu={anchor => assetMenu(asset, anchor)} />)}
          </div>}
        </section>
      </>}
    </section>

    {!managementPage ? <DetailPanel detail={selectedDetail} api={api} onClose={() => setSelectedAssetId(null)} onDownload={asset => void downloadAsset(asset)} /> : null}
    <footer className="statusbar"><span><HardDrive />{summary.assetCount} 个资产 · {formatBytes(summary.totalBytes)}</span><span className="statusbar__upload"><Upload />上传队列 {uploads.filter(item => item.state === 'uploading').length} 个任务</span></footer>
    <UploadQueue items={uploads} onClear={() => setUploads(previous => previous.filter(item => item.state === 'uploading'))} />
    {menu ? <ContextMenu {...menu} onClose={() => setMenu(null)} /> : null}
    {dialog ? <ActionDialog state={dialog} folders={trash ? [] : folders} canOperateRoot={account.canOperateRoot} onClose={() => setDialog(null)} onSubmit={value => void submitDialog(value)} busy={busy} /> : null}
    {changingPassword ? <ChangePasswordDialog api={api} onClose={() => setChangingPassword(false)} onChanged={() => { setChangingPassword(false); notify('密码已修改，登录令牌已安全更新') }} /> : null}
    {toast ? <div className="toast" role="status">{toast}</div> : null}
  </main>
}
