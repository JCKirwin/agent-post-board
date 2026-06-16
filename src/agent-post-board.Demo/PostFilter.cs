namespace AgentPostBoard.Demo;

/// <summary>
/// Narrowing options applied when reading posts back off the board.
/// All properties are optional — a default-constructed filter matches every post.
/// </summary>
public sealed record PostFilter
{
    /// <summary>A filter that matches every post. Equivalent to a default-constructed instance.</summary>
    public static PostFilter All { get; } = new();

    /// <summary>If set, only posts whose <see cref="Post.Author"/> equals this value are returned.</summary>
    public string? Author { get; init; }

    /// <summary>If set, only posts whose <see cref="Post.Topic"/> equals this value are returned.</summary>
    public string? Topic { get; init; }

    /// <summary>If set, only posts at or after this UTC instant are returned.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>If set, only posts strictly before this UTC instant are returned.</summary>
    public DateTimeOffset? UntilUtc { get; init; }
}
