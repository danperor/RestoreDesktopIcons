# Windows 11 桌面图标坐标守护程序：需求分析与技术修正报告

本文档针对原始需求文档中的背景、痛点、产品定位与技术实现链路进行深度技术审校。经过在 Windows 11 现代 Shell 环境（.NET 10 x64）下的实机 COM 接口验证，指出文档中存在的理论偏差、虚构/不存在的 API、隐式崩溃陷阱以及关键缺失项，并提供经过实机验证的标准修正方案。

---

## 一、 核心目的与定位理解

本工具旨在打造一个基于 **.NET 10 的现代化、单文件、无后台常驻的控制台 CLI 守护工具**：
1. **解决痛点**：在 Windows 11 下，由于旧版基于 `SysListView32` 句柄和 `LVM_SETITEMPOSITION` 消息的跨进程注入方案全面失效（被现代 DirectUI/Shell 架构拦截、重绘覆盖或 OneDrive 覆盖），需要一种合规、官方底层原生支持的方案。
2. **场景支持**：主要应对多显示器热插拔、高低分辨率全屏游戏切换、显卡驱动更新等导致桌面图标乱飞的高频破坏场景。
3. **核心功能**：
   - `DesktopIconTool.exe save`：抓取当前桌面图标（含公用桌面、用户桌面及特殊虚拟 Shell 文件夹项）的精确坐标与拓扑元数据，序列化写入 `layout.json`。
   - `DesktopIconTool.exe restore`：读取 `layout.json`，根据唯一命名空间标识符将桌面图标批量恢复至原位，并保证系统刷新后不失效。

---

## 二、 原始文档中不准确与潜在陷阱的逐项分析

### 1. 获取桌面视图接口的链路混淆 (`SHGetDesktopFolder` 无法获取视图)
- **原文描述**：
  > “通过 `SHGetDesktopFolder` 或实例化 `ShellWindows` (CLSID: `9BA05972-F6A8-11CF-A442-00A0C90A8F39`) 抓取当前的桌面窗口。逐层获取接口：`IShellBrowser` -> `IShellView` -> `IFolderView` (或 `IFolderView2`)。”
- **不准确之处与深度分析**：
  1. **`SHGetDesktopFolder` 不能获取当前桌面活动视图**：
     - `SHGetDesktopFolder` 返回的是 `IShellFolder` 接口。这是 Shell 命名空间的**数据模型层 (Model)**，并不拥有任何正在屏幕上渲染的窗口或活动视图。
     - 如果强行调用 `IShellFolder::CreateViewObject`，它会创建一个**脱机的独立视图对象**，根本无法操作当前屏幕上正在运行的桌面。
  2. **唯一正确链路是 `ShellWindows` 派生**：
     - 必须实例化 `CLSID_ShellWindows` (`9BA05972-F6A8-11CF-A442-00A0C90A8F39`)。
     - 调用 `IShellWindows.FindWindowSW(CSIDL_DESKTOP, ..., SWC_DESKTOP(8), out hwnd, SWFO_NEEDDISPATCH(1))` 获取桌面 WebBrowser/Shell 调度对象 (`IDispatch`)。
     - 将该对象 QI 到 `IServiceProvider`，并调用 `QueryService(SID_STopLevelBrowser, IID_IShellBrowser)` 拿到 `IShellBrowser`。
     - 调用 `IShellBrowser::QueryActiveShellView` 拿到活动视图 `IShellView`。
     - 最后 QI 到 `IFolderView`。
     - `SHGetDesktopFolder` 的真正用武之地，是后续通过 PIDL 解析显示名称 (`GetDisplayNameOf`)，或者更推荐直接由 `IFolderView::GetFolder` 动态获取当前桌面的 `IShellFolder`，无需独立调用 `SHGetDesktopFolder`。

---

### 2. 虚构 API 纠错 (`IFolderView::SetItemPosition` 不存在)
- **原文描述**：
  > “调用 `IFolderView::SelectAndPositionItems` (或者使用 `IFolderView::SetItemPosition`) 将坐标批量写回系统。”
- **不准确之处与深度分析**：
  - **严重错误**：查阅 Windows SDK 官方头文件 `ShObjIdl_core.h` 中的 `IFolderViewVtbl`，**`IFolderView` 根本不存在 `SetItemPosition` 方法**！
  - `IFolderView` 只有获取坐标的 `GetItemPosition`，以及批量定位的 `SelectAndPositionItems`。
  - 所谓的 `SetItemPosition` 是传统 ListView 控件宏 `ListView_SetItemPosition` (`LVM_SETITEMPOSITION`) 的名称混淆。
  - **结论**：写入坐标的**唯一正规合法方法**就是 `IFolderView::SelectAndPositionItems`。

