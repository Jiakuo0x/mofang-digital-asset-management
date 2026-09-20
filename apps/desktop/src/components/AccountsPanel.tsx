import { useCallback, useEffect, useMemo, useState } from 'react'
import { ChevronRight, Folder as FolderIcon, FolderOpen, KeyRound, LoaderCircle, Plus, Save, Shield, Trash2, UserRound, Users } from 'lucide-react'
import type { MofangApi } from '../api/client'
import type { Account, DirectoryPermission, Folder } from '../types'
import { buildPermissionRows, changeDirectoryPermission, permissionKey, type PermissionRow } from '../directoryPermissions'
import { Dialog } from './Dialog'

function PermissionTable({ rows, busy, onChange }: {
  rows: PermissionRow[]
  busy: boolean
  onChange: (folderId: string | null, field: 'canView' | 'canOperate', checked: boolean) => void
}) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set())
  const visibleRows = rows.filter(row => row.ancestors.every(id => expanded.has(permissionKey(id))))
  const toggle = (key: string) => setExpanded(previous => {
    const next = new Set(previous)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    return next
  })

  return <div className="permission-table">
    <div className="permission-row permission-row--head"><span>目录</span><span>可见</span><span>可操作</span></div>
    {visibleRows.map(row => {
      const key = permissionKey(row.folderId)
      const isExpanded = expanded.has(key)
      const icon = isExpanded && row.hasChildren ? <FolderOpen /> : <FolderIcon />
      return <div className="permission-row" key={key}>
        <span className="permission-directory" style={{ '--permission-level': row.level } as React.CSSProperties}>
          {row.hasChildren ? <button type="button" className="permission-directory-toggle" aria-label={`${isExpanded ? '折叠' : '展开'}${row.name}`} aria-expanded={isExpanded} onClick={() => toggle(key)}>
            <ChevronRight className={`permission-chevron${isExpanded ? ' is-expanded' : ''}`} />{icon}<span>{row.name}</span>
          </button> : <span className="permission-directory-leaf"><span className="permission-chevron-spacer" aria-hidden="true" />{icon}<span>{row.name}</span></span>}
        </span>
        {(['canView', 'canOperate'] as const).map(field => {
          const inherited = field === 'canView' ? row.inheritedView : row.inheritedOperate
          const label = field === 'canView' ? '可见' : '可操作'
          return <label key={field} title={inherited ? '继承上级目录权限，请在上级目录调整' : `${row.name} · ${label}`}><input type="checkbox" aria-label={`${row.name} · ${label}`} checked={row[field]} disabled={busy || inherited} onChange={event => onChange(row.folderId, field, event.target.checked)} />{inherited ? <small>继承</small> : null}</label>
        })}
      </div>
    })}
  </div>
}

