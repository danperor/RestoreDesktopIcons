# Windows 桌面图标守护程序 (RestoreDesktopIcons)

<div align="center">
  <img src="app.png" width="128" height="128" alt="RestoreDesktopIcons Logo" />
  <p><strong>现代 Windows 11 风格多屏桌面图标布局守护、自动隐藏与工业级灾备恢复神器</strong></p>
  <p>
    <img src="https://img.shields.io/badge/.NET-10.0_Windows-512BD4?logo=dotnet" alt=".NET 10" />
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D6?logo=windows" alt="Platform" />
    <img src="https://img.shields.io/badge/License-PolyForm%20Noncommercial-blue.svg" alt="License" />
  </p>
</div>

---

## 🌟 核心特性

- **🖥️ 多显示器拓扑智能自适应 (Multi-Monitor Topology)**
  - 完美支持主屏 + 多副屏、横竖混排、不同 DPI 缩放下的桌面图标物理绝对坐标记录。
  - **引力锚定防幽灵处理 (Gravity Clamping)**：拔掉外接屏幕或切换分辨率时，自动将越界图标安全吸附至当前可视工作区，并自动避让防重叠。
  - **一键副屏图标归巢 (`fix-to-primary`)**：断开副屏后，一键将副屏图标安全平移回主屏幕。

- **📸 全息纯净桌面多屏快照 (Pure Holographic Capture)**
  - 支持**原生图层直取**与**全息无遮挡合成**双引擎，截取快照时**绝不被前台打开的窗口遮挡**。
  - 自动为每个物理显示器生成独立纯净切片图及多屏拼接全景图，直观预览各历史布局。
  - **安全备份护航**：每次执行恢复前，系统自动在当前现场创建灾备快照，支持无损回退。

- **⏱️ AutoHide 智能自动隐藏与桌面双击唤醒**
  - 支持设定空闲超时（3秒/5秒/10秒/30秒/1分钟/5分钟）自动隐藏桌面图标，还你纯净壁纸。
  - 支持在桌面空白处**鼠标左键双击**（防误触）或单击瞬时唤醒恢复图标。
  - **零卡顿常驻独立线程钩子架构**：
    - **按需动态挂载**：仅在桌面图标处于隐藏状态时激活全局钩子，平时（99.9%使用场景）**0 钩子裸跑**，鼠标 100% 原始直通；
    - **独立专职 STA 线程**：赋予 `ThreadPriority.Highest` 最高调度优先级与极简专职消息泵，微秒级即时分发，彻底杜绝鼠标漂移与卡顿。

- **🛡️ 独创工业级双层容灾守护 (Dual-Layer Safeguard)**
  - **第 1 层：全生命周期进程内拦截**：捕获全局托管未捕获异常、WPF UI 线程异常、Task 逃逸异常、Windows 关机/注销及控制台信号，退出前必先还原桌面；
  - **第 2 层：内核级外部 Watchdog 守护子进程**：
    - 启动时自动伴随派生一个极低占用（0.00% CPU）的内核同步监控子进程；
    - 即使主程序遭遇 `taskkill /F`、IDE 强制停止调试（`Shift + F5`）或原生底层 Crash，**外部守护进程在 150 毫秒内自动强制唤醒 Windows Shell 弹回桌面图标**，彻底告别桌面留白瘫痪！
  - **CleanExit 内核事件握手**：正常退出与异常崩溃严格握手分离，绝不产生重复 Toggle 竞态。

- **🎨 现代 Windows 11 Fluent 视觉美学**
  - 原生支持 Fluent Design 晶透圆角、悬浮卡片、柔和动效与现代交互规范。
  - 内嵌 7 档全规格（16px ~ 256px）高清自适应图标，任务栏与系统托盘锐利细腻。

- **⌨️ 完整的 CLI 命令行终端支持**
  - 可无缝接入 Windows 任务计划程序、批处理脚本或快捷指令。

---

## 🚀 快速开始

