import { Box, File, FileArchive, FileAudio, FileText, Folder as FolderIcon, MoreVertical, Play, SquarePlay } from 'lucide-react'
import type { Asset, Folder } from '../types'
import type { MofangApi } from '../api/client'
import { formatBytes } from '../format'
import { DocumentFormatIcon } from './DocumentFormatIcon'

const TypeIcon = ({ asset }: { asset: Asset }) => {
  if (asset.assetType === 'Video') return <SquarePlay />
  if (asset.assetType === 'Audio') return <FileAudio />
  if (asset.assetType === 'Document') return <DocumentFormatIcon extension={asset.extension} fallback={<FileText />} />
  if (asset.assetType === 'Model3D') return <Box />
  if (asset.assetType === 'Archive') return <FileArchive />
  return <File />
}

export function FolderItem({ folder, view, onOpen, onMenu }: { folder: Folder; view: 'grid' | 'list'; onOpen: () => void; onMenu: (anchor: HTMLElement) => void }) {
  return (
    <article className={`asset-item folder-item asset-item--${view} ${folder.navigationOnly ? 'is-navigation-only' : ''}`} onDoubleClick={onOpen} onContextMenu={event => { event.preventDefault(); if (folder.canView) onMenu(event.currentTarget) }}>
      <div className="asset-item__visual folder-visual"><FolderIcon /></div>
      <div className="asset-item__copy"><strong>{folder.name}</strong><span>文件夹</span></div>
      {folder.canView ? <button className="asset-item__menu" onClick={event => { event.stopPropagation(); onMenu(event.currentTarget) }} onDoubleClick={event => event.stopPropagation()} aria-label={`${folder.name} 操作`}><MoreVertical /></button> : null}
    </article>
  )
}

export function AssetItem({ asset, api, selected, located = false, view, onSelect, onOpen, onMenu }: { asset: Asset; api: MofangApi; selected: boolean; located?: boolean; view: 'grid' | 'list'; onSelect: () => void; onOpen?: () => void; onMenu: (anchor: HTMLElement) => void }) {
  const preview = api.resolveUrl(asset.thumbnailUrl ?? (asset.assetType === 'Image' ? asset.previewUrl : null))
  return (
    <article
      className={`asset-item asset-item--${view} ${selected ? 'is-selected' : ''} ${located ? 'is-located' : ''}`}
      data-asset-id={asset.id}
      onClick={onSelect}
      onDoubleClick={onOpen}
      onContextMenu={event => { event.preventDefault(); onSelect(); onMenu(event.currentTarget) }}
      tabIndex={0}
      onKeyDown={event => event.key === 'Enter' && onSelect()}
    >
      <div className={`asset-item__visual type-${asset.assetType.toLowerCase()}`}>
        {preview ? <img src={preview} alt="" loading="lazy" draggable={false} /> : <TypeIcon asset={asset} />}
        {asset.assetType === 'Video' ? <span className="video-play"><Play fill="currentColor" /></span> : null}
        {asset.thumbnailStatus === 'Pending' || asset.thumbnailStatus === 'Processing' ? <span className="thumbnail-pending">处理中</span> : null}
      </div>
      <div className="asset-item__copy"><strong title={asset.fileName}>{asset.fileName}</strong><span>{asset.extension.slice(1).toUpperCase() || asset.assetType} · {formatBytes(asset.fileSize)}</span></div>
      <button className="asset-item__menu" onClick={event => { event.stopPropagation(); onMenu(event.currentTarget) }} onDoubleClick={event => event.stopPropagation()} aria-label={`${asset.fileName} 操作`}><MoreVertical /></button>
    </article>
  )
}
