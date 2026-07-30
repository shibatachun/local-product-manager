# API 隔离测试

双击项目根目录的 `test_api.bat` 即可执行，Aspire 可以保持运行。

也可以在 PowerShell 中执行：

```powershell
dotnet test LocalProductManager.Api.Tests\LocalProductManager.Api.Tests.csproj --configuration Debug
```

## 隔离方式

- 每个测试实例使用 `%TEMP%` 下随机命名的 SQLite 数据库。
- 不会连接或修改 `LocalProductManager.Api\Data\local-product-manager.db`。
- 测试结束后会清除临时 `.db`、`-wal` 和 `-shm` 文件。
- 测试环境会关闭 YouTube、Meta 和 AI 等后台同步任务。
- 使用 Debug 输出，避免与 Aspire 正在运行的 Release API 文件冲突。

## 当前覆盖

- `/health` 健康检查。
- `/api/app/version` API 身份与版本响应。
- 产品 API 的创建与按 ID 读取。
- 产品、平台链接、ASIN 的新增、读取、修改和删除流程。
- 仓库、货架、层级、库位库存和布局的完整流程。
- 登录、权限用户列表、社交账号、指标、视频趋势和店铺链接流程。
- 所有 Controller API 路由均已注册且不存在重复端点。
- 外部平台状态、OAuth、报表上传、AI 报告和图片接口的离线安全契约。
- 20 个请求同时入库时没有丢失更新。
- 多个请求同时出库时库存不会小于零。
- 库存不足时返回 HTTP 409，且数据保持不变。
- 每次库存变化后，产品总库存与所有库位合计保持一致。

这套测试验证关键接口与库存并发规则，并不表示所有业务端点已经获得完整覆盖。新增 API 时，应继续在
`LocalProductManager.Api.Tests` 中添加对应测试。
