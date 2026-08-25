using ImageMinify.Helpers;

namespace ImageMinify.Tests;

public sealed class ReleaseNotesFormatterTests
{
    private const string Notes = """
        <!-- imageminify-notes:zh -->
        ## 新功能
        - **一键更新**：[详情](https://example.com)
        <!-- imageminify-notes:en -->
        ## New
        - **One-click updates**: [details](https://example.com)
        """;

    [Fact]
    public void ForDisplay_SelectsChineseAndRemovesBasicMarkdown()
    {
        var result = ReleaseNotesFormatter.ForDisplay(Notes, Language.Chinese);

        Assert.Contains("新功能", result);
        Assert.Contains("一键更新", result);
        Assert.DoesNotContain("One-click", result);
        Assert.DoesNotContain("**", result);
    }

    [Fact]
    public void ForDisplay_SelectsEnglishAndRemovesBasicMarkdown()
    {
        var result = ReleaseNotesFormatter.ForDisplay(Notes, Language.English);

        Assert.Contains("New", result);
        Assert.Contains("One-click updates", result);
        Assert.DoesNotContain("新功能", result);
        Assert.DoesNotContain("https://", result);
    }
}
