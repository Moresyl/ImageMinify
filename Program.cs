using Velopack;
using ImageMinify.Helpers;

namespace ImageMinify;

/// <summary>The process entry point, kept explicit so updater hooks run before WPF starts.</summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception exception)
        {
            StartupFailure.Report(exception);
            Environment.ExitCode = 1;
        }
    }
}
