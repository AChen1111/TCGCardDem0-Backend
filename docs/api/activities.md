# 活动 CSV 发布与客户端协议 2

活动为独立全平台配置，不修改普通内容发布或主包启动协议。客户端请求需 Bearer 登录令牌以及 X-Activity-Schema: 2。活动发布、列表、签到访问、弹窗回执和纯金币领取不读取普通内容或检查资源引用；实际发放卡牌时仍通过 X-Content-Target/X-Config-Hash 读取既有卡牌结算配置。

| 方法 | 路径 | 行为 |
| --- | --- | --- |
| PUT | /api/admin/activities/config | Publish Key 上传平铺 ZIP，完整校验后激活 |
| GET | /api/admin/activities | Publish Key 查询当前发布与总表记录（只读） |
| GET | /api/admin/activities/history | Publish Key 查询发布历史（只读） |
| GET | /api/activities | 总表、发布版本、子表信息、服务端时间及玩家状态 |
| GET | /api/activities/files/{release}/{table}.bytes | 按版本和表名下载不可变 BinaryTable 文件 |
| POST | /api/activities/visit | 实际大厅访问，累计北京时间自然日签到 |
| POST | /api/activities/{id}/claim | 礼包、签到、里程碑领取 |
| POST | /api/activities/{id}/exchange | 金币兑换 |
| POST | /api/activities/{id}/popup-shown | 策略版本与周期的弹窗回执 |

上传内容类型 application/zip，上限 32 MiB；平铺 manifest.json + activities.bytes + 全部引用子表.bytes。清单示意如下（SHA、大小、源哈希替换为真实值）：

```json
{"schemaVersion":2,"releaseId":"f39d0b3e-3b83-4bc6-b7a8-309728132ab7","expectedRevision":0,"sourceHash":"实际源哈希","files":[{"table":"activities","size":123,"sha256":"实际SHA256"}]}
```

ExpectedRevision 仅匹配当前活动发布 Revision，首次为 0，用于避免同时发布相互覆盖。活动清单不含普通版本平台或配置哈希；后端不查询普通内容，不检查文案/图片/卡牌/卡包是否已发布。没有普通版本或已有平台缺少普通配置表时，仍可发布活动。ReleaseId 不可复用。发布失败不切换指针；下载仅允许发布记录清单包含的表，返回 ETag SHA-256。

```json
{"schemaVersion":2,"releaseId":"当前UUID","definitionsRevision":1,"playerStateRevision":0,"serverTime":"2026-10-01T04:00:00Z","serverDay":"2026-10-01","nextResetAt":"2026-10-02T00:00:00+08:00","activities":[{"master":{"activityId":"daily_gold","type":1,"detailTable":"daily_gold","isEnabled":true,"scheduleMode":0},"definitionVersion":1,"status":"running","eligible":true,"shouldShow":false,"playerState":{"progress":0,"completed":false,"entryStates":[]}}],"files":[{"table":"daily_gold","size":123,"sha256":"实际SHA256","url":"/api/activities/files/当前UUID/daily_gold.bytes"}]}
```

以上仅展示部分字段；接口不返回子表正文。Master 使用 ActivityMasterRow 全部公共字段，玩家状态继续包含每档领取周期、次数和 CanClaim。禁用、未来和结束活动仍在总表中。CSV/BinaryTable 结构与校验以共享 HotUpdate 源 ActivityCsvConfiguration 为准。

数据库仅保存总表与版本文件元数据，子表存于 ContentDelivery:StorageRoot/activities/{release}/{table}.bytes。部署和备份必须包含数据库及这些不可变文件。历史文件为已发布版本下载和问题追查保留，不自动删除。

同 ActivityId 首次发布即固定玩法/档位/周期/次数等业务身份；奖励、成本及排期可变，次数和进度保留。每活动的总表或子表改变才递增 DefinitionVersion。领取请求仍包含 EntryId、DefinitionVersion、ExpectedRevision、PeriodKey、RequestId；幂等结果优先于版本、排期和次数校验。返回 Player、Activities（协议 2 总表索引）、金币/UR 与卡牌结算结果。

错误包含 INVALID_ACTIVITY_PACKAGE（422，活动表结构或文件错误，具体表名/字段）、ACTIVITY_VERSION_CHANGED（409，并发或领取定义版本变化）、ACTIVITY_SCHEMA_CHANGED（409，旧客户端）及原结算错误。旧草稿、礼包定义编辑、单活动发布/复制/下架接口已移除。停止活动通过 CSV 禁用/结束时间后发布完整包；历史行人工保留。

ActivityCsvRelease 迁移移除旧配置正文列，不转换旧 JSON。玩家记录不删除。后台只读总表和发布历史；CSV 是唯一编辑来源。

## 本地活动时间

Development 环境可在 `src/AChen.Backend.Api/Data/activity-clock.json` 设置活动测试时间，该文件仅保存在本机，不提交 Git。例如：

```json
{"Activities":{"DevelopmentTime":"2026-10-01T12:00:00+08:00"}}
```

重启后端后，活动时钟从指定时间继续走动。活动开启、签到日、每日领取和里程碑使用该时钟；登录令牌、账号创建时间和发布历史仍使用真实时间。`GET /api/admin/activities` 的 `serverTime` 返回当前活动时间。删除这个本地文件并重启即可恢复真实时间；其他环境始终使用真实时间。
