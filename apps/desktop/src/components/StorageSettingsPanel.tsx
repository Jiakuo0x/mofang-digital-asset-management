import { useEffect, useState } from 'react'
import { CircleAlert, Database, Eye, EyeOff, KeyRound, PlugZap, RefreshCw, Save, Server, ShieldCheck } from 'lucide-react'
import type { MofangApi } from '../api/client'
import type { MinioSettings, UpdateMinioSettings } from '../types'

type Props = { api: MofangApi; embedded?: boolean; onSaved?: (message: string) => void }
type FormState = Omit<UpdateMinioSettings, 'secretKey'> & { secretKey: string }

const emptyForm: FormState = {
  serviceUrl: '',
  publicUrl: '',
  accessKey: '',
  secretKey: '',
  assetBucket: '',
  thumbnailBucket: '',
}

const toForm = (settings: MinioSettings): FormState => ({
  serviceUrl: settings.serviceUrl,
  publicUrl: settings.publicUrl,
  accessKey: settings.accessKey,
  secretKey: '',
  assetBucket: settings.assetBucket,
  thumbnailBucket: settings.thumbnailBucket,
})

const toRequest = (form: FormState): UpdateMinioSettings => ({
  ...form,
  secretKey: form.secretKey || null,
})

const isHttpUrl = (value: string) => {
  try {
    const url = new URL(value)
    return (url.protocol === 'http:' || url.protocol === 'https:') && url.pathname === '/' && !url.search && !url.hash && !url.username && !url.password
  } catch {
    return false
  }
}

