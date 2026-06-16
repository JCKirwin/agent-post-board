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
/// markdown heading <c>## &lt;timestamp&gt; | &lt;author&gt; | &lt;topic&gt;</c>, then its body lines
/// (each indented by one space), then a blank line that commits the post. Posts are ordered
/// by byte position within a file and by filename across files.
/// <para>
/// The trailing blank line is the commit marker: the reader only yields a post once it has
/// seen that terminator. Because a file append fills bytes in order, seeing the terminator
/// guarantees the whole post precedes it — so a reader that catches a writer mid-append sees a
/// consistent prefix, missing the in-flight post rather than yielding it half-written. Indenting
/// body lines keeps post content from ever looking like a heading or the blank terminator.
/// </para>
/// </remarks>
public sealed class FileSystemPostBoard : IPostBoard
{
    private const string HeadingPrefix = "## ";
    private const string FieldSeparator = " | ";
    private const string PostTerminator = "\n\n";
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

        // Indent every body line by one space so body content can never be mistaken for a
        // heading ("## ") or the blank commit terminator.
        foreach (var line in SplitLines(post.Body))
        {
            sb.Append(' ').Append(line).Append('\n');
        }

        sb.Append('\n'); // blank line commits the post
        return sb.ToString();
    }

    private static IEnumerable<Post> ParsePosts(string content)
    {
        var records = NormalizeNewlines(content).Split(PostTerminator);

        // Every committed post is followed by the terminator, so the final element is either
        // empty (the file ended cleanly) or an unterminated tail caught mid-append. Either way
        // the last element is never a committed post — skipping it is what lets a reader miss an
        // in-flight post instead of yielding it half-written.
        for (var i = 0; i < records.Length - 1; i++)
        {
            if (TryBuildPost(records[i], out var post))
            {
                yield return post;
            }
        }
    }

    private static bool TryBuildPost(string record, out Post post)
    {
        post = default!;
        if (record.Length == 0)
        {
            return false;
        }

        var newline = record.IndexOf('\n');
        var headingLine = newline < 0 ? record : record[..newline];
        if (!headingLine.StartsWith(HeadingPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = headingLine[HeadingPrefix.Length..].Split(FieldSeparator);
        if (fields.Length < 3)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return false;
        }

        var body = string.Empty;
        if (newline >= 0)
        {
            var bodyLines = record[(newline + 1)..].Split('\n');
            for (var i = 0; i < bodyLines.Length; i++)
            {
                // Strip the one-space indent the writer added.
                if (bodyLines[i].StartsWith(' '))
                {
                    bodyLines[i] = bodyLines[i][1..];
                }
            }

            body = string.Join('\n', bodyLines);
        }

        post = new Post(UnescapeField(fields[1]), UnescapeField(fields[2]), body, timestamp);
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
}
