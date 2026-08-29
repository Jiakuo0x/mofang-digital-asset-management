import type {
  Account,
  AccountPermissions,
  AccountSession,
  ApiInfo,
  Asset,
  AssetDetail,
  AssetPage,
  AuthTokens,
  DirectoryPermission,
  Folder,
  LibraryConfig,
  MinioSettings,
  OperationLogPage,
  StorageSummary,
  UpdateMinioSettings,
} from '../types'
import { getBaseUrl } from '../types'

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

const parseError = async (response: Response) => {
  try {
    const body = await response.json() as { title?: string; detail?: string; errors?: Record<string, string[]> }
    const validation = body.errors ? Object.values(body.errors).flat()[0] : null
    return validation || body.detail || body.title || `请求失败 (${response.status})`
  } catch {
    return `请求失败 (${response.status})`
  }
}

const withExpiry = (tokens: Omit<AuthTokens, 'expiresAt'> | AuthTokens): AuthTokens => ({
  ...tokens,
  expiresAt: Date.now() + Math.max(1, tokens.expiresIn) * 1000,
})

export const openExternal = async (url: string) => {
  if (window.mofangDesktop) await window.mofangDesktop.openExternal(url)
  else window.open(url, '_blank', 'noopener,noreferrer')
}

export const openFile = async (url: string, fileName: string) => {
  if (window.mofangDesktop) await window.mofangDesktop.openFile(url, fileName)
  else await openExternal(url)
}

