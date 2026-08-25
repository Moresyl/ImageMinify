# 安装指南

本文档提供了 ImageMinify 图片压缩工具的详细安装说明。

## 系统要求

- **操作系统**：Windows 10/11
- **.NET 版本**（仅从源码构建时需要）：.NET 10 SDK
- **磁盘空间**：约 50MB

## 方法 1：使用预编译的可执行文件（推荐）

这是最简单的安装方法，不需要安装 .NET SDK。

1. 访问 [GitHub Releases 页面](https://github.com/Moresyl/ImageMinify/releases)
2. 下载最新版本的压缩包
3. 解压缩下载的文件到任意位置
4. 双击 `ImageMinify.exe` 运行程序

> 自包含版本已内置 .NET 运行时，无需额外安装。

## 方法 2：从源代码构建

如果您想从源代码构建或进行开发，请按照以下步骤操作。

### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git（可选，用于克隆仓库）

### 步骤

```bash
# 克隆仓库
git clone https://github.com/Moresyl/ImageMinify
cd ImageMinify

# 还原 NuGet 依赖
dotnet restore

# 构建项目
dotnet build -c Release

# 运行程序
dotnet run -c Release
```

### 发布为独立可执行文件

```bash
# 发布为独立应用（包含 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained true

# 发布为依赖框架的应用（需要用户安装 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained false
```

输出文件位于 `bin/Release/net10.0-windows/win-x64/publish/` 目录。

## 可选：原生压缩工具

ImageMinify 支持三个可选的外部工具以提升压缩率：

| 工具 | 作用 | 文件名 |
|------|------|--------|
| **imagequant** | PNG 量化，调色板质量优于内置量化器 | `libimagequant.dll` 或 `imagequant.dll` |
| **MozJPEG** | JPEG 无损重编码，通常再省 5–15% | `jpegtran.exe` 或 `mozjpeg.exe` |
| **oxipng** | PNG 无损二次优化 | `oxipng.exe` |

放到以下任意位置即可被自动发现：

- 程序所在目录
- 程序目录下的 `bin/` 子目录
- 系统 `PATH` 中的任意目录

三者都不是必需的。未安装时程序照常工作，仅使用 ImageSharp 内置压缩，压缩率略低。启动后界面右下角的「压缩引擎」区域会显示当前实际生效的是哪一个，据此可以确认工具是否被找到。

## 卸载

直接删除应用程序文件夹即可。设置存储在 `%AppData%\ImageMinify\settings.json`，如需彻底清理，删除该文件夹即可。
