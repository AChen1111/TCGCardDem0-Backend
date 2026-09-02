# 后端 API

本地地址：`http://127.0.0.1:5080`。JSON 请求使用 `Content-Type: application/json`。错误通常返回 `application/problem+json`，响应头 `X-Request-Id` 可用于追踪。

## 鉴权

- 玩家接口：`Authorization: Bearer <access-token>`。
- 内容管理接口：`X-Content-Publish-Key: <publish-key>`。
- 公开接口：健康检查、游戏配置、内容 Manifest 和 Release 文件。

## 路由

| 方法 | 路径 | 鉴权 | 用途 |
| --- | --- | --- | --- |
| GET | `/health` | 无 | 进程存活检查 |
| GET | `/ready` | 无 | 数据库与内容存储就绪检查 |
| POST | `/api/auth/register` | 无 | 注册并返回 Token、用户和玩家 |
| POST | `/api/auth/login` | 无 | 用户名或邮箱登录 |
| POST | `/api/auth/refresh` | 无 | 刷新并轮换 Token |
| POST | `/api/auth/logout` | 无 | 吊销 Refresh Token |
| GET | `/api/auth/me` | Bearer | 当前用户 |
| GET | `/api/player/bootstrap` | Bearer | 当前玩家数据（含当前头像和已拥有头像列表） |
| PATCH | `/api/player/profile` | Bearer | 更新昵称、当前头像和背景图；不能改已拥有头像列表 |
| GET | `/api/game-config/bootstrap` | 无 | 已发布的头像和卡包配置；支持 `If-None-Match` |
| POST | `/api/content/releases` | Publish Key | 创建内容 Release |
| PUT | `/api/content/releases/{id}/artifact` | Publish Key | 上传 `application/zip` 内容包 |
| GET | `/api/content/releases` | Publish Key | 分页查询 Release |
| GET | `/api/content/releases/{id}` | Publish Key | Release 详情 |
| DELETE | `/api/content/releases/{id}` | Publish Key | 删除未使用的 Release |
| GET | `/api/content/active-releases/{channel}/{platform}/{appVersion}` | Publish Key | 当前活动 Release |
| PUT | `/api/content/active-releases/{channel}/{platform}/{appVersion}` | Publish Key | 切换活动 Release |
| GET | `/api/content/publications` | Publish Key | 分页查询发布历史 |
| GET | `/api/content/manifests/latest` | 无 | 获取最新内容 Manifest |
| GET/HEAD | `/content/releases/{id}/{relativePath}` | 无 | 下载不可变内容文件 |

## 常用请求体

注册：

```json
{
  "username": "LocalPlayer",
  "email": "local@example.com",
  "password": "correct-horse-42"
}
```

更新玩家资料：

```json
{
  "nickname": "Local Player",
  "avatarId": 1,
  "backgroundId": 1,
  "expectedRevision": 0
}
```

玩家数据含 `avatarId`（当前头像）和 `ownedAvatarIds`（已拥有头像 ID 列表）。注册默认昵称等于账号、`avatarId` 为 0、`backgroundId` 为 1，并拥有头像 0。`PATCH /api/player/profile` 只能装备已拥有且已发布的头像，不能改拥有列表。

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

上传 ZIP 时可附加 `X-Artifact-Sha256`。列表接口支持 `page`、`pageSize` 及对应筛选参数。完整可执行示例见 `Backend/src/AChen.Backend.Api/AChen.Backend.Api.http`。