export const createApi = (
  config: LibraryConfig,
  initialTokens: AuthTokens | null = null,
  onTokens?: (tokens: AuthTokens) => void,
  onUnauthorized?: () => void,
) => {
  const baseUrl = getBaseUrl(config)
  let tokens = initialTokens
  let refreshPromise: Promise<AuthTokens> | null = null
  const resolveUrl = (path: string | null) => path ? (path.startsWith('http') ? path : `${baseUrl}${path}`) : null

  const publicRequest = async <T>(path: string, init?: RequestInit): Promise<T> => {
    const response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: { ...(init?.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }), ...init?.headers },
    })
    if (!response.ok) throw new ApiError(await parseError(response), response.status)
    if (response.status === 204) return undefined as T
    return response.json() as Promise<T>
  }

  const refresh = async () => {
    if (!tokens?.refreshToken) throw new ApiError('登录已过期，请重新登录。', 401)
    if (!refreshPromise)
    {
      refreshPromise = publicRequest<Omit<AuthTokens, 'expiresAt'>>('/api/auth/refresh', { method: 'POST', body: JSON.stringify({ refreshToken: tokens.refreshToken }) })
        .then(next => {
          tokens = withExpiry(next)
          onTokens?.(tokens)
          return tokens
        })
        .catch(reason => {
          tokens = null
          onUnauthorized?.()
          throw reason
        })
        .finally(() => { refreshPromise = null })
    }
    return refreshPromise
  }

  const ensureToken = async () => {
    if (!tokens) throw new ApiError('请先登录。', 401)
    if (tokens.expiresAt <= Date.now() + 30_000) await refresh()
    return tokens!.accessToken
  }

  const request = async <T>(path: string, init?: RequestInit, retry = true): Promise<T> => {
    const accessToken = await ensureToken()
    const response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: {
        ...(init?.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
        Authorization: `Bearer ${accessToken}`,
        ...init?.headers,
      },
    })
    if (response.status === 401 && retry && tokens?.refreshToken)
    {
      await refresh()
      return request<T>(path, init, false)
    }
    if (!response.ok) throw new ApiError(await parseError(response), response.status)
    if (response.status === 204) return undefined as T
    return response.json() as Promise<T>
  }

  return {
    baseUrl,
    resolveUrl,
    info: () => publicRequest<ApiInfo>('/api/info'),
    setupStatus: () => publicRequest<{ requiresSetup: boolean }>('/api/auth/setup-status'),
    setup: (userName: string, displayName: string, password: string) => publicRequest<Account>('/api/auth/setup', { method: 'POST', body: JSON.stringify({ userName, displayName, password }) }),
    login: async (userName: string, password: string) => withExpiry(await publicRequest<Omit<AuthTokens, 'expiresAt'>>('/api/auth/login', { method: 'POST', body: JSON.stringify({ userName, password }) })),
    session: () => request<AccountSession>('/api/auth/session'),
    changePassword: async (currentPassword: string, newPassword: string) => {
      const next = withExpiry(await request<Omit<AuthTokens, 'expiresAt'>>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }))
      tokens = next
      onTokens?.(next)
      return next
    },
    folders: (trash = false) => request<Folder[]>(`/api/folders?trash=${trash}`),
    createFolder: (parentId: string | null, name: string) => request<Folder>('/api/folders', { method: 'POST', body: JSON.stringify({ parentId, name }) }),
    renameFolder: (id: string, name: string) => request<Folder>(`/api/folders/${id}/name`, { method: 'PUT', body: JSON.stringify({ name }) }),
    moveFolder: (id: string, folderId: string | null) => request<Folder>(`/api/folders/${id}/move`, { method: 'PUT', body: JSON.stringify({ folderId }) }),
    deleteFolder: (id: string) => request<void>(`/api/folders/${id}`, { method: 'DELETE' }),
    permanentlyDeleteFolder: (id: string) => request<void>(`/api/folders/${id}/permanent`, { method: 'DELETE' }),
    restoreFolder: (id: string, folderId: string | null) => request<Folder>(`/api/folders/${id}/restore`, { method: 'POST', body: JSON.stringify({ folderId }) }),
    assets: (params: URLSearchParams) => request<AssetPage>(`/api/assets?${params}`),
    asset: (id: string) => request<AssetDetail>(`/api/assets/${id}`),
    renameAsset: (id: string, name: string) => request<Asset>(`/api/assets/${id}/name`, { method: 'PUT', body: JSON.stringify({ name }) }),
    moveAsset: (id: string, folderId: string | null) => request<Asset>(`/api/assets/${id}/move`, { method: 'PUT', body: JSON.stringify({ folderId }) }),
    copyAsset: (id: string, folderId: string | null, name?: string) => request<Asset>(`/api/assets/${id}/copy`, { method: 'POST', body: JSON.stringify({ folderId, name }) }),
    deleteAsset: (id: string) => request<void>(`/api/assets/${id}`, { method: 'DELETE' }),
    permanentlyDeleteAsset: (id: string) => request<void>(`/api/assets/${id}/permanent`, { method: 'DELETE' }),
    restoreAsset: (id: string, folderId: string | null) => request<Asset>(`/api/assets/${id}/restore`, { method: 'POST', body: JSON.stringify({ folderId }) }),
    text: async (id: string) => {
      const accessToken = await ensureToken()
      const response = await fetch(`${baseUrl}/api/assets/${id}/text`, { headers: { Accept: 'text/plain', Authorization: `Bearer ${accessToken}` } })
      if (!response.ok) throw new ApiError(await parseError(response), response.status)
      return response.text()
    },
    downloadLink: (id: string) => request<{ url: string; fileName: string }>(`/api/assets/${id}/download-link`, { method: 'POST' }),
    operations: (params: URLSearchParams) => request<OperationLogPage>(`/api/operations?${params}`),
    storageSummary: () => request<StorageSummary>('/api/storage/summary'),
    minioSettings: () => request<MinioSettings>('/api/admin/storage/minio'),
    testMinioSettings: (settings: UpdateMinioSettings) => request<{ success: boolean; message: string }>('/api/admin/storage/minio/test', { method: 'POST', body: JSON.stringify(settings) }),
    updateMinioSettings: (settings: UpdateMinioSettings) => request<MinioSettings>('/api/admin/storage/minio', { method: 'PUT', body: JSON.stringify(settings) }),
    accounts: () => request<Account[]>('/api/admin/accounts'),
    createAccount: (userName: string, displayName: string, password: string) => request<Account>('/api/admin/accounts', { method: 'POST', body: JSON.stringify({ userName, displayName, password }) }),
    updateAccount: (id: string, displayName: string, isEnabled: boolean) => request<Account>(`/api/admin/accounts/${id}`, { method: 'PUT', body: JSON.stringify({ displayName, isEnabled }) }),
    resetPassword: (id: string, password: string) => request<void>(`/api/admin/accounts/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ password }) }),
    accountPermissions: (id: string) => request<AccountPermissions>(`/api/admin/accounts/${id}/permissions`),
    replaceAccountPermissions: (id: string, permissions: DirectoryPermission[]) => request<AccountPermissions>(`/api/admin/accounts/${id}/permissions`, { method: 'PUT', body: JSON.stringify({ permissions }) }),
    upload: async (folderId: string | null, file: File, onProgress: (progress: number) => void) => {
      const accessToken = await ensureToken()
      return new Promise<Asset[]>((resolve, reject) => {
        const body = new FormData()
        if (folderId) body.append('folderId', folderId)
        body.append('files', file)
        const xhr = new XMLHttpRequest()
        xhr.open('POST', `${baseUrl}/api/assets/upload`)
        xhr.setRequestHeader('Authorization', `Bearer ${accessToken}`)
        xhr.upload.onprogress = event => {
          if (event.lengthComputable) onProgress(Math.round((event.loaded / event.total) * 100))
        }
        xhr.onload = () => {
          if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText) as Asset[])
          else {
            if (xhr.status === 401) onUnauthorized?.()
            try {
              const body = JSON.parse(xhr.responseText) as { title?: string; detail?: string }
              reject(new ApiError(body.detail ?? body.title ?? `上传失败 (${xhr.status})`, xhr.status))
            } catch {
              reject(new ApiError(`上传失败 (${xhr.status})`, xhr.status))
            }
          }
        }
        xhr.onerror = () => reject(new ApiError('无法连接资产库。', 0))
        xhr.send(body)
      })
    },
  }
}

export type MofangApi = ReturnType<typeof createApi>
