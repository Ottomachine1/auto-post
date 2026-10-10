# v0.2 部署、备份与回滚

指定服务器`223.254.148.90:14876`。已发现服务器Caddy占用443、Nginx监听80，与旧文档“Nginx直接提供TLS”不同。保留现有服务；新站点经现有Caddy → 专用Nginx8091 → API8090。检查代理容器网关/IP后再安装模板，不照搬其他服务器的桥接地址。

## 构建与首次安装

在本地或CI构建，避免在4GB共享服务器上运行构建工具：

```sh
docker build -f deploy/Dockerfile.dotnet --target api -t auto-post-api:local .
docker build -f deploy/Dockerfile.dotnet --target worker -t auto-post-worker:local .
docker build -f deploy/Dockerfile.dotnet --target tools -t auto-post-tools:local .
docker save -o auto-post-images.tar auto-post-api:local auto-post-worker:local auto-post-tools:local postgres:17-alpine
```

传输镜像与经过检查的源码，放入`/mnt/storage/auto-post/releases/<version>`。创建current链接指向版本目录，加载镜像后运行`sh deploy/bootstrap.sh`。脚本生成权限600的`/mnt/storage/auto-post/private.env`，令牌不输出；配置文件链接到版本目录`.env`。数据库与会话密钥在数据盘，数据库不开放公网。

首次默认为demo，提供可视化页面与演示事件。管理员在服务器私有终端读取令牌登录。添加真实来源并填写Key后，将API和Worker同时切换live，重建这两个服务。自动规则仍关闭，全局暂停仍开启。

安装Nginx专用站点前备份配置；`nginx -t`成功后reload。向原Caddyfile追加独立域名配置前备份并执行`caddy validate`。当前Caddy为2.10.2且admin off，不能通过admin API热重载，SIGUSR1热重载也需要较新版本；加载新站点可能需短暂重启该代理，必须先安排现有站点影响窗口。不要为本项目擅自升级共享代理。

域名经Cloudflare代理时证书申请需保证HTTP挑战路径抵达签发服务，或由域名管理员提供有效源站证书。公网HTTPS未通过前，不宣称部署完成。

## 更新与回滚

更新前暂停自动发布，等待发送中请求完成，执行备份。保留镜像版本及Git SHA。停止Worker，再运行显式迁移；部署API、Worker，验证内部健康、页面、登录、SignalR及公网路径。

回滚先停止新Worker，记录所有published/unknown投递再切换兼容旧镜像。数据库不兼容时恢复到独立数据库核对后切换。不要仅恢复旧数据库就启动发布器，会丢失备份后的投递记录并可能重复发送。旧Python仅为迁移参考，禁止与新Worker同时发布。

## 备份与恢复

`sh deploy/backup.sh`使用pg_dump自定义格式，校验目录后写入`backups/`，权限600，保留最近7天。systemd timer每日北京时间03:30执行，目录位于数据盘current版本；升级应保留已有备份或将其迁移至共享位置。

恢复演练必须使用独立数据库：

```sh
docker compose --env-file .env -f deploy/compose.dotnet.yaml exec -T postgres createdb -U autopost autopost_restore_check
docker compose --env-file .env -f deploy/compose.dotnet.yaml exec -T postgres pg_restore -U autopost -d autopost_restore_check < backups/SELECTED.dump
```

核对关键表数量、草稿版本和投递状态；不要把演练库接入Worker。会话密钥单独备份，私有环境配置不得进入GitHub。SQLite导入命令`dotnet run --project tools/AutoPost.Tools -- import-sqlite /absolute/path/legacy.db`，重复执行应保持记录数不变；所有旧草稿隔离。

## 本机已准备的公网切换

指定服务器原有 Cloudflare Origin 证书覆盖 `*.maxson.cc`；候选配置复用该证书（不复制私钥）。此配置要求域名继续由 Cloudflare 代理，并使用严格源站 HTTPS。切换候选为 `/mnt/storage/auto-post/Caddyfile.candidate`，原配置备份为 `/mnt/storage/auto-post/backups/Caddyfile.before-auto-post`。共享代理重启前需确认现有站点的短暂影响窗口。

已验证的镜像运行版本为 fc28fb5；后续提交的浏览器测试修正和部署记录不改变运行代码。
