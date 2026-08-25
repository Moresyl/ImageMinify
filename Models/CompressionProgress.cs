namespace ImageMinify.Models;

/// <summary>
/// One file finishing, reported as the batch runs.
/// </summary>
/// <param name="Completed">How many files have finished, in any order.</param>
/// <param name="Total">How many files the run started with.</param>
/// <param name="Result">The file that just finished.</param>
/// <remarks>
/// Files finish out of order because the batch runs one per core, so <paramref name="Completed"/> is
/// a count and not an index — the result it arrives with is whichever file happened to finish, not
/// the <c>Completed</c>-th one in the list.
/// </remarks>
public readonly record struct CompressionProgress(int Completed, int Total, CompressionResult Result)
{
    public double Percent => Total == 0 ? 0 : Completed * 100D / Total;
}
