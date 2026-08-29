import { useEffect, useMemo, useState } from 'react'
import { ChevronDown, ChevronUp, Clock3, FileText, Folder as FolderIcon, HardDrive, History, Search, X } from 'lucide-react'
import type { MofangApi } from '../api/client'
import type { Folder, OperationLog, OperationLogPage } from '../types'

const actionNames: Record<string, string> = {
  CreateFolder: '创建文件夹', Rename: '重命名', Upload: '上传', Download: '下载', Delete: '删除', PermanentDelete: '彻底删除', Restore: '恢复', Move: '移动', Copy: '复制',
  Login: '登录', SetupMaster: '初始化主账号', CreateAccount: '创建账户', UpdateAccount: '修改账户', ChangePassword: '修改自己的密码', ResetPassword: '重置密码', ConfigurePermissions: '配置权限', ConfigureMinio: '配置 MinIO',
}

const flattenFolders = (folders: Folder[], level = 0): Array<{ folder: Folder; level: number }> => folders.flatMap(folder => [{ folder, level }, ...flattenFolders(folder.children ?? [], level + 1)])

const prettyDetail = (log: OperationLog) => {
  try { return JSON.stringify(JSON.parse(log.detail), null, 2) }
  catch { return log.detail }
}

export function OperationsPanel({ api, folders }: { api: MofangApi; folders: Folder[] }) {
  const [data, setData] = useState<OperationLogPage>({ items: [], page: 1, pageSize: 100, total: 0, accounts: [] })
  const [directory, setDirectory] = useState('')
  const [folderId, setFolderId] = useState('')
  const [fileName, setFileName] = useState('')
  const [accountId, setAccountId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<string | null>(null)
  const folderRows = useMemo(() => flattenFolders(folders).filter(x => x.folder.canView), [folders])

  useEffect(() => {
    let ignore = false
    const handle = window.setTimeout(() => {
      const params = new URLSearchParams({ page: '1', pageSize: '200' })
      if (directory.trim()) params.set('directory', directory.trim())
      if (folderId) params.set('folderId', folderId)
      if (fileName.trim()) params.set('fileName', fileName.trim())
      if (accountId) params.set('accountId', accountId)
      setLoading(true); setError('')
      void api.operations(params).then(value => { if (!ignore) setData(value) }).catch(reason => { if (!ignore) setError(reason instanceof Error ? reason.message : '无法读取操作日志。') }).finally(() => { if (!ignore) setLoading(false) })
    }, 220)
    return () => { ignore = true; window.clearTimeout(handle) }
  }, [accountId, api, directory, fileName, folderId])

  const clear = () => { setDirectory(''); setFolderId(''); setFileName(''); setAccountId('') }

  return <section className="operations-panel">
    <div className="operations-heading"><div><h1>操作日志</h1><p>记录账户、文件名称、目录路径以及重命名前后值</p></div><History /></div>
    <div className="operation-filters">
      <label><span>目录名称或路径</span><div className="filter-input"><Search /><input value={directory} onChange={event => setDirectory(event.target.value)} placeholder="例如：/项目/成片" /></div></label>
      <label><span>指定目录（含子目录）</span><select value={folderId} onChange={event => setFolderId(event.target.value)}><option value="">全部可见目录</option>{folderRows.map(({ folder, level }) => <option value={folder.id} key={folder.id}>{'　'.repeat(level)}{folder.name}</option>)}</select></label>
      <label><span>文件或文件夹名称</span><div className="filter-input"><FileText /><input value={fileName} onChange={event => setFileName(event.target.value)} placeholder="支持旧名称和新名称" /></div></label>
      <label><span>账号</span><select value={accountId} onChange={event => setAccountId(event.target.value)}><option value="">全部账号</option>{data.accounts.map(account => <option value={account.id ?? ''} key={`${account.id}-${account.name}`}>{account.name}</option>)}</select></label>
      <button className="button button--ghost" onClick={clear}><X />清空</button>
    </div>
    <div className="operation-result-count">共 {data.total} 条记录</div>
    {loading ? <div className="empty-state"><Clock3 className="spin" /><p>正在读取操作日志…</p></div> : error ? <div className="empty-state empty-state--error"><History /><p>{error}</p></div> : data.items.length === 0 ? <div className="empty-state"><History /><p>没有匹配的操作记录</p></div> :
      <div className="operations-table" role="table">
        <div className="operations-row operations-row--head" role="row"><span>操作</span><span>对象 / 名称变化</span><span>目录</span><span>账号</span><span>时间</span><span /></div>
        {data.items.map(log => <div className="operation-entry" key={log.id}>
          <div className="operations-row" role="row">
            <span className="operation-action">{log.targetType === 'Folder' ? <FolderIcon /> : log.targetType === 'Asset' ? <HardDrive /> : <History />}{actionNames[log.action] ?? log.action}</span>
            <span className="operation-target"><strong>{log.targetName || '历史记录'}</strong>{log.previousName || log.newName ? <small>{log.previousName ?? '—'} → {log.newName ?? '—'}</small> : <small>{log.targetType}</small>}</span>
            <span className="operation-path" title={log.folderPath}>{log.folderPath || '/'}</span>
            <span>{log.accountName}</span>
            <time>{new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(log.createdAt))}</time>
            <button className="operation-expand" onClick={() => setExpanded(value => value === log.id ? null : log.id)} aria-label="查看详细记录">{expanded === log.id ? <ChevronUp /> : <ChevronDown />}</button>
          </div>
          {expanded === log.id ? <pre className="operation-detail">{prettyDetail(log)}</pre> : null}
        </div>)}
      </div>}
  </section>
}
