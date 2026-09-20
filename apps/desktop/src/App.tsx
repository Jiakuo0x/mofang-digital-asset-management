import { useCallback, useMemo, useState } from 'react'
import './App.css'
import { createApi } from './api/client'
import { AccessScreen } from './components/AccessScreen'
import { ConnectionScreen } from './components/ConnectionScreen'
import { LibraryScreen } from './components/LibraryScreen'
import { clearAuth, getBaseUrl, loadAuth, loadConfig, saveAuth, saveConfig, type AccountSession, type AuthTokens, type LibraryConfig } from './types'

function App() {
  const [config, setConfig] = useState<LibraryConfig | null>(() => loadConfig())
  const [configuring, setConfiguring] = useState(!config)
  const [tokens, setTokens] = useState<AuthTokens | null>(() => config ? loadAuth(config) : null)
  const [account, setAccount] = useState<AccountSession | null>(null)

  const updateTokens = useCallback((next: AuthTokens) => {
    if (!config) return
    setTokens(next)
    saveAuth(config, next)
  }, [config])

  const unauthorized = useCallback(() => {
    clearAuth()
    setTokens(null)
    setAccount(null)
  }, [])

  const api = useMemo(() => config && tokens ? createApi(config, tokens, updateTokens, unauthorized) : null, [config, tokens, unauthorized, updateTokens])

  const configure = () => {
    setConfiguring(true)
  }

  const connected = (next: LibraryConfig) => {
    const sameEndpoint = config && getBaseUrl(config) === getBaseUrl(next)
    saveConfig(next)
    setConfig(next)
    if (!sameEndpoint) {
      setTokens(loadAuth(next))
      setAccount(null)
    }
    setConfiguring(false)
  }

  if (!config || configuring) return <ConnectionScreen initial={config} storageApi={account?.isMasterAdmin && api ? api : undefined} onConnected={connected} onCancel={config ? () => setConfiguring(false) : undefined} />
  if (!account || !tokens || !api) return <AccessScreen config={config} initialTokens={tokens} onConfigure={configure} onAuthenticated={(nextAccount, nextTokens) => { saveAuth(config, nextTokens); setTokens(nextTokens); setAccount(nextAccount) }} />
  return <LibraryScreen config={config} api={api} account={account} appVersion={__APP_VERSION__} onConfigure={configure} onLogout={unauthorized} onAccountUpdated={updated => setAccount(previous => previous?.id === updated.id ? { ...previous, userName: updated.userName, displayName: updated.displayName } : previous)} />
}

export default App
