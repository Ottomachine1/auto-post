# 管理访问令牌

打开 https://auto-post.maxson.cc，在“连接工作空间”中填入部署时生成的 `ADMIN_TOKEN`。此令牌不是 GitHub Token，也不是 SiliconFlow API Key。

令牌仅保存在服务器 `/mnt/storage/auto-post/private.env`，文件权限600；不提交GitHub。已获授权的管理员可在自己的终端执行：

```powershell
ssh -p 14876 -i C:/Users/Administrator/.ssh/codex_clothes_223 root@223.254.148.90 "grep '^ADMIN_TOKEN=' /mnt/storage/auto-post/private.env"
```

复制输出中 `ADMIN_TOKEN=` 后的完整值到登录框。上述密钥路径对应当前开发电脑；其他电脑需使用其已授权的SSH密钥。不要把令牌粘贴到聊天、截图或提交记录中。

登录后服务器签发 HttpOnly、Secure 会话Cookie，写操作使用防伪令牌。退出登录会清除会话；再次登录仍使用当前管理令牌。

如需更换令牌，在服务器私有配置中生成至少32字符的随机值，并重启API和Worker加载配置。更换管理令牌不等于撤销已经签发的会话；需要强制所有会话失效时，应另行安排认证密钥轮换和重新登录。