---

### 3. 接口类型支持错误 (`IFolderView2` 在 Win11 桌面视图上不受支持)
- **原文描述**：
  > “最终将 `IShellView` 强制转换为 `IFolderView` (或 `IFolderView2`) 接口。”
- **不准确之处与深度分析**：
  - 实机测试验证，对 Windows 11 桌面的 `IShellView` 执行 `QueryInterface(IID_IFolderView2)` 会直接抛出：
    `Unable to cast COM object ... 不支持此接口 (0x80004002 (E_NOINTERFACE))`。
  - Windows 11 桌面视图（`CDefView`）虽然是现代 Shell，但其桌面窗口视图实例**仅实现了 `IFolderView`**，并未暴露 `IFolderView2`！
  - 若代码直接按 `IFolderView2` 转换，程序将立即崩溃退出。

---

### 4. 忽略了致命外部制约：“自动排列”与“对齐到网格”
- **原文描述**：
  - 原文完全未提及 Windows 桌面的自动排列状态。
- **严重潜在缺陷**：
  - 如果用户在桌面右键菜单中勾选了 **“自动排列图标” (Auto arrange icons)**，即使 `SelectAndPositionItems` 调用成功返回 `S_OK`，Windows Shell 也会在下次刷新或重绘时**瞬间强制重新排版，使恢复操作完全失效**！
  - **修正方案**：
    - 在 Save/Restore 前，必须调用 `IFolderView::GetAutoArrange()` 检查状态（返回 `S_OK` 即 0 表示开启，返回 `S_FALSE` 即 1 表示关闭）。
    - 若开启了自动排列，程序必须警告用户或通过用户交互进行指引，否则会导致“软件已提示恢复成功，但桌面上图标依旧被自动整理”的误解。

---

### 5. 数据模型缺陷：仅记录“文件名”无法应对虚拟项与多源桌面
- **原文描述**：
  > “通过 `IShellFolder::GetDisplayNameOf` 将 PIDL 转换为可读的文件名，将‘文件名 - 坐标(X,Y)’的键值对存入 JSON。”
  > “遍历当前桌面项目，通过名称匹配 JSON 中的记录。”
- **不准确之处与痛点分析**：
  1. **虚拟 Shell 项 (Virtual Folders)**：
     - 桌面上常驻的“回收站”、“此电脑”、“网络”、“控制面板”等不是普通物理文件。
     - 它们如果只取常规显示名（如“回收站”），一方面会随系统语言变更而失效，另一方面容易与用户自己创建的普通名为“回收站”的文件夹混淆。
     - 它们的真实唯一标识是解析名称 (Parsing Name / CLSID GUID)，例如：
       - 回收站：`::{645FF040-5081-101B-9F08-00AA002F954E}`
       - 此电脑：`::{20D04FE0-3AEA-1069-A2D8-08002B30309D}`
       - 网络：`::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}`
       - 控制面板：`::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}`
  2. **公用桌面 vs 用户桌面 (Composite Desktop)**：
     - 桌面由 `%USERPROFILE%\Desktop` 与 `C:\Users\Public\Desktop` 双目录聚合而成。
     - 若仅保存简单无后缀或同名文件名，无法保证唯一性。
  3. **更优解**：
     - JSON 中每个图标应存储：
       - `Name`：人类可读的 DisplayName（如 "此电脑"、"微信"）。
       - `ParsingName`：绝对解析路径或 GUID（如 `::{20D04FE0-...}` 或 `C:\Users\Public\Desktop\Edge.lnk`），作为唯一主键用于精准匹配。
       - `Position`：`{ "x": 1346, "y": 1442 }`。

---

### 6. 忽略了多显示器拓扑与越界风险 (Multi-Monitor & DPI)
- **原文描述**：
  > 痛点中提到了“外接多显示器热插拔、运行全屏低分辨率游戏”。
  > 但技术实现中仅保存了简单的二维坐标 `(X, Y)`。
