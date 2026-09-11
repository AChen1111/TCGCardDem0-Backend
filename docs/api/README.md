# 后端 API

本地地址：`http://127.0.0.1:5080`。JSON 请求使用 `Content-Type: application/json`。错误通常返回 `application/problem+json`，带 `code` 与 `traceId`；响应头 `X-Request-Id` 与 `traceId` 相同，用于追踪。

管理台页面与环境变量见 [日常操作](../../project/operations.md)。完整可执行示例见 `Backend/src/AChen.Backend.Api/AChen.Backend.Api.http`。

## 鉴权

| 方式 | 凭证 | 用在哪 |
| --- | --- | --- |
| 无 | — | 健康检查、注册登录刷新、游戏配置引导、Manifest、Release 文件 |
| Bearer | `Authorization: Bearer <access-token>` | 当前用户、玩家资料、购买 |
| Publish Key | `X-Content-Publish-Key: <publish-key>` | 配置草稿/发布、内容 Release、账号金币 |

浏览器管理台用发布密钥换 Cookie `AChen.ContentAdmin`，不走这张表的 Bearer。`/register` 是账号注册页，与 JSON 注册接口并行。

## 路由

| 方法 | 路径 | 鉴权 | 用途 |
| --- | --- | --- | --- |
| GET | `/health` | 无 | 进程存活 |
| GET | `/ready` | 无 | 数据库与内容存储就绪 |
| POST | `/api/auth/register` | 无 | 注册并返回 Token、用户和玩家 |
| POST | `/api/auth/login` | 无 | 用户名登录 |
| POST | `/api/auth/refresh` | 无 | 刷新并轮换 Token |
| POST | `/api/auth/logout` | 无 | 吊销 Refresh Token |
| GET | `/api/auth/me` | Bearer | 当前用户 |
| GET | `/api/player/bootstrap` | Bearer | 当前玩家（头像、壁纸、金币、已拥有列表） |
| PATCH | `/api/player/profile` | Bearer | 改昵称、当前头像和背景；不能改金币或已拥有列表 |
| POST | `/api/player/purchase` | Bearer | 购买头像或壁纸；价格由已发布配置计算 |
| GET | `/api/accounts/admin/gold?username=` | Publish Key | 按账号查金币 |
| POST | `/api/accounts/admin/gold` | Publish Key | 给指定账号加金币 |
| GET | `/api/game-config/bootstrap` | 无 | 已发布的头像、壁纸、卡包；支持 `If-None-Match` |
| GET | `/api/game-config/admin/draft` | Publish Key | 当前草稿与修订号 |
| PUT | `/api/game-config/admin/draft` | Publish Key | 整表替换草稿 |
| POST | `/api/game-config/admin/publish` | Publish Key | 把当前草稿发布给客户端 |
| POST | `/api/content/releases` | Publish Key | 创建内容 Release |
| PUT | `/api/content/releases/{id}/artifact` | Publish Key | 上传 `application/zip` |
| GET | `/api/content/releases` | Publish Key | 分页查询 Release |
| GET | `/api/content/releases/{id}` | Publish Key | Release 详情 |
| DELETE | `/api/content/releases/{id}` | Publish Key | 删除未使用的 Release |
| GET | `/api/content/active-releases/{channel}/{platform}/{appVersion}` | Publish Key | 当前活动 Release |
| PUT | `/api/content/active-releases/{channel}/{platform}/{appVersion}` | Publish Key | 切换活动 Release |
| GET | `/api/content/publications` | Publish Key | 分页查询发布历史 |
| GET | `/api/content/manifests/latest` | 无 | 最新 Manifest；查询参数 `channel`、`platform`、`appVersion` 必填 |
| GET/HEAD | `/content/releases/{id}/{relativePath}` | 无 | 下载不可变内容文件 |

`GET /api/game-config/bootstrap` 命中相同 ETag 时返回 `304`。响应头带 `ETag`、`X-Game-Config-Revision`、`X-Server-Time`。

