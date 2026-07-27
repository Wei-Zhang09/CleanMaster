# CleanMaster - 项目工作指南

## 构建与打包

### 环境要求
- .NET 9 SDK
- Inno Setup 6 (用于打包安装包)

### Inno Setup 路径
**ISCC.exe 路径**: `D:\Work_app\Inno Setup 6\ISCC.exe`

调用示例:
```powershell
& "D:\Work_app\Inno Setup 6\ISCC.exe" installer.iss
```

### 发布流程
1. 发布 Release 构建:
   ```powershell
   dotnet publish CleanMaster.csproj -c Release -r win-x64 --self-contained false -o bin\Release\publish
   ```
2. 打包安装包 (生成到 `installer\` 目录):
   ```powershell
   & "D:\Work_app\Inno Setup 6\ISCC.exe" installer.iss
   ```
3. 输出文件名格式: `CleanMaster-Setup-v{版本号}.exe`

### 版本号修改
- 改 `installer.iss` 中的 `AppVersion` 和 `OutputBaseFilename`
- 当前版本: v1.2.0

### 测试
```powershell
dotnet test tests\CleanMaster.Tests.csproj -c Debug
```

### Lint / TypeCheck
本项目为 C# WPF 项目，构建即类型检查:
```powershell
dotnet build CleanMaster.csproj -c Debug
```
