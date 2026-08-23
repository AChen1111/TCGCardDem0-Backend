# AChen backend

ASP.NET Core 8 模块化单体，当前包含账号认证和 `ContentDelivery` 两个 feature。内容模块负责完整 Release 的上传、校验、不可变存储、下载、版本切换与回滚；Unity 仍负责构建 Addressables 与 HybridCLR。

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

服务监听 `http://127.0.0.1:5080`，启动时自动执行 EF Core migration。可用性检查为 `GET /health` 和 `GET /ready`；`/ready` 会同时探测 SQLite 与内容存储目录。管理后台位于 `http://127.0.0.1:5080/admin/content`。

SQLite 默认位于 `Backend/src/AChen.Backend.Api/Data/achen.db`，内容文件默认位于 `Backend/src/AChen.Backend.Api/Data/content`，两者均被 Git 忽略。生产环境应使用 HTTPS 反向代理，并将存储目录放在持久卷上。

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
