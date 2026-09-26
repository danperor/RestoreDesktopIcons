# Windows 11 桌面图标布局守护程序 (WPF + CsWin32) 核心架构与详细设计规范

本文档为“Windows 11 桌面图标布局守护程序”的终极技术与架构规范。针对传统工具在 Windows 11 下普遍存在的**“幽灵图标（移出屏幕看不见）”、“名称匹配错乱”、“系统刷新/重绘后瞬间弹回原样”、“高分屏 DPI 缩放漂移”**等顽疾，本设计提出并实施**“四维全息守护架构 (4D Holographic Desktop Guard Architecture)”**，从数据指纹、时空引力拓扑、系统持久化通道、工业级 COM 互操作四个维度实现根本性解决。

---

## 1. 项目定位与核心目标 (Product Vision)

- **形态**：精简现代化 WPF 桌面独立程序（.NET 10 x64，无第三方臃肿依赖，类 DesktopOK 的极简高效体验）。
- **使命**：在多显示器热插拔、全屏低分辨率游戏切换、显卡驱动更新等场景下，实现桌面图标的**毫秒级无损保存与 100% 绝对还原**。
- **质量基线**：**0 幽灵图标（无越界隐藏）、0 匹配错乱、0 刷新弹回、0 DPI 漂移**。

---

## 2. 强制技术选型与规范 (Mandatory Constraints)

1. **框架与运行时**：
   - 目标框架：.NET 10 (`net10.0-windows`)。
   - 界面框架：WPF (Windows Presentation Foundation) + MVVM 架构。
   - 平台：Windows 11 (x64 优化)。
2. **底层互操作约束 (CsWin32 强制规范)**：
   - **全面使用微软官方代码源生成器 `Microsoft.Windows.CsWin32`**。
   - **绝对禁止任何手工编写的 `[ComImport]` 接口或手写虚函数表结构**，防止 32/64 位对齐错误与插槽错位（实测已证实手写容易漏 slot 导致不可预知崩溃）。
   - 严禁任何内存注入（`VirtualAllocEx` / `WriteProcessMemory`）或废弃的 `LVM_SETITEMPOSITION` 消息。
3. **环境与模式**：
   - 主线程严格运行于 `[STAThread]`。
   - 编译级启用 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 用于原生指针高效安全互操作。
   - 嵌入式清单 `app.manifest` 强制开启 `<dpiAwareness>PerMonitorV2</dpiAwareness>`。

---

## 3. 降维打击核心：四维全息守护架构 (The 4D Holographic Architecture)

```
                       ┌────────────────────────────────────────────────────────┐
                       │           四维全息桌面守护架构 (CsWin32 + WPF)          │
                       └────────────────────────────────────────────────────────┘
                                                   │
         ┌───────────────────┬─────────────────────┴───────────────────┬───────────────────┐
         ▼                   ▼                                         ▼                   ▼
【第一维：全息多重身份指纹】 【第二维：多屏时空引力锚定】              【第三维：双通道原子提交】   【第四维：CsWin32 零漏洞】
 (Holographic Identity)   (Topology Gravity Clamping)               (Atomic State Flush)     (Zero-Defect Interop)
   • 绝对 ParsingName        • 屏幕物理设备与拓扑指纹                  • 自动排列前置硬拦截       • 微软官方代码源生成
   • Shell CLSID GUID        • 多屏工作区与虚拟屏幕总边界              • 批量无闪烁原子注入       • 64 位虚函数表精确对齐
   • 二进制 PIDL Base64      • 智能可视区约束 (防幽灵算法)             • Shell 视图状态冲刷       • 托管指针与 PIDL 生命周期
   • LNK 快捷方式目标解析    • DPI 缩放率与桌面网格间距 (Spacing)      • 彻底防系统重绘弹回       • PerMonitorV2 像素级无漂移
   • 友好 DisplayName 兜底   • 视图模式 (ViewMode) 记录                • 杜绝 Explorer 缓存竞争   • 0 手写 ComImport
```

### 3.1 第一维：全息多重身份指纹体系 (Holographic Identity System)
传统工具仅记录“文件名”，在遇到桌面合成源目录（当前用户与公用桌面）、特殊虚拟项（此电脑、回收站）、系统切换语言或扩展名隐藏时必然匹配混乱。本项目采用四级降级指纹：

