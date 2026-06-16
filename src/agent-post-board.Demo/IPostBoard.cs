namespace AgentPostBoard.Demo;

/// <summary>
/// Append-only bulletin board backed by a directory of dated markdown files.
/// Implementations must be safe to read while another process is appending.
/// </summary>
public interface IPostBoard
{
    /// <summary>
    /// Append a single post to the board. Returns once the post is durable enough
    /// that a subsequent <see cref="ReadAsync"/> call from any process will observe it.
    /// </summary>
    /// <param name="post">The post to append. Treated as immutable by the board.</param>
    /// <param name="cancellationToken">Token to cancel the append.</param>
    Task AppendAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read posts from the board, optionally narrowed by <paramref name="filter"/>.
    /// Posts are yielded in board order: filename lexicographic across files,
    /// byte order within a file. A partial or corrupt post in one file does not
    /// break iteration over other files.
    /// </summary>
    /// <param name="filter">Narrowing options. Pass <c>null</c> or a default instance to read everything.</param>
    /// <param name="cancellationToken">Token to cancel the read.</param>
    IAsyncEnumerable<Post> ReadAsync(
        PostFilter? filter = null,
        CancellationToken cancellationToken = default);
}
