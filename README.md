# LocalProductManager

LocalProductManager 是公司内部使用的商品、库存和营销管理系统，由 ASP.NET Core API、Angular 前端、SQLite、PWA 和 Windows Tauri 桌面外壳组成。

生产地址：

```text
https://inventory.jjgqaz.com:7265/
```

域名指向公司局域网服务器 `192.168.0.160`。系统具有公开可信的 HTTPS 证书，但没有配置公网入口，只供公司内网使用。

## 主要功能

- 商品、SKU、INV Number、库存数量和库位管理
- 仓库、货架和 Layer 可视化
- QR 标签设计、双排卷纸布局和打印
- 手机扫码、入库和出库
- Excel 导入、导出与商品资料维护
- 定价、单位转换、文件存储和 3D 商品编辑
- Amazon Attribution 与社交平台数据管理
- 管理员、普通用户和细分权限
- Windows 桌面应用、PWA 和签名自动更新

## 普通用户安装与使用

### Windows

1. 连接公司有线网络或公司主 Wi-Fi，不要使用访客 Wi-Fi。
2. 从管理员处获取最新版 `LocalProductManager_x.y.z_x64-setup.exe`。
3. 双击安装，并从开始菜单打开 `LocalProductManager`。
4. 使用管理员分配的个人账号和密码登录。

安装更高版本时不需要卸载旧版本，直接运行新安装包即可覆盖升级。自 `0.1.1` 起，应用会在启动时检查公司服务器；发现更高版本后会自动下载、验证签名、安装并重启。

旧于 `0.1.1` 的版本不包含自动更新模块，必须手动安装一次 `0.1.1` 或更高版本。

### 浏览器与 PWA

统一使用：

```text
https://inventory.jjgqaz.com:7265/
```

不要把 `https://192.168.0.160:7265/` 作为日常入口。证书签发给域名而不是 IP，使用 IP 可能引起证书、摄像头或 API 请求异常。

- Android Chrome：选择“安装应用”或“添加到主屏幕”。
- iPhone Safari：点击“分享”并选择“添加到主屏幕”。
- 手机扫码必须通过 HTTPS 域名访问。
- 访客网络、VPN、代理和 iCloud Private Relay 可能阻止库存请求。

PWA 和 Windows 外壳读取服务器上的实时 Angular 页面，因此普通前端更新后通常不需要重新安装。

### 使用注意事项

- 不要共享账号；管理员应为每位员工建立独立账号。
- 删除仓库、货架和 Layer 等结构操作仅允许管理员执行。
- 删除包含库存的 Layer 前，必须清空或移动其中库存。
- 扫码操作前应核对 INV Number、SKU、商品、数量和库位。
- 双排标签卷当前预设为单标签 `50 mm × 30 mm`。
- 公司外部无法直接访问；远程办公应使用公司 VPN。

## 项目结构

```text
LocalProductManager.Api/       ASP.NET Core API、SQLite 和后台任务
LocalProductManager.Web/       Angular 前端与 PWA
LocalProductManager.Web/src-tauri/
                               Windows Tauri 桌面外壳
tools/                         证书续期和安装包构建脚本
certificates/                  HTTPS 证书，不提交 Git
updates/                       自动更新发布文件，不提交 Git
_tools/                        win-acme 等本机工具，不提交 Git
_backups/                      本机备份，不提交 Git
start_server.bat               生产服务管理器
build_installer.bat            安装包与自动更新发布脚本
renew_certificate.bat          HTTPS 证书续期脚本
```

## 源码部署

### 环境要求

建议使用 Windows 10/11 或 Windows Server，并安装：

- Git
- .NET 10 SDK
- Node.js 与 npm（当前项目使用 npm 11）
- Rust/Cargo，仅构建 Windows 安装包时需要
- Microsoft Edge WebView2 Runtime

### 1. 获取源码

```powershell
git clone \\YANG\GitRepos\local-product-manager.git
cd local-product-manager
```

更新现有目录：

