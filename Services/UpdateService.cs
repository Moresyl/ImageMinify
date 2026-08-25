using ImageMinify.Models;
using Velopack;
using Velopack.Sources;

namespace ImageMinify.Services;

/// <summary>Checks, downloads and applies verified Velopack releases from this project's GitHub.</summary>
public sealed class UpdateService : IUpdateService
{
    private const string RepositoryUrl = "https://github.com/Moresyl/ImageMinify";

    private readonly Func<UpdateManager> _managerFactory;

    public UpdateService()
        : this(() => new UpdateManager(new GithubSource(RepositoryUrl, null, false)))
    {
    }

    internal UpdateService(Func<UpdateManager> managerFactory)
    {
        _managerFactory = managerFactory;
    }

    public bool IsInstalled
    {
        get
        {
            try
            {
                return _managerFactory().IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = _managerFactory();
        if (!manager.IsInstalled)
        {
            return null;
        }

        var update = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        if (update is null)
        {
            return null;
        }

        return new UpdateRelease(update.TargetFullRelease.Version.ToString(), update.TargetFullRelease.NotesMarkdown ?? string.Empty);
    }

    public async Task DownloadAndApplyAsync(
        UpdateRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var manager = _managerFactory();
        if (!manager.IsInstalled)
        {
            throw new InvalidOperationException("This build is not installed by Velopack.");
        }

        var update = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken)
            ?? throw new InvalidOperationException("The update is no longer available.");

        var actualVersion = update.TargetFullRelease.Version.ToString();
        if (!string.Equals(actualVersion, release.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The available update changed from {release.Version} to {actualVersion}.");
        }

        await manager.DownloadUpdatesAsync(update, value => progress?.Report(value), cancellationToken);
        manager.ApplyUpdatesAndRestart(update);
    }
}
