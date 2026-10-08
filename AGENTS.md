# CleanMaster - 项目工作指南

## 构建与打包

### 环境要求
- .NET 9 SDK
- Inno Setup 6（用于打包安装包，ISCC.exe 需在 PATH 中或通过环境变量指定）

### 版本号（单一来源）
版本号**只在 `CleanMaster.csproj` 的 `<Version>` 中定义**一处：
- `installer.iss` 通过 `ISCC.exe /DAppVersion={版本号}` 传入（含 `#ifndef` 默认值兜底）。
- `SettingsViewModel.AppVersion` 从程序集版本自动读取。
- 发布新版本只需改 csproj 的 `<Version>`，然后跑下面的打包脚本。

### 发布流程
1. 发布 Release 构建（自包含单文件，面向普通用户免装 .NET 运行时）：
   ```powershell
   dotnet publish CleanMaster.csproj -c Release -r win-x64 --self-contained true -o bin\Release\publish
   ```
2. 打包安装包（从 csproj 读版本号传入，生成到 `installer\` 目录）：
   ```powershell
   $v = dotnet msbuild CleanMaster.csproj -getProperty:Version
   & "ISCC.exe" /DAppVersion=$v installer.iss
   ```
3. 输出文件名格式：`CleanMaster-Setup-v{版本号}.exe`

### 测试
```powershell
dotnet test tests\CleanMaster.Tests.csproj -c Debug
```

### Lint / TypeCheck
本项目为 C# WPF 项目，构建即类型检查：
```powershell
dotnet build CleanMaster.csproj -c Debug
```
