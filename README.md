# PetTools · 下班倒计时

基于 C#、.NET 8 和 WinUI 3 的 Windows 桌面小工具，包含下班倒计时、排班与休息日设置、发薪日、薪资隐私验证、喝水/走动提醒以及宠物互动。玻璃外观提供日间、夜间和系统自适应模式。

此仓库保存源代码、运行必需的素材、第三方许可证和安装包构建脚本，不保存已发布程序、安装包、编译缓存或个人配置。

## 开发与构建

在 Windows x64 上安装 .NET 8 SDK，以及 Visual Studio 2022 的 .NET 桌面开发和 Windows 应用开发所需工具。推荐在 Windows 11 x64 开发和运行；项目最低目标为 Windows 10 2004，Windows 10 与 ARM64 尚未完成实机验收。

```powershell
.\build.ps1
.\build.ps1 -Publish
```

首次构建会通过 NuGet 还原项目指定版本的 Windows App SDK、LiquidGlassWinUI 和 WinUIEx。发布输出位于 `publish/`，是自包含目录，必须保留其 DLL、资源和运行时文件；不能只分发主 EXE。

## 构建安装包

1. 按 [prerequisites/README.md](prerequisites/README.md) 获取微软 VC++ x64 运行库安装程序，放到 `prerequisites/VC_redist.x64.exe`。
2. 安装 Inno Setup 6.7.3 或更新版本。
3. 运行：

```powershell
.\installer\build-installer.ps1 -CompilerPath 'C:\工具\Inno Setup 6\ISCC.exe'
```

详见 [安装包构建说明](installer/README.md)。脚本会验证微软运行库签名，并生成安装 EXE 和 SHA256 文件。安装器支持当前用户安装、快捷方式、覆盖升级和卸载；用户设置在升级与卸载时保留。

## 项目入口

- `MainWindow.cs`：桌面组件、托盘、宠物交互与退出处理。
- `SettingsWindow.cs`：设置界面和排班选项。
- `Core.cs`、`ChinaHolidays.cs`、`Reminders.cs`：时间、节假日与提醒逻辑。
- `PrivacyStore.cs`：当前 Windows 用户 DPAPI 加密与密码验证。
- `GlassBackdrop.cs`、`DesktopGlassSource.cs` 等：玻璃与背景兼容处理。
- `assets/`：图标、宠物图集、节假日数据及素材说明。
- `installer/`：Inno Setup 安装脚本与中文翻译。

使用方法和兼容性限制见 [使用说明](使用说明.md)。其中历史验证记录引用的工作区测试工具不属于本次上传范围。

## 数据与兼容性

个人数据保存在 `%LOCALAPPDATA%\OffWorkWidget`，不属于源代码。薪资与密码验证信息位于独立的隐私数据库中，复制仓库不会复制个人设置。

玻璃背景采用兼容采样路径，部分 GPU、受保护或半透明窗口可能无法正确提供画面，此时使用半透明外观。不能保证所有应用的背景效果完全一致。

## 第三方说明

- LiquidGlassWinUI：见 [许可证](LiquidGlassWinUI-LICENSE.txt)。
- 节假日数据：见 [许可证](assets/holiday-cn-LICENSE.txt)。
- 宠物与图标：见 [素材说明](assets/素材说明.md)。
- Inno Setup 中文翻译：保留 `installer/ChineseSimplified.isl` 内的原作者声明。

项目原创代码尚未指定许可证，本次上传未新增许可证。