```powershell
git pull
```

拉取前先确认没有需要保留的未提交修改。

### 2. 安装依赖并构建前端

```powershell
cd LocalProductManager.Web
npm ci
npm run build
cd ..
```

生产文件生成在：

```text
LocalProductManager.Web/dist/LocalProductManager.Web/browser
```

API 从该目录提供网页。

### 3. 数据库、配置与密钥

默认数据库：

```text
LocalProductManager.Api/Data/local-product-manager.db
```

首次启动会创建数据库并升级表结构。迁移服务器时，应先停止 API 再复制数据库，避免库存和账号丢失。

基础配置在 `LocalProductManager.Api/appsettings.json`。生产密钥应使用环境变量或服务器私有配置，例如：

```powershell
$env:GEMINI_API_KEY = "由管理员保存的密钥"
```

禁止提交：

- API Key、OAuth Client Secret 和 Token
- SQLite 数据库及 WAL/SHM
- PFX、根证书和自动更新私钥
- 日志、AI 报告、备份和构建输出

共享图片目录使用 UNC 路径，并保证运行 API 的 Windows 账号有读取权限：

```json
{
  "ImageStorage": {
    "AllowedRoots": [
      "\\\\IMAGE-COMPUTER\\ProductImages"
    ]
  }
}
```

### 4. 局域网 DNS

公司路由器必须设置：

```text
inventory.jjgqaz.com → 192.168.0.160
```

Verizon Internet Gateway 通常位于：

```text
Advanced → Network Settings → DNS Server
```

添加 DNS Entry，必要时仅为该域名添加 DNS Rebind Protection 例外。不要全局关闭 DNS Rebind Protection。

服务器应使用固定 IP 或 DHCP Reservation；服务器 IP 改变时必须同步更新路由器 DNS。

### 5. HTTPS 证书

生产启动需要：

```text
certificates/inventory-jjgqaz-com.pfx
```

证书和 PFX 密码由服务器管理员维护，不在 Git 中。新服务器部署时必须从公司安全备份恢复。

可信 HTTPS 用于保护登录与库存数据，也是手机摄像头、PWA、Service Worker 和桌面自动更新的必要条件。

### 6. Windows 防火墙

允许 TCP `7265` 入站，但范围应限制为公司专用网络或公司网段。不要面向公共网络无限制开放。

```powershell
Test-NetConnection 192.168.0.160 -Port 7265
```

### 7. 启动生产服务

双击根目录：

```text
start_server.bat
```

服务管理器会构建 Angular、在 `https://0.0.0.0:7265` 启动 API、加载 PFX，并提供状态、日志和停止菜单。

验证：

```text
https://inventory.jjgqaz.com:7265/
https://inventory.jjgqaz.com:7265/api/app/version
```

不要直接关闭服务管理器窗口。停服时选择“Stop both servers and exit”，避免残留后台进程。

## 本地开发

Angular：

```powershell
cd LocalProductManager.Web
npm ci
npm start
```

开发地址为 `http://localhost:4200/`。修改前端后执行 `npm run build` 才会更新生产站点。

API：

```powershell
cd LocalProductManager.Api
dotnet restore
dotnet run --launch-profile http
```

开发 HTTP 地址默认是 `http://localhost:5077/`，不能代替生产 HTTPS。

构建验证：

```powershell
cd LocalProductManager.Web
npm run build

cd ..\LocalProductManager.Api
dotnet build
```

## Windows 安装包与版本

### 什么时候需要重新打包

需要重新打包：

- 修改 `src-tauri` Rust 代码
- 修改桌面窗口、权限、图标、名称或 Tauri 配置
- 修改自动更新逻辑
- 升级 Tauri/Rust 依赖

通常不需要：

- 修改 Angular 页面或样式
- 修改 API
- 更新库存、商品或数据库

桌面外壳访问服务器上的实时网页，所以 Angular 部署后，浏览器、PWA 和桌面端都会读取新页面。

### 构建并发布

双击：

