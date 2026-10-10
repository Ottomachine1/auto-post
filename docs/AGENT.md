# 情报 Agent

右下角入口打开对话面板。桌面侧栏、移动全屏，支持历史、停止任务、附带当前事件、最新情报、来源刷新、分析和待审核草稿。

## 权限与数据

只有明确的界面操作可以触发工具，模型输出不执行工具调用。工具为检索、刷新已启用且未暂时停用的来源、分析和创建草稿；每轮最多三种工具操作，不包含审核、发布、规则启用、任意网络请求或系统命令。

引用由服务器从事件库关联，模型不能添加来源 URL。正文与历史视为不可信数据，页面只渲染纯文本；回答保留事实/报道/预测/传闻、不确定性、来源及发布/采集时间。最新情报表示已入库的最新内容，不承诺全网覆盖或来源实时送达。

## 接口

- `GET /api/agent/status`：模型配置、每日预算、能力、云库核验状态。
- `GET/POST /api/agent/sessions`：当前运行模式的最近100个会话/创建会话。
- `GET /api/agent/sessions/{id}/messages?before={messageId}`：每页100条，可向前分页。
- `POST /api/agent/sessions/{id}/messages`：`{action,prompt,requestId,eventId?}`；action 为 latest/search/refresh/analyse/draft，requestId 在会话内幂等。每个会话同时一个活动任务。
- `GET /api/agent/tasks/{id}`、`POST /api/agent/tasks/{id}/cancel`：查询和取消。

接口沿用认证、CSRF、防止演示/真实模式串读。会话、消息、Job、Change、审计持久化。SignalR 通知变化，HTTP 查询补齐状态，活动面板另每1.5秒恢复任务状态。

## 模型、预算与恢复

使用私有 OPENAI_BASE_URL/OPENAI_API_KEY/OPENAI_MODEL，默认 SiliconFlow。生产 Key 不存在时检索与来源刷新可用，分析和起草明确提示未配置；不会伪造模型结果。上线必须实际验证指定模型权限后才记为真实 AI 接入。

AGENT_DAILY_LIMIT 默认100，按北京时间每日重置，以模型尝试计数；独立 Worker 通道使用数据库领导锁并发1。事件分析同时使用已有 analysis 配额，缓存结果不重复计费。达到额度进入 waiting_quota、保留任务至次日；显式停止始终可用。用量与模型保存在消息记录。

取消通过独立数据库查询中断模型请求，提交结果/草稿前再次检查取消状态。运行中服务中断在租约恢复后标记失败，要求重新发起，避免不确定调用自动重复；等待配额可安全恢复。模型异常标记失败，不生成成功结果。Agent 分析不进入规则自动发布流程。

## 云数据库与上线

生产当前使用 live 模式与 PostgreSQL，云 MySQL 显示待证书核验。证书未独立确认前不迁移，不关闭 TLS 验证。核验步骤见 CLOUD-CERTIFICATE-VERIFICATION.md；CA 不下载的指纹固定方案须在独立确认后完成客户端握手校验专项验证，不能将现有只读检查脚本当作连接安全控制。

PostgreSQL 显式迁移202610100006_Agent增加两表与索引；API/Worker 不自行迁移。MySQL 首次建库包含两表，已存在的旧 MySQL 库必须执行后续显式升级后才可启用 Agent。离线迁移工具扩展为15张业务表。升级前备份，旧镜像回滚时不删除 Agent 表。

## 模型选择（v0.5.2）

系统设置分别保存“后台情报分析模型”和“Agent 默认模型”；Agent 面板的“本次 Agent 模型”覆盖单次请求，不修改系统默认值。默认选项继承服务器 OPENAI_MODEL。任务提交时固定模型，后续默认值变化不改变排队任务。历史结果保留模型及实际用量，不因选择切换被覆盖。

`GET /api/ai/models` 使用私有Key调用供应商 `/models?sub_type=chat`，只返回模型ID，不返回凭据。保存设置和显式单次模型均验证账号当前目录。模型目录表示供应商列出的模型，不保证每种模型都支持当前结构化分析流程；格式异常、额度或权限不足会失败，不自动改用另一个模型。前端目录缓存5分钟，可在失败时重试。

硅基流动接口说明：https://api-docs.siliconflow.cn/docs/api/models-get 。Agent结构化输出最多4096 token，截断响应视为失败，禁止生成成功结果。