- **潜在严重后果**：
  - 桌面坐标为**虚拟屏幕绝对坐标**。在多显示器环境下：
    - 副屏幕位于主屏幕左侧或上方时，坐标可能为**负数**（如 `x = -1920`）。
    - 当用户拔掉外接显示器后执行 `restore`，如果盲目恢复负数或超大坐标，图标将被置于当前可视屏幕范围之外（Off-Screen 幽灵图标），用户在桌面上将彻底看不见该图标！
  - **修正方案**：
    - `save` 时，JSON 应同时持久化当前环境的“显示拓扑元数据”（所有显示器的分辨率、工作区范围、虚拟桌面总边界 `SM_XVIRTUALSCREEN`, `SM_YVIRTUALSCREEN`, `SM_CXVIRTUALSCREEN`, `SM_CYVIRTUALSCREEN`）。
    - `restore` 时，检查保存时的屏幕配置与当前屏幕配置是否一致。若检测到坐标越界，应进行边界保护或给用户明确的预警提示。

---

### 7. .NET 10 / C# COM 互操作的关键规范遗漏
- **原文描述**：
  > “需要在 C# 中正确声明这些非托管 COM 接口...确保内存释放正确无误”。
- **实测必须补充的技术细节**：
  1. **必须标注 `[STAThread]`**：
     - .NET 控制台应用默认主线程是 MTA (Multi-Threaded Apartment)。
     - Shell COM 接口（尤其是 `ShellWindows`、`IShellBrowser` 等）强依赖 STA 单线程单元模式。若未在 `Main` 标注 `[STAThread]`，会导致跨套间转换异常 (`RPC_E_WRONG_THREAD`) 或直接失败。
  2. **`IShellWindows` 虚函数表对齐**：
     - 若使用 `[ComImport]` 和 `InterfaceIsDual`，C# 接口声明中的方法顺序必须与 `ExDisp.h` 完全一致（包含被隐藏的方法如 `OnNavigate`, `OnActivated`）。顺序一旦错位一个插槽，调用 `FindWindowSW` 将会误调用相邻方法导致 `ArgumentException`。
  3. **PIDL 内存释放必须遵循 Windows 规范**：
     - 每次通过 `IFolderView::Item` 获得的 `IntPtr pidl`，必须在处理后立即调用 `CoTaskMemFree(pidl)` 或 `ILFree(pidl)`，不可依赖 GC。
  4. **清单配置 (DPI-Awareness)**：
     - CLI 工具必须包含 `app.manifest`，声明 `<dpiAwareness>PerMonitorV2</dpiAwareness>`。
     - 若未声明 DPI 感知，高分屏下 Windows 会对进程进行 DPI 虚拟化缩放，导致读取或写入的图标坐标产生非预期的偏移或漂移。

---

## 三、 修正后的现代技术架构路线图

```
[CLI 入口: Main (STAThread, PerMonitorV2)]
   │
   ├─► 1. 实例化 ShellWindows (CLSID_ShellWindows)
   │      │
   │      └─► IShellWindows.FindWindowSW(CSIDL_DESKTOP, ..., SWC_DESKTOP, SWFO_NEEDDISPATCH)
   │
   ├─► 2. QueryService(SID_STopLevelBrowser) ──► IShellBrowser
   │      │
   │      └─► IShellBrowser.QueryActiveShellView ──► IShellView
   │             │
   │             └─► QI ──► IFolderView (严格只用 IFolderView)
   │
   ├─► 3. 视图环境检查:
   │      ├─► IFolderView.GetAutoArrange() ──► 检查是否开启“自动排列图标”
   │      └─► 记录/比对虚拟屏幕尺寸与多显示器拓扑 (Virtual Screen Bounds)
   │
   ├─► 4. Save 操作:
   │      ├─► IFolderView.ItemCount(SVGIO_ALL) ──► 遍历每个索引 i
   │      ├─► IFolderView.Item(i, out pidl)
   │      ├─► IFolderView.GetItemPosition(pidl, out POINT pt)
   │      ├─► SHCreateItemFromIDList(pidl) ──► IShellItem
   │      │      ├─► GetDisplayName(NORMALDISPLAY) ──► 友好名称 (用于展示)
   │      │      └─► GetDisplayName(DESKTOPABSOLUTEPARSING) ──► 唯一路径/CLSID GUID (主键)
   │      ├─► CoTaskMemFree(pidl)
   │      └─► 序列化并存储至 layout.json
   │
   └─► 5. Restore 操作:
          ├─► 读取 layout.json 并反序列化
          ├─► 遍历当前桌面全部实时项目，以 ParsingName/DisplayName 为索引建立字典映射
          ├─► 整理匹配项的 apidl 数组与 apt (POINT[]) 坐标数组
          ├─► 坐标边界有效性校验 (防越界)
          ├─► IFolderView.SelectAndPositionItems(cidl, apidl, apt, dwFlags: 0)
          └─► 释放分配的临时资源与 COM 引用
```
