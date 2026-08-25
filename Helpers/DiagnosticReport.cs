using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ImageMinify.Helpers;

/// <summary>Builds copyable support evidence while removing common local secrets and user paths.</summary>
public static partial class DiagnosticReport
{
    public static string Build(Exception? exception = null)
    {
        var report = new StringBuilder()
            .AppendLine("ImageMinify diagnostics")
            .Append("Version: ").AppendLine(AppInfo.Version)
            .Append("OS: ").AppendLine(RuntimeInformation.OSDescription)
            .Append("Architecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString())
            .Append("Runtime: ").AppendLine(RuntimeInformation.FrameworkDescription)
            .Append("Language: ").AppendLine(Strings.Current.ToString());

        if (exception is not null)
        {
            report.AppendLine().AppendLine("Error:").AppendLine(exception.ToString());
        }

        return Redact(report.ToString().TrimEnd());
    }

    public static string Redact(string value)
    {
        var redacted = value;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            redacted = redacted.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        redacted = SecretPattern().Replace(redacted, "$1=[REDACTED]");
        redacted = BearerPattern().Replace(redacted, "$1[REDACTED]");
        return redacted;
    }

    [GeneratedRegex("(?i)\\b(api[_-]?key|token|password|secret)\\s*[:=]\\s*([^\\s,;]+)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)\\b(authorization\\s*:\\s*bearer\\s+)[^\\s]+")]
    private static partial Regex BearerPattern();
}
