using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class PngQualityTests
{
    [Theory]
    [InlineData(0, 0, 8, 8)]
    [InlineData(64, 64, 1, 1)]
    [InlineData(64, 0, 1, 8)]
    [InlineData(0, 64, 8, 1)]
    public void RejectsLocalDamageEvenWhenWholeImageAverageIsSmall(int left, int top, int width, int height)
    {
        using var source = new Image<Rgba32>(65, 65, new Rgba32(100, 100, 100));
        using var candidate = source.Clone();
        for (var y = top; y < top + height; y++)
            for (var x = left; x < left + width; x++)
                candidate[x, y] = new Rgba32(120, 120, 120);
        // At quality 94 a 20-level difference passes the per-pixel bound (34),
        // and is diluted below the whole-image MSE limit, but damages this tile.
        Assert.False(PngQuality.Accepts(source, candidate, 94));
    }

    [Fact]
    public void AllowsSmallDistributedErrorsWithoutChangingAlpha()
    {
        using var source = new Image<Rgba32>(65, 65, new Rgba32(100, 100, 100, 127));
        using var candidate = new Image<Rgba32>(65, 65, new Rgba32(101, 99, 100, 127));
        Assert.True(PngQuality.Accepts(source, candidate, 94));
    }

    [Fact]
    public void ChecksEachAnimationFrameIndependently()
    {
        using var source = new Image<Rgba32>(16, 16, new Rgba32(100, 100, 100));
        source.Frames.AddFrame(source.Frames.RootFrame);
        using var candidate = source.Clone();
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                candidate.Frames[1][x, y] = new Rgba32(120, 120, 120);
        Assert.False(PngQuality.Accepts(source, candidate, 94));
    }
}
