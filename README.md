# AChen backend

ASP.NET Core 8 模块化单体，当前包含账号认证、`Players`、`GameConfig` 和 `ContentDelivery`。玩家模块保存基础资料与服务器权威金币；游戏配置模块以同一 Revision 发布头像与卡包快照；内容模块负责完整 Release 的上传、校验、不可变存储、下载、版本切换与回滚。

## 本地启动

提交的配置不包含任何密钥。首次运行请写入 .NET User Secrets：

```powershell
$authBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($authBytes)
$authKey = [Convert]::ToBase64String($authBytes)

$publishBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($publishBytes)
$publishKey = [Convert]::ToBase64String($publishBytes)

dotnet user-secrets set "Auth:SigningKey" $authKey --project Backend/src/AChen.Backend.Api
dotnet user-secrets set "ContentDelivery:PublishKey" $publishKey --project Backend/src/AChen.Backend.Api
```

也可用环境变量 `ContentDelivery__PublishKey` 向后端提供发布密钥。Unity Editor 发布工具读取同一值时使用单独的环境变量 `ACHEN_CONTENT_PUBLISH_KEY`；密钥不得写入仓库或 EditorPrefs。

启动：

```powershell
dotnet run --project Backend/src/AChen.Backend.Api --launch-profile http
```

服务监听 `http://127.0.0.1:5080`，启动时自动执行 EF Core migration。可用性检查为 `GET /health` 和 `GET /ready`；`/ready` 会同时探测 SQLite 与内容存储目录。内容后台位于 `http://127.0.0.1:5080/admin/content`，头像与卡包配置位于 `http://127.0.0.1:5080/admin/game-config`，两者复用发布密钥登录。

SQLite 默认位于 `Backend/src/AChen.Backend.Api/Data/achen.db`，内容文件默认位于 `Backend/src/AChen.Backend.Api/Data/content`，两者均被 Git 忽略。生产环境应使用 HTTPS 反向代理，并将存储目录放在持久卷上。

## 玩家数据 API

账号注册时会创建一份一对一的 `PlayerProfile`；迁移前已存在的账号会在首次读取时自动补建。第一版字段为 `Id`、`Nickname`、可空整数 `AvatarId`、`Gold`、`Revision` 和创建/更新时间，不包含等级与经验。

| 方法 | 路由 | 用途 |
| --- | --- | --- |
| GET | `/api/player/bootstrap` | 读取当前 JWT 用户的完整玩家基础数据 |
| PATCH | `/api/player/profile` | 更新昵称和头像，必须提交 `expectedRevision` |

金币是服务器权威字段，当前不提供客户端修改接口。头像修改必须引用当前已发布且启用的头像，否则返回 `422 AVATAR_NOT_AVAILABLE`；已经选择但后来禁用的头像仍可读取。资料更新若使用过期 Revision，返回 `409 PLAYER_DATA_CHANGED`；Unity 可通过 `AuthClient.GetPlayerAsync()` 和 `UpdatePlayerProfileAsync()` 访问，401 时会自动刷新一次 Token。

## 游戏配置

`GameConfig` 使用一个可编辑草稿和多个不可变 Published Revision。后台保存头像或卡包时递增 `EditRevision`；点击“发布整份配置”后，当前草稿原子变为 Published，并完整复制为下一个 Revision 的新草稿。发布过的 ID 只能禁用，不能删除。

公共接口为 `GET /api/game-config/bootstrap`，不要求玩家 JWT。响应包含 `schemaVersion=1`、Revision、发布时间、全部头像和卡包；禁用项也会返回，以便解析玩家已经选择的旧头像。接口支持：

- `If-None-Match: "game-config-{revision}"`，未变化返回 `304`。
- `ETag`、`X-Game-Config-Revision`、`X-Server-Time`。
- `Cache-Control: public, max-age=0, must-revalidate`。
- 尚未首次发布时返回 `404 GAME_CONFIG_NOT_PUBLISHED`。

Unity 在进入主界面前由 `GameConfigManager` 加载按后端地址隔离的本地缓存并向服务端校验；启动后每 5 分钟、恢复前台和打开商店时按需检查。无网络但存在缓存时使用最后可用 Revision，没有缓存则停止进入依赖配置的界面。商店根据服务器时间过滤卡包，并使用 `CoverResourceKey` 交给 `AddressableLoader` 加载封面。

### 配置工作台、CSV 与 Git

`/admin/game-config` 提供头像和卡包的搜索、筛选、逐项编辑、整份 CSV 导入导出和 Git 历史界面。CSV 使用固定表头 `Table,Id,Name,ResourceKey,PriceGold,StartsAt,EndsAt,SortOrder,IsEnabled`，一个文件同时包含 `Avatar` 与 `CardPack` 行；导入限制为 5 MiB，所有行校验通过后才会原子替换草稿。导入或载入旧 Git 快照时，历史上已经发布但文件中缺失的 ID 会自动保留为停用，避免破坏旧玩家数据。

Git 使用独立配置仓库，不会操作项目源码仓库。默认仓库位于 `Data/game-config-git`：

```json
{
  "GameConfigGit": {
    "RepositoryRoot": "Data/game-config-git",
    "Branch": "main",
    "RemoteName": "origin",
    "RemoteUrl": "",
    "HistoryLimit": 30
  }
}
```

