# 更新记录

版本号遵循软件正式名称，形如 `PhotoLensWin-1.0`。本文件与 [README.md](README.md) 保持同步。

版本号唯一来源：根目录 `Directory.Build.props` 的 `<Version>`（csproj 与手写 AssemblyInfo 不再各自声明）。

## 发布索引（版本 ↔ 提交 ↔ tag）

| 版本 | 日期 | 提交 | tag |
|---|---|---|---|
| 1.2.6 | 2026-11-22 | `acc1db8` | `PhotoLensWin-1.2.6` |
| 1.2.5 | 2026-11-14 | `b258a6a` | `PhotoLensWin-1.2.5` |
| 1.2.4 | 2026-11-14 | `b18b123` | `PhotoLensWin-1.2.4` |
| 1.2.3 | 2026-11-14 | `23b9e5e` | `PhotoLensWin-1.2.3` |
| 1.2.2 | 2026-11-14 | `080bc3c` | `PhotoLensWin-1.2.2` |
| 1.2.1 | 2026-11-14 | `d9262b8` | `PhotoLensWin-1.2.1` |
| 1.2.0 | 2026-11-14 | `908a5f2` | `PhotoLensWin-1.2.0` |
| 1.1.1 | 2026-11-12 | `5030f88` | `PhotoLensWin-1.1.1` |
| 1.1 | 2026-11-10 | `c8a1946` | `PhotoLensWin-1.1` |
| 1.0 | 2026-11-06 | `75d71c2` | `PhotoLensWin-1.0` |

---

## PhotoLensWin-1.2.7 — 2026-11-24

本轮为独立全量评审（P0–P3 + UI）后的修复发布；版本号仍只来自 `Directory.Build.props`。

### 修复（数据安全 / 正确性）
- **删除**：补 `FOF_WANTNUKEWARNING`（回收站不可用而将永久删除时 Shell 再警告一次）；`CanRecycle` 对可移动卷探测卷根 `$Recycle.Bin`；删除前确认目标仍是普通文件且非重解析点；删除改回 UI 线程执行（Shell 警告框需要消息泵）；`_deleting` 防重入；确认框显示完整路径
- **另存为**：按 EXIF 方向 × 用户旋转把变换烘进像素（`Flatten`）；文件名加 `-校正` 后缀，避免顺手覆盖原图
- **缓存**：`ImageStore` 单飞解码；`Put` 覆盖前释放旧实例；两个缓存字典改 `OrdinalIgnoreCase`；窗口关闭时释放两套缓存
- **异步**：新增 `_presentVersion` 代次守卫（列表选择 / 拖入预览 / 列表重建）；载入完成按当前排序重排
- **主题**：新增警告/强调/遮罩等 token，修复亮色 Toast 与激活按钮对比度
- **本次补充**：永久删除的 Toast 文案与"移到回收站"区分；永久删除确认框高度随内容自适应（原固定 170 会裁掉折行后的长路径）

### 变更
- 解码像素闸值（2 亿像素）；命名管道 `CurrentUserOnly` + 64KB 上限；单实例改 `WaitOne(0)` 防抢注；配置 1MB 上限 + 最近目录规范化；`LastFolder` 仅本地路径自动打开；日志剥离控制字符并截断；快照自检仅 Debug 构建存在
- `build.ps1`：传 `-p:Version`、单文件压缩、发布前清理旧产物、排除 `.pdb`
- UI：空态禁用无效按钮、信息面板描边、胶片卡圆角/填充、缩略图失败占位、交互态、可访问性 `Name`/`LiveSetting`、焦点环、状态栏 `MinHeight`、字号/间距收敛

### 验证
- Release/Debug 构建 0 警告 0 错误；104/104 测试通过
- Release 程序集无 `PHOTOLENS_SNAPSHOT`，Debug 保留
- 760×540（对比+信息）与 1100×760 两种场景布局自检 `PASS`
- 说明：评审报告所述「`IsStillCurrent` 补 `_current` 校验」未采纳——切图时 `_current` 仍指向上一张，加上该条件会让缓存命中分支不显示，代码保持原样

### 未完成（明确保留）
- 位图延迟释放队列、胶片栏虚拟化、大图降采样、`MainWindow` 拆分、`IFileOperation` 替换

---

## PhotoLensWin-1.2.6 — 2026-11-22

本轮按「多维度审核 Bot」结果修复，每条都先复现/定位再改。

