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

## AdminWeb

独立 Vite 应用，默认 `http://127.0.0.1:5173`，通过 CORS 访问 5080。

```powershell
cd AdminWeb
npm install
npm run dev
```

作为游戏仓库子模块时，路径是 `Backend/AdminWeb`。

## 共享契约

`src/AChen.Backend.Api/Shared/` 里的 `PublishedGameConfig.cs`、`CardRow.cs`、`TranslationRow.cs` 是游戏仓库 `Assets/Shared` 的副本。改发布格式或表结构时两边一起改，否则结算与客户端展示会不一致。

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
