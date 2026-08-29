/// <reference types="vite/client" />

declare const __APP_VERSION__: string

interface Window {
  mofangDesktop?: {
    platform: string
    openExternal: (url: string) => Promise<void>
    openFile: (url: string, fileName: string) => Promise<void>
    setModalActive: (active: boolean) => void
  }
}
