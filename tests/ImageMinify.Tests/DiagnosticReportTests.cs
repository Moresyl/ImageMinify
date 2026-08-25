using ImageMinify.Helpers;

namespace ImageMinify.Tests;

public sealed class DiagnosticReportTests
{
    [Fact]
    public void Build_IncludesSupportContextAndRedactsTheUserProfile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exception = new InvalidOperationException(Path.Combine(profile, "Pictures", "private.jpg"));

        var report = DiagnosticReport.Build(exception);

        Assert.Contains("ImageMinify diagnostics", report);
        Assert.Contains($"Version: {AppInfo.Version}", report);
        Assert.Contains("%USERPROFILE%", report);
        Assert.DoesNotContain(profile, report, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("token=abc123", "token=[REDACTED]")]
    [InlineData("Authorization: Bearer abc123", "Authorization: Bearer [REDACTED]")]
    [InlineData("api_key: secret-value", "api_key=[REDACTED]")]
    public void Redact_RemovesCommonSecrets(string input, string expected)
    {
        Assert.Equal(expected, DiagnosticReport.Redact(input));
    }
}
