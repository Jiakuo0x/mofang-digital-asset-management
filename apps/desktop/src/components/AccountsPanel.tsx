import { useCallback, useEffect, useMemo, useState } from 'react'
import { KeyRound, LoaderCircle, Plus, Save, Shield, UserRound, Users } from 'lucide-react'
import type { MofangApi } from '../api/client'
import type { Account, DirectoryPermission, Folder } from '../types'

const flattenFolders = (folders: Folder[], level = 0): Array<{ folder: Folder; level: number }> => folders.flatMap(folder => [{ folder, level }, ...flattenFolders(folder.children ?? [], level + 1)])
const permissionKey = (folderId: string | null) => folderId ?? 'root'

export function AccountsPanel({ api, folders, currentAccountId }: { api: MofangApi; folders: Folder[]; currentAccountId: string }) {
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
  const [displayName, setDisplayName] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [resetPassword, setResetPassword] = useState('')
  const folderRows = useMemo(() => flattenFolders(folders), [folders])
  const selected = accounts.find(account => account.id === selectedId) ?? null

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
    let ignore = false
    const handle = window.setTimeout(() => {
      setDisplayName(selected.displayName)
      setEnabled(selected.isEnabled)
      setResetPassword('')
      if (selected.isMasterAdmin) { setPermissions(new Map()); return }
      void api.accountPermissions(selected.id).then(value => {
        if (!ignore) setPermissions(new Map(value.permissions.map(permission => [permissionKey(permission.folderId), permission])))
      }).catch(reason => { if (!ignore) setMessage(reason instanceof Error ? reason.message : '无法读取权限配置。') })
    }, 0)
    return () => { ignore = true; window.clearTimeout(handle) }
  }, [api, selected])

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
    try { await api.updateAccount(selected.id, displayName.trim(), enabled); await loadAccounts(); notify('账户信息已保存') }
    catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败。') }
    finally { setBusy(false) }
  }

  const changePermission = (folderId: string | null, field: 'canView' | 'canOperate', checked: boolean) => {
    setPermissions(previous => {
      const next = new Map(previous)
      const key = permissionKey(folderId)
      const current = next.get(key) ?? { folderId, canView: false, canOperate: false }
      const value = field === 'canOperate'
        ? { ...current, canOperate: checked, canView: checked ? true : current.canView }
        : { ...current, canView: checked, canOperate: checked ? current.canOperate : false }
      if (!value.canView && !value.canOperate) next.delete(key)
      else next.set(key, value)
      return next
    })
  }

  const savePermissions = async () => {
    if (!selected) return
    setBusy(true)
    try { await api.replaceAccountPermissions(selected.id, [...permissions.values()]); await loadAccounts(); notify('目录权限已保存') }
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

  return <section className="accounts-panel">
    <div className="operations-heading"><div><h1>账户与目录权限</h1><p>主账号可创建账户，并配置目录可见与可操作权限</p></div><Users /></div>
    <div className="accounts-layout">
      <aside className="account-list">
        <button className="button button--primary account-create-button" onClick={() => setCreating(value => !value)}><Plus />新建账户</button>
        {creating ? <div className="account-create-form">
          <label><span>账号</span><input value={newUserName} onChange={event => setNewUserName(event.target.value)} /></label>
          <label><span>显示名称</span><input value={newDisplayName} onChange={event => setNewDisplayName(event.target.value)} /></label>
          <label><span>初始密码</span><input type="password" value={newPassword} onChange={event => setNewPassword(event.target.value)} placeholder="至少 8 个字符" /></label>
          <button className="button button--secondary" disabled={busy || !newUserName.trim() || !newDisplayName.trim() || newPassword.length < 8} onClick={() => void create()}>{busy ? <LoaderCircle className="spin" /> : <Plus />}确认创建</button>
        </div> : null}
        {loading ? <div className="account-list-empty"><LoaderCircle className="spin" />正在读取…</div> : accounts.map(account => <button key={account.id} className={`account-list-item ${selectedId === account.id ? 'is-selected' : ''}`} onClick={() => setSelectedId(account.id)}>
          <span className="account-avatar">{account.isMasterAdmin ? <Shield /> : <UserRound />}</span><span><strong>{account.displayName}</strong><small>{account.userName} · {account.isEnabled ? '已启用' : '已停用'}</small></span>
        </button>)}
      </aside>
      {selected ? <div className="account-editor">
        <section className="account-section"><div className="account-section-heading"><div><h2>账户信息</h2><p>{selected.isMasterAdmin ? '主账号管理员' : '普通账户'} · 创建于 {new Date(selected.createdAt).toLocaleString('zh-CN')}</p></div><button className="button button--secondary" disabled={busy} onClick={() => void saveAccount()}><Save />保存</button></div>
          <div className="account-fields"><label><span>账号</span><input value={selected.userName} disabled /></label><label><span>显示名称</span><input value={displayName} onChange={event => setDisplayName(event.target.value)} /></label><label className="toggle-field"><input type="checkbox" checked={enabled} disabled={selected.isMasterAdmin || selected.id === currentAccountId} onChange={event => setEnabled(event.target.checked)} /><span>允许登录</span></label></div>
        </section>
        {selected.id === currentAccountId ? <section className="account-section"><div className="master-permission"><KeyRound />当前登录账号请通过左下角“修改自己的密码”入口验证旧密码后修改。</div></section> : <section className="account-section"><div className="account-section-heading"><div><h2>重置密码</h2><p>主账号可为其他账号设置新密码；旧刷新令牌将失效</p></div></div><div className="password-reset-row"><input type="password" value={resetPassword} onChange={event => setResetPassword(event.target.value)} placeholder="输入至少 8 位的新密码" /><button className="button button--secondary" disabled={busy || resetPassword.length < 8} onClick={() => void reset()}><KeyRound />重置</button></div></section>}
        <section className="account-section permission-section"><div className="account-section-heading"><div><h2>目录权限</h2><p>授权自动继承到该目录的全部子目录；“可操作”自动包含“可见”</p></div>{!selected.isMasterAdmin ? <button className="button button--primary" disabled={busy} onClick={() => void savePermissions()}><Save />保存权限</button> : null}</div>
          {selected.isMasterAdmin ? <div className="master-permission"><Shield />主账号默认拥有全部目录的可见与可操作权限。</div> : <div className="permission-table">
            <div className="permission-row permission-row--head"><span>目录</span><span>可见</span><span>可操作</span></div>
            {[{ folder: null, level: 0 }, ...folderRows].map(row => {
              const id = row.folder?.id ?? null
              const item = permissions.get(permissionKey(id))
              return <div className="permission-row" key={id ?? 'root'}><span style={{ '--permission-level': row.level } as React.CSSProperties}>{row.folder ? row.folder.name : '资产库根目录（全部目录）'}</span><label><input type="checkbox" checked={item?.canView ?? false} onChange={event => changePermission(id, 'canView', event.target.checked)} /><i /></label><label><input type="checkbox" checked={item?.canOperate ?? false} onChange={event => changePermission(id, 'canOperate', event.target.checked)} /><i /></label></div>
            })}
          </div>}
        </section>
      </div> : <div className="empty-state"><Users /><p>请选择一个账户</p></div>}
    </div>
    {message ? <div className="toast" role="status">{message}</div> : null}
  </section>
}
