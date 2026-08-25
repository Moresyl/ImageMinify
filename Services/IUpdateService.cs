using ImageMinify.Models;

namespace ImageMinify.Services;

public interface IUpdateService
{
    bool IsInstalled { get; }

    Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAndApplyAsync(
        UpdateRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
