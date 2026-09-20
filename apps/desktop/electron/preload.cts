import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('mofangDesktop', {
  platform: process.platform,
  openExternal: (url: string) => ipcRenderer.invoke('mofang:open-external', url),
  openFile: (url: string, fileName: string) => ipcRenderer.invoke('mofang:open-file', url, fileName),
  saveFile: (url: string, fileName: string) => ipcRenderer.invoke('mofang:save-file', url, fileName),
  setModalActive: (active: boolean) => ipcRenderer.send('mofang:set-modal-active', active),
})
