# 开发者文档

本文档提供了 ImageMinify 的技术细节和开发指南。

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET 10 (`net10.0-windows`)，C# 14 |
| UI 框架 | WPF（自绘控件模板，无第三方 UI 库） |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.2.2 |
| 图像处理 | [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.1.12 |
| EXIF 读取 | [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) 2.8.1 |
| 测试 | xUnit 2.9.0 |

## 项目结构

```
ImageMinify/
├── App.xaml                        # WPF 应用定义与资源合并
├── App.xaml.cs                     # 应用启动与依赖装配
├── GlobalUsings.cs                 # 全局 using 声明
├── ImageMinify.csproj              # 项目文件
├── ImageMinify.sln                 # 解决方案文件
│
├── Models/                         # 数据模型层（纯数据，无逻辑）
│   ├── AppSettingsSnapshot.cs      # 持久化设置快照
│   ├── CompressionOptions.cs       # 单次运行的全部参数
│   ├── CompressionPreset.cs        # 内置预设与匹配判定
│   ├── CompressionProgress.cs      # 进度回报
│   ├── CompressionResult.cs        # 单张图片压缩结果
│   ├── CompressionSummary.cs       # 批量压缩统计
│   ├── EngineCapabilities.cs       # 压缩引擎能力
│   ├── SourceItem.cs               # 待处理的文件或文件夹
│   └── StatusSeverity.cs           # 状态级别枚举
│
├── Services/                       # 业务逻辑层（与 UI 无关）
│   ├── IImageCompressor.cs         # 压缩器接口
│   ├── ImageCompressor.cs          # 压缩编排：展开、并行、缩放、搜索、写盘
│   ├── IFormatEncoder.cs           # 单一输出格式的编码契约
│   ├── JpegCompressor.cs           # JPEG 编码 + MozJPEG 后处理
│   ├── PngCompressor.cs            # PNG 量化 / 无损 + oxipng 后处理
│   ├── WebpCompressor.cs           # WebP 编码
│   ├── BmpPassthroughEncoder.cs    # BMP 直通（无质量参数）
│   ├── ImagequantNativeQuantizer.cs # imagequant 原生调用
│   ├── ExifService.cs              # 方向校正 / 精简 / 清除
│   ├── ThemeService.cs             # 深浅色调色板切换
│   ├── IUpdateService.cs           # 更新服务契约
│   ├── UpdateService.cs            # Velopack / GitHub Release 更新实现
│   ├── ISettingsStore.cs           # 设置存储接口
│   ├── WindowsSettingsStore.cs     # JSON 文件存储实现
│   └── SettingsService.cs          # 设置管理服务
│
├── ViewModels/                     # 视图模型层
│   └── MainViewModel.cs            # 主视图模型（UI 状态与命令）
│
├── Views/                          # 视图层
│   └── MainWindow.xaml(.cs)        # 主窗口
│
├── Controls/                       # 自绘控件
│   ├── ComparisonSurface.cs        # 前后对比舞台：擦除、缩放、平移
│   └── ComparisonGeometry.cs       # 其几何计算（纯函数，可单测）
│
├── Themes/                         # 设计系统
│   ├── Palette.Light.xaml          # 浅色调色板
│   ├── Palette.Dark.xaml           # 深色调色板
│   ├── Controls.xaml               # 控件模板与排版变量
│   └── Icons.xaml                  # 矢量图标
│
├── Helpers/                        # 工具类
│   ├── Converters.cs               # 绑定转换器
│   ├── FileSizeFormatter.cs        # 文件大小格式化
│   ├── PixelInspector.cs           # 像素级判定（透明度检测）
│   ├── ReportExporter.cs           # CSV 报告导出
│   ├── AppInfo.cs                  # 版本与项目地址
│   ├── DiagnosticReport.cs         # 脱敏诊断信息
│   ├── ReleaseNotesFormatter.cs    # 双语发行说明选择与格式化
│   ├── Strings.cs                  # 界面语言切换与字符串查找
│   ├── Strings.Table.cs            # 中英对照字符串表（一行一条）
│   └── NativeLibraryLoader.cs      # 原生库 / 外部工具定位
│
├── Interop/                        # Win32 互操作
│   ├── NativeMethods.cs
│   └── WindowFrame.cs              # 自定义窗口边框与阴影
│
├── Assets/                         # 资源文件
│   └── icon.ico                    # 应用图标
│
└── tests/ImageMinify.Tests/        # 单元测试
    ├── ImageCompressorTests.cs
    ├── PngCompressorTests.cs
    ├── JpegCompressorTests.cs
    ├── ExifServiceTests.cs
    ├── FileSizeFormatterTests.cs
    ├── MainViewModelTests.cs
    ├── SettingsServiceTests.cs
    ├── ComparisonGeometryTests.cs
    ├── StringsTests.cs
    └── InMemorySettingsStore.cs    # 测试用内存存储
```

## 架构设计

### MVVM 模式

```
View (XAML) ←→ ViewModel (CommunityToolkit.Mvvm) → Services → Models
```

- **View**：纯 XAML 视图，通过数据绑定与 ViewModel 交互
- **ViewModel**：使用 `[ObservableProperty]` 管理 UI 状态，命令在构造函数中显式创建以便控制 `CanExecute`
- **Services**：业务逻辑，不引用任何 WPF 类型，因此可以直接在测试中构造
- **Models**：纯数据类

对话框和文件选择都通过 ViewModel 暴露的状态驱动（`IsDialogOpen` + `TaskCompletionSource`），而不是由 Service 弹窗，所以整条运行链路（含「覆盖前确认」这类交互）都能在无 UI 的测试里跑通。

### 压缩服务架构

`ImageCompressor` 负责编排，具体格式交给实现了 `IFormatEncoder` 的编码器：

```
ImageCompressor（编排）
├── 展开来源     → 去重、过滤不支持的扩展名、跳过自己的输出
├── 并行执行     → Parallel.ForEachAsync，按索引写回保持顺序
├── 单张流程     → 读取 → 方向校正 → 缩放 → 元数据 → Prepare → 编码 → 原子写入 → PostProcess
└── 目标体积搜索 → 质量 5–100 二分
    │
    └── IFormatEncoder
        ├── JpegCompressor          → ImageSharp + MozJPEG / jpegtran（可选）
        ├── PngCompressor           → ImageSharp / imagequant（可选）+ oxipng（可选）
        ├── WebpCompressor          → ImageSharp
        └── BmpPassthroughEncoder   → 无质量参数
```

`IFormatEncoder` 把「准备画布」和「编码」拆成两步（`Prepare` / `Encode`），因为格式转换要复制整张图，而目标体积搜索会对同一张画布编码最多七次 —— 拆开之后转换只做一次。

### 目标体积搜索

搜索建立在两个不变量上，破坏任何一个都会让结果失准：

1. **体积随质量单调递增**。二分依赖这一点，`JpegCompressorTests.Encode_SizeRisesMonotonicallyWithQuality` 是它的守卫
2. **同一质量下，慢档编码不大于快档**。搜索用快档试压、用慢档出片，正因为有这条保证才不需要回头验证结果。`PngCompressorTests.Encode_TheSlowPathIsNeverLargerThanTheFastOne` 是它的守卫

新增编码器时必须同时满足这两条，否则不要把 `SupportsQuality` 设为 `true`。

### 设置持久化

通过接口抽象实现可测试性：

```
ISettingsStore (接口)
├── WindowsSettingsStore  → %AppData%\ImageMinify\settings.json（生产环境）
└── InMemorySettingsStore → 内存存储（测试环境）
```

写入采用「先写临时文件再替换」，避免进程退出打断写入后留下无法解析的半个 JSON —— 那会让所有偏好静默恢复默认值。保存带防抖，`ShutdownAsync` 会强制冲刷尚未落盘的改动。

### 主题

`Themes/Palette.Light.xaml` 与 `Palette.Dark.xaml` 定义同一组键，`ThemeService` 在运行时整体替换合并字典中的调色板。所有控件模板只引用语义化的键（表面、描边、前景、强调色等），不写死颜色，因此切换主题不需要改动任何模板。

排版同理：字号、间距、圆角都在 `Controls.xaml` 中集中定义，界面各处引用变量而非字面量，这是「字号统一」能持续成立的前提。

### 界面语言

中英两种语言共用一张表：`Helpers/Strings.Table.cs` 里一行一条，键、中文、英文写在同一行上。分成两个文件或两份资源字典也可以，但那样一条译文的两半就不在同一处，其中一边改了词而另一边没跟上时，看着文件是发现不了的。

`Helpers/Strings.cs` 负责把这张表送到两类使用者手上：

- **XAML** 通过 `{DynamicResource Str.Xxx}` 取字符串。切换语言时 `Strings.Apply` 整体替换合并字典中那一份，界面自行刷新 —— 与 `ThemeService` 换调色板是同一套机制，同样不重载窗口、不通知任何控件
- **C#** 通过 `Strings.Get` / `Strings.Format` 直接读表，因此在没有 `Application` 的测试里照常可用

两条约定：

- **模型不存译文，只存键。** `CompressionPreset` 保存 `NameKey`，`Name` 在读取时才解析；`EngineCapabilities.PngLossless` 存的是找到的工具名，找不到时为空字符串，「未安装」这句话由显示属性在读取时补上。启动时构造一次的记录若把词句存进去，语言就被冻在了那一刻
- **代码拼出来后存起来的文字**（摘要行、状态提示）不会像 `DynamicResource` 那样自己重解析，由 `MainViewModel.RefreshLocalizedText` 在切换后重建。`Presets` 与 `Results` 是 `ObservableCollection`，用 `items[i] = items[i]` 原地重述一遍即可让绑定重读，不必清空重填 —— 后者会闪一下空状态并丢掉选中行

加一句界面文字就是加一行表。`StringsTests` 会扫描 `MainWindow.xaml`，窗口引用了而表里没有的键会导致测试失败 —— 否则界面上只是一块空白，而空白看不出是缺字符串还是布局出了问题。同一测试还会核对两种语言的占位符集合一致：译文可以调换 `{0}` `{1}` 的顺序，但不能凭空多一个（运行时抛异常）或少一个（那个数字就悄悄消失了）。

### 版本、更新与诊断

项目版本只在 `ImageMinify.csproj` 维护，程序集、文件版本、设置中心与发行流水线都从这里读取。应用入口 `Program.Main` 必须先执行 `VelopackApp.Build().Run()`，再启动 WPF；这是更新器处理安装、卸载与重启钩子的前提。

`UpdateService` 只面向公开的 `Moresyl/ImageMinify` GitHub Release 源。开发目录不是 Velopack 管理的安装目录，因此会返回明确的“未安装构建”状态，不伪装成已经支持更新。正式包可检查更新、读取当前语言对应的发行说明、下载并由 Velopack 校验包，随后重启应用完成安装。

`.github/workflows/release.yml` 是唯一发布入口：标签必须与项目版本一致，先还原、测试与自包含发布，再由固定版本的 Velopack CLI 生成安装包、便携包和更新清单，最后生成 `SHA256SUMS.txt` 并验证公开 Release 资产。若配置 `WINDOWS_CERTIFICATE_BASE64` 与 `WINDOWS_CERTIFICATE_PASSWORD`，流水线会为 Windows 文件做 Authenticode 签名；未配置时仍可发布，但 Windows 可能显示未知发布者提示。

每个版本必须提供 `.github/release-notes/<version>.md`，并分别使用 `<!-- imageminify-notes:zh -->` 与 `<!-- imageminify-notes:en -->` 标记。软件只显示当前语言段，GitHub Release 保留完整双语说明。

所有可预期失败都通过 `ShowExceptionAsync` 进入同一种错误对话框。界面详情和复制内容都会由 `DiagnosticReport` 脱敏：替换用户目录、API key、令牌、密码、secret 与 Bearer 凭据。诊断信息不主动读取图片内容或完整设置文件。

### 前后对比舞台

`ComparisonSurface` 是本项目唯一的自绘控件，直接在 `OnRender` 里把棋盘格、压缩后的图、裁到分割线左侧的原图与分割线本身画在同一个矩形上。

它取代的是一叠 `Image` 元素 —— 各层宽度由转换器把容器宽度与分割比例相乘得出。那套写法在各层需要就一个布局系统并不知道的尺寸达成一致时失效，两张图错开成了并排的两张，而绑定失配在界面上不会报任何错。改成一个元素之后，「它们画在同一个矩形上」不再是一组绑定共同维持的巧合，而是代码里的同一个 `Rect` 变量。

几何计算全部放在 `ComparisonGeometry` 里，是纯函数：给定图片尺寸、舞台尺寸、缩放倍率与平移量，算出图片矩形、允许的平移范围、以指针为中心缩放后的新平移量。WPF 控件在测试环境里渲染不出来，把这部分拆开，正确性就不必依赖肉眼。

两个与画质判断直接相关的细节：

- **原图按压缩后的像素宽解码**。两张图必须落在同一个矩形上才有可比性，而在解码器里缩一次，好过每帧重采样一张两千四百万像素的照片
- **100% 以上用最近邻，以下用高质量插值**。平滑插值会把振铃与色块糊掉 —— 那正是这个界面存在的理由；而在 100% 以下用最近邻，会把照片混叠成两个文件都不像的样子

## 开发指南

### 环境配置

```bash
# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行
dotnet run

# 运行测试
dotnet test
```

### 添加新的图片格式支持

1. 在 `Services/` 下创建实现 `IFormatEncoder` 的类（如 `AvifCompressor.cs`）
2. 在 `ImageCompressor` 的 `Extensions` 与 `ResolveEncoder` 中注册
3. 在 `MainViewModel` 与 `MainWindow.xaml` 中添加格式选项
4. 补上编码器测试，其中必须包含上文两条不变量的守卫

### 添加新的设置项

1. 在 `Models/CompressionOptions.cs` 中添加属性（`AppSettingsSnapshot` 整体持有它，无需同步修改）
2. 在 `SettingsService` 中添加读写
3. 在 `MainViewModel` 中暴露为 `[ObservableProperty]` 并在 `BuildOptions` 中带上
4. 在 `MainWindow.xaml` 中绑定

### 测试

项目使用 xUnit：

```bash
# 运行所有测试
dotnet test

# 运行特定测试
dotnet test --filter "FullyQualifiedName~PngCompressorTests"
```

两个容易踩的坑：

- `Progress<T>` 在没有 `SynchronizationContext` 的测试环境里回调走线程池，所以进度断言必须用线程安全的集合收集，并轮询等待而不是直接读
- 每次运行结束都会等待完成对话框被关闭，因此测试里跑完整流程时必须应答该对话框，否则 `StartCommand` 永远不会返回

### 图像处理注意事项

以下都是 ImageSharp 的实际行为，曾各自造成过真实缺陷，改动相关代码前请留意：

- `OctreeQuantizer` 只在 RGB 三通道上建树，输出的调色板颜色一律不透明；需要保留 alpha 时必须用 `WuQuantizer`
- `JpegEncoder.SkipMetadata` **不会**移除 EXIF（PNG 与 WebP 编码器会），JPEG 的清除必须在写入时自行摘除配置
- 方向标记要在测量尺寸之前应用，否则竖拍照片的宽高是反的，缩放也会算错

## NuGet 依赖

| 包 | 用途 |
|----|------|
| CommunityToolkit.Mvvm 8.2.2 | MVVM 框架 |
| MetadataExtractor 2.8.1 | EXIF 元数据读取 |
| SixLabors.ImageSharp 3.1.12 | 图像处理 |

外部原生工具（imagequant、MozJPEG / jpegtran、oxipng）不是 NuGet 依赖，由 `NativeLibraryLoader` 在运行时定位，缺失时自动回退到 ImageSharp。

## 版本历史

| 版本 | 技术栈 | 说明 |
|------|--------|------|
| v0.9 – v1.1 | Python + PyQt5 | 初始版本 |
| v2.0 – v2.1 | C# + WPF + WPF-UI | 迁移到 .NET |
| v3.0 | C# + WPF（自绘模板） | 界面重构、压缩引擎重写、移除重命名功能 |
