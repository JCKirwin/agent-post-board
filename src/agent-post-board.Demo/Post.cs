namespace AgentPostBoard.Demo;

/// <summary>
/// A single bulletin-board entry. Posts are immutable once written;
/// the board only ever appends, never edits.
/// </summary>
/// <param name="Author">Stable identifier of the process or person that wrote the post.</param>
/// <param name="Topic">Short tag used for filtering. Free-form; consumers decide the vocabulary.</param>
/// <param name="Body">The post content. Plain text. No length cap is enforced by the type.</param>
/// <param name="TimestampUtc">When the post was created, in UTC. Set by the writer at append time.</param>
public sealed record Post(
    string Author,
    string Topic,
    string Body,
    DateTimeOffset TimestampUtc);
