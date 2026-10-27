using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class PngRgbCandidateTests
{
    [Theory]
    [InlineData(82)]
    [InlineData(94)]
    public void TrueColorFallback_KeepsAlphaAndHiddenRgbWhileReducingSize(int quality)
    {
        using var source = new Image<Rgba32>(96, 96);
        var random = new Random(20261027);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source[x, y] = new Rgba32((byte)random.Next(256), (byte)random.Next(256),
                    (byte)random.Next(256), (byte)((x + y * source.Width) % 256));
        using var original = source.Clone();
        using var smart = new MemoryStream();
        using var exact = new MemoryStream();
        var encoder = new PngCompressor();
        encoder.Encode(source, smart, new EncodeRequest(quality, true));
        encoder.Encode(source, exact, new EncodeRequest(100, true));
        Assert.True(smart.Length < exact.Length);
        using var result = Image.Load<Rgba32>(smart.ToArray());
        Assert.True(PngQuality.Accepts(source, result, quality));
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                Assert.Equal(original[x, y], source[x, y]);
                Assert.Equal(source[x, y].A, result[x, y].A);
                if (source[x, y].A == 0) Assert.Equal(source[x, y], result[x, y]);
                Assert.InRange(Math.Abs(source[x, y].R - result[x, y].R), 0, 4);
                Assert.InRange(Math.Abs(source[x, y].G - result[x, y].G), 0, 4);
                Assert.InRange(Math.Abs(source[x, y].B - result[x, y].B), 0, 4);
            }
    }

    [Fact]
    public void Candidate_KeepsAllAnimationFramesAndSourceUnmodified()
    {
        using var source = new Image<Rgba32>(16, 16, new Rgba32(123, 43, 91, 127));
        source.Frames.AddFrame(source.Frames.RootFrame);
        source.Frames[1][0, 0] = new Rgba32(1, 2, 3, 0);
        using var candidate = PngRgbCandidate.Create(source, 82);
        Assert.Equal(2, candidate.Frames.Count);
        Assert.True(PngQuality.Accepts(source, candidate, 82));
        Assert.Equal(new Rgba32(123, 43, 91, 127), source[0, 0]);
        Assert.Equal(source.Frames[1][0, 0], candidate.Frames[1][0, 0]);
    }
}
