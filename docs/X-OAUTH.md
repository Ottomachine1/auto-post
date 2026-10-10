# X 长期用户授权刷新

发布与图片上传共用一个用户访问令牌。静态 `X_USER_ACCESS_TOKEN` 仍兼容；配置刷新授权后优先使用刷新流程，不在失败时退回旧静态令牌。

## 私有配置

首次授权仍需管理员通过自己的 X 开发者应用完成 OAuth2 PKCE，并获得包含 `offline.access` 的 refresh token；本版本提供刷新执行与持久化，不提供首次网页登录向导。文字发布通常需要 tweet.read、tweet.write、users.read，图片还需 media.write；按应用实际权限验证。

将以下变量填入服务器私有环境配置，不提交Git：

```dotenv
X_CLIENT_ID=
X_CLIENT_SECRET=
X_OAUTH_REFRESH_TOKEN=
```

机密客户端提供Client Secret；公共客户端留空，刷新请求在表单传Client ID。Compose自动设置 `X_OAUTH_STATE_PATH=/oauth/x.json`，仅Worker挂载服务器 `/mnt/storage/auto-post/state/oauth`。目录权限700、属主1654，令牌状态文件权限600。自定义运行方式必须提供绝对私有持久路径，路径缺失时禁止请求刷新。

## 刷新与恢复

发布前读取缓存；距离过期不足60秒时刷新。刷新状态通过文件锁串行化，轮换后的访问令牌、刷新令牌及到期时间先原子保存，再允许文章请求发送。图片与文章使用同一令牌。本版本为单管理员、单账号，不提供账号切换界面。

发出刷新请求前持久保存pending标记。请求丢失、超时、进程重启或响应格式无效时，不重复使用结果不明确的刷新令牌，不自动重发文章；需要重新授权后将新refresh token放入私有配置并重启Worker。Client ID或初始refresh token变化时启用新的授权链。不要仅删除pending标记后继续使用旧令牌。

不明确的刷新只表示授权交换未确认，文章尚未发送；文章请求发出后的不确定性仍沿用投递unknown和人工核验流程。此能力不启用自动发布，也不替代人工审核。

OAuth状态包含明文敏感令牌，依赖服务器私有目录权限保护，不进入业务数据库、API响应或Git。备份应将该目录作为秘密单独保护。恢复旧OAuth快照可能回退已轮换的refresh token，必须重新授权，不能直接启用旧快照；数据库回滚不回滚当前OAuth状态。

## 验收边界

模拟测试覆盖轮换后缓存、跨实例并发、重启后复用、未知结果禁止重复交换、无效响应拒绝及静态令牌兼容。没有真实X应用与账号授权前，不计为真实刷新或真实发布验收通过。

请求格式和授权范围依据 [X 官方 OAuth2 文档](https://docs.x.com/fundamentals/authentication/oauth-2-0/authorization-code)。
