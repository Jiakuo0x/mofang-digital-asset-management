import { useCallback, useEffect, useRef, useState } from 'react'
import { ClipboardPaste, File, Folder, LocateFixed, RefreshCw } from 'lucide-react'
import type { MofangApi } from '../api/client'
import type { AssetLocation, LocationResult } from '../types'
import { formatBytes } from '../format'
import { Dialog } from './Dialog'

export function LocationDialog({ api, initialInput, onClose, onLocate, onConfigure }: {
  api: MofangApi
  initialInput: string
  onClose: () => void
  onLocate: (location: AssetLocation) => Promise<void>
  onConfigure: (input: string) => void
}) {
  const [input, setInput] = useState(initialInput)
  const [result, setResult] = useState<LocationResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const sequence = useRef(0)
  const field = useRef<HTMLTextAreaElement>(null)
  const latestLocate = useRef(onLocate)
  const latestApi = useRef(api)
  useEffect(() => { latestLocate.current = onLocate }, [onLocate])
  useEffect(() => { latestApi.current = api }, [api])

  const resolve = useCallback(async (value: string) => {
    const request = ++sequence.current
    setBusy(true); setError(''); setResult(null)
    try {
      const next = await latestApi.current.resolveLocation(value)
      if (request !== sequence.current) return
      if (next.match === 'exact' && next.items[0]?.status === 'Active') await latestLocate.current(next.items[0])
      else setResult(next)
    } catch (reason) {
      if (request === sequence.current) setError(reason instanceof Error ? reason.message : '无法解析位置，请重试。')
    } finally { if (request === sequence.current) setBusy(false) }
  }, [])

  useEffect(() => {
    const requests = sequence
    const timer = window.setTimeout(() => { if (initialInput.trim()) void resolve(initialInput) }, 0)
    return () => { window.clearTimeout(timer); requests.current++ }
  }, [initialInput, resolve])

  const choose = async (location: AssetLocation) => {
    setBusy(true); setError('')
    try { await onLocate(location) }
    catch (reason) { setError(reason instanceof Error ? reason.message : '无法定位，请重试。') }
    finally { setBusy(false) }
  }

  const paste = async () => {
    const request = ++sequence.current
    try {
      const text = await navigator.clipboard.readText()
      if (request !== sequence.current) return
      setInput(text); await resolve(text)
    } catch {
      if (request === sequence.current) { setError('无法读取剪贴板，请在输入框中按 Ctrl+V 或 ⌘V 粘贴。'); field.current?.focus() }
    }
  }

  return <Dialog title="粘贴定位" onClose={onClose} className="dialog--location" footer={<>
    <button className="button button--secondary" onClick={onClose}>取消</button>
    <button className="button button--primary" disabled={busy || !input.trim()} onClick={() => void resolve(input)}>{busy ? <RefreshCw className="spin" /> : <LocateFixed />}{busy ? '正在定位…' : '解析并定位'}</button>
  </>}>
    <p className="location-intro">粘贴别人发来的文件或文件夹位置，直接找到它。也支持目录路径和名称。</p>
    <label className="form-field"><span>位置、定位码或名称</span><textarea ref={field} autoFocus maxLength={8192} value={input} disabled={busy} onChange={event => { sequence.current++; setInput(event.target.value); setResult(null); setError('') }} placeholder={'/项目素材/宣传片/视频\n或粘贴完整的位置说明'} onKeyDown={event => {
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter' && !busy && input.trim()) { event.preventDefault(); void resolve(input) }
    }} /></label>
    <div className="location-input-footer"><span>仅查找你有权限查看的内容</span><button disabled={busy} onClick={() => void paste()}><ClipboardPaste />从剪贴板粘贴</button></div>
    {error ? <p className="location-message form-error" role="alert">{error}</p> : null}
    {result?.match === 'differentLibrary' ? <div className="location-message" role="status"><strong>这个位置属于另一个资产库</strong><p>请在连接设置中连接对应的资产库，再继续定位。</p><small>资产库 ID：{result.expectedLibraryId}</small><button className="button button--secondary" onClick={() => onConfigure(input)}>前往连接设置</button></div> : null}
    {result?.match === 'none' ? <div className="location-message" role="status">没有找到可访问的文件或文件夹。请检查路径，或请发送者重新复制位置。</div> : null}
    {result && result.items.length > 0 ? <div className="location-results">
      <p>{result.match === 'exact' ? '该项目已移入回收站' : '请选择要定位的文件或文件夹'}{result.hasMore ? '（仅显示前 50 项，请补充路径缩小范围）' : ''}</p>
      <div className="location-results__list">{result.items.map(item => <button className="location-result" key={`${item.kind}:${item.id}`} disabled={busy} onClick={() => void choose(item)}>
        {item.kind === 'folder' ? <Folder /> : <File />}
        <span><strong>{item.name}</strong><span title={item.path}>{item.path}</span><small>{item.kind === 'folder' ? '文件夹' : `文件 · ${formatBytes(item.fileSize ?? 0)}`}{item.updatedAt ? ` · ${new Date(item.updatedAt).toLocaleString('zh-CN')}` : ''}{item.status === 'Deleted' ? ' · 回收站' : ''}</small></span>
        <LocateFixed />
      </button>)}</div>
    </div> : null}
  </Dialog>
}
