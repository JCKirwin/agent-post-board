namespace AgentPostBoard.Demo;

/// <summary>
/// Directory-backed <see cref="IPostBoard"/>. Each post is a heading-delimited
/// section inside a dated markdown file under <see cref="RootPath"/>.
/// </summary>
public sealed class FileSystemPostBoard : IPostBoard
{
    /// <summary>
    /// Create a board rooted at the given directory. The directory is created on demand
    /// at first append; it does not need to exist when the board is constructed.
    /// </summary>
    /// <param name="rootPath">Absolute or relative path to the directory that holds the board files.</param>
    public FileSystemPostBoard(string rootPath)
    {
        RootPath = rootPath;
    }

    /// <summary>The directory that backs this board.</summary>
    public string RootPath { get; }

    /// <inheritdoc />
    public Task AppendAsync(Post post, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Post> ReadAsync(
        PostFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
