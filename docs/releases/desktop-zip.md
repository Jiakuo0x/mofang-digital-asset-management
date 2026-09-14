魔方数字资产管理桌面客户端免安装版。下载对应系统的 ZIP，完整解压后即可运行，无需安装 Node.js、.NET SDK 或 Docker。

| 下载文件 | 适用系统 | 启动方式 |
| --- | --- | --- |
| `Mofang-<版本>-windows-x64.zip` | Windows x64 | 完整解压后双击 `Mofang.exe`，保留同目录的全部文件 |
| `Mofang-<版本>-macos-universal.zip` | macOS，兼容 Intel 和 Apple 芯片 | 解压后双击 `魔方数字资产管理.app`，也可拖入“应用程序” |

首次运行填写已部署的 API 服务器地址与端口（默认 `5080`），测试连接后保存。ZIP 包含桌面客户端；API、PostgreSQL 与 MinIO 仍需单独部署，详见[部署说明](https://github.com/Jiakuo0x/mofang-digital-asset-management#readme)。

macOS 版使用临时签名，尚未经过 Apple 公证。首次打开如被系统拦截，可在“系统设置 → 隐私与安全性”对该应用选择“仍要打开”并确认。Windows 版未配置发布者代码签名。

发布前已在各平台解压 ZIP 并验证客户端启动、本地页面、预加载桥接、图片资源和连接测试交互；macOS 额外验证 Intel/ARM 双架构与应用签名。API 连接交互使用测试响应，不代表已对用户的实际服务器进行联网验证。

附带中文使用说明 `README.txt` 和 `SHA256SUMS.txt`。配置与登录信息保存在当前操作系统用户目录；更新客户端不会删除服务器上的资产数据。
