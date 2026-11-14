# 更新记录

版本号遵循软件正式名称，形如 `PhotoLensWin-1.0`。本文件与 [README.md](README.md) 保持同步。

---

## PhotoLensWin-1.2.0 — 2026-11-14

全量代码审查（Core 逻辑 / 运行时健壮性 / UI 一致性 / 使用体验四维度）后的修复与体验补齐。

### 稳定性（P0）
- **拖入外部图后删除/另存为作用错对象**：拖入预览补 `_current`，删除与另存为不再命中列表上一张
- **同图对比后删除崩溃**：删除改为「先摘画面 → 再释放缓存位图」，并校验行是否仍有效后才 `RemoveAt`
- **双开静默不启动**：命名 Mutex 单实例 + `ShowWindow/SetForegroundWindow` 激活已有窗口
- **启动即崩的路径**：日志清理包 try，`OnOpened` 整体兜底，异常写 `%TEMP%\photolens-crash.log` 并 Toast

### 画质评分（对齐 Mac 原版）
- **分辨率分改用原图尺寸**：此前误用 320px 统计缓冲，2400×800 只给 0.7 分，现为 5.0
- **曝光高光侧封顶 4 分**：白底产品图/雪景/文档扫描不再被判 0；纯白由 0 → 6.0
- **透明像素（alpha < 26）不参与统计**：去背 PNG 的透明区不再被当成死黑
- 补充/重写 6 项评分与缩放测试（含字节序、透明像素、原图分辨率回归）

### 交互与体验
- 胶片栏选中跟随主图并自动滚动
- 空文件夹清空界面并提示，不再残留上一目录缩略图
- 工具条右侧固定「适合窗口/实际大小」，左侧可横向滚动，窄窗口不裁切
- 「实际大小」仅在 100% 且非适合窗口时高亮
- Esc 退出对比；隐藏/显示列表后保留原列宽
- 缩略图宽度随列宽自适应，消除左右黑边
- 信息面板两列排版，宽度 216、限制高度，不遮挡翻页箭头
- 对比空态提示明确（下拉选择或拖拽）

### 主题
- **补齐亮色主题**：窗口/菜单/工具条/胶片卡片/状态栏/信息面板全部改为语义色资源（ThemeDictionaries），切换主题不再黑字黑底
- 路径比较统一为 `OrdinalIgnoreCase`（对比图、当前图、选中项）

### 性能与健壮性
- 缩略图缓存淘汰时保护屏幕上正在显示的位图
- 目录递归跳过 ReparsePoint，避免符号链接成环
- 拖放目录扫描改为后台线程，避免卡 UI
- 应用图标路径改用 `AppContext.BaseDirectory`，兼容单文件发布

### 行为变更
- 「适合窗口」下小图也放大铺满可用区域（对齐 Mac 与系统预览），不再限制为 100%

### 验证
- `dotnet build`：**0 警告 0 错误**
- `dotnet test`：**71 项全部通过**（原 69 项，新增 2 项回归测试）
- 快照核验：暗色/亮色主题、工具条、信息面板、胶片栏、小图放大

---

## PhotoLensWin-1.1.1 — 2026-11-12

用户反馈补丁：第二轮对齐 Mac 原版修复（7 项）+ GridSplitter 约束 + ScrollBar 优化。

### Bug 修复（对齐 Mac 原版）
- **FitPadding 24→6**（Mac 原版 `Core.swift` 6pt）：消除主图窗口和胶片缩略图的大片黑边
- **GridSplitter 拖动 Filmstrip 不跟随**：去掉 Filmstrip 硬编码 `Width="232"` 和 FilmItem 样式固定 `Width="208"`，让 Grid 列宽驱动尺寸
- **信息面板占大面板**：改为浮动右上角小面板（`VerticalAlignment=Top` + `Margin=0,18,18,0`），宽度 290→260
- **适合窗口按钮无激活态**：`BtnFit.IsFitted=true` 时自动加 `toggleOn` 高亮类
- **拖入主图修改原列表**：`HandleMainDrop` 区分来源 — 列表里有的 `SelectIndex`，外部拖入只 `LoadAndDropInAsync` 展示（不碰 `_images` 和 Filmstrip）
- **滚轮缩放需按 Ctrl**：改为直接滚轮缩放（factor 1.15），拖动平移保留

### 交互改进
- **GridSplitter 调节下限**：列表 MinWidth=120 / MaxWidth=480，主图 MinWidth=200，不被无限缩放
- **Filmstrip ScrollBar Auto**：有内容才出现，减少两根竖条视觉混乱

### 验证
- `dotnet build`：**0 警告 0 错误**
- `dotnet test`：**69 项全部通过**（FitScale_LeavesPadding 预期值随 FitPadding 更新）

