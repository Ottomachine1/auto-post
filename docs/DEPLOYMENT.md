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

现有反向代理环境或尚无域名时可用 `deploy/compose.existing-proxy.yaml`，仅绑定服务器 loopback 端口 8090，避免抢占 80/443。`APP_PORT` 可改为检查后确认空闲的端口，配置包含应用健康检查。

SSH 可达后，先执行以下只读检查；输出可能含已有站点名称，保存时注意访问控制：

```bash
cat /etc/os-release
uname -m
df -h
ss -lntp
docker version
docker compose version
docker ps --format '{{.Names}} {{.Ports}} {{.Status}}'
systemctl --no-pager --type=service --state=running
```

确认 8090 未占用后，在克隆与配置 `.env` 完成的项目目录执行（明确项目名以隔离其他服务）：

```bash
APP_PORT=8090 docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . config --quiet
APP_PORT=8090 docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . up -d --build
docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . ps
curl --fail http://127.0.0.1:8090/api/health
```

不要启动默认 Compose 的 Caddy，除非确认现有 80/443 未占用并具备域名。无域名时，在自己的电脑建立隧道：

```bash
ssh -N -L 18090:127.0.0.1:8090 -p 14876 root@223.254.148.90
```

浏览器打开 `http://127.0.0.1:18090`，后端地址留空。此时访问经过 SSH 加密隧道；无需开放服务器 8090 公网端口。更新、备份、回滚时也必须使用同一个 `-p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory .`，否则可能操作错误项目或数据卷。

## 构建环境的真实 RSS 验收

```bash
python -m scripts.verify_rss_workflow
```

脚本使用临时数据库和临时管理令牌，实际请求 `RSS_URLS`（未设置则使用 CoinDesk 和 BBC World），验证来源健康、去重、人工草稿、审核门槛、人工渠道状态、审计及模型未配置时的阻断。脚本在自己的进程中清除模型和平台凭据，不执行真实外部发文，不读取部署数据库。成功不代表目标服务器能访问来源，也不代表已完成公网部署。执行记录见 [本次验证报告](VERIFICATION-2026-10-10.md)。

## 备份

以下命令用于当前指定的现有 Nginx 部署，在项目根目录执行。使用 SQLite 在线备份 API，不在运行期间仅复制 db 文件（WAL 中可能还有数据）。若实际使用其他 Compose 项目，请改成对应项目名与配置文件。

```bash
mkdir -p backups
chmod 700 backups
docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . exec -T app python -c "import sqlite3; a=sqlite3.connect('/data/intelligence.db'); b=sqlite3.connect('/data/backup.db'); a.backup(b); b.close(); a.close()"
docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . cp app:/data/backup.db backups/atlas-backup.db
chmod 600 backups/atlas-backup.db
```

将备份保存在受控存储，按业务规则设置保留时间。数据包含草稿、来源、发文记录和审计，不应公开。

## 更新与回滚

更新前记录当前 Git commit 并备份数据库。`git pull --ff-only` 后运行必要检查，并执行 `docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . up -d --build`。回滚可检出原 commit 并重建，勿执行 `docker compose down -v`，该参数删除数据卷。未来有破坏性数据库迁移时必须先增加迁移与回滚方案。

发送结果 unknown 代表平台可能已经收到，必须到平台核验；服务不自动重发。failed 也不自动重试；当前需要人工处理并根据确认结果决定是否新建草稿。新建草稿不是远端幂等保证，必须防止人工重复发布。

当前指定方案：服务器现有 Nginx + auto-post.maxson.cc + SiliconFlow GLM，见 [专项部署说明](NGINX-SILICONFLOW.md)。X、RSS、Telegram 暂未提供，默认来源列表为空。
