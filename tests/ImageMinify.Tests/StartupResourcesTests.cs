using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ImageMinify.Tests;

public sealed class StartupResourcesTests
{
    [Fact]
    public void MainWindow_LoadsCompiledResourcesOnAnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                app = new App();
                app.InitializeComponent();
                var settings = new ImageMinify.Services.SettingsService(new InMemorySettingsStore());
                var compressor = new ImageMinify.Services.ImageCompressor(
                    new ImageMinify.Services.ExifService(), new ImageMinify.Services.JpegCompressor(),
                    new ImageMinify.Services.PngCompressor(), new ImageMinify.Services.WebpCompressor());
                var viewModel = new ImageMinify.ViewModels.MainViewModel(
                    compressor, settings, new ImageMinify.Services.UpdateService());
                var window = new ImageMinify.Views.MainWindow(viewModel, settings);
                window.ApplyTemplate();
                window.Measure(new System.Windows.Size(1200, 820));
                Assert.NotNull(window.Content);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (app is not null)
                {
                    app.Shutdown();
                    // Shutdown is queued; drain it before this STA exits so Application.Current
                    // cannot leak into the non-UI tests.
                    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
                        new Action(dispatcher.InvokeShutdown));
                    System.Windows.Threading.Dispatcher.Run();
                }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Window construction timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void MainWindow_StaticResourcesHaveDefinitions()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = AppContext.BaseDirectory;
        var files = Directory.GetFiles(Path.Combine(root, "Themes"), "*.xaml")
            .Concat([Path.Combine(root, "App.xaml"), Path.Combine(root, "Views", "MainWindow.xaml")]);
        var documents = files.Select(XDocument.Load).ToArray();
        var keys = documents.SelectMany(document => document.Descendants())
            .Select(element => (string?)element.Attribute(xaml + "Key"))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var window = documents.Last();
        var missing = window.Descendants().Attributes()
            .SelectMany(attribute => Regex.Matches(attribute.Value, @"\{StaticResource ([A-Za-z0-9_.]+)\}"))
            .Select(match => match.Groups[1].Value).Where(key => !keys.Contains(key)).Distinct();
        Assert.Empty(missing);
    }
}
