# v0.2 API

除health和POST session外需Cookie或`Authorization: Bearer ADMIN_TOKEN`。Cookie写请求附`X-CSRF-TOKEN`，由GET session获取。未登录401，来源/防伪失败403，业务状态冲突409，排队202。

| 接口 | 行为 |
|---|---|
| POST/GET/DELETE /api/session | `{token}`登录 / 获取csrfToken / 注销 |
| GET /api/events | 旧列表形状，保留snake_case事件字段；source/q/limit |
| GET /api/events/page | source/q/category/cursor，返回items和nextCursor |
| GET /api/events/{id} | 原文、分析、关联来源、草稿 |
| POST /api/events/{id}/analysis | 返回缓存或202排队 |
| GET /api/changes?after=N | 最多200条增量，继续读取直至不足200条 |
| GET /api/stream | SSE，支持Last-Event-ID |
| /hubs/events | SignalR changes通知，HTTP补齐 |
| GET/POST /api/drafts | 列表 / `{content,channels,eventId?}`创建 |
| PUT /api/drafts/{id} | `{content,channels,revision}`新版本 |
| GET /api/drafts/{id}/versions | 历史版本 |
| POST /api/drafts/preview | `{content,channels}`分渠道预览 |
| POST /api/drafts/{id}/approve?revision=N | 必须指定明确版本，旧客户端需更新 |
| POST /api/drafts/{id}/publish | 当前审核版本排队 |
| POST /api/deliveries/{id}/retry | 仅failed/blocked允许 |
| POST /api/deliveries/{id}/resolve | `{status:published/failed,remoteId?,note}`，确认发布需远端ID |
| GET/POST /api/rules | 列表 / 创建，创建始终关闭 |
| PUT/DELETE /api/rules/{id} | 更新版本 / 删除 |
| POST /api/rules/{id}/test | 最近50条匹配模拟，不发送 |
| GET/POST /api/sources | 列表 / 创建 |
| PUT /api/sources/{id} | 修改；采集中拒绝；改变地址清空游标 |
| POST /api/collect | 启用来源排队 |
| GET /api/status | 来源、配置、心跳、队列、每日用量 |
| PUT /api/settings | `{autoPaused,analysisDailyLimit,channelDailyLimit}` |
| GET /api/tasks /api/audit | 各最近100条 |

规则输入：`name,sources[],keywords[],channels[],enabled,account:"default",dailyLimit:10,cooldownMinutes:15,startHour:0,endHour:24`。

来源输入：`kind:"rss"/"x",name,address,enabled:false,intervalSeconds:60`。来源白名单为配置ID。

X预检URL按23、CJK按2，复杂emoji可能保守拒绝，最终限制以平台为准。Telegram纯文本最多4096 Unicode字符。其余渠道人工导出。

## RSS/Atom 来源扩展

- `POST /api/sources/catalog`：导入缺失的76个候选，默认关闭。
- `POST /api/sources/validate-all`、`POST /api/sources/{id}/validate`：排队预检，首次启用须通过。
- `DELETE /api/sources/{id}`：删除；有正在运行的来源任务时拒绝。
- `POST /api/sources/topic-url`：生成Google News关键词订阅地址。

来源支持分类、优先级、间隔、主题及地区语言；返回检测时间、最新发布时间、HTTP状态、ETag、Last-Modified与退避状态。关键词通过来源CRUD管理，服务端统一生成地址。详见RSS-NETWORK.md和当前端点实现。