| 识别级别 | 字段名称 | 说明与作用 | 解决的问题 |
| :--- | :--- | :--- | :--- |
| **Tier 1 (主键)** | `ParsingName` | 物理文件绝对路径（如 `C:\Users\Public\Desktop\Edge.lnk`）或 Shell 虚拟项 GUID（如回收站 `::{645FF040-5081-101B-9F08-00AA002F954E}`、此电脑 `::{20D04FE0-3AEA-1069-A2D8-08002B30309D}`） | 100% 区分用户/公用桌面同名项；保证虚拟项在任何语言下绝不失效 |
| **Tier 2 (二进制)** | `PidlBase64` | `ITEMIDLIST` 的原始二进制字节流序列化 Base64 | 即使文件处于未完全解析的 Shell 状态，也能通过底层结构比对 |
| **Tier 3 (目标)** | `ShortcutTarget` | 若为 `.lnk` 快捷方式，解析其真实指向的物理目标绝对路径（Target Path） | 用户若修改了桌面快捷方式显示名称，依然能通过指向的程序精准找回并恢复 |
| **Tier 4 (兜底)** | `DisplayName` | 最终的友好显示名称（如 "火绒安全"、"微信"） | 用于界面展示及极端情况下的降级匹配 |

---

### 3.2 第二维：多屏时空拓扑与智能引力锚定 (Display Topology & Spatial Gravity Clamping Engine)
“幽灵图标”的本质：当用户拔掉外接屏幕、或分辨率从 4K 变为 1080p 时，原来位于 `(2500, 1400)` 的图标落在了**当前任何屏幕都无法渲染的死区**中，导致人眼不可见但底层坐标存在。

#### 1. 时空拓扑记录 (Save 阶段)
每个快照不仅保存图标，同时捕获完整的屏幕时空环境：
- 所有活动显示器的设备名（`\\.\DISPLAY1` 等）、物理矩形（`rcMonitor`）、可用工作区（`rcWork`）。
- 虚拟屏幕总矩形（`SM_XVIRTUALSCREEN`, `SM_YVIRTUALSCREEN`, `SM_CXVIRTUALSCREEN`, `SM_CYVIRTUALSCREEN`）。
- 桌面当前视图模式（`ViewMode`）与网格物理间距（`IFolderView::GetSpacing`）。

#### 2. 智能引力吸附算法 (Restore 阶段防幽灵引擎)
```
[读取快照图标坐标 (X, Y)]
         │
         ▼
[当前活动显示器工作区校验] ──► 是否处于任一活动显示器的 rcWork 内部？
         │
         ├─► [是] ──► 保持原始物理坐标 (X, Y)，直接恢复
         │
         └─► [否 (检测到越界幽灵坐标!)]
                 │
                 ▼
         【触发智能引力锚定引擎】:
           1. 计算该坐标距离最近的当前活动显示器边界
           2. 按照桌面网格间距 (Grid Spacing) 进行量化对齐
           3. 智能吸附投影到当前主屏幕/就近屏幕的可视工作区边缘
           4. 界面标记高亮并向用户发出“坐标引力吸附保护”提示
```
> **结果**：绝无任何一个图标会丢失或流落在屏幕之外，100% 确保所有人眼所见即所得。

---

### 3.3 第三维：双通道原子提交与状态树持久化冲刷 (Dual-Channel Atomic Flush)
为什么有些工具恢复后，右键刷新或系统几秒钟后又弹回原样？因为 Explorer 把视图状态缓存在内存中，没有即时刷入注册表和 Shell 状态树，被系统后台刷新逻辑（如 OneDrive、文件监控、Explorer 重排）瞬间覆盖。

1. **自动排列前置硬拦截 (Auto-Arrange Guard)**：
   - 恢复前调用 `IFolderView::GetAutoArrange()`。
   - 若检测到 `S_OK`（开启自动排列），系统在界面弹出醒目警报指示灯，阻断盲目恢复，指引用户关闭自动排列（或自动执行规避）。
2. **原子级批量无缝写入**：
   - 不采用逐个 item 循环写入，而是使用原生指针数组一次性调用 `IFolderView::SelectAndPositionItems(cidl, apidl, apt, 0)`。
   - 彻底避免界面逐个图标跳动的闪烁感和渲染竞争。
