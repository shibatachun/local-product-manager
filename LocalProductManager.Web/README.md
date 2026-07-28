# LocalProductManager Web

本目录包含 Angular 前端、PWA 资源和 `src-tauri` Windows 桌面外壳。

开发：

```powershell
npm ci
npm start
```

生产构建：

```powershell
npm run build
```

Windows 安装包应从仓库根目录运行 `build_installer.bat`，不要直接使用普通 Tauri 构建命令，以免遗漏版本同步、更新签名和 `latest.json` 发布。

完整的普通用户安装、源码部署、HTTPS、PWA、安装包、自动更新和注意事项，请阅读根目录的 [README.md](../README.md)。
