# 需求与上下文文档：Windows 11 桌面图标坐标守护程序 (C#)

## 1. 项目背景 (Background)
在早期的 Windows 系统（Win7/Win8 及部分早期 Win11）中，桌面本质上是一个标准的 ListView 控件 (`SysListView32`)。当时的第三方图标恢复工具（如 DesktopOK、ReIcon 等）普遍采用向该窗口发送 `LVM_SETITEMPOSITION` 消息的方式来强行修改图标坐标。

然而，随着 Windows 11 底层架构和桌面渲染机制的重构，微软彻底废弃了这种跨进程的消息机制。现代系统的桌面由 Shell 基础架构接管，强行发送传统坐标修改消息会被系统底层的图标管理逻辑无视、拦截，甚至被 OneDrive 桌面备份机制瞬间覆盖，导致市面上的老牌工具大面积失效。我们需要彻底抛弃旧时代的黑客式注入方案，转而使用微软官方支持的现代 COM 接口进行正规调用。

## 2. 当前面临的痛点 (Pain Points)
- **传统工具全面失效**：老牌软件无法在较新版本的 Windows 11 中成功写入图标坐标，或者写入后被系统立即重绘重置，导致“保存成功，恢复失败”。
- **开源生态质量堪忧**：GitHub 上相关的开源工具要么久未维护（基于旧 API），要么星标极少、代码不透明，作为需要底层权限的系统级工具，盲目运行存在安全隐患和系统稳定性风险。
- **高频破坏场景**：日常外接多显示器热插拔、运行全屏低分辨率游戏、或是显卡驱动更新，都会导致 Windows 11 桌面图标瞬间被打乱。系统缺乏原生的“快照回滚”功能，每次手动整理耗时且令人抓狂。

## 3. 产品目的与核心逻辑 (Product Purpose)
我们希望使用 C# 和 .NET 10 开发一个极轻量、纯透明、无后台驻留的控制台命令行工具（CLI）。它不提供臃肿的 GUI 界面，只专注做好一件事：通过合规的现代 API 精准读取和恢复桌面图标坐标。

### 核心功能形态：
- **形态**：单文件可执行程序 (Single-file Executable)，便于后续挂载到 Windows 计划任务或右键菜单。
- **操作指令**：
  - `DesktopIconTool.exe save`：读取当前桌面所有快捷方式/文件的绝对坐标，并序列化保存到本地同目录下的 `layout.json` 文件中。
  - `DesktopIconTool.exe restore`：读取 `layout.json`，并将桌面图标精准恢复到记录的坐标位置。

## 4. 技术实现路径规范 (Technical Specs & Constraints)
- 严禁使用任何基于 `FindWindow` 获取 `Progman` 或 `SysListView32` 句柄并发送 `SendMessage` / `PostMessage` 的方案。
- 必须使用基于 Windows Shell COM 接口的现代方案。AI 在编写代码时需遵循以下核心技术链路：

### 1) 获取桌面视图接口：
- 通过 `SHGetDesktopFolder` 或实例化 `ShellWindows` (CLSID: `9BA05972-F6A8-11CF-A442-00A0C90A8F39`) 抓取当前的桌面窗口。
- 逐层获取接口：`IShellBrowser` -> `IShellView`。
- 最终将 `IShellView` 强制转换为 `IFolderView` (或 `IFolderView2`) 接口。这是整个项目的核心。

### 2) 数据读取 (Save 逻辑)：
- 调用 `IFolderView::Items` 遍历当前桌面上的所有项目，获取它们的 PIDL (Pointer to an Item ID List)。
- 调用 `IFolderView::GetItemPosition(pidl, out POINT pt)` 获取每个图标的精确二维坐标。
- 通过 `IShellFolder::GetDisplayNameOf` 将 PIDL 转换为可读的文件名，将“文件名 - 坐标(X,Y)”的键值对存入 JSON。

### 3) 数据恢复 (Restore 逻辑)：
- 解析 JSON 文件。
- 遍历当前桌面项目，通过名称匹配 JSON 中的记录。
- 使用匹配到的 PIDL 数组和 POINT 数组，调用 `IFolderView::SelectAndPositionItems` (或者使用 `IFolderView::SetItemPosition`) 将坐标批量写回系统。该接口是 Win11 下唯一合规且不会被系统强制重绘覆盖的底层通道。

### 4) COM 互操作处理：
- 需要在 C# 中正确声明这些非托管 COM 接口（如 `IFolderView`, `POINT`, `PCUITEMID_CHILD` 等）的 `[ComImport]` 和 `[Guid]`。
- 确保内存释放（如 `Marshal.ReleaseComObject` 和 `CoTaskMemFree`）正确无误，避免内存泄漏。