### 涉及文件
```
修改:
  src/PhotoLensWin.App/PhotoLensWin.App.csproj      (版本号 1.1.0 → 1.1.1)
  src/PhotoLensWin.Core/PhotoLensWin.Core.csproj    (版本号 1.1.0 → 1.1.1)
  src/PhotoLensWin.Core/ViewTransform.cs            (FitPadding 24 → 6)
  src/PhotoLensWin.App/Views/MainWindow.axaml       (Filmstrip 去硬编码 Width, InfoPanel 改浮动)
  src/PhotoLensWin.App/Views/MainWindow.axaml.cs    (UpdateFitButtonState, HandleMainDrop, ApplyColumnConstraints)
  src/PhotoLensWin.App/Views/ImageViewer.cs         (OnWheel 直接缩放)
  src/PhotoLensWin.App/Views/Filmstrip.cs           (ScrollBarVisibility Auto)
  tests/PhotoLensWin.Tests/ViewTransformTests.cs    (FitScale_LeavesPadding 预期值更新)
```

---

## PhotoLensWin-1.1 — 2026-11-10

首版发布后全量审查 review（从真实用户体验、功能完整性、代码质量三维度提出 22 条改进），本次落地 15 项：
框架升级、高频功能补齐、核心体验优化、工程质量改进。

### 框架升级（P0）
- **目标框架 net7.0 → net9.0**：不再依赖已 EOL 的 .NET 7 Runtime，Windows 部署零门槛（net9.0 自包含发布约 60MB）
- **LangVersion 11 → 12**：用上 C# 12 的 `collection expression` 等语法糖
- **测试项目依赖升级**：xunit 2.9.2、VSTest 17.11.1、coverlet 6.0.2

### 新增功能（P0/P1）
- **移到回收站**：右键菜单新增"移到回收站"，用 `shell32!SHFileOperation`（P/Invoke，零 NuGet 依赖）；删除后自动从列表移除并跳到下一张
- **窗口位置/大小持久化**：`AppSettings` 新增 WindowLeft/Top/Width/Height，关闭时保存、启动时恢复
- **暗色/亮色主题切换**：菜单"视图 → 主题"一键切换，状态持久化
- **最近打开的文件夹**：菜单"文件 → 最近打开"动态子菜单（上限 10 条），清理不存在的条目
- **幻灯片/自动播放模式**：工具栏按钮 + 菜单项，3 秒/张循环，Esc 停止；到末尾自动回到开头
- **工具栏 ToolTip**：所有按钮悬停显示快捷键提示

### 体验优化（P1）
- **前 20 张缩略图优先解码**：`ThumbnailStore` 新增高优先级 2 并发通道 + 普通 4 并发通道，大目录首次载入前几张秒出，其余排队
- **缩略图 LRU 淘汰**：之前随机删，现在按 `LastAccessTime` 最旧优先淘汰，`ConcurrentDictionary` 内部维护时间戳
- **信息面板零重复读盘**：`FullImage` 解码时一次性读好 metadata directories 缓存进 `CachedDirectories`，刷新信息面板直接复用
- **画质评分标注说明**：信息面板新增"注：非 AI 评分，锐度/曝光/对比/色彩加权"，避免用户误解

### 工程改进（P2）
- **统一 EXIF 入口**：新增 `Core/MetadataReader.cs`（`Orientation()` + `OrientationFromPath()`），替代 App 层原 `ExifHelper` 零散实现
- **ExifLines 支持预缓存**：`Build()` 签名扩展接受 `cachedDirectories`，与 ImageStore 解码阶段复用同一份元数据
- **ThumbnailStore 实现 IDisposable**：释放 `SemaphoreSlim` + 全部缓存位图
- **错误日志**：`%APPDATA%/PhotoLensWin/error.log`，`MainWindow.LogError()` 统一入口
- **各项目正式版本号**：csproj 加 `<Version>1.1.0</Version>`，构建产物 dll 自带版本

### 未实现 / 延后说明
- **旋转写回文件**：保持临时旋转只改画面（与 Mac 原版语义一致），需要时可主动触发重编码
- **GIF/WebP 动画帧**：SkiaSharp `SKCodec` 支持但 Avalonia 显示端每帧重建 Bitmap 成本高，延后到 v1.2 做选项开关
- **另存为 JPEG 质量可调**：当前硬编码 92，加菜单选档不难但时间紧张，延后

### 验证
- `dotnet build`：**0 警告 0 错误**
- `dotnet test`：**69 项全部通过**（QualityScorerTests.Lines_ProvidesSummaryText 从 2 行更新为 3 行）
- 应用启动验证：无崩溃，空态提示正常