export function AccountsPanel({ api, folders, currentAccountId, onAccountUpdated }: { api: MofangApi; folders: Folder[]; currentAccountId: string; onAccountUpdated?: (account: Account) => void }) {
  const [accounts, setAccounts] = useState<Account[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [permissions, setPermissions] = useState<Map<string, DirectoryPermission>>(new Map())
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [creating, setCreating] = useState(false)
  const [newUserName, setNewUserName] = useState('')
  const [newDisplayName, setNewDisplayName] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [userName, setUserName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [resetPassword, setResetPassword] = useState('')
  const [permissionsAccountId, setPermissionsAccountId] = useState<string | null>(null)
  const [permissionError, setPermissionError] = useState('')
  const [permissionReload, setPermissionReload] = useState(0)
  const [deleteTarget, setDeleteTarget] = useState<Account | null>(null)
  const [deleteError, setDeleteError] = useState('')
  const folderRows = useMemo(() => buildPermissionRows(folders, permissions), [folders, permissions])
  const selected = accounts.find(account => account.id === selectedId) ?? null
  const selectedIsMaster = selected?.isMasterAdmin
  const permissionsReady = selected !== null && permissionsAccountId === selected.id

  const loadAccounts = useCallback(async () => {
    setLoading(true)
    try {
      const rows = await api.accounts()
      setAccounts(rows)
      setSelectedId(previous => previous && rows.some(x => x.id === previous) ? previous : rows.find(x => !x.isMasterAdmin)?.id ?? rows[0]?.id ?? null)
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : '无法读取账户。')
    } finally {
      setLoading(false)
    }
  }, [api])

  useEffect(() => { const handle = window.setTimeout(() => void loadAccounts(), 0); return () => window.clearTimeout(handle) }, [loadAccounts])
  useEffect(() => {
    if (!selected) return
    const handle = window.setTimeout(() => {
      setDisplayName(selected.displayName)
      setUserName(selected.userName)
      setEnabled(selected.isEnabled)
      setResetPassword('')
    }, 0)
    return () => window.clearTimeout(handle)
  }, [selected])

  useEffect(() => {
    if (!selectedId || selectedIsMaster === undefined) return
    let ignore = false
    const handle = window.setTimeout(() => {
      setPermissionsAccountId(null)
      setPermissionError('')
      setPermissions(new Map())
      if (selectedIsMaster) return
      void api.accountPermissions(selectedId).then(value => {
        if (!ignore) {
          setPermissions(new Map(value.permissions.map(permission => [permissionKey(permission.folderId), permission])))
          setPermissionsAccountId(selectedId)
        }
      }).catch(reason => { if (!ignore) setPermissionError(reason instanceof Error ? reason.message : '无法读取权限配置。') })
    }, 0)
    return () => { ignore = true; window.clearTimeout(handle) }
  }, [api, selectedId, selectedIsMaster, permissionReload])

  const notify = (value: string) => { setMessage(value); window.setTimeout(() => setMessage(''), 2600) }

  const create = async () => {
    setBusy(true)
    try {
      const account = await api.createAccount(newUserName.trim(), newDisplayName.trim(), newPassword)
      setNewUserName(''); setNewDisplayName(''); setNewPassword(''); setCreating(false)
      await loadAccounts(); setSelectedId(account.id); notify('账户已创建')
    } catch (reason) { notify(reason instanceof Error ? reason.message : '创建账户失败。') }
    finally { setBusy(false) }
  }

  const saveAccount = async () => {
    if (!selected) return
    setBusy(true)
    try { const account = await api.updateAccount(selected.id, userName.trim(), displayName.trim(), enabled); await loadAccounts(); onAccountUpdated?.(account); notify('账户信息已保存') }
    catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败。') }
    finally { setBusy(false) }
  }

  const changePermission = (folderId: string | null, field: 'canView' | 'canOperate', checked: boolean) => {
    setPermissions(previous => changeDirectoryPermission(previous, folderRows, folderId, field, checked))
  }

  const savePermissions = async () => {
    if (!selected || !permissionsReady) return
    setBusy(true)
    try {
      const result = await api.replaceAccountPermissions(selected.id, [...permissions.values()])
      setPermissions(new Map(result.permissions.map(permission => [permissionKey(permission.folderId), permission])))
      notify('目录权限已保存')
    }
    catch (reason) { notify(reason instanceof Error ? reason.message : '权限保存失败。') }
    finally { setBusy(false) }
  }

  const reset = async () => {
    if (!selected || !resetPassword) return
    setBusy(true)
    try { await api.resetPassword(selected.id, resetPassword); setResetPassword(''); notify('密码已重置') }
    catch (reason) { notify(reason instanceof Error ? reason.message : '密码重置失败。') }
    finally { setBusy(false) }
  }

  const deleteAccount = async () => {
    if (!deleteTarget || deleteTarget.isMasterAdmin || deleteTarget.id === currentAccountId) return
    setBusy(true); setDeleteError('')
    try {
      await api.deleteAccount(deleteTarget.id)
      setAccounts(previous => previous.filter(account => account.id !== deleteTarget.id))
      setSelectedId(null); setPermissionsAccountId(null); setDeleteTarget(null)
      await loadAccounts(); notify('账户已删除')
    } catch (reason) { setDeleteError(reason instanceof Error ? reason.message : '删除账户失败。') }
    finally { setBusy(false) }
  }

  return <section className="accounts-panel">
    <div className="operations-heading"><div><h1>账户与目录权限</h1><p>主账号可创建、修改和删除账户，并配置目录可见与可操作权限</p></div><Users /></div>
    <div className="accounts-layout">
      <aside className="account-list">
        <button className="button button--primary account-create-button" disabled={busy} onClick={() => setCreating(value => !value)}><Plus />新建账户</button>
        {creating ? <div className="account-create-form">
          <label><span>账号</span><input value={newUserName} onChange={event => setNewUserName(event.target.value)} /></label>
          <label><span>显示名称</span><input value={newDisplayName} onChange={event => setNewDisplayName(event.target.value)} /></label>
          <label><span>初始密码</span><input type="password" value={newPassword} onChange={event => setNewPassword(event.target.value)} placeholder="至少 8 个字符" /></label>
          <button className="button button--secondary" disabled={busy || !newUserName.trim() || !newDisplayName.trim() || newPassword.length < 8} onClick={() => void create()}>{busy ? <LoaderCircle className="spin" /> : <Plus />}确认创建</button>
        </div> : null}
        {loading ? <div className="account-list-empty"><LoaderCircle className="spin" />正在读取…</div> : accounts.map(account => <button key={account.id} disabled={busy} className={`account-list-item ${selectedId === account.id ? 'is-selected' : ''}`} onClick={() => { if (selectedId !== account.id) setPermissionsAccountId(null); setSelectedId(account.id) }}>
          <span className="account-avatar">{account.isMasterAdmin ? <Shield /> : <UserRound />}</span><span><strong>{account.displayName}</strong><small>{account.userName} · {account.isEnabled ? '已启用' : '已停用'}</small></span>
        </button>)}
      </aside>
      {selected ? <div className="account-editor">
        <section className="account-section"><div className="account-section-heading"><div><h2>账户信息</h2><p>{selected.isMasterAdmin ? '主账号管理员' : '普通账户'} · 创建于 {new Date(selected.createdAt).toLocaleString('zh-CN')}</p></div><div className="account-editor-actions">{!selected.isMasterAdmin && selected.id !== currentAccountId ? <button className="button account-delete-button" disabled={busy} onClick={() => { setDeleteError(''); setDeleteTarget(selected) }}><Trash2 />删除账户</button> : null}<button className="button button--secondary" disabled={busy || userName.trim().length < 3 || !displayName.trim()} onClick={() => void saveAccount()}><Save />保存修改</button></div></div>
          <div className="account-fields"><label><span>账号</span><input value={userName} maxLength={50} disabled={busy} onChange={event => setUserName(event.target.value)} /></label><label><span>显示名称</span><input value={displayName} maxLength={50} disabled={busy} onChange={event => setDisplayName(event.target.value)} /></label><label className="toggle-field"><input type="checkbox" checked={enabled} disabled={busy || selected.isMasterAdmin || selected.id === currentAccountId} onChange={event => setEnabled(event.target.checked)} /><span>允许登录</span></label></div>
        </section>
        {selected.id === currentAccountId ? <section className="account-section"><div className="master-permission"><KeyRound />当前登录账号请通过左下角“修改自己的密码”入口验证旧密码后修改。</div></section> : <section className="account-section"><div className="account-section-heading"><div><h2>重置密码</h2><p>主账号可为其他账号设置新密码；旧刷新令牌将失效</p></div></div><div className="password-reset-row"><input type="password" value={resetPassword} onChange={event => setResetPassword(event.target.value)} placeholder="输入至少 8 位的新密码" /><button className="button button--secondary" disabled={busy || resetPassword.length < 8} onClick={() => void reset()}><KeyRound />重置</button></div></section>}
        <section className="account-section permission-section"><div className="account-section-heading"><div><h2>目录权限</h2><p>勾选父目录会自动勾选全部子目录；取消时同步取消子目录对应权限。</p><p>“可操作”包含“可见”；标记“继承”的权限请在上级目录调整。</p></div>{!selected.isMasterAdmin ? <button className="button button--primary" disabled={busy || !permissionsReady} onClick={() => void savePermissions()}><Save />保存权限</button> : null}</div>
          {selected.isMasterAdmin ? <div className="master-permission"><Shield />主账号默认拥有全部目录的可见与可操作权限。</div> : !permissionsReady ? <div className="account-list-empty" role="status">{permissionError ? <>{permissionError}<button className="button button--secondary" onClick={() => setPermissionReload(value => value + 1)}>重试</button></> : <><LoaderCircle className="spin" />正在读取目录权限…</>}</div> : <PermissionTable key={selected.id} rows={folderRows} busy={busy} onChange={changePermission} />}
        </section>
      </div> : <div className="empty-state"><Users /><p>请选择一个账户</p></div>}
    </div>
    {deleteTarget ? <Dialog title="删除账户" onClose={() => { if (!busy) setDeleteTarget(null) }} footer={<><button className="button button--secondary" disabled={busy} onClick={() => setDeleteTarget(null)}>取消</button><button className="button button--danger" disabled={busy} onClick={() => void deleteAccount()}>{busy ? <LoaderCircle className="spin" /> : <Trash2 />}确认删除</button></>}>
      <p>确定删除账户 <strong>{deleteTarget.displayName}（{deleteTarget.userName}）</strong>？</p>
      <p>删除后该账户将无法登录，目录授权一并移除。已有资产及历史操作日志保留，此操作不可撤销。</p>
      {deleteError ? <p className="form-error" role="alert">{deleteError}</p> : null}
    </Dialog> : null}
    {message ? <div className="toast" role="status">{message}</div> : null}
  </section>
}
