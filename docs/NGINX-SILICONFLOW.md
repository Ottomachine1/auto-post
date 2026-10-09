# auto-post.maxson.cc / Nginx / SiliconFlow 部署

## 已确认配置

- 使用服务器已有 Nginx；应用容器只绑定 `127.0.0.1:8090`，不启动新的 Caddy。
- 域名：`auto-post.maxson.cc`。
- 模型：`zai-org/GLM-5.3-Flash`。
- OpenAI 兼容基础地址：`https://api.siliconflow.cn/v1`。
- 实际调用路径：`https://api.siliconflow.cn/v1/chat/completions`。
- API Key 仅填写服务器私有 `.env` 或密钥管理器；仓库只含空白占位符。
- X、RSS、Telegram 待提供；`.env.example` 的 RSS 列表保持空白，不擅自启用新闻来源。

**状态：配置与模板已准备，尚未部署服务器或验证真实模型调用。** 当前构建环境 SSH TCP 代理不可用；SiliconFlow 网络探测被代理阻断。需要云环境允许 TCP 访问 `223.254.148.90:14876`，HTTP/HTTPS 访问 `api.siliconflow.cn`；最终域名验收还需要 `auto-post.maxson.cc` 的访问权限。

## 云端 DNS

在 Cloudflare 的 maxson.cc 区域创建 A 记录：名称 `auto-post`，目标为服务器 IP。申请 Let's Encrypt 证书时建议先使用仅 DNS 模式；站点 TLS 正常后可开启代理，SSL/TLS 模式为 **Full (strict)**。DNS 记录本身不会安装 Nginx 或部署应用。

如果使用 Cloudflare Origin Certificate，需要把 HTTPS 模板的证书路径改为服务器上实际的 Origin Certificate 与私钥路径，仍使用 Full (strict)，且源站证书不一定受浏览器直接信任。

## 服务器操作顺序

1. 检查系统、Docker Compose、Nginx 配置目录、监听端口、已有证书；确认 8090 空闲。备份准备修改的 Nginx 配置。下面路径按 Debian/Ubuntu 示例，实际以服务器为准。
2. 拉取仓库，复制 `.env.example` 为 `.env` 并设置权限 600。填写管理令牌与 SiliconFlow Key；前两者不可提交到仓库。生产模式 `APP_MODE=live`，ADMIN_TOKEN 至少 32 字符；没有 RSS/X 配置时真实事件流为空，人工草稿仍可用。
3. 启动仅应用容器：

```bash
cd /opt/signal-atlas
docker compose -p signal-atlas -f deploy/compose.existing-proxy.yaml --project-directory . up -d --build
curl --fail http://127.0.0.1:8090/api/health
```

4. 在实际 Nginx 站点目录安装 `deploy/nginx.auto-post.http.conf.example` 的 HTTP 引导配置。创建 `/var/www/letsencrypt`；先 `nginx -t`，成功后 reload。HTTP 配置只用于 ACME 与 HTTPS 跳转，不在公网 HTTP 上接收管理员令牌。
5. 如使用 Let's Encrypt，DNS 已生效且端口 80 可达后，通过服务器现有证书管理工具或 Certbot webroot 签发证书：

```bash
certbot certonly --webroot -w /var/www/letsencrypt -d auto-post.maxson.cc
```

此步骤需要服务器已安装 Certbot 与有效的 ACME 账户配置。检查证书路径再安装 HTTPS 模板，执行 `nginx -t` 后 reload。不要覆盖其他域名站点，不要在证书存在前加载 HTTPS 配置。
6. 打开 `https://auto-post.maxson.cc`，后端地址留空，输入服务器生成的 ADMIN_TOKEN。验证模型调用：需要确认该账号有模型访问权限及余额，模型是否支持 `response_format: json_object`，不能把“Key 已配置”当成模型已验证。
7. API Key、服务器密码均不输出到共享日志或诊断命令；检查请求错误时只报告状态码和脱敏说明。

## 回滚与续期

应用更新前备份数据库及当前 Git SHA；Nginx 配置变更失败时恢复此前文件并 `nginx -t`。已有反向代理方案使用显式 `-p signal-atlas` 固定数据卷归属，不混用默认 Compose 项目名。检查 Certbot 自动续期定时器与 Nginx 重载机制；Origin Certificate 则按其有效期单独维护。

若 8090 已占用，在服务器 `.env` 修改 `APP_PORT`，并同步修改 Nginx 模板的 `proxy_pass` 及健康检查/隧道访问端口；不要仅改变容器端口而保留代理指向旧端口。
