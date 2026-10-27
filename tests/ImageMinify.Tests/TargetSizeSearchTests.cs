using ImageMinify.Models;
using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class TargetSizeSearchTests
{
    private sealed class ProbeEncoder(Func<EncodeRequest, int> size) : IFormatEncoder
    {
        public List<EncodeRequest> Requests { get; } = [];
        public string Extension => ".png";
        public bool SupportsQuality => true;
        public Image<Rgba32> Prepare(Image<Rgba32> image) => image;
        public void PostProcess(string path, bool stripMetadata) { }
        public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
        {
            Requests.Add(request);
            output.Write(new byte[size(request)]);
        }
    }

    [Fact]
    public void KeepsFittingProbeWhenFullEffortIsLarger()
    {
        using var image = new Image<Rgba32>(1, 1);
        var encoder = new ProbeEncoder(request => request.Fast ? request.Quality * 10 : 1000);
        var result = ImageCompressor.SearchForTargetSize(encoder, image,
            new CompressionOptions { TargetSizeBytes = 500 }, CancellationToken.None);
        Assert.True(result.MetTarget);
        Assert.Equal(50, result.Quality);
        Assert.Equal(500, result.Payload.Length);
        Assert.InRange(encoder.Requests.Count, 2, 8);
        Assert.Equal(result.Quality, encoder.Requests.Last().Quality);
        Assert.False(encoder.Requests.Last().Fast);
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(600, false)]
    public void ReportsActualFinalSizeWhenNoFastProbeFits(int finalSize, bool metTarget)
    {
        using var image = new Image<Rgba32>(1, 1);
        var encoder = new ProbeEncoder(request => request.Fast ? 1000 : finalSize);
        var result = ImageCompressor.SearchForTargetSize(encoder, image,
            new CompressionOptions { TargetSizeBytes = 500 }, CancellationToken.None);
        Assert.Equal(metTarget, result.MetTarget);
        Assert.Equal(finalSize, result.Payload.Length);
        Assert.Equal(5, result.Quality);
    }

    [Fact]
    public void UsesSmallerFullEffortResult()
    {
        using var image = new Image<Rgba32>(1, 1);
        var encoder = new ProbeEncoder(request => request.Fast ? 500 : 400);
        var result = ImageCompressor.SearchForTargetSize(encoder, image,
            new CompressionOptions { TargetSizeBytes = 500 }, CancellationToken.None);
        Assert.True(result.MetTarget);
        Assert.Equal(100, result.Quality);
        Assert.Equal(400, result.Payload.Length);
    }
}
