using System.Xml.Linq;
using ImageMinify.Helpers;

namespace ImageMinify.Tests;

public sealed class ReleaseContractTests
{
    private static readonly string ContractDirectory = Path.Combine(AppContext.BaseDirectory, "ReleaseContract");

    [Fact]
    public void AssemblyVersion_MatchesTheSingleProjectVersion()
    {
        var project = XDocument.Load(Path.Combine(ContractDirectory, "ImageMinify.csproj"));
        var version = project.Descendants("Version").Single().Value;

        Assert.Equal(version, AppInfo.Version);
    }

    [Fact]
    public void ReleaseWorkflow_BuildsEveryUpdaterAssetAndVerifiesItPublicly()
    {
        var workflow = File.ReadAllText(Path.Combine(ContractDirectory, "release.yml"));

        Assert.Contains("dotnet test ImageMinify.sln -c Release", workflow);
        Assert.Contains("vpk download github", workflow);
        Assert.Contains("'pack'", workflow);
        Assert.Contains("vpk upload github", workflow);
        Assert.Contains("'*-Setup.exe','*-Portable.zip','*-full.nupkg','releases.win.json','SHA256SUMS.txt'", workflow);
    }

    [Fact]
    public void CurrentReleaseNotes_AreDetailedAndBilingual()
    {
        var notes = File.ReadAllText(Path.Combine(ContractDirectory, $"{AppInfo.Version}.md"));

        Assert.Contains("<!-- imageminify-notes:zh -->", notes);
        Assert.Contains("<!-- imageminify-notes:en -->", notes);
        Assert.Contains("### 安装", notes);
        Assert.Contains("### Installation", notes);
        Assert.True(notes.Length > 1_000, "Release notes must describe concrete changes, not a generic link.");
    }
}