配置 `GameConfigGit__RemoteUrl` 后，后台可以把当前草稿规范化为 `game-config.csv + manifest.json`，创建 Git commit 并一键 push；远端拉取只接受 fast-forward。历史版本的“载入草稿”不会直接改变 Published Revision，仍需管理员检查并点击“发布整份配置”。HTTP(S) 远端 URL 禁止包含 Token 或密码，请在服务器上配置 Git credential helper 或 SSH 凭据，凭据不会进入 HTML、日志或配置快照。

## ContentDelivery 配置

```json
{
  "ContentDelivery": {
    "StorageRoot": "Data/content",
    "PublishKey": "",
    "MaxArchiveBytes": 2147483648,
    "MaxExpandedBytes": 4294967296,
    "MaxFileCount": 10000,
    "AllowedChannels": ["development"]
  }
}
```

第一版仅支持 `development`，平台为 `StandaloneWindows64`、`Android`、`iOS`。内容版本必须是严格 SemVer；`platform + appVersion + contentVersion` 唯一且 Ready Release 永久不可覆盖。元数据保存在 SQLite，物理存储通过 `IContentStorage` 隔离，当前实现为本机磁盘。

发布包根目录：

```text
release-manifest.json
HybridCLR/HotUpdate.dll.bytes
Addressables/catalog_*.bin
Addressables/catalog_*.hash
Addressables/*.bundle
```

`release-manifest.json` 的 `schemaVersion` 为 `1`，声明 Release 身份、三个关键路径及每个文件的路径、长度和 SHA-256。服务端拒绝路径穿越、绝对路径、符号链接、重复/未声明/缺失文件以及超限压缩包；验证完成后同盘原子移动到 `releases/{releaseId}`。

## 内容 API

管理 API 使用 `X-Content-Publish-Key`，不接受玩家 JWT：

| 方法 | 路由 | 用途 |
| --- | --- | --- |
| POST | `/api/content/releases` | 创建 Release |
| PUT | `/api/content/releases/{id}/artifact` | 上传 ZIP，需 `X-Artifact-Sha256` |
| GET | `/api/content/releases` | 分页及条件查询 |
| GET | `/api/content/releases/{id}` | Release 与文件明细 |
| DELETE | `/api/content/releases/{id}` | 删除未完成或失败版本 |
| GET/PUT | `/api/content/active-releases/{channel}/{platform}/{appVersion}` | 读取/切换当前版本 |
| GET | `/api/content/publications` | 发布与回滚历史 |

公共 API：

| 方法 | 路由 | 用途 |
| --- | --- | --- |
| GET | `/api/content/manifests/latest?...` | 获取当前完整 Release 清单，`no-store` |
| GET/HEAD | `/content/releases/{releaseId}/{relativePath}` | Range、ETag、一年 immutable 缓存下载 |

所有业务错误返回 Problem Details，并包含稳定 `code` 与响应头 `X-Request-Id`。切换当前版本时必须传 `expectedCurrentReleaseId`；它与实际指针不一致会返回 `409 ACTIVE_RELEASE_CHANGED`。

## Unity 发布与启动

在 Unity 中打开 `Window > AChen > 后端服务`，点击“一键启动后端服务”。工具会先增量构建 ASP.NET Core 项目，再启动 `http://127.0.0.1:5080`；同一按钮会切换为“关闭后端服务”。窗口显示当前状态和最近 200 行构建/运行日志，并可直接打开注册页。也可以使用 `Tools > AChen > 启动后端服务` 和 `Tools > AChen > 关闭后端服务`。

工具只会终止由自己启动且通过 PID 与启动时间校验的进程。若 5080 已被其他程序占用，只显示“检测到外部服务”，不会误杀。正常退出 Unity Editor 时会关闭受管后端；进程标记只写入本机 `Library`。发布密钥优先读取 `ACHEN_CONTENT_PUBLISH_KEY`，未配置时仅为当前 Editor 会话生成并注入内存，不写入 EditorPrefs、源码或日志。

在 Unity 中打开 `Tools > HotUpdate > Build And Publish Release`：

1. 选择当前 BuildTarget，并确认 `PlayerSettings.bundleVersion`。
2. 输入手工 SemVer 内容版本及可选备注。
3. 配置后端地址；发布密钥来自 `ACHEN_CONTENT_PUBLISH_KEY`，或仅在当前窗口内输入。
4. 工具构建 Addressables、编译 HybridCLR、生成逐文件 SHA-256 ZIP、创建并上传 Release，最后切换 development 指针。

Player 默认访问 `http://127.0.0.1:5080`。启动时按 `Application.version + platform + development` 获取清单，将 DLL 缓存在 `persistentDataPath/Content/{releaseId}`，每次加载前验证长度与 SHA-256，然后在 Addressables 初始化前注入 `{AChen.ContentBaseUrl}`。网络失败会重试两次；校验失败会停止启动。Editor 默认继续使用本地程序集和本地 Addressables，可在 `LoadDll` 组件勾选远端内容模式。

旧 Python CDN 与 `Publish Code To Local CDN`、`Start Local CDN` 菜单已移除。现有 `ServerData` 不会自动导入；请通过新窗口重新构建并发布首个 Release。

## 验证

```powershell
dotnet test Backend/AChen.Backend.sln
dotnet build Backend/AChen.Backend.sln -c Release
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build HotUpdate.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
dotnet build HotUpdate.Editor.csproj --no-restore
```

Unity Editor 已打开时，应从 Test Runner 执行 EditMode 测试；不要另开一个使用同一项目目录的 batchmode Editor。