### 涉及文件
```
修改:
  src/PhotoLensWin.App/PhotoLensWin.App.csproj      (版本号, 框架, LangVersion)
  src/PhotoLensWin.Core/PhotoLensWin.Core.csproj    (版本号, 框架, LangVersion)
  tests/PhotoLensWin.Tests/PhotoLensWin.Tests.csproj (版本号, xunit/VSTest/coverlet 升级)
  src/PhotoLensWin.Core/AppSettings.cs              (窗口持久化, 主题, 最近文件夹, TouchRecentFolder)
  src/PhotoLensWin.Core/QualityScorer.cs             (评分说明文字)
  src/PhotoLensWin.Core/ExifLines.cs                (支持 cachedDirectories, 新增 ReadDirectories)
  src/PhotoLensWin.App/Services/ImageStore.cs       (CachedDirectories, 统一 MetadataReader)
  src/PhotoLensWin.App/Services/ThumbnailStore.cs   (LRU, Dispose, 高优先级通道)
  src/PhotoLensWin.App/Views/Filmstrip.cs            (前 20 张高优先级解码)
  src/PhotoLensWin.App/Views/MainWindow.axaml        (工具栏 ToolTip, 幻灯片按钮)
  src/PhotoLensWin.App/Views/MainWindow.axaml.cs     (回收站删除, 主题切换, 最近文件夹, 幻灯片, 窗口持久化, 错误日志)
  tests/PhotoLensWin.Tests/QualityScorerTests.cs     (Lines 长度断言 2 → 3)

新增:
  src/PhotoLensWin.Core/MetadataReader.cs           (统一 EXIF 方向入口)
```

### 已知限制（承自 1.0）
- GIF/WebP 只显示首帧；旋转不写回文件
- 胶片栏未虚拟化（大目录全部建控件）；1.1 已做"前 20 张优先解码"缓解，但超大目录（>5000 张）仍可能卡
- 拖放、触屏、剪贴板粘贴、explorer 调起、实拍 EXIF 方向等 Windows 真机项**尚未人工验证**

---

## PhotoLensWin-1.0 — 2026-11-06

首个 Windows 发布版：macOS 版 Photo Lens 的 C# + Avalonia UI 移植，功能与快捷键语义对齐原版。

### 功能
- 文件夹/文件载入：递归收集、隐藏文件过滤、命令行参数启动
- 六种排序：名称 A→Z / Z→A（自然排序）、修改时间、文件大小；切换时保持当前图片位置，同值按文件名稳定兜底
- 缩放与平移：Ctrl+滚轮锚点缩放、滚轮/Shift+滚轮平移、双击切换、触屏捏合、按住 `Z` 放大镜
- 对比模式：左右分栏、胶片栏拖拽设置对比图、外部图片不入列表、主/对比图互斥
- 画质评分：锐度/曝光/对比/分辨率/色彩加权；夜景不因整体偏暗扣曝光分
- 图片信息：像素尺寸、文件大小、EXIF 拍摄参数、修改时间、路径
- 右键菜单：复制图像 / 复制文件 / 复制路径 / 打开所在位置 / 另存为 JPEG / PNG
- 设置持久化：上次文件夹、排序规则、列表显隐

### 代码审查与修复
全量审查 Core 与 App 两层（插件报告 `review-1110355d-b352-4398-94f3-a654f0de3b50`，17 条问题），修复 15 条真缺陷，2 条经评估保留为已知限制：

- 大图缓存淘汰改为真正释放 `Bitmap`/`SKBitmap`，且不淘汰正在显示的主图/对比图
- 解码后失败路径释放已分配的原生位图
- 对比图异步载入增加「仍是最新选择」校验，失败时清理选择状态
- 目录递归枚举与排序移出 UI 线程，加入载入版本号防止旧结果覆盖新结果
- EXIF 方向矩阵迁入 Core，修正方向 7（副对角线翻转）的映射
- 剪贴板 PNG 流改为字段保活，修复 Windows 延迟渲染下「复制图像」失效
- 实现触屏捏合（`Gestures.PinchEvent`）
- 指针捕获丢失或左键提前松开时结束平移，修复粘滞拖动
- 放大镜平移量换算到局部坐标，修正整体偏移
- 过期解码失败不再对旧文件弹提示
- 胶片栏初始化选中不再重复触发宿主载入
- 文件与其父目录同时拖入时路径去重
- 评分失败回退为「画质评分 不可用」，不再显示误导性低分
- 复用载入时解析的 EXIF 方向，显示路径不再重复读盘
- 对比下拉框重建期间抑制 `SelectionChanged`，避免重复载入
- 复制文件改为可等待，失败时给出提示

### 验证
- `dotnet build`：0 警告 0 错误
- `dotnet test`：69 项全部通过
- 4 张整窗快照：基础布局、信息面板、对比模式、逐张浏览（缓存淘汰路径）

### 已知限制
- GIF/WebP 只显示首帧；旋转不写回文件
- 胶片栏未虚拟化，超大目录首次载入较慢
- 缩略图淘汰依赖 GC 释放
- 拖放、触屏、剪贴板粘贴、explorer 调起、实拍 EXIF 方向等 Windows 真机项尚未人工验证
