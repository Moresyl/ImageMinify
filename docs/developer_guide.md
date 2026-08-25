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
