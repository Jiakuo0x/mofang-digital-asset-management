import { app, BrowserWindow, ipcMain, net, shell } from 'electron'
import { createWriteStream } from 'node:fs'
import { mkdtemp, rm } from 'node:fs/promises'
import path from 'node:path'
import { Readable } from 'node:stream'
import { pipeline } from 'node:stream/promises'
import { fileURLToPath } from 'node:url'

const currentDirectory = path.dirname(fileURLToPath(import.meta.url))
const devServer = process.env.VITE_DEV_SERVER_URL
const appIconPath = path.join(
  currentDirectory,
  devServer ? '../public/brand/mofang-cube.png' : '../dist/brand/mofang-cube.png',
)

const defaultTitleBarOverlay = {
  color: '#131920',
  symbolColor: '#aeb8c4',
  height: 72,
}

// Matches the visual result of the page's rgba(5, 8, 11, .72) dialog backdrop.
const modalTitleBarOverlay = {
  color: '#090d11',
  symbolColor: '#34393f',
  height: 72,
}

if (process.platform === 'win32') app.setAppUserModelId('com.mofang.dam')

ipcMain.handle('mofang:open-external', async (_event, url: string) => {
  const target = new URL(url)
  if (target.protocol !== 'http:' && target.protocol !== 'https:') throw new Error('不支持的外部链接。')
  await shell.openExternal(target.toString())
})

ipcMain.on('mofang:set-modal-active', (event, active: unknown) => {
  if (process.platform !== 'win32' || typeof active !== 'boolean') return
  const window = BrowserWindow.fromWebContents(event.sender)
  if (!window || window.isDestroyed()) return
  window.setTitleBarOverlay(active ? modalTitleBarOverlay : defaultTitleBarOverlay)
})

const blockedOpenExtensions = new Set([
  '.app', '.bat', '.cmd', '.com', '.cpl', '.exe', '.gadget', '.hta', '.inf', '.ins', '.iso',
  '.isp', '.jar', '.js', '.jse', '.lnk', '.msc', '.msi', '.msp', '.mst', '.pif', '.ps1',
  '.reg', '.scr', '.sct', '.sh', '.url', '.vb', '.vbe', '.vbs', '.ws', '.wsc', '.wsf', '.wsh',
])

const safeTemporaryFileName = (fileName: string) => {
  const cleaned = Array.from(path.basename(fileName), character => {
    const code = character.charCodeAt(0)
    return code <= 31 || '<>:"/\\|?*'.includes(character) ? '_' : character
  }).join('').replace(/[. ]+$/g, '')
  if (!cleaned) return 'asset'
  const extension = path.extname(cleaned)
  const stem = path.basename(cleaned, extension)
  const availableStemLength = Math.max(1, 180 - extension.length)
  return `${stem.slice(0, availableStemLength)}${extension.slice(0, 20)}`
}

ipcMain.handle('mofang:open-file', async (_event, url: string, fileName: string) => {
  const target = new URL(url)
  if (target.protocol !== 'http:' && target.protocol !== 'https:') throw new Error('不支持的文件地址。')

  const safeFileName = safeTemporaryFileName(fileName)
  if (blockedOpenExtensions.has(path.extname(safeFileName).toLowerCase())) {
    throw new Error('出于安全考虑，不能直接打开可执行文件。请下载后手动确认。')
  }

  const temporaryDirectory = await mkdtemp(path.join(app.getPath('temp'), 'mofang-dam-'))
  const temporaryFile = path.join(temporaryDirectory, safeFileName)
  try {
    const response = await net.fetch(target.toString())
    if (!response.ok || !response.body) throw new Error(`文件下载失败 (${response.status})。`)
    const responseBody = response.body as unknown as import('node:stream/web').ReadableStream<Uint8Array>
    await pipeline(Readable.fromWeb(responseBody), createWriteStream(temporaryFile))
    const openError = await shell.openPath(temporaryFile)
    if (openError) throw new Error(openError)
  } catch (error) {
    await rm(temporaryDirectory, { recursive: true, force: true })
    throw error
  }
})

const createWindow = () => {
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 960,
    minHeight: 640,
    backgroundColor: '#101419',
    title: '魔方数字资产管理',
    icon: appIconPath,
    autoHideMenuBar: true,
    ...(process.platform === 'win32' ? {
      titleBarStyle: 'hidden' as const,
      titleBarOverlay: defaultTitleBarOverlay,
    } : {}),
    webPreferences: {
      preload: path.join(currentDirectory, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  window.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('http://') || url.startsWith('https://')) void shell.openExternal(url)
    return { action: 'deny' }
  })

  if (devServer) void window.loadURL(devServer)
  else void window.loadFile(path.join(currentDirectory, '../dist/index.html'))
}

app.whenReady().then(() => {
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
