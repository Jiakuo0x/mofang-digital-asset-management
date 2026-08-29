import { useEffect, useMemo, useState } from 'react'
import { ArrowRight, KeyRound, LoaderCircle, Settings, ShieldCheck, UserRound } from 'lucide-react'
import { createApi } from '../api/client'
import type { AccountSession, ApiInfo, AuthTokens, LibraryConfig } from '../types'
import { saveAuth } from '../types'
import { Brand } from './Brand'

type Props = {
  config: LibraryConfig
  initialTokens: AuthTokens | null
  onAuthenticated: (account: AccountSession, tokens: AuthTokens) => void
  onConfigure: () => void
}

export function AccessScreen({ config, initialTokens, onAuthenticated, onConfigure }: Props) {
  const [mode, setMode] = useState<'checking' | 'login' | 'setup'>('checking')
  const [info, setInfo] = useState<ApiInfo | null>(null)
  const [userName, setUserName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const publicApi = useMemo(() => createApi(config), [config])

  useEffect(() => {
    let ignore = false
    let currentTokens = initialTokens
    const inspect = async () => {
      try {
        const [nextInfo, setup] = await Promise.all([publicApi.info(), publicApi.setupStatus()])
        if (ignore) return
        setInfo(nextInfo)
        if (setup.requiresSetup) { setMode('setup'); return }
        if (initialTokens)
        {
          try {
            const authenticatedApi = createApi(config, initialTokens, next => { currentTokens = next; saveAuth(config, next) })
            const account = await authenticatedApi.session()
            if (!ignore && currentTokens) onAuthenticated(account, currentTokens)
            return
          } catch {
            // The login form below is the recovery path for an expired or revoked session.
          }
        }
        if (!ignore) setMode('login')
      } catch (reason) {
        if (!ignore) { setError(reason instanceof Error ? reason.message : '无法连接资产库。'); setMode('login') }
      }
    }
    void inspect()
    return () => { ignore = true }
  }, [config, initialTokens, onAuthenticated, publicApi])

  const submit = async () => {
    if (!userName.trim() || !password) return
    if (mode === 'setup' && password !== confirmPassword) { setError('两次输入的密码不一致。'); return }
    setBusy(true)
    setError('')
    try {
      if (mode === 'setup') await publicApi.setup(userName.trim(), displayName.trim(), password)
      const nextTokens = await publicApi.login(userName.trim(), password)
      let currentTokens = nextTokens
      saveAuth(config, nextTokens)
      const authenticatedApi = createApi(config, nextTokens, next => { currentTokens = next; saveAuth(config, next) })
      const account = await authenticatedApi.session()
      onAuthenticated(account, currentTokens)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '登录失败。')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="access-screen">
      <header className="connection-titlebar"><Brand compact /><button className="access-configure" onClick={onConfigure}><Settings />连接设置</button></header>
      <section className="access-card">
        <div className="access-icon">{mode === 'setup' ? <ShieldCheck /> : <KeyRound />}</div>
        <h1>{mode === 'checking' ? '正在验证登录状态' : mode === 'setup' ? '初始化主账号' : '登录资产库'}</h1>
        <p>{mode === 'setup' ? '首次使用请创建唯一的主账号管理员' : `${config.name} · ${config.host}:${config.port}`}</p>
        {mode === 'checking' ? <div className="access-loading"><LoaderCircle className="spin" />正在连接安全服务…</div> : <>
          <label><span>账号</span><div className="input-with-icon"><UserRound /><input autoFocus autoComplete="username" value={userName} onChange={event => setUserName(event.target.value)} onKeyDown={event => event.key === 'Enter' && void submit()} placeholder="请输入账号" /></div></label>
          {mode === 'setup' ? <label><span>显示名称</span><input value={displayName} onChange={event => setDisplayName(event.target.value)} placeholder="例如：系统管理员" /></label> : null}
          <label><span>密码</span><input type="password" autoComplete={mode === 'setup' ? 'new-password' : 'current-password'} value={password} onChange={event => setPassword(event.target.value)} onKeyDown={event => event.key === 'Enter' && void submit()} placeholder="至少 8 个字符" /></label>
          {mode === 'setup' ? <label><span>确认密码</span><input type="password" autoComplete="new-password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} onKeyDown={event => event.key === 'Enter' && void submit()} /></label> : null}
          <div className={`access-error ${error ? 'is-visible' : ''}`} role="alert">{error || ' '}</div>
          <button className="button button--primary access-submit" disabled={busy || !userName.trim() || !password || (mode === 'setup' && (!displayName.trim() || !confirmPassword))} onClick={() => void submit()}>
            {busy ? <LoaderCircle className="spin" /> : <ArrowRight />}{busy ? '处理中…' : mode === 'setup' ? '创建主账号并登录' : '登录'}
          </button>
        </>}
        <div className="version-line"><span>客户端 v{__APP_VERSION__}</span><span>API {info ? `v${info.version}` : '未连接'}</span></div>
      </section>
    </main>
  )
}
