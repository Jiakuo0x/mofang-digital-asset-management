export type LibraryConfig = {
  version: 1
  name: string
  host: string
  port: number
}

export type ApiInfo = { name: string; version: string; status: string; serverTime: string }

export type Folder = {
  id: string
  parentId: string | null
  name: string
  status: string
  createdAt: string
  updatedAt: string
  canView: boolean
  canOperate: boolean
  navigationOnly: boolean
  children?: Folder[]
}

export type Asset = {
  id: string
  folderId: string | null
  fileName: string
  originalFileName: string
  extension: string
  mimeType: string
  fileSize: number
  assetType: 'Image' | 'Video' | 'Audio' | 'Document' | 'Model3D' | 'Archive' | 'ProjectFile' | 'Other'
  hash: string
  status: string
  createdAt: string
  updatedAt: string
  previewUrl: string
  downloadUrl: string
  thumbnailUrl: string | null
  thumbnailStatus: string
  canOperate: boolean
}

export type AssetDetail = { asset: Asset; folderPath: string; bucket: string; objectKey: string; currentVersionNumber: number }
export type AssetPage = { items: Asset[]; page: number; pageSize: number; total: number }
export type StorageSummary = { assetCount: number; totalBytes: number; deletedCount: number }
export type MinioSettings = {
  serviceUrl: string
  publicUrl: string
  accessKey: string
  secretKeyConfigured: boolean
  assetBucket: string
  thumbnailBucket: string
  source: 'deployment' | 'database'
  updatedAt: string | null
  updatedBy: string | null
}
export type UpdateMinioSettings = {
  serviceUrl: string
  publicUrl: string
  accessKey: string
  secretKey: string | null
  assetBucket: string
  thumbnailBucket: string
}

export type AccountSession = {
  id: string
  userName: string
  displayName: string
  isMasterAdmin: boolean
  canViewRoot: boolean
  canOperateRoot: boolean
}

export type Account = {
  id: string
  userName: string
  displayName: string
  isMasterAdmin: boolean
  isEnabled: boolean
  permissionCount: number
  createdAt: string
  lastLoginAt: string | null
}

export type DirectoryPermission = { folderId: string | null; canView: boolean; canOperate: boolean }
export type AccountPermissions = { accountId: string; permissions: DirectoryPermission[] }
export type OperationAccountOption = { id: string | null; name: string }
export type OperationLog = {
  id: string
  accountId: string | null
  accountName: string
  action: string
  targetType: string
  targetId: string
  targetName: string
  folderId: string | null
  folderPath: string
  previousName: string | null
  newName: string | null
  detail: string
  createdAt: string
}
export type OperationLogPage = { items: OperationLog[]; page: number; pageSize: number; total: number; accounts: OperationAccountOption[] }
export type AuthTokens = { tokenType: string; accessToken: string; expiresIn: number; refreshToken: string; expiresAt: number }
export type UploadItem = { id: string; name: string; progress: number; state: 'uploading' | 'done' | 'failed'; error?: string }

export const CONFIG_KEY = 'mofang.library-config.v1'
export const AUTH_KEY = 'mofang.auth.v1'

export const loadConfig = (): LibraryConfig | null => {
  try {
    const value = localStorage.getItem(CONFIG_KEY)
    if (!value) return null
    const parsed = JSON.parse(value) as LibraryConfig
    return parsed.version === 1 ? parsed : null
  } catch {
    return null
  }
}

export const getBaseUrl = (config: LibraryConfig) => `http://${config.host}:${config.port}`
export const saveConfig = (config: LibraryConfig) => localStorage.setItem(CONFIG_KEY, JSON.stringify(config))
export const clearConfig = () => localStorage.removeItem(CONFIG_KEY)

export const loadAuth = (config: LibraryConfig): AuthTokens | null => {
  try {
    const value = localStorage.getItem(AUTH_KEY)
    if (!value) return null
    const parsed = JSON.parse(value) as { baseUrl: string; tokens: AuthTokens }
    return parsed.baseUrl === getBaseUrl(config) && parsed.tokens?.accessToken && parsed.tokens?.refreshToken ? parsed.tokens : null
  } catch {
    return null
  }
}

export const saveAuth = (config: LibraryConfig, tokens: AuthTokens) => localStorage.setItem(AUTH_KEY, JSON.stringify({ baseUrl: getBaseUrl(config), tokens }))
export const clearAuth = () => localStorage.removeItem(AUTH_KEY)
