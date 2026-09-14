import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { _electron as electron } from 'playwright-core'

// Launch the actual packaged executable with an isolated profile, never the dev server.
const executablePath = process.argv[2]
assert(executablePath, 'Usage: npm run test:packaged -- <packaged executable>')
const { version } = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'))
const profile = await mkdtemp(path.join(os.tmpdir(), 'mofang-package-smoke-'))
const { ELECTRON_RUN_AS_NODE: _runAsNode, VITE_DEV_SERVER_URL: _devUrl, ...env } = process.env
let application
try {
  application = await electron.launch({
    executablePath: path.resolve(executablePath),
    args: [`--user-data-dir=${profile}`],
    env,
    timeout: 60_000,
  })
  application.context().setDefaultTimeout(30_000)
  const errors = []
  const page = await application.firstWindow()
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text())
  })
  await page.getByRole('button', { name: '测试连接', exact: true }).waitFor()
  assert.equal(await page.title(), '魔方数字资产管理')
  assert(page.url().startsWith('file://'), 'Packaged UI must load from local files')
  assert.deepEqual(await application.evaluate(({ app }) => ({
    packaged: app.isPackaged,
    version: app.getVersion(),
  })), { packaged: true, version })
  assert.equal(await page.evaluate(() => window.mofangDesktop?.platform), process.platform,
    'Sandboxed preload bridge must be available')
  assert.equal(await page.evaluate(() => typeof window.require), 'undefined')
  assert.equal(await page.locator('vite-error-overlay').count(), 0)
  await page.waitForFunction(() => [...document.images].every(image => image.complete && image.naturalWidth > 0))

  // A short desktop window must keep the form scrollable above the footer.
  await application.evaluate(({ BrowserWindow }) => {
    BrowserWindow.getAllWindows()[0].setContentSize(960, 640)
  })

  // This also verifies that file:// renderer requests reach the configured API.
  await page.route('http://127.0.0.1:5080/api/info', route => route.fulfill({
    json: { name: 'Mofang API', version },
  }))
  await page.getByRole('button', { name: '测试连接', exact: true }).click()
  await page.getByText(`连接成功 · Mofang API v${version}`, { exact: true }).waitFor()
  assert(await page.getByRole('button', { name: '保存并进入' }).isEnabled())
  assert.deepEqual(errors, [])
  if (process.env.MOFANG_SMOKE_SCREENSHOT) {
    await page.screenshot({ path: process.env.MOFANG_SMOKE_SCREENSHOT })
  }
  console.log(`PASS: packaged ${process.platform} ${version}; local UI, preload, images and connection interaction`)
} finally {
  if (application) await application.close()
  await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 })
}
