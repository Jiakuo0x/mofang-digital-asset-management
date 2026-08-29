import { Check, ChevronDown, CircleAlert, CloudUpload, X } from 'lucide-react'
import type { UploadItem } from '../types'

export function UploadQueue({ items, onClear }: { items: UploadItem[]; onClear: () => void }) {
  if (items.length === 0) return null
  const active = items.filter(item => item.state === 'uploading').length
  return (
    <aside className="upload-queue">
      <header><div><CloudUpload /><strong>上传队列</strong><span>{active ? `${active} 个任务` : '已完成'}</span></div><button className="icon-button" onClick={onClear} aria-label="清空已完成任务">{active ? <ChevronDown /> : <X />}</button></header>
      <div className="upload-queue__items">{items.map(item => <div className="upload-row" key={item.id}>
        <span className={`upload-row__state is-${item.state}`}>{item.state === 'done' ? <Check /> : item.state === 'failed' ? <CircleAlert /> : item.progress}</span>
        <div><strong title={item.name}>{item.name}</strong><div className="progress-track"><i style={{ width: `${item.progress}%` }} /></div><small>{item.error ?? (item.state === 'done' ? '上传完成' : `${item.progress}%`)}</small></div>
      </div>)}</div>
    </aside>
  )
}
