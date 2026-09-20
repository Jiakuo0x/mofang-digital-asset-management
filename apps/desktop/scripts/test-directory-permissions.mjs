import assert from 'node:assert/strict'
import { test } from 'node:test'
import { buildPermissionRows, changeDirectoryPermission, permissionKey } from '../src/directoryPermissions.ts'

const folders = [{ id: 'parent', name: '父目录', children: [{ id: 'child', name: '子目录', children: [{ id: 'grandchild', name: '孙目录' }] }] }, { id: 'sibling', name: '其他目录' }]
const grants = (...items) => new Map(items.map(([folderId, canView, canOperate]) => [permissionKey(folderId), { folderId, canView, canOperate }]))
const update = (state, id, field, checked) => changeDirectoryPermission(state, buildPermissionRows(folders, state), id, field, checked)

test('parent view applies at every depth while siblings and root remain unselected', () => {
  const state = update(new Map(), 'parent', 'canView', true)
  const rows = buildPermissionRows(folders, state)
  assert.deepEqual(rows.map(row => row.canView), [false, true, true, true, false])
  assert.deepEqual(rows.map(row => row.inheritedView), [false, false, true, true, false])
  assert(rows.every(row => !row.canOperate))
  assert.equal(state.size, 1)
})

test('root operation grants select both columns throughout the tree after save/reload', () => {
  const state = update(new Map(), null, 'canOperate', true)
  const restored = grants(...JSON.parse(JSON.stringify([...state.values()])).map(p => [p.folderId, p.canView, p.canOperate]))
  assert(buildPermissionRows(folders, restored).every(row => row.canView && row.canOperate))
  assert.equal(state.size, 1)
})

test('removing parent view clears all descendant grants but preserves siblings', () => {
  const state = grants(['parent', true, false], ['child', true, true], ['grandchild', true, false], ['sibling', true, true])
  const next = update(state, 'parent', 'canView', false)
  assert.deepEqual(buildPermissionRows(folders, next).map(row => [row.canView, row.canOperate]), [[false, false], [false, false], [false, false], [false, false], [true, true]])
})

test('removing parent operation preserves visibility and clears explicit descendant operations', () => {
  const next = update(grants(['parent', true, true], ['grandchild', true, true]), 'parent', 'canOperate', false)
  const rows = buildPermissionRows(folders, next)
  assert(rows.every(row => !row.canOperate))
  assert(rows.filter(row => ['parent', 'child', 'grandchild'].includes(row.folderId)).every(row => row.canView))
})

test('independent child grants do not give access to ancestors or siblings', () => {
  const rows = buildPermissionRows(folders, update(new Map(), 'child', 'canOperate', true))
  assert.deepEqual(rows.map(row => row.canOperate), [false, false, true, true, false])
})

test('new folders inherit a stored parent grant without extra permission records', () => {
  const state = grants(['parent', true, true])
  const nextFolders = [...folders, { id: 'newRootChild', name: '新增根目录' }]
  nextFolders[0] = { ...folders[0], children: [...folders[0].children, { id: 'newChild', name: '新增子目录' }] }
  const rows = buildPermissionRows(nextFolders, state)
  assert.equal(rows.find(row => row.folderId === 'newChild').canOperate, true)
  assert.equal(rows.find(row => row.folderId === 'newRootChild').canView, false)
})

test('clearing root access also removes grants for folders absent from the current tree', () => {
  const state = grants([null, true, true], ['trashed-folder', true, true])
  assert.equal(update(state, null, 'canView', false).size, 0)
})