3. **Shell 状态树持久化冲刷 (View State Flush)**：
   - 写入完成后，调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSHNOWAIT, ...)` 触发 Shell 全局缓存同步，促使 Explorer 立即将内存布局提交至视图流，杜绝回滚。

---

### 3.4 第四维：基于 CsWin32 的工业级零漏洞互操作
所有底层结构与函数由微软 `Microsoft.Windows.CsWin32` 源生成：
- `NativeMethods.txt` 精确声明：
  - `IShellWindows`, `IServiceProvider`, `IShellBrowser`, `IFolderView`, `IShellItem`, `IShellFolder`
  - `SHCreateItemFromIDList`, `SHChangeNotify`, `GetSystemMetrics`, `CoTaskMemFree`
- 原生指针管理：通过严格的 RAII 模式管理 `PCUITEMID_CHILD` 与 `IntPtr`，在 `finally` 块中确定性释放，杜绝非托管内存泄露。
- 清单严格配置 `PerMonitorV2`，在 100%、125%、150%、200% 混用缩放的多屏环境下无任何 DPI 缩放漂移。

---

## 4. 数据模型规范 (`layouts.json`)

```json
{
  "version": 2,
  "snapshots": [
    {
      "id": "e4f8b2d1-7c9a-4e2b-9f0a-1a2b3c4d5e6f",
      "name": "日常办公_双屏全景布局",
      "createdAt": "2026-09-23T15:25:00+08:00",
      "screenResolutionSummary": "3840x2160 + 1920x1080",
      "viewMode": 1,
      "gridSpacing": { "x": 117, "y": 147 },
      "topology": {
        "virtualScreen": { "left": 0, "top": 0, "width": 5760, "height": 2160 },
        "monitors": [
          {
            "deviceName": "\\\\.\\DISPLAY1",
            "isPrimary": true,
            "bounds": { "left": 0, "top": 0, "right": 3840, "bottom": 2160 },
            "workArea": { "left": 0, "top": 0, "right": 3840, "bottom": 2100 },
            "dpi": 144
          },
          {
            "deviceName": "\\\\.\\DISPLAY2",
            "isPrimary": false,
            "bounds": { "left": 3840, "top": 0, "right": 5760, "bottom": 1080 },
            "workArea": { "left": 3840, "top": 0, "right": 5760, "bottom": 1040 },
            "dpi": 96
          }
        ]
      },
      "icons": [
        {
          "displayName": "回收站",
          "parsingName": "::{645FF040-5081-101B-9F08-00AA002F954E}",
          "pidlBase64": "GAAAAFEAbwBu...",
          "shortcutTarget": null,
          "x": 1346,
          "y": 1442
        },
        {
          "displayName": "Microsoft Edge",
          "parsingName": "C:\\Users\\Public\\Desktop\\Microsoft Edge.lnk",
          "pidlBase64": "LgAAAFkAYwBu...",
          "shortcutTarget": "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
          "x": 1346,
          "y": 1589
        }
      ]
    }
  ]
}
```

### 3.5 第五维：双引擎桌面视觉快照架构 (Dual-Engine Desktop Capture)

为兼顾“100% 物理真实尺寸还原”与“后台纯净静默无闪烁重绘”，系统实现了顶级的**双引擎桌面视觉快照架构**：

1. **引擎 1：真实物理硬件 DWM 快照 (Direct DWM Snapshot，默认推荐)**：
   - **机制**：调用 Windows Shell 原生瞬间曝光通道（`ToggleDesktop` 约 260ms），在应用窗口隐去的瞬间，通过虚拟屏幕设备上下文（`GetDC(HWND.Null)`）直接从显卡物理前缓冲区按各物理屏幕边界执行 `BitBlt` 采集。
   - **特点**：图标尺寸、文字阴影、壁纸拉伸算法与用户肉眼所见 $1:1$ 绝对真实，解决高分屏（如 2240x1400，150% DPI）下的视觉缩放差异。
   - **工业级容灾**：`finally` 块中 100% 保证前台所有窗口自动恢复；若硬件采集受阻，自动平滑安全降级至引擎 2。

2. **引擎 2：纯净全息重构引擎 (Pure Synthetic Hologram，完整保留并升级)**：
   - **机制**：从系统缓存提取原始壁纸，使用 Shell 接口（`SHGetFileInfo`）提取高保真图标，按物理坐标全后台静默重绘。
   - **升级**：加入物理屏幕 DPI 缩放感知（支持 48px/64px 高清大图标与自适应字体）。
   - **特点**：前台完全零闪烁、不最小化任何应用窗口。

3. **用户偏好持久化与多屏全景缝合**：
   - 主界面工具栏提供 `[x] 📸 真实物理截图 (推荐)` 勾选开关，配置自动写入 `settings.json`；
   - 两个引擎产出的物理分屏位图均通过统一的 `BuildPanoramicImage` 生成带有深色科技风格屏幕标识的横向全景图。

---

## 4. 核心数据模型 (Data Contract)

```json
{
  "id": "3cc7100d-efc8-400e-90e9-593a51539892",
  "name": "2026-09-23 22:38 (2240x1400* + 1920x1080 (共2屏))",
  "createdAt": "2026-09-23T22:38:41.7885046+08:00",
  "viewMode": 1,
  "gridSpacingX": 117,
  "gridSpacingY": 147,
  "screenshotPath": "screenshots\\3cc7100d-efc8-400e-90e9-593a51539892.jpg",
  "monitorScreenshots": {
    "\\\\.\\DISPLAY1": "screenshots\\3cc7100d-efc8-400e-90e9-593a51539892_DISPLAY1.jpg",
    "\\\\.\\DISPLAY2": "screenshots\\3cc7100d-efc8-400e-90e9-593a51539892_DISPLAY2.jpg"
  },
  "topology": {
    "virtualScreen": { "left": 0, "top": 0, "right": 4160, "bottom": 1400, "width": 4160, "height": 1400 },
    "monitors": [
      {
        "deviceName": "\\\\.\\DISPLAY1",
        "isPrimary": true,
        "bounds": { "left": 0, "top": 0, "right": 2240, "bottom": 1400, "width": 2240, "height": 1400 },
        "clientBounds": { "left": 0, "top": 0, "right": 2240, "bottom": 1400, "width": 2240, "height": 1400 }
      }
    ]
  },
  "icons": [
    {
      "displayName": "回收站",
      "parsingName": "::{645FF040-5081-101B-9F08-00AA002F954E}",
      "pidlBase64": "FAAfeEDwX2SBUBsQnwgAqgAvlU4AAA==",
      "shortcutTarget": null,
      "x": 22,
      "y": 2
    }
  ]
}
```

---

## 5. 交互界面设计 (DesktopOK 经典形态)

采用极简紧凑工具箱形态（Compact Tool Window）：
1. **顶部操作栏**：
   - 💾 **保存当前布局 (Save)**：即刻读取当前桌面并生成纯净无遮挡各分屏视觉快照与横向全景图。
   - 🔄 **恢复选中布局 (Restore)**：将选中快照通过原子通道回填，若检测到多屏拓扑变化自动提示引力吸附状态，恢复前自动生成现场灾备快照。
   - 🖼️ **查看截图 (View Screenshot)**：一键查看当前快照的横向并排全景图或独立主屏图。
   - ✏️ **重命名 (Rename)**：自由定制快照名称（如“双屏剪辑专用”、“游戏防乱专用”）。
   - 🗑️ **删除快照 (Delete)**：清除冗余快照，并自动同步级联清理关联的所有分屏截图文件。
2. **中间快照列表 (DataGrid)**：
   - 列定义：快照名称、分辨率与拓扑概览、图标总数、截图预览（快速打开按钮）、保存时间。
   - 右键丰富快捷菜单：恢复布局、查看并排全景截图、查看主屏纯净截图 (DISPLAY1)、查看副屏纯净截图 (DISPLAY2)、打开截图保存目录。
3. **状态与预警监控区 (Status & Guard Bar)**：
   - ⚠️ **自动排列警示灯**：实时检测桌面是否勾选“自动排列”，亮警示栏并提示“需取消勾选以保证恢复生效”。
   - 🖥️ **当前拓扑指纹**：实时显示当前活动屏幕总分辨率与工作区。
   - 🛡️ **幽灵防护计数器**：恢复时显示“成功恢复 N 个图标，引力吸附防护 0 个”。
