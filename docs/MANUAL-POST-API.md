# 人工发文 API

所有业务请求需要 `Authorization: Bearer <ADMIN_TOKEN>`，JSON 请求需要 `Content-Type: application/json`。仅在 HTTPS 或本地隧道发送令牌。

1. `POST /api/drafts`

```json
{"content":"人工编辑后的内容","channels":["x","telegram","binance"],"event_id":null}
```

返回 `{ "id": "...", "status": "draft" }`。可用渠道：x、telegram、binance、okx、truth。X 当前按保守的 280 字符检查，平台自身仍可能因链接、字符权重或套餐限制拒绝。

2. `POST /api/drafts/{id}/approve`

人工审核内容后调用，返回 approved。草稿不可覆盖修改，编辑需创建新草稿并重新审核。

3. `POST /api/drafts/{id}/publish`

仅 live 模式，必须先审核。返回 published 或 partial；最终结果请读取 `GET /api/drafts` 的每个 deliveries 条目。再次调用不会对同一草稿同一渠道重复提交。

4. `GET /api/drafts`

包含 content、channels、status 和每个渠道的状态、remote_id、error。

| 渠道状态 | 含义 |
| --- | --- |
| sending | 请求正在进行 |
| published | 平台明确返回成功和 ID |
| manual_required | 需导出后人工发文，未自动发布 |
| failed | 请求失败，未自动重试 |
| unknown | 请求可能已送达，必须先人工核验 |

其他 API：GET /api/events（source、q、limit），GET /api/stream（SSE，认证头，不使用 URL 令牌），POST /api/events/{id}/analysis，POST /api/collect，GET /api/status，GET /api/audit。GET /api/health 不需要认证，只暴露服务状态和运行模式。

默认前后端同源；没有开放跨域的业务 API。v0.1 是单管理员接口，不区分独立用户、机器人和审批角色。
