# API 与权限清单

请使用环境配置或服务器密钥管理提供凭据，不把密钥发到聊天或提交到仓库。

## 第一批：形成真实运行闭环

| 应用/用途 | 需要提供 | 当前实现 |
| --- | --- | --- |
| 服务器 | SSH 可达网络、系统、站点域名、可用 HTTP/HTTPS 端口、Docker 或安装条件 | Docker Compose 配置已提供，尚未远程部署 |
| 新闻 RSS | 合法可使用的 HTTPS RSS 地址，逗号分隔到 RSS_URLS | 实际采集与来源 ID 去重 |
| OpenAI 或兼容模型 | OPENAI_API_KEY、OPENAI_BASE_URL、OPENAI_MODEL；确认支持 chat/completions 与 JSON object | 按需分析，结果缓存 |
| X 读取 | X_BEARER_TOKEN、最近搜索读取权限/额度、X_QUERY | 最近搜索轮询，尚未全量分页或 filtered stream |
| X 发文 | X_USER_ACCESS_TOKEN，**用户上下文** OAuth 2.0，tweet.write / tweet.read / users.read；需要合适套餐 | POST /2/tweets；不使用 app-only 读取 Token 发文 |
| Telegram | TELEGRAM_BOT_TOKEN、TELEGRAM_CHAT_ID；机器人应有目标聊天发文权限 | sendMessage |
| 管理认证 | 在服务器生成 ADMIN_TOKEN，生产至少 32 字符 | 统一管理令牌；无多人账户 |

X Token 刷新/授权回调当前未实现，长时间运营需要后续加密保存 refresh token 与自动刷新。不要把读取 Bearer Token 当成用户发文授权。

## 第二批：需要先确认能力

| 应用 | 需要确认 | 当前行为 |
| --- | --- | --- |
| Truth Social 采集 | 官方许可接口，或授权数据服务文档、Base URL、Key、账号/查询范围、额度 | 不抓取登录页面，不伪装已接入；演示样例 |
| Truth Social 发文 | 是否提供账户内容发布 API 与授权方式 | 人工导出 |
| 币安广场 | 创作者账号、官方**内容发布 API**及权限；交易 API 不提供同等能力 | 人工导出 |
| OKX 内容平台 | 目标产品名称、官方社区内容发布 API 文档与权限 | 人工导出 |
| NewsAPI / GDELT / 商业新闻 | 来源选择、商业转载许可、密钥与配额 | 未提供独立适配器；当前使用 RSS |
| 其他平台 | 名称、官方发文 API 文档、OAuth scopes、字数/媒体限制 | 后续按适配器扩展 |

## 需要你确定的运营参数

- 重点国家/人物/账号、语言与关键词。
- 数据来源、订阅套餐、可接受采集延迟与每日 API/模型预算。
- 允许发布的平台账号；是否永久人工审核，或将来为低风险内容单独授权自动审核规则。
- 发布风格、频率、保留时间、版权引用方式、内容附件需求。

当前所有发文都必须显式人工审核。对外发布按钮调用真实 API，演示模式在服务端禁止发送。
