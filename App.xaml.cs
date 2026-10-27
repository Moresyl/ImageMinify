using System.Windows;
using System.Windows.Threading;
using ImageMinify.Helpers;
using ImageMinify.Services;
using ImageMinify.ViewModels;

namespace ImageMinify;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Publishes the default language's strings before anything is shown. The saved choice is
        // applied a moment later, once the settings have been read — the same order the theme
        // follows, and for the same reason: a window whose labels are all missing while it waits for
        // a disk read is worse than one that switches language on the frame after it appears.
        Strings.Apply(Language.Chinese);

        var compressor = new ImageCompressor(
            new ExifService(),
            new JpegCompressor(),
            new PngCompressor(),
            new WebpCompressor());

        // One settings service, shared. The view model saves the user's choices through it and the
        // window saves its own placement through it; two instances would be two write paths to the
        // same store, and the later flush would drop whatever the other one had queued.
        var settingsService = new SettingsService();
        var viewModel = new MainViewModel(compressor, settingsService, new UpdateService());
        _viewModel = viewModel;

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
        if (_viewModel is not null && MainWindow?.IsVisible == true)
        {
            _ = _viewModel.ShowExceptionAsync(
                Strings.Get("Str.App.Crash"),
                Strings.Get("Str.App.Crash.Body"),
                e.Exception);
        }
        else
        {
            StartupFailure.Report(e.Exception);
            Shutdown(1);
        }

        e.Handled = true;
    }
}
