import type { DirectoryPermission, Folder } from './types'

export const permissionKey = (folderId: string | null) => folderId ?? 'root'

export type PermissionRow = {
  folderId: string | null
  name: string
  level: number
  hasChildren: boolean
  ancestors: Array<string | null>
  canView: boolean
  canOperate: boolean
  inheritedView: boolean
  inheritedOperate: boolean
}

// Keep direct grants separate from effective access so future children inherit too.
export function buildPermissionRows(folders: Folder[], permissions: Map<string, DirectoryPermission>): PermissionRow[] {
  const rows: PermissionRow[] = []
  const visit = (folder: Folder | null, parent?: PermissionRow) => {
    const folderId = folder?.id ?? null
    const grant = permissions.get(permissionKey(folderId))
    const canOperate = !!grant?.canOperate || !!parent?.canOperate
    const row: PermissionRow = {
      folderId,
      name: folder?.name ?? '资产库根目录（全部目录）',
      level: parent ? parent.level + 1 : 0,
      hasChildren: (folder ? folder.children?.length ?? 0 : folders.length) > 0,
      ancestors: parent ? [...parent.ancestors, parent.folderId] : [],
      canView: !!grant?.canView || !!parent?.canView || canOperate,
      canOperate,
      inheritedView: !!parent?.canView,
      inheritedOperate: !!parent?.canOperate,
    }
    rows.push(row)
    for (const child of folder ? folder.children ?? [] : folders) visit(child, row)
  }
  visit(null)
  return rows
}

export function changeDirectoryPermission(
  permissions: Map<string, DirectoryPermission>, rows: PermissionRow[],
  folderId: string | null, field: 'canView' | 'canOperate', checked: boolean,
): Map<string, DirectoryPermission> {
  const next = new Map(permissions)
  // Removing a parent grant also clears matching direct grants in its subtree.
  const affected = checked ? [folderId] : folderId === null
    ? [null, ...new Set([...permissions.values()].map(permission => permission.folderId))]
    : rows.filter(row => row.folderId === folderId || row.ancestors.includes(folderId)).map(row => row.folderId)
  for (const id of affected) {
    const key = permissionKey(id)
    const current = next.get(key) ?? { folderId: id, canView: false, canOperate: false }
    const value = field === 'canOperate'
      ? { ...current, canOperate: checked, canView: checked || current.canView }
      : { ...current, canView: checked, canOperate: checked && current.canOperate }
    if (value.canView || value.canOperate) next.set(key, value)
    else next.delete(key)
  }
  return next
}
