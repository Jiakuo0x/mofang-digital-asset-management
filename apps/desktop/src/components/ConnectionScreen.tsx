import { useMemo, useState } from 'react'
import { ArrowLeft, ArrowRight, CheckCircle2, ChevronRight, Database, FolderArchive, HardDrive, LoaderCircle, RefreshCcw, ShieldCheck } from 'lucide-react'
import { createApi, type MofangApi } from '../api/client'
import type { ApiInfo, LibraryConfig } from '../types'
import { Brand } from './Brand'
import { StorageSettingsPanel } from './StorageSettingsPanel'

type Props = { initial?: LibraryConfig | null; storageApi?: MofangApi; onConnected: (config: LibraryConfig) => void; onCancel?: () => void }

export function ConnectionScreen({ initial, storageApi, onConnected, onCancel }: Props) {
  const [activeSection, setActiveSection] = useState<'library' | 'storage'>('storage')
  const [name, setName] = useState(initial?.name ?? '本地魔方资产库')
  const [host, setHost] = useState(initial?.host ?? '127.0.0.1')
  const [port, setPort] = useState(String(initial?.port ?? 5080))
  const [testing, setTesting] = useState(false)
  const [result, setResult] = useState<ApiInfo | null>(null)
  const [error, setError] = useState('')
  const config = useMemo<LibraryConfig>(() => ({ version: 1, name: name.trim(), host: host.trim(), port: Number(port) }), [host, name, port])
  const valid = Boolean(config.name && config.host && Number.isInteger(config.port) && config.port > 0 && config.port < 65536)

  const test = async () => {
    if (!valid) return
    setTesting(true)
    setResult(null)
    setError('')
    try {
      setResult(await createApi(config).info())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '连接失败，请检查地址与端口。')
    } finally {
      setTesting(false)
    }
  }

  const fields = <>
    <div className="connection-fields">
      <label><span>资产库名称</span><input value={name} onChange={event => { setName(event.target.value); setResult(null) }} /></label>
      <label><span>API 地址</span><input autoFocus={!storageApi} value={host} onChange={event => { setHost(event.target.value); setResult(null) }} placeholder="127.0.0.1" /></label>
      <label><span>API 端口</span><input inputMode="numeric" value={port} onChange={event => { setPort(event.target.value); setResult(null) }} placeholder="5080" /></label>
    </div>
    <div className={`connection-result ${error ? 'connection-result--error' : ''}`} aria-live="polite">
      {result ? <><CheckCircle2 size={17} />连接成功 · Mofang API v{result.version}</> : error ? error : '请先测试与后端服务的连接'}
    </div>
    <div className="connection-actions">
      <button className="button button--secondary" disabled={!valid || testing} onClick={test}>
        {testing ? <LoaderCircle className="spin" size={17} /> : <RefreshCcw size={17} />}测试连接
      </button>
      <button className="button button--primary" disabled={!result} onClick={() => onConnected(config)}>{storageApi ? '保存连接' : '保存并进入'}<ArrowRight size={17} /></button>
    </div>
  </>

  if (storageApi) return (
    <main className="connection-screen connection-screen--settings">
      <header className="connection-titlebar"><Brand compact /></header>
      <div className="settings-workspace">
        <aside className="settings-navigation">
          <div className="settings-navigation__heading">
            <h1>连接设置</h1>
            <p>管理客户端与服务的连接，确保资产数据安全可访问。</p>
          </div>
          {onCancel ? <button className="settings-navigation__exit" onClick={onCancel}><ArrowLeft />返回资产库</button> : null}
          <nav aria-label="连接设置分类">
            <button className={activeSection === 'library' ? 'is-selected' : ''} onClick={() => setActiveSection('library')}>
              <span className="settings-navigation__icon"><HardDrive /></span>
              <span><strong>资产库服务</strong><small>客户端 API 连接</small></span>
              <ChevronRight />
            </button>
            <button className={activeSection === 'storage' ? 'is-selected' : ''} onClick={() => setActiveSection('storage')}>
              <span className="settings-navigation__icon"><Database /></span>
              <span><strong>MinIO 对象存储</strong><small>地址、凭据与 bucket</small></span>
              <span className="settings-navigation__role">主账号</span>
            </button>
          </nav>
          <div className="settings-navigation__security"><ShieldCheck /><span>客户端连接保存在本机，存储凭据加密保存在服务端。</span></div>
        </aside>

        <section className="settings-content">
          <div className="settings-content__inner">
            {activeSection === 'library' ? <section className="settings-editor">
              <header className="settings-editor-header">
                <div><h2>资产库服务</h2><p>配置当前客户端连接的魔方资产库 API 服务。</p></div>
                <span className="settings-current-client"><i />当前客户端</span>
              </header>
              <div className="settings-editor-card">
                <section className="settings-form-group">
                  <div className="settings-form-group__heading"><HardDrive /><div><h3>连接信息</h3><p>更换 API 地址后需要重新登录。</p></div></div>
                  <div className="library-connection-grid">
                    <label className="form-field"><span>资产库名称</span><input value={name} onChange={event => { setName(event.target.value); setResult(null) }} /></label>
                    <label className="form-field"><span>API 地址</span><input value={host} onChange={event => { setHost(event.target.value); setResult(null) }} placeholder="127.0.0.1" /></label>
                    <label className="form-field"><span>API 端口</span><input inputMode="numeric" value={port} onChange={event => { setPort(event.target.value); setResult(null) }} placeholder="5080" /></label>
                  </div>
                </section>
                <div className={`settings-connection-result ${result ? 'is-success' : error ? 'is-error' : ''}`} role="status" aria-live="polite">
                  {result ? <><CheckCircle2 />连接成功 · Mofang API v{result.version}</> : error ? error : <><RefreshCcw />测试连接后才能保存新的客户端配置</>}
                </div>
                <div className="settings-editor-actions">
                  <span>配置仅保存在当前电脑</span>
                  <div>
                    <button className="button button--secondary" disabled={!valid || testing} onClick={() => void test()}>{testing ? <LoaderCircle className="spin" /> : <RefreshCcw />}测试连接</button>
                    <button className="button button--primary" disabled={!result} onClick={() => onConnected(config)}>保存连接<ArrowRight /></button>
                  </div>
                </div>
              </div>
            </section> : <StorageSettingsPanel api={storageApi} embedded />}
          </div>
        </section>
      </div>
      <footer className="connection-footer"><ShieldCheck size={17} />客户端连接保存在本机，MinIO 凭据加密保存在服务端<span>客户端 v{__APP_VERSION__}</span></footer>
    </main>
  )

  return (
    <main className="connection-screen">
      <header className="connection-titlebar"><Brand compact /></header>
      {onCancel ? <button className="connection-back" onClick={onCancel}><ArrowLeft />返回</button> : null}
      <div className="connection-stage">
        <aside className="connection-branding">
          <Brand />
          <div className="cube-field" aria-hidden="true"><span /><span /><span /><span /></div>
        </aside>
        <section className="connection-card">
          <h1>连接资产库</h1>
          <p>配置本地或局域网中的魔方资产库服务</p>
          {fields}
        </section>
        <aside className="connection-diagram" aria-label="服务连接示意">
          <div><Database /><span>魔方资产库服务</span></div><i />
          <div><FolderArchive /><span>资产存储</span></div>
        </aside>
      </div>
      <footer className="connection-footer"><ShieldCheck size={17} />配置仅保存在本机<span>客户端 v{__APP_VERSION__}</span></footer>
    </main>
  )
}