平台取值：`StandaloneWindows64`、`Android`、`iOS`。默认频道 `development`。列表接口支持 `page`、`pageSize` 及对应筛选参数。

## 常用请求体

注册 / 登录（无邮箱字段；登录只认用户名，不认邮箱）：

```json
{
  "username": "LocalPlayer",
  "password": "correct-horse-42"
}
```

用户名 `3-24` 位英文、数字或下划线。密码 `8-128` 位；注册时必须同时包含字母与非字母字符。

更新玩家资料：

```json
{
  "nickname": "Local Player",
  "avatarId": 1,
  "backgroundId": 1,
  "expectedRevision": 0
}
```

玩家数据含 `avatarId`、`ownedAvatarIds`、`backgroundId`、`ownedBackgroundIds`、`gold`、`revision`。注册默认昵称等于账号、`avatarId` 为 0、`backgroundId` 为 1，并拥有头像 0 与壁纸 1，`gold` 为 0。`PATCH /api/player/profile` 只能装备已拥有且已发布在售的头像或壁纸。

购买商品：

```json
{
  "catalogType": "avatar",
  "itemId": 2,
  "expectedRevision": 0
}
```

`catalogType` 仅 `avatar` 和 `wallpaper`。卡包会出现在配置引导和大厅列表里，购买接口尚未接入。价格取已发布配置的 `priceGold`，请求体不能指定金额。购买成功只加入拥有列表，不自动装备。

给账号增加金币：

```json
{
  "username": "AChen1234",
  "amount": 100000
}
```

`amount` 必须大于 0。

替换配置草稿：

```json
{
  "expectedEditRevision": 0,
  "avatars": [
    {
      "id": 0,
      "name": "默认头像",
      "resourceKey": "a_00",
      "priceGold": 0,
      "sortOrder": 0,
      "isEnabled": true,
      "startsAt": null,
      "endsAt": null
    }
  ],
  "wallpapers": [],
  "cardPacks": []
}
```

`expectedEditRevision` 必须等于当前草稿的编辑修订；冲突返回 `GAME_CONFIG_CHANGED`。已发布过的条目不能从草稿删除，返回 `PUBLISHED_CONFIG_ITEM_CANNOT_BE_DELETED`。

发布配置：

```json
{
  "expectedEditRevision": 1
}
```

发布成功后客户端 `bootstrap` 拿到新 `revision`；服务端生成下一份草稿。

创建内容 Release：

```json
{
  "platform": "StandaloneWindows64",
  "appVersion": "0.1.0",
  "contentVersion": "0.1.1",
  "notes": "release notes"
}
```

切换活动 Release：

```json
{
  "releaseId": "00000000-0000-0000-0000-000000000000",
  "expectedCurrentReleaseId": null
}
```

上传 ZIP 时可附加 `X-Artifact-Sha256`。Content-Type 必须是 `application/zip`。

## 错误码

响应体形如：

```json
{
  "title": "金币不足",
  "status": 422,
  "code": "INSUFFICIENT_GOLD",
  "traceId": "0H..."
}
```

字段校验失败时 `code` 为 `VALIDATION_ERROR`，并带 `errors` 字典。未捕获异常为 `500` / `INTERNAL_ERROR`，不回传内部细节。

