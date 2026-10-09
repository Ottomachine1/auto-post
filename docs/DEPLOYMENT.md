# 部署手册

## 当前环境的部署障碍

截至本次构建，云环境未配置目标服务器的 TCP 访问，配置的 TCP CONNECT 代理连接被拒绝；没有执行 SSH 登录或远程改动。Sites 工具可用，但执行环境缺少 Sites 技能要求的 source helper / 本地插件脚本，无法完成支持的源同步和打包发布流程；没有创建空 Site 或宣称部署成功。

## 部署前检查

SSH 管理入口与网站端口是两回事。先确认系统版本、Docker/Compose、磁盘、已有监听端口和反向代理。不要覆盖已有网站，已有 80/443 服务时使用现有代理转发到独立应用端口。将站点域名解析到服务器；公网开放 80/443 用于 Caddy HTTPS。避免直接用 root 密码做长期自动部署身份。

当前仓库不含服务器密码，也不保存 SSH 连接凭据。

## 安装与启动（服务器执行）

已有 Docker 的情况下：

```bash
git clone https://github.com/Ottomachine1/auto-post.git /opt/signal-atlas
cd /opt/signal-atlas
cp .env.example .env
chmod 600 .env
python3 -c 'import secrets; print(secrets.token_urlsafe(48))'
```

将生成结果填写到 `.env` 的 ADMIN_TOKEN。配置 SITE_DOMAIN 为你控制并已正确解析的域名；填写需要的业务 API。先以 `APP_MODE=demo` 验证部署，再改为 live 启用真实采集与外部发送。不要把生成的令牌提交到仓库或打印到共享日志。

```bash
docker compose config --quiet
docker compose up -d --build
docker compose ps
```

打开 `https://你的域名`，点击连接设置，后端地址留空，输入管理令牌。检查实际来源状态，采集记录保留在 source_health；AI 和发布凭据的“已配置”不等于已经通过真实 API 验证。人工审核一个测试草稿，在授权测试账号发布后确认远端内容与 delivery ID。

## 没有域名

可以在受限网络使用独立 HTTP 端口调试布局；公网管理接口应先配置域名与 HTTPS 再发送管理令牌和发文请求。不要通过未加密公网 HTTP 输入访问令牌。也可以使用 SSH 隧道将服务器本地端口映射到你的本机浏览器。

现有反向代理环境可用 `deploy/compose.existing-proxy.yaml`，仅绑定服务器 loopback 端口 8090，再由现有 HTTPS 代理转发，避免抢占 80/443。

## 备份

使用 SQLite 在线备份 API，不在运行期间仅复制 db 文件（WAL 中可能还有数据）。例如：

```bash
mkdir -p backups
chmod 700 backups
docker compose exec -T app python -c "import sqlite3; a=sqlite3.connect('/data/intelligence.db'); b=sqlite3.connect('/data/backup.db'); a.backup(b); b.close(); a.close()"
docker compose cp app:/data/backup.db backups/atlas-backup.db
chmod 600 backups/atlas-backup.db
```

将备份保存在受控存储，按业务规则设置保留时间。数据包含草稿、来源、发文记录和审计，不应公开。

## 更新与回滚

更新前记录当前 Git commit 并备份数据库。`git pull --ff-only` 后运行测试并 `docker compose up -d --build`。回滚可检出原 commit 并重建，勿执行 `docker compose down -v`，该参数删除数据卷。未来有破坏性数据库迁移时必须先增加迁移与回滚方案。

发送结果 unknown 代表平台可能已经收到，必须到平台核验；服务不自动重发。failed 也不自动重试；当前需要人工处理并根据确认结果决定是否新建草稿。新建草稿不是远端幂等保证，必须防止人工重复发布。
