# 实际验收记录

日期：2026-10-10。本地项目已迁移至 `D:\Project\auto-post`，Git 历史及未提交文件保留，旧源码路径已移出使用。

## 当前交付

- 公网站点：https://auto-post.maxson.cc 。健康检查返回 `status: ok, mode: live`。
- 服务器 `/mnt/storage/auto-post/current` 指向 `releases/v0.3.1`，运行源码397f9b4，API/Worker/Tools镜像0.3.1。后续文档与验收脚本提交不改变运行程序。
- 76个候选订阅中66个通过服务器预检并启用，10个保持关闭。完整逐源报告见 [rss-server-validation.json](rss-server-validation.json)。截至验收已采集超过2,600条真实事件；恢复测试快照包含3,217条总事件。
- 公网验证：未登录拒绝访问、Secure会话Cookie、防伪拒绝、历史分页无重复、关键词创建/修改频率/删除均通过。
- 公网20路强制WebSocket SignalR连接全部成功并收到变化，全部连接耗时约4.04秒。
- 自动发布保持全局暂停，备份快照投递记录0条。SiliconFlow、X、Telegram凭据未配置，真实模型分析和发布不计为通过。
- 用户批准后已重启共享Caddy接通入口。crypto/clothes入口正常；data入口502，独立检查确认旧8088后端未监听，未修改该服务。

## 测试与恢复

- 当前代码CI全部通过：.NET工作流38035050917，33项真实PostgreSQL测试、前端类型检查/构建、桌面与移动端浏览器测试、三个镜像构建；旧Python工作流38035050912，8项测试通过。
- 本地迁移后前端构建通过；清理旧绝对路径构建缓存后，本地预览从新目录启动于 http://127.0.0.1:5080/ 。
- 服务器最新备份 `autopost-20261010T074426Z.dump` 恢复至独立库 `autopost_restore_v031` 成功：Events 3,217、Sources 76、Deliveries 0。演练库未接入Worker。
- 每日北京时间03:30备份计时器启用，备份保留7天；私有配置位于 `/mnt/storage/auto-post/private.env`，权限600。
- 早期本地参考：20路SignalR最大送达约1169ms，HTTP回放通过；10万事件/20路并发/400次查询P50约7.8ms、P95约51.9ms。此结果不代表公网或外部平台性能。
- SQLite样例6事件/1草稿/1投递重复导入数量不变；草稿隔离，sending转unknown。

## 待验证与限制

- 本轮公网浏览器自动操作接口超时，未完成部署后人工界面逐项验收；CI桌面/移动浏览器检查已通过。20路连接测试不等同20个完整浏览器UI并发。
- 模型分类、摘要、影响分析和可选语义向量关联需要SiliconFlow私有配置后真实验证；每日预算按事件分析尝试计数，开启向量时每次可能额外调用embedding接口。
- 10个未通过来源：CryptoSlate三源及Glassnode访问受限，DL News及两个Google主题过旧，Google AI返回非有效XML，VentureBeat限流，arXiv ML为空。后续检测结果可能随上游变化。
- Truth未接入；币安、OKX仅人工导出。X近期搜索受平台时间窗口限制，OAuth刷新、多用户、媒体未实现。
- 多个转载不算独立证据；相似度关联只是辅助，模型标记来源独立性未核实。事件/审计暂无自动归档。
- 主分支尚未合并，完整开发分支为 `codex/dotnet-rebuild`，草稿PR #2。
