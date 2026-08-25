using System.Windows;
using System.Windows.Threading;
using ImageMinify.Services;
using ImageMinify.ViewModels;

namespace ImageMinify;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var compressor = new ImageCompressor(
            new ExifService(),
            new JpegCompressor(),
            new PngCompressor(),
            new WebpCompressor());

        // One settings service, shared. The view model saves the user's choices through it and the
        // window saves its own placement through it; two instances would be two write paths to the
        // same store, and the later flush would drop whatever the other one had queued.
        var settingsService = new SettingsService();
        var viewModel = new MainViewModel(compressor, settingsService);

        var window = new Views.MainWindow(viewModel, settingsService);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Reports an exception that escaped a command instead of letting the window vanish.
    /// </summary>
    /// <remarks>
    /// Compression touches the file system across many threads, where a drive going away mid-run or
    /// a folder losing its permissions produces exceptions that no amount of local handling
    /// anticipates. Marking the exception handled keeps the application alive with the user's
    /// settings and results intact; the alternative is a process that disappears without a word.
    /// </remarks>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "ImageMinify 遇到问题",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }
}
