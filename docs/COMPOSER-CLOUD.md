# v0.4 图片创作、币安发布与云数据库

## 创作与发布

普通推文编辑器支持多行文字、链接、表情插入、图片选择/拖拽/粘贴、排序、移除、替代文字和成稿预览。最多4张静态PNG/JPEG/WebP，每张5MB。前端字数使用twitter-text；后端保守预检，最终以X平台接受为准。视频、GIF动画、投票、线程和X Premium文章不在当前交付中。

`POST /api/media` 接收 `{data:base64}`，验证大小与文件签名；`GET /api/media/{id}` 需要认证且隔离演示模式。图片数据保存在数据库，草稿和版本快照保存不可变图片ID及替代文字。修改图片与正文一样创建新版本、取消旧审核。上传中的创作禁止保存/关闭；取消创作的未引用图片暂时保留，尚无自动清理。

`POST /api/link-preview` 使用公共HTTPS、DNS/socket校验、禁止重定向、20秒超时及256KB上限读取网页标题/摘要；不请求外部封面，不存储私有链接。不支持的网页仍可保留文字链接。远端X展示的卡片由X抓取，可能不同于本站预览。

图片投递当前支持X和币安广场，其余渠道阻止附图发送，避免静默丢失图片。X需要用户令牌的media.write及发帖权限，先上传图片/替代文字再发送原文。币安通过发布专用Key上传、等待处理后使用返回图片URL创建内容；不改写原文、标签或链接。

币安官方协议来源：[Square Post](https://github.com/binance/binance-skills-hub/tree/main/skills/binance/square-post)。遵循官方上传/处理/创建字段；对发布504、5xx、断线、缺少ID或无法解析回执保守记录unknown，禁止自动重发。当前没有获得具体测试文章授权，未对真实账号发送测试内容。

## MySQL兼容与切换

新增 `DATABASE_PROVIDER=mysql`，使用官方MySql.EntityFrameworkCore 10.0.1，与PostgreSQL双实现并存。MySQL 8.4任务使用行锁、READ COMMITTED、SKIP LOCKED；会话GET_LOCK隔离执行通道。生成列唯一索引限制活动任务与来源ID，字符使用utf8mb4_bin保留区分大小写的ID。所有写操作依旧事务保存审计及变化序号。

远端MySQL必须使用VerifyCA/VerifyFull和受信任CA；不得退回明文或关闭身份校验。用户给出的实例3306为内部端口，已从应用服务器探测公网58343返回MySQL8.4.6，公网3306拒绝连接。TLS1.3握手可用，但自动生成的证书无法按公共信任链验证。云迁移正在等待用户提供控制台CA证书/连接域名；当前线上仍使用PostgreSQL，未向未验证云端提交数据库凭据或业务数据。

MySQL首次基线通过独立 `migrate` 命令建立，使用专属空数据库；不会在API或Worker启动时创建结构。后续MySQL表结构升级需新增显式升级步骤，不能把EnsureCreated当作增量迁移。PostgreSQL保留原有EF迁移链，新增加202610100005_ComposerMedia。

配置模板（实际凭据只在私有环境文件）：

```text
DATABASE_PROVIDER=mysql
DATABASE_URL=Server=DB_HOST;Port=PUBLIC_PORT;Database=autopost;User ID=APP_USER;Password=PRIVATE_PASSWORD;SslMode=VerifyCA;SslCa=/mysql-ca/ca.pem;CharSet=utf8mb4
BINANCE_SQUARE_OPENAPI_KEY=PRIVATE_KEY
```

使用 `deploy/compose.mysql.yaml`，将控制台CA存放于 `/mnt/storage/auto-post/private/mysql-ca/ca.pem`，只读挂载。优先创建专用数据库及最小权限应用账号，不使用其他业务表。

## 可核对的数据转移

备份旧库并停止API/Worker等全部写入者，再使用Tools：

```sh
# 旧PostgreSQL私有配置环境下
AutoPost.Tools export-db /private/transfer.json
# 新MySQL私有配置环境下
AutoPost.Tools migrate
AutoPost.Tools import-db /private/transfer.json
AutoPost.Tools verify-db /private/transfer.json
```

包括事件、分析、草稿/版本、图片、投递、规则、来源、任务、变化序号、审计、预算及设置；逐表输出数量及完整字段SHA256，已有相同数据可重复导入，不同数据拒绝覆盖。迁移文件包含业务内容，应权限600并在私有目录保存。导入只允许离线执行；开始新写入后不应再次导入旧快照。

切换前确认sending投递已核验，全局自动发布暂停。核对后更新私有环境并启动新服务，保留原PG只读备份。回滚先停新Worker，导出云端切换后的投递/业务变化并核对，不得直接启用旧发布器。云库备份必须在切换后启用云提供商备份并完成独立恢复测试；旧PG定时备份不能算作云库备份。
