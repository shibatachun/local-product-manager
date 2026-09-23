# Local Product Manager

[![English](https://img.shields.io/badge/Language-English-2563eb)](README.md)
[![简体中文](https://img.shields.io/badge/语言-简体中文-dc2626)](README.zh-CN.md)

**简体中文** | [English](README.md)

Local Product Manager 是公司内部从零开发的商品、仓库、直播销售和营销运营系统。系统将商品资料、库存位置、扫码操作、BID 批次、标签打印、平台定价和营销数据集中在同一个局域网应用中，减少手工表格、重复录入及库存信息不同步的问题。

当前版本：`0.1.6`

生产入口：

```text
https://inventory.jjgqaz.com/
```

兼容入口：

```text
https://inventory.jjgqaz.com:7265/
```

系统仅供公司网络使用，不应直接开放到公网。

## 当前功能

### 商品与仓库

- 商品、父子 SKU、INV Number、分类编号和扩展资料管理
- 商品成本、重量、尺寸、平台链接及图片目录管理
- 仓库、货架、层位、Bin 和实际库位库存管理
- Stock In、Stock Out、库存移动与操作历史
- 扫码枪与移动端相机扫描 LPM 二维码
- 批量 Excel/CSV 导入、导出和资料维护

### Label Maker 与 BarTender

- 浏览器原生标签设计与打印继续保留
- 产品标签、仓位标签、单排/双排卷纸和自定义预设
- 管理员可配置第三方标签打印 HTTP 接口
- 已接通 BarTender 2022 Integration Builder
- BID Product 可从网站直接发送 UUID 和打印份数
- BarTender 使用 PostgreSQL View 查询对应记录并打印 `.btw` 模板
- 外部 CSV 已在国内贴标的产品不会重复进入本地 BarTender 打印流程

BarTender 数据 View：

```text
bartender_bid_products
```

为兼容现有模板，`Inventory` heading 显示实际库存；原应有库存追加为 `Expected Inventory`。

BarTender Integration 示例：

```text
POST http://localhost:5160/Integration/lpm-label-print/Execute
```

请求中的主要变量：

```json
{
  "action": "print",
  "recordId": "BID|B000001|P010",
  "copies": 1
}
```

生产环境应在 Admin → Third-party label printing 中配置实际 endpoint、模板和打印机。`localhost` 指运行 BarTender Integration Service 的同一台服务器；跨主机时必须使用其局域网 IP。

### Live Stream BID Products

- BID 批次作为一级条目，支持展开、增加、编辑和删除批次内产品
- 支持导入 1688/外部 BarTender CSV，也支持网站直接创建本地批次
- 本地编号按批次号和产品序号生成稳定二维码，例如 `BID|B000001|P010`
- Actual SKU、包装数量、成本、重量、材质和 Whatnot 安全起拍价
- 批次与产品级 BarTender CSV 导出
- 网站直接调用 BarTender 打印当前产品标签
- 扫码确认直播成交并记录售价、汇率、成本和利润

BID 库存分为：

- `Expected inventory`：批次或 CSV 中应有的系统库存
- `Actual inventory`：已经实际到仓并完成清点的库存

外部 CSV 新导入时实际库存默认为 `0`；本地创建或从仓库转入时按当前数量初始化。BID 扫码模式提供增加/减少实际库存按钮；确认成交需要实际库存大于 0，并同时扣减两套库存。

### Whatnot 安全起拍价

安全价模型包含：

- 商品人民币成本及实时/备用人民币兑美元汇率
- 10% 商品成本安全缓冲
- `$0.25` 包装预留
- 主播人工：`$25 ÷ 60包 = $0.4167/包`
- 8% 平台佣金
- 2.9% 支付处理费
- 1% 手续费税费预留
- `$0.30` 固定处理费
- 假定买家运费与税费产生的支付处理费

人工和包装成本会先加入保本成本，再按平台留存率反推；结果向上取整为整数美元。BID 表头旁的信息按钮会显示完整计算过程。该结果是保本安全估算，不是最终售价或利润保证。

### Converter 与 Auto Listing

- `1688-bartender-converter/` 可在没有 1688 订单时创建和编辑 BarTender 数据
- 支持按包价格/按数量单价、成本、重量、库存及 Whatnot 安全价
- CSV heading 与 BarTender 模板保持兼容，数值保留两位小数
- Auto Listing 页面预留 eBay、Walmart 和 Etsy 通道
- 当前 eBay 流程以人工审核 Draft 为主，可生成 Markdown、Seller Hub CSV 和图片素材；Walmart/Etsy 尚未接入正式 API

### 营销、分析与管理

- Amazon ASIN、Attribution 链接和营销商品管理
- YouTube、Meta、Pinterest、Instagram、Facebook、TikTok 数据接入/导入
- 社交指标、视频趋势和 AI 分析后台任务
- 平台定价估算、单位换算、文件存储与 3D 商品场景编辑
- 管理员、用户、Session 和细分权限
- 社交数据导入、导出与维护
- PWA、Windows Tauri 桌面壳和签名自动更新

## 技术架构

```text
Angular 22 / PWA / Tauri
          │
          ▼
ASP.NET Core HTTPS Gateway
          │
          ▼
ASP.NET Core API (.NET 10)
          │
          ├── PostgreSQL（生产）
          ├── PostgreSQL（唯一运行时数据库）
          ├── BarTender Integration Builder
          └── 图片目录、营销平台与后台服务
```

生产服务由 .NET Aspire AppHost 编排：

- `inventory-api`：内部 API，默认端口 `5077`
- `inventory-https-gateway`：Angular 静态文件、HTTPS 和 API 反向代理
- PostgreSQL：生产业务数据
- Aspire Dashboard：资源状态和日志

## 项目结构

```text
LocalProductManager.Api/               ASP.NET Core API、业务逻辑和数据库升级
LocalProductManager.Api.Tests/         API 与库存工作流测试
LocalProductManager.AppHost/           .NET Aspire 生产编排
LocalProductManager.Gateway/           HTTPS、前端静态文件与 API 反向代理
LocalProductManager.DatabaseMigration/ 数据库迁移辅助项目
LocalProductManager.Web/               Angular、PWA 与 Tauri 桌面客户端
1688-bartender-converter/               独立 BarTender CSV 转换工具
config/deployment.json                  本机部署配置（不要提交真实密码）
tools/                                  Aspire、备份、证书和安装包脚本
updates/                                桌面端自动更新文件
```

## 本地开发

环境要求：

- Windows 10/11 或 Windows Server
- .NET 10 SDK
- Node.js 与 npm 11
- PostgreSQL（按生产方式开发时）
- Rust/Cargo 与 WebView2（仅构建 Tauri 安装包时）
- BarTender 2022 Automation/Enterprise（仅测试一键标签打印时）

安装前端依赖：

```powershell
cd LocalProductManager.Web
npm ci
```

启动 Angular 开发服务器：

```powershell
npm start
```

默认地址：

```text
http://localhost:4200/
```

单独启动 API：

```powershell
dotnet run --project LocalProductManager.Api --launch-profile http
```

默认地址：

```text
http://localhost:5077/
```

使用 Aspire 启动完整环境：

```text
start_aspire.bat
```

也可使用：

```text
manage_aspire.bat
```

## 部署配置

复制并填写：

```text
config/deployment.example.json → config/deployment.json
```

配置包括：

- 局域网固定 IP
- HTTPS/API/Dashboard 端口
- PFX 路径与密码
- PostgreSQL host、database、username 和 password
- 自动更新路径

不要将真实数据库密码、OAuth Secret、API Key、PFX 密码或私钥提交到 Git。

前端构建前会自动运行 `scripts/sync-deployment.mjs`，将允许公开的部署地址同步到 Angular 配置；数据库凭据不会进入前端。

## 构建与验证

前端：

```powershell
cd LocalProductManager.Web
npm run build
```

API：

```powershell
dotnet build LocalProductManager.Api/LocalProductManager.Api.csproj
```

测试：

```powershell
dotnet test LocalProductManager.Api.Tests/LocalProductManager.Api.Tests.csproj
```

当前 Angular 输出目录：

```text
LocalProductManager.Web/dist/LocalProductManager.Web/browser
```

## 数据库升级与备份

API 启动时会自动补齐 PostgreSQL 表字段、索引和 BarTender View。旧 SQLite 迁移代码归档在 `deprecated/sqlite`，不再进入 API 运行时。

每日 PostgreSQL 备份：

```text
install_postgres_daily_backup.bat
```

手动工具：

```text
manage_postgres.bat
```

备份至少应覆盖：

- PostgreSQL 数据库
- 商品图片和共享文件目录
- `config/deployment.json` 的安全副本
- HTTPS PFX 与证书续期配置
- Tauri updater 私钥
- BarTender `.btw` 模板和 `.btin` Integration 文件

## BarTender 运行要求

网站一键打印依赖：

- BarTender Integration Service
- BarTender Print Scheduler Service
- 已部署并处于 Running 状态的 Web Service Integration
- Integration Service 对 `.btw` 模板、PostgreSQL 和目标打印机的访问权限
- 防火墙允许所配置的 Integration 端口（当前示例为 `5160`）

模板通过 Query Prompt `BID_UUID` 接收 Integration Variable `recordId`，Copies 使用变量 `copies`。测试请求的 `action=test` 不打印；正式请求的 `action=print` 才执行 Print Document。

## Windows 桌面版与更新

构建安装包：

```text
build_installer.bat
```

安装包和更新元数据发布到 `updates/`。仅修改 Angular 页面或 API 通常不需要重新构建 Tauri 壳；修改 Rust、窗口权限、图标或 updater 时需要重新打包。

版本号遵循语义化版本：

- Patch：兼容的小修复
- Minor：兼容的新功能
- Major：重大或不兼容升级

## 常见问题

### 更新后页面仍是旧版本

- 重新执行 `npm run build`
- 确认 Gateway 指向正确的 Angular `dist` 目录
- 重启 Aspire 资源
- 强制刷新或清除站点/PWA Service Worker 缓存

### BarTender Test connection 成功但不打印

- 确认 Integration 已部署且 Running
- 确认 Print Document 条件为 `action = print`
- 确认 Query Prompt 为 `BID_UUID = recordId`
- 确认 `recordId` 和 `copies` 已声明为 Integration Variables，并由 JSON Variables 覆盖
- 在 Integration Builder 的 Messages、Action Status 和 Variables 中查看实际值

### BarTender 显示 No records found

- 在 `bartender_bid_products` 中确认 UUID 存在
- UUID 必须包含 `BID|` 前缀
- 检查模板 Query Prompt 名称是否为 `BID_UUID`
- 外部 CSV 产品默认不进入本地标签 View

### 手机扫码无法启动相机

- 必须使用 HTTPS
- 检查浏览器相机权限
- iPhone 优先使用 Safari/PWA
- 确认没有其他应用占用摄像头

### API 启动时 View 升级失败

PostgreSQL `CREATE OR REPLACE VIEW` 要求旧字段的名称和顺序保持不变。新增字段必须追加到 View 末尾，或在维护窗口中显式删除并重建 View。

## 安全原则

- 仅在公司局域网或受控 VPN 中使用
- 为员工分配独立账号并遵循最小权限
- 不在代码、截图、聊天、日志或提交记录中保存凭据
- 删除仓库结构或库存前先确认关联数据
- 定期验证数据库、图片、证书和 updater 私钥可恢复
- 第三方打印 endpoint 只允许受信任的内部服务