| code | HTTP | 含义 |
| --- | --- | --- |
| `VALIDATION_ERROR` | 422 | 字段格式不合法 |
| `HTTP_ERROR` | 与状态码相同 | 框架层 HTTP 错误（方法、体积、类型等） |
| `RATE_LIMITED` | 429 | 触发限流；`Retry-After: 60` |
| `INTERNAL_ERROR` | 500 | 未处理异常 |
| `ACCOUNT_EXISTS` | 409 | 用户名已注册 |
| `INVALID_CREDENTIALS` | 401 | 账号或密码错误 |
| `INVALID_ACCESS_TOKEN` | 401 | Access Token 无效或用户不存在 |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh Token 无效、过期或已轮换 |
| `INVALID_CONTENT_PUBLISH_KEY` | 401 | 发布密钥错误 |
| `PLAYER_DATA_CHANGED` | 409 | `expectedRevision` 与当前玩家资料不一致 |
| `AVATAR_NOT_OWNED` / `WALLPAPER_NOT_OWNED` | 422 | 装备了未拥有的外观 |
| `AVATAR_NOT_AVAILABLE` / `WALLPAPER_NOT_AVAILABLE` | 422 | 外观不存在、未启用或不在售卖窗口 |
| `ITEM_ALREADY_OWNED` | 422 | 重复购买 |
| `INSUFFICIENT_GOLD` | 422 | 金币不足 |
| `ACCOUNT_NOT_FOUND` | 404 | 运营接口找不到账号 |
| `INVALID_USERNAME` | 400 | 运营接口账号为空 |
| `INVALID_GOLD_AMOUNT` | 400 | 加金币数量不大于 0 |
| `GOLD_OVERFLOW` | 400 | 金币超出上限 |
| `GAME_CONFIG_NOT_PUBLISHED` | 404 | 还没有发布过配置 |
| `GAME_CONFIG_CHANGED` | 409 | 配置草稿 `expectedEditRevision` 过期 |
| `AVATAR_NOT_FOUND` / `WALLPAPER_NOT_FOUND` / `CARD_PACK_NOT_FOUND` | 404 | 草稿里没有该条目 |
| `PUBLISHED_CONFIG_ITEM_CANNOT_BE_DELETED` | 422 | 已发布条目不能从草稿删除 |
| `CONTENT_RELEASE_EXISTS` | 409 | 同平台同应用版本的 Release 已存在 |
| `CONTENT_RELEASE_NOT_FOUND` | 404 | Release 不存在 |
| `ACTIVE_CONTENT_RELEASE_NOT_FOUND` | 404 | 该渠道/平台/版本没有活动 Release |
| `CONTENT_RELEASE_NOT_READY` | 409 | 未就绪不能设为活动版本 |
| `CONTENT_RELEASE_IMMUTABLE` | 409 | 已就绪不能删除 |
| `CONTENT_RELEASE_TARGET_MISMATCH` | 409 | 切换活动版本时平台或应用版本对不上 |
| `ACTIVE_RELEASE_CHANGED` | 409 | 活动版本并发冲突 |
| `CONTENT_FILE_NOT_FOUND` | 404 | 内容文件不存在 |
| `CONTENT_ARCHIVE_TOO_LARGE` | 413 | 压缩包超过上限 |
| `CONTENT_ARCHIVE_EXPANDED_TOO_LARGE` | 413 | 解压后超过上限 |
| `CONTENT_ARCHIVE_FILE_LIMIT` | 413 | 文件数超过上限 |
| `CONTENT_ARCHIVE_HASH_MISMATCH` | 422 | 与 `X-Artifact-Sha256` 不一致 |
| `INVALID_CONTENT_PACKAGE` | 422 | 包结构不合法 |
| `RELEASE_ARTIFACT_CONFLICT` | 409 | 产物状态冲突 |
| `GIT_NOT_AVAILABLE` | 503 | 本机没有可用 git |
| `GIT_REPOSITORY_NOT_MANAGED` | 503 | 配置 Git 仓库不可用 |
| `GIT_REMOTE_NOT_CONFIGURED` | 503 | 未配置远端 |
| `GIT_WORKTREE_NOT_CLEAN` | 422 | 工作区不干净，不能拉取 |
| `GIT_COMMIT_INVALID` | 422 | 提交标识无效 |
| `GIT_SNAPSHOT_INVALID` | 422 | 快照无法还原为草稿 |
| `GIT_COMMAND_FAILED` | 503 | git 命令失败 |
