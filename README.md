# Quick App

Windows 桌面应用启动器：启动时扫描开始菜单和桌面，把常用应用放到可拖拽排序的
圆角卡片首页，支持搜索、分组、深色主题、自定义图标（PNG/ICO，可拖入）和网页搜索。
WPF + .NET Framework 4.x，运行时 Windows 自带，无需安装任何依赖。

## 文件说明

| 文件 | 说明 |
|---|---|
| `Launcher.cs` | 主程序（Quick App） |
| `Setup.cs` | 安装/卸载器，构建时把 `AppShelf.exe` 作为资源内嵌 |
| `Build.cmd` | 一键构建脚本（调用系统自带 csc，无需 SDK） |
| `BuildIcon.py` / `KuaiApps.ico` | 图标生成脚本与成品图标（需 Pillow） |

## 构建

双击 `Build.cmd`，或在命令行运行。产物：

- `AppShelf.exe` —— 主程序，可直接运行；`--self-test` 跑内置自测
- `KuaiApps-Setup.exe` —— 安装包；`--self-test` 自测、`--uninstall` 卸载

发布时把产物重命名为带版本号的名字（如 `QuickApp-Setup-0.1.3.exe`），并把
`Launcher.cs`、`Setup.cs`、`Build.cmd`、`BuildIcon.py`、`KuaiApps.ico`、`README.md`
打包为 `QuickApp-<版本>-Source.zip`。

## 数据位置

- 程序：`%LOCALAPPDATA%\AppShelf\Program`
- 配置与图标：`%LOCALAPPDATA%\AppShelf\Data`（卸载时保留）

## 版本号

版本号出现在三处，发布时同步更新：

- `Launcher.cs` 顶部的 `AssemblyVersion` / `AssemblyFileVersion`
- `Setup.cs` 的 `AssemblyVersion` / `AssemblyFileVersion` 与 `const string Version`
- `Launcher.cs` 内的 `Notes`（应用内「版本更新」弹窗文案）