### 环境依赖
- Windows 10 (1809+) 或 Windows 11 (64位)
- [.NET 10.0 SDK / Runtime (Windows Desktop)](https://dotnet.microsoft.com/download)

### 编译构建
```bash
# 克隆代码仓库
git clone <your-repo-url>
cd RestoreDesktopIcons

# 还原并编译发布版本
dotnet build -c Release
```
编译产物位于 `bin/x64/Release/net10.0-windows/RestoreDesktopIcons.exe`。

---

## 💻 CLI 命令行使用指南

程序支持丰富的命令行参数，执行完即刻退出，轻量快捷：

```bash
# 保存当前桌面图标布局并生成多屏快照
RestoreDesktopIcons.exe save

# 恢复最新快照（恢复前会自动创建现场灾备快照）
RestoreDesktopIcons.exe restore

# 诊断当前屏幕拓扑、分辨率与桌面所有图标物理坐标
RestoreDesktopIcons.exe diag

# 一键将副屏上的所有图标平移回主屏幕
RestoreDesktopIcons.exe fix-to-primary

# 立即隐藏所有桌面图标
RestoreDesktopIcons.exe hide

# 立即显示所有桌面图标
RestoreDesktopIcons.exe show

# 切换桌面图标显隐状态
RestoreDesktopIcons.exe toggle

# 静默启动并最小化至托盘后台运行
RestoreDesktopIcons.exe --minimized

# 查看命令行帮助说明
RestoreDesktopIcons.exe help
```

---

## 🏗️ 架构概览

```
RestoreDesktopIcons/
├── Models/                 # 实体数据契约 (快照模型、屏幕拓扑、图标属性、配置模型)
│   ├── AppSettingsModel.cs
│   ├── IconItemModel.cs
│   ├── LayoutSnapshotModel.cs
│   └── ScreenTopologyModel.cs
├── Services/               # 核心业务与底层 Win32/COM 驱动服务
│   ├── AppLogger.cs                      # 线程安全文件与控制台统一日志
│   ├── AutoHideCoordinator.cs            # 自动隐藏总调度与状态互斥
│   ├── AutoStartupHelper.cs              # 开机自启动注册表管理
│   ├── DesktopIconVisibilityService.cs   # 权威物理显隐控制与纳秒缓存
│   ├── DesktopInteractionHook.cs         # 独立高优先级线程低级鼠标钩子
│   ├── DesktopShellCsWin32Service.cs     # 原生 Shell COM/Win32 坐标抓取与原子写入
│   ├── EmergencyRecoveryService.cs       # 进程内信号拦截与外部 Watchdog 容灾
│   ├── GravityClampingEngine.cs          # 引力锚定防幽灵图标与碰撞规避算法
│   ├── IdleDetectionService.cs           # 系统真实空闲时间毫秒级探测
│   ├── LayoutStorageService.cs           # 快照与配置 JSON 持久化存储
│   ├── ScreenCaptureService.cs           # 原生多屏无遮挡切片与全景拼图引擎
│   ├── TopologyService.cs                # 物理显示器拓扑与坐标原点测量
│   └── TrayIconService.cs                # 任务栏右下角托盘常驻与右键菜单
├── ViewModels/             # MVVM 视图模型驱动
│   ├── MainViewModel.cs
│   └── ViewModelBase.cs
├── Views/                  # Fluent 风格 WPF 界面
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── app.ico                 # 7档高DPI内嵌多规格 Windows 图标
├── app.png                 # 高清 Fluent 徽标资源
├── NativeMethods.txt       # Microsoft.Windows.CsWin32 原生调用声明清单
└── RestoreDesktopIcons.csproj
```

---

## 📜 许可证 (License)

本项目采用 **[PolyForm Noncommercial License 1.0.0](LICENSE)** 授权：

- **个人及非商业用途 (Personal & Noncommercial)**：**完全免费**。任何人均可免费使用、下载、复制、定制及非商业分发。
- **商业用途 (Commercial Use)**：任何以商业营利、企业生产环境部署、商业软件捆绑或收费服务为目的的使用，**必须取得版权所有者的正式商业授权**。

如需商业许可，请联系代码仓库维护者。
