using Velopack;

namespace ImageMinify;

/// <summary>The process entry point, kept explicit so updater hooks run before WPF starts.</summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
