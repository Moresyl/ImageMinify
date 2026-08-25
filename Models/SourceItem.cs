namespace ImageMinify.Models;

/// <summary>
/// One entry in the source list: a file the user picked, or a folder to walk.
/// </summary>
/// <remarks>
/// Files and folders share one list rather than living in separate "directory mode" and "file mode"
/// slots. Dropping a folder and three loose images onto the window is an ordinary thing to want, and
/// a mode has to reject one of them.
/// </remarks>
public sealed record SourceItem(string Path, bool IsDirectory)
{
    public string DisplayName
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }

    /// <summary>The containing folder, so two identically-named entries can still be told apart.</summary>
    public string Location => System.IO.Path.GetDirectoryName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? Path;

    public static SourceItem? TryCreate(string path)
    {
        if (Directory.Exists(path))
        {
            return new SourceItem(path, IsDirectory: true);
        }

        return File.Exists(path) ? new SourceItem(path, IsDirectory: false) : null;
    }
}
