namespace ImageMinify.Helpers;

/// <summary>
/// The strings themselves. See <c>Strings.cs</c> for how they reach the interface.
/// </summary>
/// <remarks>
/// One row per string, both languages on the same line, grouped in the order the interface reads:
/// title bar, then left column top to bottom, then right column, then the things that only appear
/// once something has happened. A translation that has drifted is then visible by looking at it,
/// which is the only review this table ever really gets.
/// </remarks>
public static partial class Strings
{
    private static readonly (string Key, string Chinese, string English)[] Table =
    [
        // ==== Title bar ====
        ("Str.App.Tagline", "图片压缩工具", "Image compressor"),
        ("Str.Caption.Theme", "切换深色 / 浅色主题", "Switch between light and dark"),
        ("Str.Caption.Minimize", "最小化", "Minimize"),
        ("Str.Caption.Maximize", "最大化", "Maximize"),
        ("Str.Caption.Restore", "向下还原", "Restore down"),
        ("Str.Caption.Close", "关闭", "Close"),

        // ==== Sources ====
        ("Str.Source.Heading", "图片来源", "Sources"),
        ("Str.Source.AddFiles", "添加图片", "Add images"),
        ("Str.Source.AddFolder", "添加文件夹", "Add folder"),
        ("Str.Source.Clear", "清空来源列表", "Clear the list"),
        ("Str.Source.EmptyTitle", "把图片或文件夹拖到这里", "Drop images or folders here"),
        ("Str.Source.EmptyHint", "支持 JPG · PNG · WebP · BMP", "JPG · PNG · WebP · BMP"),
        ("Str.Source.Remove", "移除", "Remove"),
        ("Str.Source.Prompt", "拖入图片或文件夹，或点击下方按钮选择", "Drop images or folders in, or use the buttons below"),
        ("Str.Source.None", "所选位置没有可压缩的图片", "Nothing here can be compressed"),
        ("Str.Source.Unreadable", "无法读取所选位置", "That location could not be read"),
        ("Str.Source.Count", "{0} 张图片 · {1}", "{0} images · {1}"),
        ("Str.Source.Locations", "{0} 个位置", "{0} locations"),

        // ==== Results ====
        ("Str.Results.Heading", "压缩结果", "Results"),
        ("Str.Results.Export", "导出报告", "Export report"),
        ("Str.Results.Clear", "清空结果", "Clear results"),
        ("Str.Results.Compare", "对比原图与压缩后", "Compare before and after"),
        ("Str.Results.Reveal", "在文件资源管理器中显示", "Show in File Explorer"),
        ("Str.Results.MissedTarget", "未能达到目标大小，可尝试更小的尺寸或改用 WebP", "Could not reach the target size — try smaller dimensions, or WebP"),
        ("Str.Results.EmptyTitle", "还没有压缩结果", "No results yet"),
        ("Str.Results.EmptyHint", "在右侧调好参数，然后点击“开始压缩”", "Set it up on the right, then press Compress"),

        ("Str.Stats.Compressed", "已压缩", "Compressed"),
        ("Str.Stats.Saved", "节省空间", "Saved"),
        ("Str.Stats.Ratio", "平均压缩率", "Average"),
        ("Str.Stats.Elapsed", "用时", "Elapsed"),

        ("Str.Column.File", "文件", "File"),
        ("Str.Column.Dimensions", "尺寸", "Dimensions"),
        ("Str.Column.Before", "原始", "Before"),
        ("Str.Column.After", "压缩后", "After"),
        ("Str.Column.Ratio", "压缩率", "Saved"),

        // ==== Presets ====
        ("Str.Preset.Heading", "预设", "Presets"),
        ("Str.Preset.Web", "网页", "Web"),
        ("Str.Preset.Web.Detail", "WebP · 质量 80 · 长边 1920 · 清除元数据", "WebP · quality 80 · 1920 px · no metadata"),
        ("Str.Preset.Social", "社交媒体", "Social"),
        ("Str.Preset.Social.Detail", "JPEG · 质量 85 · 长边 2048 · 清除元数据", "JPEG · quality 85 · 2048 px · no metadata"),
        ("Str.Preset.Archive", "存档", "Archive"),
        ("Str.Preset.Archive.Detail", "保持原格式 · 质量 92 · 原始尺寸 · 保留元数据", "Keep format · quality 92 · full size · keep metadata"),
        ("Str.Preset.Minimum", "极限压缩", "Smallest"),
        ("Str.Preset.Minimum.Detail", "WebP · 质量 60 · 长边 1280 · 清除元数据", "WebP · quality 60 · 1280 px · no metadata"),

        // ==== Format ====
        ("Str.Format.Heading", "输出格式", "Output format"),
        ("Str.Format.Original", "原格式", "Keep format"),
        ("Str.Format.LockedByOverwrite", "覆盖原文件时无法转换格式", "Overwriting cannot change the format"),

        // ==== Quality ====
        ("Str.Quality.Heading", "画质", "Quality"),
        ("Str.Quality.Smaller", "更小", "Smaller"),
        ("Str.Quality.Sharper", "更清晰", "Sharper"),
        ("Str.Quality.TargetSize", "目标大小", "Target size"),
        ("Str.Quality.TargetSizeHint", "每张图片压缩到不超过这个大小；0 表示不限制", "Compress each image to at most this size; 0 for no limit"),
        ("Str.Quality.TargetSizeActive", "已按目标大小自动搜索画质，上方滑块暂停使用", "Quality is being searched for to meet the target size; the slider is paused"),

        // ==== Resize ====
        ("Str.Scale.Heading", "尺寸", "Resize"),
        ("Str.Scale.None", "保持原尺寸", "Keep size"),
        ("Str.Scale.LongestEdge", "限制长边", "Longest edge"),
        ("Str.Scale.Percent", "按百分比", "Percentage"),
        ("Str.Scale.Value", "数值", "Value"),
        ("Str.Scale.NeverUpscales", "只会缩小，不会把小图拉大", "Only shrinks — a smaller image is left as it is"),

        // ==== Handling ====
        ("Str.Handling.Heading", "处理方式", "Handling"),
        ("Str.Handling.StripMetadata", "清除元数据", "Strip metadata"),
        ("Str.Handling.StripMetadataHint", "删除 GPS、相机信息与缩略图", "Removes GPS, camera details and thumbnails"),
        ("Str.Handling.SkipLarger", "跳过变大的图片", "Skip images that grow"),
        ("Str.Handling.SkipLargerHint", "压缩后更大时保留原文件", "Keeps the original when the output is bigger"),
        ("Str.Handling.Recursive", "包含子文件夹", "Include sub-folders"),
        ("Str.Handling.RecursiveHint", "递归处理文件夹内的所有层级", "Walks every level inside the folder"),

        // ==== Output ====
        ("Str.Output.Heading", "输出位置", "Output"),
        ("Str.Output.Suffix", "加后缀", "Suffix"),
        ("Str.Output.SuffixHint", "写在原图旁边，文件名加上后缀", "Beside the original, with a suffix on the name"),
        ("Str.Output.SubFolder", "子文件夹", "Sub-folder"),
        ("Str.Output.SubFolderHint", "写进原图所在目录下的 compressed 文件夹", "Into a “compressed” folder beside the original"),
        ("Str.Output.Custom", "指定目录", "Chosen folder"),
        ("Str.Output.CustomHint", "写到你选择的文件夹，并保留原有的目录层级", "Into a folder you pick, keeping the sub-folder structure"),
        ("Str.Output.Overwrite", "覆盖原图", "Overwrite"),
        ("Str.Output.OverwriteHint", "直接替换原文件，不保留备份", "Replaces the original, with no backup"),
        ("Str.Output.SuffixLabel", "文件名后缀", "Suffix"),
        ("Str.Output.Browse", "选择", "Browse"),
        ("Str.Output.OverwriteWarning", "原文件会被直接替换，且不进入回收站。", "Originals are replaced outright, and do not go to the Recycle Bin."),

        // ==== Engine ====
        ("Str.Engine.Heading", "压缩引擎", "Engine"),
        ("Str.Engine.NotInstalled", "未安装", "not installed"),
        ("Str.Engine.Jpeg", "JPEG · {0}", "JPEG · {0}"),
        ("Str.Engine.PngQuantization", "PNG 量化 · {0}", "PNG quantiser · {0}"),
        ("Str.Engine.PngLossless", "PNG 无损 · {0}", "PNG lossless · {0}"),
        ("Str.Engine.Parallelism", "并行 · {0} 线程", "Parallel · {0} threads"),

        // ==== Run ====
        ("Str.Run.Start", "开始压缩", "Compress"),
        ("Str.Run.Stop", "停止", "Stop"),
        ("Str.Progress.Scanning", "正在统计文件…", "Counting files…"),
        ("Str.Progress.Running", "{0} / {1} · 已省 {2}", "{0} / {1} · {2} saved"),
        ("Str.Progress.Stopping", "正在停止…", "Stopping…"),
        ("Str.Progress.Cancelled", "已取消", "Cancelled"),

        // ==== Drag and drop ====
        ("Str.Drop.Title", "松开即可添加", "Drop to add"),
        ("Str.Drop.Hint", "图片或整个文件夹都可以", "Images or whole folders"),

        // ==== Dialogs ====
        ("Str.Dialog.Ok", "确定", "OK"),
        ("Str.Dialog.Cancel", "取消", "Cancel"),
        ("Str.Dialog.Acknowledge", "知道了", "Got it"),
        ("Str.Dialog.CopyError", "复制错误信息", "Copy error details"),
        ("Str.Dialog.Copied", "已复制", "Copied"),

        ("Str.Warn.NeedFolder.Title", "需要输出文件夹", "An output folder is needed"),
        ("Str.Warn.NeedFolder.Body", "当前输出方式为“自定义文件夹”，请先选择一个目标位置。", "The output is set to a chosen folder — pick one first."),
        ("Str.Warn.NoImages.Title", "没有可压缩的图片", "Nothing to compress"),
        ("Str.Warn.NoImages.Body", "所选文件夹中没有支持的图片格式，或其中的图片都是本工具此前的输出。", "The folder holds no supported images, or every image in it is already output from this tool."),

        ("Str.Confirm.Overwrite.Title", "覆盖原文件？", "Overwrite the originals?"),
        ("Str.Confirm.Overwrite.Body", "将直接替换 {0} 中的图片，原文件不会保留，也无法通过回收站恢复。", "The images in {0} will be replaced outright. The originals are not kept, and the Recycle Bin will not have them."),
        ("Str.Confirm.Overwrite.Detail", "建议先用“输出到子文件夹”确认效果，再改回覆盖模式。", "Try the sub-folder output first to see the result, then switch back to overwriting."),
        ("Str.Confirm.Overwrite.Confirm", "覆盖原文件", "Overwrite"),

        // ==== Summary ====
        ("Str.Summary.Title.Done", "压缩完成", "Done"),
        ("Str.Summary.Title.Partial", "压缩完成，但有文件失败", "Done, with failures"),
        ("Str.Summary.NothingSmaller", "没有文件变小，原文件已全部保留。", "Nothing came out smaller — every original was kept."),
        ("Str.Summary.Body", "{0} 个文件从 {1} 压缩到 {2}，节省 {3}（{4}）。", "{0} files went from {1} to {2}, saving {3} ({4})."),
        ("Str.Summary.Elapsed", "用时 {0}", "Took {0}"),
        ("Str.Summary.Skipped", "{0} 个文件压缩后反而更大，已保留原文件", "{0} files came out bigger and were left alone"),
        ("Str.Summary.Failed", "{0} 个文件失败，详见结果列表", "{0} files failed — see the results list"),

        // ==== File pickers ====
        ("Str.Picker.SourceFolder", "选择图片文件夹", "Choose a folder of images"),
        ("Str.Picker.OutputFolder", "选择输出文件夹", "Choose an output folder"),
        ("Str.Picker.Images", "选择图片", "Choose images"),
        ("Str.Picker.ImageFilter", "图片文件", "Images"),
        ("Str.Picker.AllFilter", "所有文件", "All files"),
        ("Str.Picker.CsvFilter", "CSV 文件", "CSV files"),
        ("Str.Picker.ExportReport", "导出压缩报告", "Export the report"),

        // ==== Report ====
        ("Str.Report.Exported", "报告已导出", "Report exported"),
        ("Str.Report.Failed", "导出失败", "Export failed"),
        ("Str.Report.Header", "文件名,原始路径,输出路径,原始大小(字节),压缩后大小(字节),节省(字节),压缩率(%),原始尺寸,输出尺寸,质量,状态,耗时(毫秒),错误", "File,Source path,Output path,Original bytes,Output bytes,Saved bytes,Saved (%),Original size,Output size,Quality,State,Elapsed (ms),Error"),
        ("Str.Report.Total", "合计,{0} 个文件,已压缩 {1},跳过 {2},失败 {3},节省 {4},压缩率 {5}", "Total,{0} files,compressed {1},skipped {2},failed {3},saved {4},ratio {5}"),

        // ==== Result state ====
        ("Str.State.Compressed", "已压缩", "Compressed"),
        ("Str.State.Skipped", "已跳过", "Skipped"),
        ("Str.State.Failed", "失败", "Failed"),
        ("Str.Elapsed.MinutesSeconds", "{0} 分 {1} 秒", "{0}m {1}s"),

        // ==== Preview ====
        ("Str.Preview.ZoomOut", "缩小（减号键）", "Zoom out (minus key)"),
        ("Str.Preview.ZoomToggle", "在原始大小与适应窗口之间切换（1 / 0）", "Actual size / fit to window (1 / 0)"),
        ("Str.Preview.ZoomIn", "放大（加号键）", "Zoom in (plus key)"),
        ("Str.Preview.Close", "关闭（Esc）", "Close (Esc)"),
        ("Str.Preview.SurfaceHint", "拖动对比 · 双击回到中间 · 滚轮缩放 · 右键拖动平移", "Drag to wipe · double-click to centre · scroll to zoom · right-drag to pan"),
        ("Str.Preview.Original", "原图", "Original"),
        ("Str.Preview.Compared", "压缩后", "Compressed"),
        ("Str.Preview.Open", "打开图片", "Open image"),
        ("Str.Preview.Reveal", "所在位置", "Show in folder"),

        // ==== Settings and updates ====
        ("Str.Caption.Settings", "设置", "Settings"),
        ("Str.Settings.Title", "设置", "Settings"),
        ("Str.Settings.Subtitle", "语言、外观、更新与诊断", "Language, appearance, updates and diagnostics"),
        ("Str.Settings.Close", "关闭设置（Esc）", "Close settings (Esc)"),
        ("Str.Settings.General", "常规", "General"),
        ("Str.Settings.Language", "界面语言", "Display language"),
        ("Str.Settings.Chinese", "简体中文", "Simplified Chinese"),
        ("Str.Settings.English", "English", "English"),
        ("Str.Settings.Appearance", "外观", "Appearance"),
        ("Str.Settings.Theme", "主题", "Theme"),
        ("Str.Settings.Light", "浅色", "Light"),
        ("Str.Settings.Dark", "深色", "Dark"),
        ("Str.Settings.Updates", "软件更新", "Software updates"),
        ("Str.Settings.AutoCheck", "启动后自动检查更新", "Check for updates after startup"),
        ("Str.Settings.AutoCheckHint", "仅访问公开的 GitHub Release，不上传设备或图片信息", "Only reads public GitHub Releases; no device or image data is uploaded"),
        ("Str.Settings.CurrentVersion", "当前版本", "Current version"),
        ("Str.Settings.CheckUpdate", "检查更新", "Check for updates"),
        ("Str.Settings.OneClickUpdate", "一键更新", "Update now"),
        ("Str.Settings.ReleaseNotes", "更新内容", "What's new"),
        ("Str.Settings.Diagnostics", "诊断与反馈", "Diagnostics and feedback"),
        ("Str.Settings.DiagnosticsHint", "复制版本、系统与运行环境信息，不包含图片和设置内容", "Copies version, system and runtime details without images or settings"),
        ("Str.Settings.CopyDiagnostics", "复制诊断信息", "Copy diagnostics"),
        ("Str.Settings.DiagnosticsCopied", "诊断信息已复制", "Diagnostics copied"),
        ("Str.Settings.Repository", "打开项目主页", "Open project page"),
        ("Str.Settings.ReportIssue", "提交问题", "Report an issue"),

        ("Str.Update.Ready", "可随时手动检查更新", "Ready to check for updates"),
        ("Str.Update.Checking", "正在检查更新…", "Checking for updates…"),
        ("Str.Update.UpToDate", "已是最新版本 v{0}", "You're up to date on v{0}"),
        ("Str.Update.Available", "发现新版本 v{0}", "Version v{0} is available"),
        ("Str.Update.NotInstalled", "当前构建不在更新器管理目录中；请安装官方安装包或便携包", "This build is outside the updater-managed directory; install the official setup or portable package"),
        ("Str.Update.Downloading", "正在下载并校验更新… {0}%", "Downloading and verifying… {0}%"),
        ("Str.Update.Restarting", "更新已就绪，正在重启…", "Update is ready; restarting…"),
        ("Str.Update.CheckFailed", "检查更新失败", "Update check failed"),
        ("Str.Update.InstallFailed", "更新失败", "Update failed"),
        ("Str.Update.CheckFailed.Title", "无法检查更新", "Couldn't check for updates"),
        ("Str.Update.CheckFailed.Body", "请检查网络连接后重试；错误详情可直接复制。", "Check your connection and try again. You can copy the error details below."),
        ("Str.Update.InstallFailed.Title", "无法完成更新", "Couldn't install the update"),
        ("Str.Update.InstallFailed.Body", "下载、校验或安装未完成，当前版本未被替换。", "The download, verification or installation did not finish; the current version was not replaced."),

        // ==== Errors ====
        ("Str.Error.Run.Title", "压缩失败", "The run failed"),
        ("Str.Error.Run.Body", "压缩过程中发生错误，已保留当前来源和结果。", "An error occurred during compression. Your current sources and results were kept."),
        ("Str.Error.Report.Body", "报告未能写入所选位置。", "The report could not be written to the selected location."),
        ("Str.Error.Copy.Title", "无法复制", "Couldn't copy"),
        ("Str.Error.Copy.Body", "系统剪贴板暂时不可用，请稍后重试。", "The system clipboard is unavailable. Try again in a moment."),
        ("Str.Error.UnsupportedFormat", "不支持的图片格式。", "Unsupported image format."),
        ("Str.App.Crash", "ImageMinify 遇到问题", "ImageMinify hit a problem"),
        ("Str.App.Crash.Body", "软件已阻止异常导致窗口退出；可复制错误信息后继续使用或提交问题。", "The app prevented the error from closing the window. Copy the details to continue troubleshooting or report the issue."),
    ];
}
