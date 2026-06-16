using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentPostBoard.Demo;

/// <summary>
/// Directory-backed <see cref="IPostBoard"/>. Each post is a heading-delimited
/// section inside a dated markdown file under <see cref="RootPath"/>.
/// </summary>
/// <remarks>
/// On-disk format (ADR 0003): one <c>YYYY-MM-DD.md</c> file per UTC day. Each post is a
/// markdown heading <c>## &lt;timestamp&gt; | &lt;author&gt; | &lt;topic&gt;</c> followed by its body
/// lines. Posts are ordered by byte position within a file and by filename across files.
/// A post is only considered readable once its body is present, so a reader that catches a
/// writer mid-append sees a consistent prefix — it may miss the in-flight post, but never a
/// torn one.
/// </remarks>
public sealed class FileSystemPostBoard : IPostBoard
{
    private const string HeadingPrefix = "## ";
    private const string FieldSeparator = " | ";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
    public async Task AppendAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        Directory.CreateDirectory(RootPath);
        var path = Path.Combine(RootPath, FileNameFor(post.TimestampUtc));
        var bytes = Utf8NoBom.GetBytes(Serialize(post));

        // FileShare.ReadWrite so a concurrent reader (or writer) is never locked out.
        // The whole post is written in one call, so readers see all-or-nothing of it.
        await using var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
            bufferSize: 4096, useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Post> ReadAsync(
        PostFilter? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        filter ??= PostFilter.All;
        if (!Directory.Exists(RootPath))
        {
            yield break;
        }

        var files = Directory.GetFiles(RootPath, "*.md");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await ReadAllTextSharedAsync(file, cancellationToken).ConfigureAwait(false);

            foreach (var post in ParsePosts(content))
            {
                if (Matches(post, filter))
                {
                    yield return post;
                }
            }
        }
    }

    private static string FileNameFor(DateTimeOffset timestamp)
        => timestamp.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md";

    private static string Serialize(Post post)
    {
        var sb = new StringBuilder();
        sb.Append(HeadingPrefix)
          .Append(post.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
          .Append(FieldSeparator).Append(EscapeField(post.Author))
          .Append(FieldSeparator).Append(EscapeField(post.Topic))
          .Append('\n');

        foreach (var line in SplitLines(post.Body))
        {
            sb.Append(EscapeBodyLine(line)).Append('\n');
        }

        return sb.ToString();
    }

    private static IEnumerable<Post> ParsePosts(string content)
    {
        var lines = NormalizeNewlines(content).Split('\n');
        string? heading = null;
        var body = new List<string>();

        foreach (var raw in lines)
        {
            if (raw.StartsWith(HeadingPrefix, StringComparison.Ordinal))
            {
                if (heading is not null && TryBuildPost(heading, body, out var previous))
                {
                    yield return previous;
                }

                heading = raw[HeadingPrefix.Length..];
                body.Clear();
            }
            else if (heading is not null)
            {
                body.Add(UnescapeBodyLine(raw));
            }
        }

        if (heading is not null && TryBuildPost(heading, body, out var last))
        {
            yield return last;
        }
    }

    private static bool TryBuildPost(string heading, List<string> bodyLines, out Post post)
    {
        post = default!;

        var fields = heading.Split(FieldSeparator);
        if (fields.Length < 3)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return false;
        }

        // Each serialized post ends with a trailing newline, which Split turns into a final
        // empty element. Drop exactly one so the body round-trips, then treat a body with no
        // real lines as an in-flight (not-yet-committed) post and skip it.
        var lines = new List<string>(bodyLines);
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return false;
        }

        post = new Post(
            UnescapeField(fields[1]),
            UnescapeField(fields[2]),
            string.Join('\n', lines),
            timestamp);
        return true;
    }

    private static bool Matches(Post post, PostFilter filter)
    {
        if (filter.Author is not null && !string.Equals(post.Author, filter.Author, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.Topic is not null && !string.Equals(post.Topic, filter.Topic, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.FromUtc is { } from && post.TimestampUtc < from)
        {
            return false;
        }

        if (filter.UntilUtc is { } until && post.TimestampUtc >= until)
        {
            return false;
        }

        return true;
    }

    private static async Task<string> ReadAllTextSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Utf8NoBom);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string[] SplitLines(string text)
        => NormalizeNewlines(text).Split('\n');

    // A field (author/topic) must survive the " | " heading separator and stay single-line.
    private static string EscapeField(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '|': sb.Append(@"\|"); break;
                case '\r': break;
                case '\n': sb.Append(@"\n"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    private static string UnescapeField(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    '|' => '|',
                    '\\' => '\\',
                    _ => next,
                });
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // A body line that would otherwise look like a heading (or a prior escape) is prefixed
    // with a backslash so the reader never mistakes post content for a new post.
    private static string EscapeBodyLine(string line)
        => line.StartsWith('#') || line.StartsWith('\\') ? "\\" + line : line;

    private static string UnescapeBodyLine(string line)
        => line.StartsWith('\\') ? line[1..] : line;
}
