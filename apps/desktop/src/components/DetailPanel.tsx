import { lazy, Suspense, useEffect, useState } from 'react'
import { Box, Copy, Download, FileAudio, FileText, Hash, Play, X } from 'lucide-react'
import type { Asset, AssetDetail } from '../types'
import type { MofangApi } from '../api/client'
import { formatBytes } from '../format'
import { DocumentFormatIcon } from './DocumentFormatIcon'

const Markdown = lazy(() => import('react-markdown'))

const formatDate = (value: string) => new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value))

function MediaPreview({ detail, api }: { detail: AssetDetail; api: MofangApi }) {
  const { asset } = detail
  const [text, setText] = useState('')
  const [textError, setTextError] = useState('')
  const previewUrl = api.resolveUrl(asset.previewUrl)!
  useEffect(() => {
    if (asset.extension === '.txt' || asset.extension === '.md') api.text(asset.id).then(setText).catch(reason => setTextError(reason instanceof Error ? reason.message : '无法加载文本'))
  }, [api, asset.extension, asset.id])

  if (asset.assetType === 'Image') return <img src={previewUrl} alt={asset.fileName} />
  if (asset.assetType === 'Video') return <video src={previewUrl} controls preload="metadata" />
  if (asset.assetType === 'Audio') return <div className="audio-preview"><FileAudio /><audio src={previewUrl} controls preload="metadata" /></div>
  if (asset.mimeType === 'application/pdf' || asset.extension === '.pdf') return <iframe src={previewUrl} title={asset.fileName} />
  if (asset.extension === '.txt') return <pre className="text-preview">{textError || text || '正在载入…'}</pre>
  if (asset.extension === '.md') return <div className="markdown-preview">{textError || <Suspense fallback="正在载入…"><Markdown>{text}</Markdown></Suspense>}</div>
  if (asset.assetType === 'Model3D') return <div className="generic-preview"><Box /><span>3D 模型文件</span><small>V1 支持存储、管理与下载</small></div>
  return <div className="generic-preview"><DocumentFormatIcon extension={asset.extension} fallback={<FileText />} /><span>{asset.extension.slice(1).toUpperCase() || '文件'}</span><small>当前格式可下载后查看</small></div>
}

export function DetailPanel({ detail, api, onClose, onDownload }: { detail: AssetDetail | null; api: MofangApi; onClose: () => void; onDownload: (asset: Asset) => void }) {
  const [copied, setCopied] = useState('')
  if (!detail) return <aside className="detail-panel detail-panel--empty"><header><h2>文件详情</h2></header><div><Play /><p>选择一个资产查看预览和详细信息</p></div></aside>
  const { asset } = detail
  const copy = async (label: string, value: string) => { await navigator.clipboard.writeText(value); setCopied(label); window.setTimeout(() => setCopied(''), 1200) }
  return (
    <aside className="detail-panel">
      <header><h2>文件详情</h2><button className="icon-button detail-close" onClick={onClose} aria-label="关闭详情"><X /></button></header>
      <div className="detail-preview"><MediaPreview key={asset.id} detail={detail} api={api} /></div>
      <dl>
        <div><dt>文件名</dt><dd>{asset.fileName}</dd></div>
        <div><dt>类型</dt><dd>{asset.assetType} · {asset.mimeType}</dd></div>
        <div><dt>大小</dt><dd>{formatBytes(asset.fileSize)}</dd></div>
        <div><dt>创建时间</dt><dd>{formatDate(asset.createdAt)}</dd></div>
        <div><dt>修改时间</dt><dd>{formatDate(asset.updatedAt)}</dd></div>
        <div><dt>所属文件夹</dt><dd className="accent-text">{detail.folderPath}</dd></div>
        <div><dt>Hash</dt><dd className="copy-value"><span>{asset.hash}</span><button onClick={() => copy('Hash', asset.hash)} aria-label="复制 Hash"><Copy /></button></dd></div>
        <div><dt>Asset ID</dt><dd className="copy-value"><span>{asset.id}</span><button onClick={() => copy('Asset ID', asset.id)} aria-label="复制 Asset ID"><Copy /></button></dd></div>
        <div><dt>版本</dt><dd>v{String(detail.currentVersionNumber).padStart(3, '0')}</dd></div>
      </dl>
      {copied ? <div className="copy-toast"><Hash />已复制 {copied}</div> : null}
      <button className="button button--secondary detail-download" onClick={() => onDownload(asset)}><Download />下载</button>
    </aside>
  )
}