### 修复
- **删除安全**：非 NTFS 本地卷与 UNC 路径上 `FOF_ALLOWUNDO` 不生效，原实现会静默永久删除；现在先弹应用内确认（默认取消）。取消判定改用 `fAnyOperationsAborted`，不再把 Shell 错误码 2 当成"用户取消"吞掉
- **缩略图淘汰**：新增保护——刚解码、即将交给界面的位图永不淘汰；全部在用时不强行淘汰（原逻辑在 >300 张且全部显示时可能 `Dispose` 后仍返回给界面）
- **旋转偏移**：EXIF 矩阵平移量是像素、层尺寸是缩放后的 DIP，两者量纲混用导致「适合窗口」下旋转后整体偏移。改为层盒子用未旋转像素尺寸×缩放、平移项同步乘 Scale、原点归零
- **窄窗口右箭头**：对比模式 + 信息面板下，让位量不再无上限，钳制在画布内
- **单实例丢参数**：第二实例改为通过命名管道把路径转交首实例并激活窗口，不再静默退出
- **复制文件路径**：剪贴板被占用时捕获异常并提示，不再冒泡
- **窗口位置恢复**：宽高做最小钳制，位置与屏幕工作区求交，落在屏外时回落居中
- **放大镜**：改为视觉笔刷取当前渲染层局部放大（3×），贴边时钳制在画布内；原来只显示 1:1 内容

### 变更
- 版本号与程序集元数据收敛到 `Directory.Build.props` 单一来源；`build.ps1` 从该文件读取版本（原默认 1.0，zip 名与代码版本脱节）；删除手写 `AssemblyInfo.cs`
- 布局越界自检：容差 0.5→1.0（吸收 1\* 分栏的亚像素偏差），豁免滚动内容与 ScrollBar/Viewbox 等模板内部
- README：包名更新为 1.2.6，.NET SDK 要求改为 9.0+

### 验证
- 布局自检：**760×540 对比+信息** 与 **1100×760 普通** 两场景均 `PASS：无元素越界`
- 旋转（真机截图量测）：旋转前 994×622、中心 (937,555) → 旋转后 462×740、中心 (937,554)
- 单实例：第二实例打开 b_portrait 后，首实例标题变为 `b_portrait.jpg — PhotoLens`，第二进程退出
- 放大镜：按住 `Z` 出现圆框，圆内文字明显放大（真机截图）
- `dotnet build -c Release` 0 警告 0 错误，71 项测试通过；FileVersion `1.2.6.0`

---

## PhotoLensWin-1.2.5 — 2026-11-14

### 修复
- **胶片栏横图卡片右侧被裁**：卡片宽度随缩略图自然宽度走，横图（232）大于列内容区（211.4）时被排出列外、右侧贴到分隔线；竖图卡片却有 10px 边距，左右不一致。改为卡片宽度锁定为列表可视宽度，拖动分隔条时同步更新

### 新增（自检门槛）
- **布局越界自检**：快照模式遍历可视树，检查已布局可见元素是否超出父容器边界（容差 0.5px，滚动内容 / Canvas / Popup 除外），结果写入 `layout-check.txt`；本次即由它查出上面两处 `FilmItem right=20.6`
- README 增加「发布前验证门槛」：视觉类改动须有真机截图 FAIL→PASS 对照、布局自检必须 PASS、逻辑类改动须先有失败测试、报错与否定性结论须自证

### 验证
- 布局自检：修复前 `FAIL：2 处越界（FilmItem right=20.6）` → 修复后 `PASS：无元素越界`
- `dotnet build -c Release` 0 警告 0 错误，71 项测试通过
- 真机屏幕截图复核胶片栏右边距与菜单栏文字（175% DPI）

---

## PhotoLensWin-1.2.4 — 2026-11-14

### 修复
- **菜单栏「文件(F)」「视图(V)」字头被裁切**：根因是 `Menu` 自带纵向 `Padding="8,4"`，而菜单栏高度只按菜单项内容计（32），导致顶部菜单项被塞进 24 高的内容区并向上溢出 4px，被菜单栏上沿裁掉——字体度量稍有差异（不同 DPI / 字体回退）就会切掉字头
- 修复方式：菜单栏纵向 Padding 归零，纵向留白改由菜单项自己的 `Padding="10,6"` 提供；菜单项不再溢出父容器，文字始终完整

