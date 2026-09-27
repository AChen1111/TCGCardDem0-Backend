# 后端 API

本地地址：`http://127.0.0.1:5080`。JSON 请求使用 `Content-Type: application/json`。错误通常返回 `application/problem+json`，带 `code` 与 `traceId`；响应头 `X-Request-Id` 与 `traceId` 相同，用于追踪。

管理台页面与环境变量见游戏仓库 [日常操作](https://github.com/AChen1111/TCGGameDem0/blob/v0.4/.doc/project/operations.md)。完整可执行示例见 `src/AChen.Backend.Api/AChen.Backend.Api.http`。

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
| GET | `/api/player/bootstrap` | Bearer | 当前玩家（头像、壁纸、卡牌收藏、金币、已拥有列表） |
| PATCH | `/api/player/profile` | Bearer | 改昵称、当前头像、头像框和背景；不能改金币或已拥有列表 |
| POST | `/api/player/purchase` | Bearer | 购买头像、头像框或壁纸；价格由已发布配置计算 |
| POST | `/api/player/card-draws` | Bearer | 服务端抽卡；先按卡池权重抽卡，再按全局稀有度权重掷 shader |
| GET | `/api/player/decks` | Bearer | 查询当前账号的卡组列表 |
| GET | `/api/player/decks/{id}` | Bearer | 查询当前账号的单套卡组 |
| POST | `/api/player/decks` | Bearer | 创建空卡组草稿 |
| PUT | `/api/player/decks/{id}` | Bearer | 按版本原子替换卡组名称和内容 |
| DELETE | `/api/player/decks/{id}?expectedRevision=…` | Bearer | 删除指定版本的卡组 |
| GET | `/api/gacha/admin/config` | Publish Key | 查看当前抽卡卡池与稀有度权重 |
| PUT | `/api/gacha/admin/config` | Publish Key | 上传 CSV，整表替换抽卡配置并立即生效 |
| GET | `/api/gacha/admin/cards` | Publish Key | 查看全部卡牌目录 |
| PUT | `/api/gacha/admin/cards` | Publish Key | 上传 CSV，整表替换全部卡牌目录并立即生效 |
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

玩家数据含 `avatarId`、`ownedAvatarIds`、`avatarFrameId`、`ownedAvatarFrameIds`、`backgroundId`、`ownedBackgroundIds`、`ownedCards`、`gold`、`ur`、`revision`。`ownedCards` 每项为 `{ cardId, rarity, count }`，`cardId` 为字符串，同一卡牌不同稀有度分开堆叠。注册默认昵称等于账号、`avatarId` 为 1010001，`avatarFrameId` 为 1030001、`backgroundId` 为 1，并拥有头像 1010001、头像框 1030001 与壁纸 1，`ownedCards` 为空，`gold` 和 `ur` 为 0。`PATCH /api/player/profile` 只能装备已拥有且已发布在售的头像或壁纸。

抽卡：

```json
{
  "poolKey": "Card01",
  "count": 1,
  "expectedRevision": 0
}
```

`count` 为 1-10。成功返回 `results: [{ cardId, rarity, sourcePool }]` 与最新 `player`。本次不扣金币。`poolKey` 为 `Card01` / `Card02` / `Card03` 时按分池权重抽；`CardAll` 从全部卡牌目录等权抽取，`sourcePool` 为该卡所属卡包。卡池 CSV 表头为 `Table,PoolKey,CardId,Weight,Rarity`；`Card` 行写卡池与卡牌权重，`Rarity` 行写全局稀有度权重（0 普通 / 1 炫彩）。全部卡牌 CSV 表头为 `CardId,SourcePool`，`CardId` 全局唯一。两张表导入即生效，不下发到 `game-config/bootstrap`。

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

## 卡组存储

创建请求：`{ "name": "青眼" }`，成功返回 `201`、`Location` 和卡组对象。名称去除首尾空白后须为 1–64 字符且不含控制字符，允许重名。初始主卡、额外均为空数组，`revision` 为 0。

卡组对象包含 `id`、`name`、`mainDeck`、`extraDeck`、`revision`、`createdAt`、`updatedAt`。列表接口返回当前账号的卡组数组，按创建时间和 ID 排序。卡组独立保存，不包含在玩家 bootstrap 中。

保存请求示例：

```json
{
  "name": "青眼卡组",
  "mainDeck": [{ "cardId": "89631139", "rarity": 0, "count": 3 }],
  "extraDeck": [{ "cardId": "01639384", "rarity": 1, "count": 1 }],
  "expectedRevision": 0
}
```

保存成功返回 `200` 和新卡组对象，版本递增。删除必须携带 `expectedRevision` 查询参数，成功返回 `204`。每套卡组使用独立版本，不修改玩家资料版本、金币或收藏。

**本阶段后端不执行组卡规则。** 客户端负责主卡 40–60、额外 0–15、CardId 合计上限、禁限表、卡牌分类和持有量检查。服务端仅检查鉴权、账号归属、请求结构和版本：两个分区数组必填且条目不能为 null，`cardId` 为非空字符串（最多 128 字符且无控制字符），`rarity` 非负，`count` 为正整数，`expectedRevision` 必填且非负。即使是未知卡牌、超过组卡上限或禁卡，结构有效也可存储。创建、保存请求体上限沿用 16 KiB。

账号归属从 Bearer 凭证确定，不接受请求体指定账号。不存在或属于其他账号的卡组均返回 `404 DECK_NOT_FOUND`；过期或并发冲突返回 `409 DECK_DATA_CHANGED`，不会覆盖原内容；结构错误返回 `422 VALIDATION_ERROR`。客户端应重新读取冲突卡组，不自动重放保存请求。

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
| `DECK_DATA_CHANGED` | 409 | 卡组版本过期或发生并发修改 |
| `DECK_NOT_FOUND` | 404 | 卡组不存在或不属于当前账号 |
| `AVATAR_NOT_OWNED` / `WALLPAPER_NOT_OWNED` | 422 | 装备了未拥有的外观 |
| `AVATAR_NOT_AVAILABLE` / `WALLPAPER_NOT_AVAILABLE` | 422 | 外观不存在、未启用或不在售卖窗口 |
| `ITEM_ALREADY_OWNED` | 422 | 重复购买 |
| `INSUFFICIENT_GOLD` | 422 | 金币不足 |
| `GACHA_CONFIG_EMPTY` | 422 | 尚未导入抽卡配置 |
| `ALL_CARDS_EMPTY` | 422 | 抽 `CardAll` 时尚未导入全部卡牌 |
| `GACHA_POOL_NOT_FOUND` | 422 | 卡池不存在 |
| `INVALID_DRAW_COUNT` | 422 | 一次抽取数量不是 1-10 |
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

## 头像框

`PATCH /api/player/profile` 支持 `avatarFrameId`，只能装备已拥有且启用的框；省略该字段保留当前框。`POST /api/player/purchase` 使用 `catalogType: "avatar-frame"`，价格来自已发布的 `avatar-frames` 配置。好友列表、搜索结果和好友申请均返回头像框编号。

```json
{"catalogType":"avatar-frame","itemId":1030002,"expectedRevision":0}
```

`PlayerAvatarFrames` 一次性迁移将旧玩家头像和拥有头像重置为 1010001，赠送并装备头像框 1030001；不会重置金币、壁纸或卡牌。配置、客户端与服务端需要配套发布。


## UR 资产与卡牌工坊

`ur` 为账号独立持久化资产（64 位整数，初始 0），所有登录、刷新令牌、玩家查询及交易返回的 `player` 均包含它。迁移 `20260927130135_PlayerUr` 仅增加默认 0 的列，保留原账号、金币、收藏、卡组及 Revision。需要配套 UR 配置和新版客户端；礼品领取的响应结构有变化。

| 方法 | 路径 | 请求／行为 |
| --- | --- | --- |
| POST | `/api/player/cards/craft` | `{ "cardId": "01639384", "expectedRevision": 7, "expectedUrAmount": 30 }` |
| POST | `/api/player/cards/dismantle` | `{ "cardId": "01639384", "rarity": 1, "count": 2, "expectedRevision": 8, "expectedUrAmount": 30 }` |

两接口要求 Bearer 和当前内容配置上下文，与抽卡接口相同。`expectedUrAmount` 是本次总花费／总收益，用来拒绝旧报价；最终价格由服务器配置决定。成功为 `200 { "player": { ...完整新资产... }, "urAmount": 30 }`，玩家 Revision 加一；失败资产不变。不以请求提供的账号 ID 决定资产归属。

合成仅支持普通版 `rarity=0`：该普通版当前持有量为 0 才可购买一张；拥有其他版本不影响资格。已发布 `all-cards` 中所有卡牌均可合成，不受额外卡分类或禁限规则限制。价格来自 `card-crafting`。

分解允许五种版本、任意正整数数量，包括最后一张；不能超出库存。收益来自 `card-recycling.DismantleUr`。每套已保存卡组分别合计主卡组和额外卡组内相同 `CardId+Rarity` 的重复条目；剩余数量须满足所有卡组，因此跨卡组取最大值，草稿也参与检查。检查和库存更新位于同一事务；不会编辑卡组。原卡组 PUT 仍仅做存储和结构校验。

| 错误码 | 状态 | 含义 |
| --- | --- | --- |
| PLAYER_DATA_CHANGED | 409 | 玩家 Revision 冲突，不自动覆盖 |
| CARD_PRICE_CHANGED | 409 | 报价改变，`errors.urAmount[0]` 为服务器当前总报价的十进制字符串；刷新后重新确认 |
| CARD_IN_DECK | 422 | `errors.decks` 给出受影响卡组名称列表 |
| NORMAL_CARD_ALREADY_OWNED | 422 | 已拥有普通版 |
| INSUFFICIENT_UR | 422 | UR 不足 |
| CARD_NOT_FOUND | 422 | 合成卡 ID 不在已发布全卡清单 |
| CARD_NOT_OWNED | 422 | 分解数量超出持有量 |
| INVALID_DISMANTLE_COUNT | 422 | 数量非正或版本不在 0–4 |
| UR_OVERFLOW | 422 | 结算超出 64 位资产范围 |

### 发卡溢出

抽卡和礼品共用结算器。每个 `CardId+Rarity` 新增持有至多 3 张，按获得顺序使用剩余名额，新增超额副本按 `card-recycling.OverflowUr` 兑换。历史已有超过 3 张的部分原样保留。例如持有 5 张再获得 2 张，结果仍持有 5 张，仅这次的 2 张兑换。禁限表不改变收藏上限。

`POST /api/player/card-draws` 的每个 `results` 条目新增：

```json
{ "cardId": "01639384", "rarity": 0, "sourcePool": "Card01", "isOverflow": true, "urGained": 10 }
```

结果仍按抽取顺序返回；未溢出为 `false, 0`。金币、收藏、UR 和玩家 Revision 一次保存。

`POST /api/gifts/{id}/claim` 的新响应：

```json
{ "player": { "ur": 40, "revision": 9 }, "urGained": 20 }
```

以上 `player` 只展示相关字段，实际为完整 PlayerResponse。领取状态、金币、收藏、UR 和 Revision 同次事务更新；重复领取失败。客户端只在 `urGained>0` 时展示收益提示。

配置要求：`card-crafting` 唯一行 `Rarity=0, CostUr>0`；`card-recycling` 必须覆盖 0–4，`DismantleUr` 和 `OverflowUr` 各自为正整数。首版分别是 30 与 10/15/20/25/30。缺失或损坏不能按免费执行；服务端旧包读取的必需表集合保持兼容，需要 UR 的操作才强制加载这两张经济配置。
