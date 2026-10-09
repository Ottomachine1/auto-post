# 2026-10-10 构建与部署推进记录（北京时间）

初始代码基线：`origin/main` / `399aba3`。已阅读 README、架构、部署、API 清单、人工发文 API 与验收文档。原工作区在另一历史的 `2a99548`，没有强制重置；新建独立 worktree 和 `deploy/rss-workflow` 分支继续。

收尾时重新 fetch 发现 main 已更新至 `c8be352`，新增 Nginx 模板和 SiliconFlow 配置；已阅读 `docs/NGINX-SILICONFLOW.md` 并将本分支 rebase 到该提交。该更新未修改应用运行时代码。模板中的 `auto-post.maxson.cc` 按本次用户要求仍视为未确认可用域名；没有验证 DNS/TLS，也没有更改 DNS。模型候选配置为 `https://api.siliconflow.cn/v1` 与 `zai-org/GLM-5.3-Flash`，尚未验证调用。保留最新 `.env.example` 的空 RSS_URLS，本次两个 RSS 来源仅用于隔离验收，不自动启用到服务器运营配置。

## 目标服务器：受阻，未部署

- 目标：`root@223.254.148.90:14876`。
- SSH TCP 连接实际返回 `Connection refused`，没有进入认证，因此没有验证密码，也没有登录或改动服务器。
- 环境 `/etc/codex/network-policy.json` 的 `tcp_network_access.domains` 与 `ip_ranges` 均为空；配置的 `proxy:8088` TCP CONNECT 服务也返回连接拒绝。
- 环境 readiness 返回无 secret binding、无 runtime variable、无 outbound identity。HTTP 网络状态为 unknown，不能将其当成 TCP 授权。
- 需要通过受支持的环境配置流程授予目标 IP 的 TCP 访问并恢复 TCP CONNECT 服务，将 SSH 凭据绑定到安全配置；再验证连接。不能从当前错误推断目标 sshd 已关闭或凭据错误。
- 系统、Docker、已有网站、监听端口、备份、远程容器健康均尚未验证。未确认域名，已完善 loopback + SSH 隧道部署步骤，不必为域名阻塞后续服务器本地部署。

## 本环境真实 RSS 与人工流程：通过

- CoinDesk：`https://www.coindesk.com/arc/outboundfeeds/rss/`，HTTP 200，25 条可解析新闻。
- BBC World：`https://feeds.bbci.co.uk/news/world/rss.xml`，HTTP 200，27 条可解析新闻。
- 使用真实 live 模式、临时 SQLite 数据库及真实网络请求采集 52 条事件；第二次采集仍为 52 条，重复来源 ID 为 0，两来源健康均 connected。
- 业务 API 未携带令牌返回 401；live 事件未混入演示数据。
- 基于真实来源事件保存人工草稿，通过数据库重新读取确认内容持久化；审核前发布返回 409，审核后进入渠道处理。
- 币安、OKX、Truth 三个渠道均返回 manual_required，remote_id 为空，整体 partial；重复提交没有增加投递记录。没有把人工导出标成已发布。
- collect、draft_created、approved、publish_attempt 审计均存在。
- 缺少模型凭据时分析返回 409；未调用真实模型。
- `pytest -q`：8 项通过；前端 `node --check` 通过。模拟 X 成功与不确定发送防重复为自动测试，不代表真实平台发文验证。
- Docker 镜像构建通过；本环境临时容器以 UID 10001、无额外 capabilities 运行，健康检查 healthy，静态页面与认证正常，两 RSS 来源在容器内 connected。人工草稿与 manual_required 投递记录经容器重启仍保留。验收容器及专用临时数据卷已删除，没有触碰其他项目数据。
- 独立端口 Compose 的 `config --quiet` 校验通过，配置 `.env` 使用临时生成令牌并在校验后删除。当前没有把应用持续部署在构建环境。

## 已实现与待接入

| 功能 | 已实现 | 本次实际验证 | 待接入或待验证 |
| --- | --- | --- | --- |
| RSS | HTTPS 获取、解析、来源 ID 去重 | 两个真实来源及 52 条事件 | 服务器网络、长期轮询稳定性、运营来源授权 |
| 人工创作/审核 | 草稿持久化、审核门槛、审计 | 真实 RSS 关联草稿完整流程 | 部署后浏览器验收 |
| 模型 | OpenAI 兼容 JSON 分析、缓存 | 未配置阻断 | OPENAI_API_KEY、OPENAI_BASE_URL、OPENAI_MODEL；chat/completions 和 JSON object 兼容性 |
| X 读取 | 最近搜索轮询 | 未调用 | X_BEARER_TOKEN、X_QUERY、读取套餐与额度 |
| X 发布 | 用户 Token 发布、防重复、unknown | 模拟测试 | X_USER_ACCESS_TOKEN；OAuth 用户上下文 tweet.write/tweet.read/users.read；真实回执 |
| Telegram | sendMessage 发布 | 未调用 | TELEGRAM_BOT_TOKEN、TELEGRAM_CHAT_ID、机器人发文权限；真实回执 |
| 币安/OKX/Truth 发布 | 人工导出、manual_required | API 状态通过 | 官方内容发布接口及权限；交易 API 不能替代 |
| Truth 读取 | 未实现 | 无 | 官方许可数据接口/授权服务 |
| 服务器部署 | Docker、独立端口配置、TLS 配置 | 本环境容器健康与重启持久化通过；目标 SSH 连接失败 | TCP 授权与代理恢复、安全凭据绑定、远程检查；公网 HTTPS 需域名 |

密钥仅使用安全环境配置；报告和代码不含登录密码或业务令牌。
