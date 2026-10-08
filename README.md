# CleanMaster 清理大师

安全、透明的 Windows 磁盘清理工具，基于 WPF + .NET 9 开发。

## 功能

- **磁盘清理**：扫描并清理临时文件、浏览器缓存、开发工具缓存、应用缓存、日志、崩溃转储等，按安全等级（安全/谨慎/危险）分类展示。
- **大文件查找**：扫描磁盘上占用空间较大的文件，支持最小大小过滤。
- **大文件夹扫描**：定位占用空间大的文件夹。
- **磁盘分析**：可视化展示磁盘空间占用分布。
- **软件管理**：查看已安装软件并卸载，自动清理残留文件和注册表。
- **启动项管理**：管理开机自启动程序（注册表 + 启动文件夹）。
- **系统清理**：调用 Windows 内置工具（DISM 组件清理、SFC 系统文件修复、DNS 缓存清理）。
- **重复文件查找**：按内容哈希查找重复文件，默认不勾选，逐组确认后清理。

## 系统要求

- Windows 10 / 11（x64）
- 安装包为自包含单文件，无需预装 .NET 运行时

## 构建

```powershell
# 环境：.NET 9 SDK
dotnet restore
dotnet build CleanMaster.csproj -c Release
```

## 测试

```powershell
dotnet test tests\CleanMaster.Tests.csproj -c Debug
```

## 打包安装包

```powershell
# 环境：Inno Setup 6（ISCC.exe 在 PATH 中）
dotnet publish CleanMaster.csproj -c Release -r win-x64 --self-contained true -o bin\Release\publish
$v = dotnet msbuild CleanMaster.csproj -getProperty:Version
& "ISCC.exe" /DAppVersion=$v installer.iss
```

输出：`installer\CleanMaster-Setup-v{版本号}.exe`

## ⚠️ 风险提示

本软件会**永久删除**磁盘文件（回收站清理除外）。使用前请仔细核对扫描结果，特别是标记为「谨慎」和「危险」的项目。删除操作不可逆，请对重要数据做好备份。

## 版本号

版本号在 `CleanMaster.csproj` 的 `<Version>` 中定义（单一来源），打包时通过 `/DAppVersion` 传入。

## 许可证

本项目采用 [GNU General Public License v3.0](LICENSE)。

