# TCGCardDem0-Backend

游戏客户端仓库 [TCGGameDem0](https://github.com/AChen1111/TCGGameDem0) 以子模块形式挂在 `Backend/`。本仓库独立管理 HTTP API、浏览器管理台 Razor Pages，以及 React 运营台 `AdminWeb/`。

## 运行 API

需要 .NET 8。监听地址默认 `http://127.0.0.1:5080`。`Auth:SigningKey` 与 `ContentDelivery:PublishKey` 长度都必须 ≥ 32。

```powershell
$env:Auth__SigningKey = "<至少32字符>"
$env:ContentDelivery__PublishKey = "<至少32字符>"
dotnet run --project src/AChen.Backend.Api
```

| 变量 / 配置键 | 用途 |
| --- | --- |
| `ACHEN_BACKEND_AUTH_SIGNING_KEY` / `Auth:SigningKey` | JWT 签名密钥 |
| `ACHEN_CONTENT_PUBLISH_KEY` / `ContentDelivery:PublishKey` | 内容与配置发布密钥 |
| `ConnectionStrings:Default` | SQLite 路径，相对后端 ContentRoot |
| `ContentDelivery:StorageRoot` | 内容文件根目录 |

启动后会执行 EF Core 迁移。SQLite 默认 `src/AChen.Backend.Api/Data/achen.db`，内容文件默认 `Data/content`，都不进 Git。

健康检查：`GET /health`。就绪：`GET /ready`。管理台：`http://127.0.0.1:5080/admin/content/login`。接口说明见 [docs/api/README.md](docs/api/README.md)。

游戏仓库 `mBulid` 使用共享内容协议 5，发送带 `contentVersion` 的 ZIP 到 `PUT /api/dev/content/Android` 或 `StandaloneWindows64`。Player 内容保存为 `Data/content/current/<平台>/<版本_安卓或win>/`，热更 DLL 路径为 `HybridCLR/HotUpdate.dll`，每次成功发布生成新的 GUID `contentId`。下载地址仍按 GUID 验证当前版本，兼容读取旧 GUID 存储目录。数据库继续保存 Manifest JSON，不新增表。

发布经鉴权与请求参数校验后，在锁内先删除目标平台旧目录和当前记录，再接收新包。上传中断、归档或文件校验失败后没有当前版本，最新内容接口返回 404；不恢复旧版，不自动重发。删除失败会终止上传。其他平台及独立 `PUT /api/dev/editor-config` 发布的 Editor 配置保留。Editor 本地启动不请求后端最新内容。

## AdminWeb

独立 Vite 应用，默认 `http://127.0.0.1:5173`，通过 CORS 访问 5080。

```powershell
cd AdminWeb
npm install
npm run dev
```

作为游戏仓库子模块时，路径是 `Backend/AdminWeb`。

## 共享契约

作为游戏仓库子模块构建时，API 工程直接链接游戏仓库 `Assets/Shared/Configuration` 及生成的 CardRow、TranslationRow；`Shared/` 旧副本已排除编译。发布协议和配置格式共用这些纯 C# 契约，变更后需同步验证客户端主包与后端分发。

## 测试

```powershell
dotnet test AChen.Backend.sln
```

## 给游戏仓库升级子模块

游戏侧 `.gitmodules` 跟踪本仓库 `main`。升级：

```powershell
git submodule update --remote Backend
git add Backend
git commit
```
