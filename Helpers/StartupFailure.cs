using System.Windows;

namespace ImageMinify.Helpers;

/// <summary>Reports startup failures without depending on application XAML or a visible window.</summary>
internal static class StartupFailure
{
    public static void Report(Exception exception)
    {
        var report = DiagnosticReport.Build(exception);
        var message = "ImageMinify 启动失败 / Could not start ImageMinify.\n\n";
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ImageMinify", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup-error.log");
            File.WriteAllText(path, report);
            message += "诊断日志 / Diagnostic log: " + path + "\n\n";
        }
        catch (Exception)
        {
            // A full or unwritable disk must not hide the original startup error.
        }

        MessageBox.Show(message + report, "ImageMinify", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
