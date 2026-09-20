import type { AssetLocation } from './types'

export const pendingLocationKey = 'mofang.pending-location.v1'

export const formatLocation = (location: AssetLocation, libraryName: string) => [
  `${location.kind === 'asset' ? '文件' : '文件夹'}：${location.name}`,
  `资产库：${libraryName.replace(/[\r\n]/g, ' ')}`,
  `位置：${location.path}`,
  `定位码：${location.code}`,
].join('\n')

export const looksLikeLocation = (text: string) => /MF\d+:|定位码[：:]|位置[：:]|^[/\\]/i.test(text.trim())
