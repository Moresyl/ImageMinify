using System.Reflection;

namespace ImageMinify.Helpers;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/Moresyl/ImageMinify";

    public const string IssuesUrl = RepositoryUrl + "/issues/new?template=bug_report.yml";

    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+', 2)[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