export function StorageSettingsPanel({ api, embedded = false, onSaved }: Props) {
  const [settings, setSettings] = useState<MinioSettings | null>(null)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [showSecret, setShowSecret] = useState(false)
  const [busy, setBusy] = useState<'loading' | 'testing' | 'saving' | null>('loading')
  const [status, setStatus] = useState<{ kind: 'success' | 'error'; message: string } | null>(null)

  const load = async () => {
    setBusy('loading'); setStatus(null)
    try {
      const value = await api.minioSettings()
      setSettings(value); setForm(toForm(value))
    } catch (reason) {
      setStatus({ kind: 'error', message: reason instanceof Error ? reason.message : '无法读取 MinIO 配置。' })
    } finally {
      setBusy(null)
    }
  }

  useEffect(() => {
    let ignore = false
    void api.minioSettings()
      .then(value => {
        if (ignore) return
        setSettings(value); setForm(toForm(value)); setBusy(null)
      })
      .catch(reason => {
        if (ignore) return
        setStatus({ kind: 'error', message: reason instanceof Error ? reason.message : '无法读取 MinIO 配置。' }); setBusy(null)
      })
    return () => { ignore = true }
  }, [api])

  const setField = (field: keyof FormState, value: string) => setForm(previous => ({ ...previous, [field]: value }))
  const valid = isHttpUrl(form.serviceUrl)
    && isHttpUrl(form.publicUrl)
    && Boolean(form.accessKey.trim())
    && (!form.secretKey || form.secretKey.length >= 8)
    && Boolean(form.assetBucket.trim())
    && Boolean(form.thumbnailBucket.trim())

  const test = async () => {
    if (!valid) return
    setBusy('testing'); setStatus(null)
    try {
      const result = await api.testMinioSettings(toRequest(form))
      setStatus({ kind: 'success', message: result.message })
    } catch (reason) {
      setStatus({ kind: 'error', message: reason instanceof Error ? reason.message : 'MinIO 连接测试失败。' })
    } finally {
      setBusy(null)
    }
  }

  const save = async () => {
    if (!valid) return
    setBusy('saving'); setStatus(null)
    try {
      const value = await api.updateMinioSettings(toRequest(form))
      setSettings(value); setForm(toForm(value))
      setStatus({ kind: 'success', message: 'MinIO 配置已保存并立即生效。' })
      onSaved?.('MinIO 配置已保存并立即生效')
    } catch (reason) {
      setStatus({ kind: 'error', message: reason instanceof Error ? reason.message : 'MinIO 配置保存失败。' })
    } finally {
      setBusy(null)
    }
  }

  return <section className={`settings-editor storage-panel ${embedded ? 'storage-panel--embedded' : ''}`}>
    <header className="settings-editor-header">
      <div><div className="settings-editor-title"><h2>MinIO 对象存储</h2>{settings ? <span className={`configuration-source is-${settings.source}`}>{settings.source === 'database' ? '主账号配置' : '部署默认值'}</span> : null}</div><p>配置服务地址、访问凭据和 bucket，用于资产与缩略图的存储访问。</p></div>
      <div className="settings-editor-header__meta">
        {settings?.updatedAt ? <small>最后由 {settings.updatedBy ?? '主账号'} 于 {new Date(settings.updatedAt).toLocaleString('zh-CN')} 更新</small> : null}
      </div>
    </header>

    {busy === 'loading' && !settings ? <div className="settings-editor-card empty-state"><RefreshCw className="spin" /><p>正在读取存储配置…</p></div> : <div className="settings-editor-card">
      <section className="settings-form-group">
        <div className="settings-form-group__heading"><Server /><div><h3>服务地址</h3><p>区分后端服务访问地址和桌面客户端公开地址。</p></div></div>
        <div className="storage-form-grid">
          <label className="form-field"><span>服务端访问地址</span><input value={form.serviceUrl} onChange={event => setField('serviceUrl', event.target.value)} placeholder="http://minio.internal:9000" /><small>供 API 上传、复制和读取对象，可使用容器服务名或内网地址。</small></label>
          <label className="form-field"><span>客户端公开地址</span><input value={form.publicUrl} onChange={event => setField('publicUrl', event.target.value)} placeholder="https://assets.example.com" /><small>写入预览和下载链接，必须能从所有桌面客户端访问。</small></label>
        </div>
      </section>

      <section className="settings-form-group">
        <div className="settings-form-group__heading"><KeyRound /><div><h3>访问凭据</h3><p>Secret Key 加密保存，接口不会返回明文。</p></div></div>
        <div className="storage-form-grid">
          <label className="form-field"><span>Access Key</span><input value={form.accessKey} onChange={event => setField('accessKey', event.target.value)} autoComplete="off" /><small>需要具备对象读写和 bucket 管理权限。</small></label>
          <label className="form-field"><span>Secret Key</span><span className="secret-input-control"><input type={showSecret ? 'text' : 'password'} value={form.secretKey} onChange={event => setField('secretKey', event.target.value)} autoComplete="new-password" placeholder={settings?.secretKeyConfigured ? '已安全保存，留空则不修改' : '至少 8 个字符'} /><button type="button" onClick={() => setShowSecret(value => !value)} aria-label={showSecret ? '隐藏 Secret Key' : '显示 Secret Key'} title={showSecret ? '隐藏 Secret Key' : '显示 Secret Key'}>{showSecret ? <EyeOff /> : <Eye />}</button></span><small>{settings?.secretKeyConfigured ? '凭据已配置；仅在需要更换时填写。' : '新 Secret Key 至少需要 8 个字符。'}</small></label>
        </div>
      </section>

      <section className="settings-form-group">
        <div className="settings-form-group__heading"><Database /><div><h3>存储空间（bucket）</h3><p>分别保存原始资产和系统生成的缩略图。</p></div></div>
        <div className="storage-form-grid">
          <label className="form-field"><span>资产 bucket</span><input value={form.assetBucket} onChange={event => setField('assetBucket', event.target.value)} /><small>用于保存资产原始文件。</small></label>
          <label className="form-field"><span>缩略图 bucket</span><input value={form.thumbnailBucket} onChange={event => setField('thumbnailBucket', event.target.value)} /><small>用于保存系统生成的预览缩略图。</small></label>
        </div>
      </section>

      {status ? <div className={`storage-test-result is-${status.kind}`} role="status">{status.kind === 'success' ? <ShieldCheck /> : <CircleAlert />}<span>{status.message}</span></div> : <div className="settings-storage-warning"><CircleAlert /><span>切换地址不会搬迁历史对象；已有资产必须保留相同 bucket 与对象路径，或先完成 MinIO 数据迁移。</span></div>}
      {!valid && settings ? <p className="storage-inline-hint"><CircleAlert />请填写完整的 HTTP(S) 地址、访问密钥和 bucket；新 Secret Key 至少 8 个字符。</p> : null}

      <div className="settings-editor-actions settings-editor-actions--storage">
        <button className="button button--ghost" disabled={Boolean(busy)} onClick={() => void load()}><RefreshCw className={busy === 'loading' ? 'spin' : ''} />重新读取</button>
        <div>
          <button className="button button--secondary" disabled={Boolean(busy) || !valid} onClick={() => void test()}>{busy === 'testing' ? <RefreshCw className="spin" /> : <PlugZap />}{busy === 'testing' ? '测试中…' : '测试连接'}</button>
          <button className="button button--primary" disabled={Boolean(busy) || !valid} onClick={() => void save()}>{busy === 'saving' ? <RefreshCw className="spin" /> : <Save />}{busy === 'saving' ? '验证并保存…' : '保存配置'}</button>
        </div>
      </div>
    </div>}
  </section>
}
