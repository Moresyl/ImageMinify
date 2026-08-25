using ImageMinify.Models;

namespace ImageMinify.Tests;

public class CompressionResultTests
{
    /// <summary>
    /// The before / after preview only has something to show when the run left two different files
    /// behind. Every other outcome would put the same picture on both sides of the wipe, which looks
    /// like a broken comparison rather than like an outcome with nothing to compare.
    /// </summary>
    [Theory]
    [InlineData(ResultState.Compressed, @"C:\a.jpg", @"C:\a_compressed.jpg", true)]
    [InlineData(ResultState.Skipped, @"C:\a.jpg", @"C:\a.jpg", false)]
    [InlineData(ResultState.Failed, @"C:\a.jpg", "", false)]
    public void CanPreview_NeedsTwoFiles(ResultState state, string original, string compressed, bool expected)
    {
        var result = new CompressionResult
        {
            OriginalPath = original,
            CompressedPath = compressed,
            State = state,
        };

        Assert.Equal(expected, result.CanPreview);
    }

    /// <summary>
    /// Overwriting is the case the state alone cannot catch: the run succeeded, so the row is
    /// Compressed, but the output path <em>is</em> the input path and the file it started from is
    /// gone. Comparing it against itself would show a wipe that never changes anything.
    /// </summary>
    [Fact]
    public void CanPreview_IsFalseWhenTheRunOverwroteTheOriginal()
    {
        var result = new CompressionResult
        {
            OriginalPath = @"C:\photos\a.jpg",
            CompressedPath = @"C:\photos\A.JPG",
            State = ResultState.Compressed,
        };

        Assert.False(result.CanPreview);
    }
}