```text
build_installer.bat
```

默认增加 Patch，例如 `0.1.1 → 0.1.2`。也可以运行：

```powershell
.\build_installer.bat -Version 0.2.0
.\build_installer.bat -Bump Minor
.\build_installer.bat -Bump Major
```

脚本会同步版本、构建 Angular/Tauri、签名安装包，并原子更新 `updates/LocalProductManager-setup.exe` 和 `updates/latest.json`。运行中的 API 会立即发布更新。

安装包输出：

```text
LocalProductManager.Web/src-tauri/target/release/bundle/nsis/
```

自动更新私钥：

```text
LocalProductManager.Web/_tools/tauri-updater/local-product-manager.key
```

该私钥不提交 Git，必须保存到公司受控备份。不要生成新密钥替换它；私钥丢失后，已安装客户端将无法信任后续更新。

版本号使用语义化版本：

- Patch：小修复，如 `0.1.1 → 0.1.2`
- Minor：兼容的新功能，如 `0.1.1 → 0.2.0`
- Major：重大或不兼容升级，如 `0.1.1 → 1.0.0`

## HTTPS 证书续期

Let’s Encrypt 证书免费。建议在过期前约 35 天运行：

```text
renew_certificate.bat
```

脚本会检查有效期、生成 DNS-01 TXT 值、等待 Whois DNS 生效、替换 PFX，并备份旧证书。TXT 名称为：

```text
_acme-challenge.inventory
```

续期成功后必须重启 API，让 Kestrel 加载新证书。不要频繁使用 `-Force`，避免触发 Let’s Encrypt 请求限制。

## 备份与恢复

建议定期备份：

- `LocalProductManager.Api/Data/local-product-manager.db`
- 商品图片和共享图片目录
- `certificates/inventory-jjgqaz-com.pfx`
- win-acme 配置
- Tauri updater 私钥
- 第三方 OAuth 配置

备份数据库前应停止 API，或使用 SQLite 一致性备份方案。不要在数据库正在写入时只复制 `.db` 而忽略 WAL。

恢复后重新确认固定 IP、路由器 DNS、防火墙 `7265`、证书、共享目录权限、OAuth 回调地址和自动更新私钥。

## 常见问题

### `ERR_NAME_NOT_RESOLVED`

通常是路由器 DNS Rebind Protection 拦截“公网域名指向私网 IP”。在路由器添加本地 DNS Entry 或仅为本域名添加例外。

### 手机 504 或无法获取库存

- 确认连接公司主 Wi-Fi，不是访客网络。
- 关闭 VPN、代理和 iCloud Private Relay。
- 检查 AP/Client Isolation。
- 使用域名，不要使用 IP。
- 断开并重新连接 Wi-Fi。

### Safari 正常、Chrome 不正常

Chrome 可能保留旧 DNS、缓存或 Service Worker。先用无痕窗口验证，再清除该站点 Cookie、缓存和网站数据。

### 摄像头无法启动

- 使用 HTTPS 域名。
- 检查摄像头权限。
- iPhone 优先使用 Safari/PWA。
- 确认摄像头没有被其他应用占用。

### 自动更新没有发生

- 已安装版本必须是 `0.1.1` 或更高。
- 新版本号必须高于当前版本。
- 客户端必须能访问 `/updates/latest.json` 和安装包。
- 更新必须由原 updater 私钥签名。
- 检查 API 和公司 DNS。

### 页面仍显示旧界面

- 重新运行 `npm run build`。
- 确认 API 使用正确的 Angular `dist`。
- 强制刷新，或重开桌面应用/PWA。
- 必要时清除站点缓存。

## 安全注意事项

- 不要把本系统直接开放到公网；远程访问应使用公司 VPN。
- 正式使用前必须修改初始管理员凭据。
- 使用独立账号并遵循最小权限原则。
- 不要在截图、聊天、提交记录或文档中保存密码和 API Key。
- 定期检查用户、会话、证书到期时间和备份可恢复性。

