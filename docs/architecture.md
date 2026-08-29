# 魔方数字资产管理 1.1 架构

## 运行拓扑

```text
Electron host
  └─ React + TypeScript UI
       └─ HTTP API
            ├─ PostgreSQL: folders / assets / versions / storage objects / operation logs
            ├─ MinIO: primary objects + thumbnails
            └─ BackgroundService: pending image thumbnail generation
```

Electron 只保存资产库名称、API 主机与端口，不连接 PostgreSQL，也不持有 MinIO 凭据。Backend 为预览和下载生成短期签名 URL；视频播放器由 MinIO 直接响应 HTTP Range 请求。MinIO 的服务端访问地址与客户端公开地址分别配置，以支持对象存储独立部署和内外网地址不同的场景。

## 身份与目录授权

账户由 ASP.NET Core Identity 管理，桌面端使用短期访问令牌和 14 天刷新令牌。首次启动仅在账户表为空时允许创建一个主账号管理员；普通账户只能由主账号创建。所有账号修改自己的密码时必须验证当前密码并轮换访问/刷新令牌；主账号只能重置其他账号的密码，不能用管理员重置绕过自己的当前密码校验。数据保护密钥保存在独立 Docker 命名卷中，服务重启不会使全部刷新令牌失效。

只有主账号可读取、测试和更新数据库中的 MinIO 配置。数据库配置优先于部署环境默认值，Secret Key 使用 ASP.NET Core Data Protection 加密保存并从不通过 API 返回。保存配置前后端会实际连接 MinIO 并确保两个 bucket 可用；连接失败不写入数据库。MinIO 不可用不会阻止 API 启动，以保留主账号修复配置的入口。

目录权限有“可见”和“可操作”两级，显式授权自动继承到全部子目录，“可操作”包含“可见”。主账号不受目录限制。API 在目录树、资产列表、详情、预览、文本、下载、存储汇总、回收站和所有写操作上统一执行服务端权限检查；界面按钮状态不是安全边界。

## 代码边界

- `Mofang.Domain`：Folder、Asset、AssetVersion、StorageObject、OperationLog 与枚举。
- `Mofang.Contracts`：稳定的 HTTP 请求/响应 DTO。
- `Mofang.Application`：`IAssetStorage`、`IDamService`、缩略图队列抽象及资产类型识别。
- `Mofang.Infrastructure`：EF Core、PostgreSQL、MinIO、业务服务与后台缩略图任务。
- `Mofang.Api`：HTTP 边界、异常到 Problem Details 映射和启动初始化。
- `apps/desktop`：Electron 外壳与 React 资产工作区。

## 数据模型

`Folder` 使用 `ParentId` 表达任意深度目录。`Asset` 表达用户可见业务资产；V1 同时保存当前对象地址用于高频读取。`AssetVersion` 是版本边界，指向独立的 `StorageObject`。后续新增版本时不需要改变资产、文件夹或存储提供方接口。

首次删除采用软删除。对象不会因进入回收站而从 MinIO 删除；恢复只修改逻辑状态和目录归属。只有回收站中的对象可被彻底删除，此时服务端先校验目录操作权限与整棵目录树的删除状态，再清理 MinIO 原文件、缩略图、资产版本、存储对象、目录授权和数据库记录；操作日志仍保留名称、路径与清理数量快照。操作日志的详情以 JSONB 保存。

`accounts` 保存 Identity 凭据和主账号/启用状态，`directory_permissions` 保存显式目录授权。`operation_logs` 同时保存账户 ID 与账号名称快照、对象名、目录 ID/路径、旧名、新名和 JSONB 详情，既保证历史可读性，也支持结构化筛选。

## 上传与预览

ASP.NET Core multipart 缓冲阈值为 64 KiB，超出部分落到临时文件；应用层把文件流直接交给 MinIO，并在读取过程中增量计算 SHA-256，不把大文件整体读入内存。V1 API 和 UI 已隔离出未来的 `UploadSession`、分片、暂停/恢复和秒传扩展点。

图片上传后写入 Pending 状态，后台任务生成最长边不超过 720 × 480 的 WebP 缩略图。视频、图片、音频和 PDF 使用签名预览 URL；TXT/Markdown 通过 Backend 限制为 2 MiB 的文本读取接口。