### 验证
- 对照实验（同一台机器、175% DPI、真实屏幕截图）：**1.2.3 发布版 exe 复现裁切**，修复后构建文字完整
- 压力测试：窗口字号临时放大到 17，菜单栏随文字自动增高、无裁切（修复对字体度量差异不再敏感）
- `dotnet build -c Release` 0 警告 0 错误，71 项测试通过

---

## PhotoLensWin-1.2.3 — 2026-11-14

按 Windows 用户习惯补充两项可配置行为（胶片栏保持左侧竖排，不改布局）。

### 新增
- **主题 → 跟随系统**：新增 `System` 选项，深色/浅色/跟随系统三选一，写入 `settings.json`
- **鼠标滚轮行为可配**：视图 → 鼠标滚轮，可选「缩放」（默认）或「切换上一张/下一张」，对齐 Windows 照片的同名设置；`Ctrl+滚轮` 始终缩放（Windows 惯例）
- 底部提示文案随滚轮设置切换，不再出现“提示说缩放、实际在翻页”
- 菜单勾选状态在设置加载后同步（此前菜单按默认值构建）

### 验证
- `dotnet build -c Release` 0 警告 0 错误，71 项测试通过
- 实机：滚轮切图（标题栏由 a_landscape.jpg 变为 b_portrait.jpg）、标题栏文件名、主题切换均生效

---

## PhotoLensWin-1.2.2 — 2026-11-14

Windows 使用习惯专项适配：核对 Windows 照片（Microsoft Photos）、Windows Photo Viewer 与资源管理器的实际行为，不再逐项照搬 mac。

### 键盘与删除（对齐 Windows 照片）
- **`Delete` 删除到回收站**：此前只能走右键菜单；删除不再弹 Shell 确认框，改为 Toast 提示 + 回收站兜底（可找回）
- **`Ctrl+R` 旋转**：与 Windows 照片一致，保留单键 `R` 兼容
- **`Home` / `End`** 跳第一张 / 最后一张；**`F5`** 开始/停止幻灯片（`Esc` 停止）

### Windows 原生观感
- **字体改用系统字体**：西文 Segoe UI、中文回退微软雅黑 UI，移除打包的 Inter 依赖，中英文混排与系统一致
- **标题栏显示当前文件名**（`文件名 — PhotoLens`），Alt+Tab / 任务栏可辨认；空状态恢复 `PhotoLens`

### 右键菜单（对齐资源管理器）
- 新增「打开方式…」「属性」，调用 Windows Shell 动词；「打开文件所在位置」改为资源管理器同款措辞

### 说明
- 滚轮仍为以光标为锚缩放（Windows 照片默认同为缩放，Ctrl+滚轮同样可用）；Windows 照片可在设置里改为「切换图片」，本版未加该开关
- 胶片栏仍在左侧：Windows 照片为底部横向胶片条，改动面较大，等你确认后再做

---

## PhotoLensWin-1.2.1 — 2026-11-14

界面布局专项复核（760×540 最小窗口、1100×760 常规窗口、暗/亮主题、单窗格/对比模式四种组合实机快照核验）。

### 布局与遮挡
- **信息面板改挂整窗右上角**（对齐 Mac 的 viewer 右上角锚定），不再作为左窗格子元素：对比模式下不再挤占右侧窗格，宽度随内容在 200–300 间自适应，去掉固定 216
- **面板不再压住翻页箭头**：面板高度随内容自适应（上限 300），窗口较矮时右箭头自动左移让位；对比模式下画布变窄、面板已不在箭头正上方时不误移
- **工具条横向滚动条常显**：760×540 + 对比模式时被裁掉的「幻灯片」等按钮可通过滚动条滚出，不再无声消失
- **状态栏改 1\*:2\*:Auto 三列**：提示文案不再挤掉文件名与索引/缩放；启动即显示操作提示（此前 HintLabel 启动为空）
- 提示文案修正：滚轮直接缩放并支持拖动平移、按住 Z 放大镜，不再写成 Ctrl+滚轮

### 可读性
- 信息面板背景改为不透明：暗色 `#F216181B`、亮色 `#F7FFFFFF`，照片内容不再透上来干扰阅读

### 交互一致
- 对比模式下「适合窗口 / 实际大小」同时作用于右侧对比图
- 对比模式下左右箭头不再整体隐藏（仍作用于左图，右侧通过下拉框选图）

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
