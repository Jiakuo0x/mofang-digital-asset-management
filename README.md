# 魔方数字资产管理

面向影视制作素材的本地或局域网数字资产管理。系统由 ASP.NET Core 10 API、PostgreSQL 17、MinIO 与 Electron + React 19 桌面端组成，当前版本为 `1.1.0`，支持账户登录、目录级权限、文件夹、批量上传、检索筛选、预览、下载、移动、复制、重命名、回收站、恢复、彻底删除和详细操作日志。

## 快速启动

### 下载免安装客户端

前往 [GitHub Releases](https://github.com/Jiakuo0x/mofang-digital-asset-management/releases/latest) 下载对应系统的 ZIP：

- Windows x64：`Mofang-<版本>-windows-x64.zip`，完整解压后双击 `Mofang.exe`，保留同目录其他文件。
- macOS（Intel / Apple 芯片通用）：`Mofang-<版本>-macos-universal.zip`，解压后双击 `魔方数字资产管理.app`。

客户端已包含运行环境，不需要安装 Node.js、.NET SDK 或 Docker。ZIP 仅包含桌面客户端，需连接下方部署的 API 服务。macOS 版使用临时签名，未经过 Apple 公证；如首次打开被系统拦截，在“系统设置 → 隐私与安全性”中对本应用选择“仍要打开”。发布页提供详细使用说明与 SHA-256 校验值。

### 部署资产服务

依赖：Docker Desktop（或 Docker Engine + Compose v2）、Node.js 22+、.NET SDK 10（仅本地开发需要）。

```powershell
Copy-Item .env.example .env
docker compose up -d --build
docker compose ps
Invoke-RestMethod http://localhost:5080/api/health
```

首次启动会自动创建数据库结构以及 `mofang-assets`、`mofang-thumbnails` 两个 MinIO bucket，无需手工初始化。桌面端首次连接后会提示创建唯一的主账号管理员；系统不包含硬编码默认账号或密码。

默认入口：

- API：`http://localhost:5080`
- API 健康检查：`http://localhost:5080/api/health`
- MinIO API：`http://localhost:9000`
- MinIO 控制台：`http://localhost:9001`

默认口令只用于本机开发。局域网或生产部署前必须在 `.env` 中更换 PostgreSQL 和 MinIO 密码。

## 启动桌面端

```powershell
Set-Location apps/desktop
npm.cmd install
npm.cmd run dev
```

首次打开时填写：

- 资产库名称：任意本地显示名称，例如 `本地魔方资产库`
- API 地址：`127.0.0.1`；局域网客户端填写部署服务器 IP
- API 端口：默认 `5080`

先点“测试连接”，成功后点“保存并进入”，然后使用账户登录。连接配置和登录令牌保存在当前电脑的 Electron 本地存储中，密码不会保存。

生产前端构建：

```powershell
Set-Location apps/desktop
npm.cmd run build
```

## 核心能力

- 账户与登录：首次创建唯一主账号；所有账号验证当前密码后可修改自己的密码；主账号可新建、启停普通账户并重置其他账号的密码。
- 目录权限：对普通账户配置目录“可见”和“可操作”授权，授权自动继承到全部子目录；主账号默认拥有全部权限。
- MinIO 配置：集成在“连接设置”中，仅主账号可配置服务端访问地址、客户端公开地址、访问密钥和 bucket；保存前会验证连接，Secret Key 加密落库且接口不返回明文。
- 任意层级文件夹：创建、进入、重命名、移动、软删除、恢复；回收站中可彻底删除文件夹及其全部内容。
- 资产上传：拖拽或选择多个文件；客户端显示逐文件进度；服务端流式写入 MinIO 并计算 SHA-256。
- 资产管理：所有查询、预览、下载和写操作均在 API 服务端执行目录权限校验。
- 操作日志：记录真实账户、对象名称、目录路径、重命名前后值和 JSON 详情，可按目录、文件名和账号组合筛选。
- 检索筛选：名称搜索、类型筛选、时间/大小 API 筛选、最近修改/创建时间/名称/大小排序。
- 预览与打开：图片缩略图；视频与音频原生播放；PDF 内嵌查看；TXT/Markdown 文本查看；双击资产会下载到系统临时目录并使用默认应用打开。
- 3D 与其他生产文件：V1 支持安全存储、分类、详情和下载，不提供 3D 在线渲染。
- 持久化：元数据在 PostgreSQL，原文件与缩略图在 MinIO，重启容器不会丢失。

## 配置

复制 `.env.example` 为 `.env` 后可修改以下项目：

| 变量 | 默认值 | 用途 |
| --- | --- | --- |
| `MOFANG_API_PORT` | `5080` | 主机 API 端口 |
| `POSTGRES_PORT` | `5432` | 主机 PostgreSQL 端口 |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | `mofang` / `mofang` / 开发口令 | 数据库连接 |
| `MINIO_API_PORT` / `MINIO_CONSOLE_PORT` | `9000` / `9001` | MinIO 端口 |
| `MINIO_SERVICE_HOST` / `MINIO_SERVICE_PORT` | `mofang-minio` / `9000` | API 启动时使用的 MinIO 服务端默认地址 |
| `MINIO_PUBLIC_HOST` | `localhost` | 桌面端能够访问的 MinIO 主机名或 IP |
| `MINIO_USE_SSL` | `false` | 启动默认地址是否使用 HTTPS |
| `MINIO_ACCESS_KEY` / `MINIO_SECRET_KEY` | 开发凭据 | MinIO 登录凭据 |
| `MINIO_ASSET_BUCKET` / `MINIO_THUMBNAIL_BUCKET` | `mofang-assets` / `mofang-thumbnails` | 对象 bucket |

这些变量是数据库尚无主账号配置时的启动默认值。主账号登录后可在“连接设置”中更新 MinIO 配置并立即生效。局域网使用时，客户端公开地址必须是其他电脑可访问的服务器 IP 或域名，不能保留 `localhost`。

### 独立部署 MinIO

MinIO 可以不与本项目部署在同一台机器。只启动 PostgreSQL 与 API：

```powershell
docker compose up -d mofang-postgres mofang-api
```

API 在 MinIO 暂时不可用时仍会启动，方便首次创建主账号并进入“连接设置”填写独立服务的 HTTP(S) 地址、凭据和 bucket。服务端访问地址供 API 使用；客户端公开地址会写入签名预览/下载链接，必须能被桌面客户端直接访问。切换 MinIO 不会自动迁移历史对象，已有对象需在 MinIO 侧保留相同 bucket 与对象路径或先完成迁移。

## 开发与验证

```powershell
dotnet restore Mofang.Dam.slnx
dotnet build Mofang.Dam.slnx
dotnet test Mofang.Dam.slnx

Set-Location apps/desktop
npm.cmd run lint
npm.cmd run build
```

生成离线烟测素材（默认写入系统临时目录的 `mofang-dam-smoke`）：

```powershell
python tests/generate-smoke-assets.py
```

### 构建免安装 ZIP

在 `apps/desktop` 目录执行 `npm ci` 后，Windows 上运行 `npm run pack:win`，macOS 上运行 `npm run pack:mac`。产物写入 `apps/desktop/release/`。macOS 通用包需要在 macOS 上合并双架构并签名。

推送与 `apps/desktop/package.json` 版本一致的 Git 标签（如 `v1.1.0`），会触发 `.github/workflows/release-desktop.yml`。工作流在 Windows 和 macOS 构建机上分别构建、解压并执行启动烟测，全部通过后发布两个 ZIP、使用说明和 `SHA256SUMS.txt`。手动运行工作流只生成 Actions 构建产物，不创建 Release。已有 Release 不会被重新运行覆盖。

主要项目：

- `src/Mofang.Api`：HTTP API、健康检查、上传与下载入口。
- `src/Mofang.Application`：应用边界与 `IAssetStorage` 抽象。
- `src/Mofang.Domain`：文件夹、资产、版本、存储对象与日志实体。
- `src/Mofang.Infrastructure`：EF Core、PostgreSQL、MinIO、后台缩略图队列。
- `apps/desktop`：Electron、React、TypeScript、Vite 桌面端。
- `docs/architecture.md`：领域、存储键、软删除与部署设计。
- `docs/design-system.md`：界面概念图、视觉令牌和交互规则。

## 主要 API

- `GET /api/info`、`GET /api/health`
- `GET /api/auth/setup-status`、`POST /api/auth/setup|login|refresh|change-password`、`GET /api/auth/session`
- `GET|POST|PUT /api/admin/accounts/*`（仅主账号）
- `GET|PUT /api/admin/storage/minio`、`POST /api/admin/storage/minio/test`（仅主账号）
- `GET|POST /api/folders`
- `PUT /api/folders/{id}/name`、`PUT /api/folders/{id}/move`
- `DELETE /api/folders/{id}`、`POST /api/folders/{id}/restore`、`DELETE /api/folders/{id}/permanent`
- `GET /api/assets`、`GET /api/assets/{id}`、`POST /api/assets/upload`
- `GET /api/assets/{id}/preview|thumbnail|download|text`
- `PUT /api/assets/{id}/name|move`、`POST /api/assets/{id}/copy|restore`
- `DELETE /api/assets/{id}`、`DELETE /api/assets/{id}/permanent`
- `GET /api/operations?folderId=&directory=&fileName=&accountId=`、`GET /api/storage/summary`

## 数据与停机

普通停机不会删除数据：

```powershell
docker compose down
```

再次执行 `docker compose up -d` 后，命名卷中的 PostgreSQL、MinIO 数据和令牌加密密钥仍会恢复。只有确定要清空全部资产、账户与元数据时才使用 `docker compose down -v`。

## 常见问题

- 连接测试失败：先确认 `docker compose ps` 中 PostgreSQL 与 MinIO 为 healthy，再访问 `/api/health`。
- 端口冲突：在 `.env` 修改主机侧端口，然后同步修改桌面端连接配置。
- 局域网能访问 API 但预览失败：检查 `MINIO_PUBLIC_HOST` 是否为客户端可达地址，并允许 9000 端口通过防火墙。
- 图片刚上传没有缩略图：缩略图由后台队列生成，稍候刷新；任务状态会持久化并在服务重启后继续处理。
- 登录后立即返回 403：账号可能已被主账号停用，或没有对应目录的权限。
- 局域网生产部署应在反向代理上启用 HTTPS；Bearer 令牌不应通过明文网络传输。
