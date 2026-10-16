# 每日新闻邮件：Gmail OAuth 设置

发送账号：`yangli.graphic.dev@gmail.com`；收件账号：`q1093677764@gmail.com`。发送脚本为 [email_daily_news.py](email_daily_news.py)。不使用 Gmail 密码或应用专用密码。

## 一次性设置

1. 用发件账号登录 [Google Cloud Console](https://console.cloud.google.com/)，创建或选用自己的项目，并在 [API Library](https://console.cloud.google.com/apis/library/gmail.googleapis.com) 启用 Gmail API。
2. 到 **Google Auth Platform → Branding / Audience / Data Access** 配置授权同意界面。个人 Gmail 账号选 **External**，添加发件账号为测试用户，权限只添加 `openid`、`email` 和 `https://www.googleapis.com/auth/gmail.send`。
3. 到 **Google Auth Platform → Clients → Create client**，应用类型选 **Desktop app**，下载 JSON，保存到本机仓库的 `.codex/secrets/gmail-oauth-client.json`。该目录被 `.gitignore` 排除；不要把 JSON 或授权令牌发送给别人。
4. 在**实际运行每天 07:00（美东时间）任务的同一台电脑**上执行：

   ```powershell
   C:\Users\jjgqa\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe scripts\email_daily_news.py --authorize
   ```

   浏览器里选 `yangli.graphic.dev@gmail.com` 并同意发送权限。授权成功后，会在 `.codex/secrets/gmail-oauth-token.json` 保存刷新令牌。
5. 无邮件测试：

   ```powershell
   C:\Users\jjgqa\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe scripts\email_daily_news.py --check
   ```

6. 发一封报告测试邮件：

   ```powershell
   C:\Users\jjgqa\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe scripts\email_daily_news.py outputs\news-factor-investing\2026-09-16-1608-news-factor-investing.md
   ```

Google 对处于 **Testing** 状态的 External 应用，测试用户的授权和刷新令牌可能在 7 天后失效。要长期无人值守发送，应在 Google Auth Platform 的 Audience 中处理发布状态；若 Google 要求验证，则须按其界面完成。详情见 [Google 官方说明](https://support.google.com/cloud/answer/15549945?hl=en)。

每天 07:00（美东时间）的自动化任务需要有效的 OAuth 授权。任务运行环境必须能读取本地 `.codex/secrets` 中的文件；若换到另一台机器，需要在那台机器上重新授权。发送前脚本会核对当前 Google 登录邮箱确为指定发件账号。
