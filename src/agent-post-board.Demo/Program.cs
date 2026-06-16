using System.Globalization;

namespace AgentPostBoard.Demo;

/// <summary>
/// Entry point for the demo. Parses <c>role=&lt;role&gt;</c> and dispatches to one of the
/// four roles defined by <see cref="DemoRole"/>. The board lives in a shared temp directory
/// so every process points at the same board and the demo is self-contained.
/// </summary>
public static class Program
{
    private const string DemoBoardDirectoryName = "agent-post-board-demo";

    private static readonly string[] Games = { "Carcassonne", "Catan", "Wingspan", "Azul" };

    /// <summary>Run the demo with the supplied command-line arguments.</summary>
    /// <param name="args">Process arguments. Expects a single <c>role=&lt;role&gt;</c> token.</param>
    /// <returns>Process exit code. <c>0</c> on clean shutdown.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (!TryParseRole(args, out var role))
        {
            Console.Error.WriteLine("Usage: dotnet run -- role=<alex|blair|casey|reader>");
            return 1;
        }

        var boardPath = Path.Combine(Path.GetTempPath(), DemoBoardDirectoryName);
        var board = new FileSystemPostBoard(boardPath);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"[{role}] board at {boardPath} — press Ctrl+C to stop.");

        return role == DemoRole.Reader
            ? await RunReaderAsync(board, cts.Token)
            : await RunWriterAsync(board, role, cts.Token);
    }

    /// <summary>
    /// Map a <c>role=&lt;role&gt;</c> argument to a <see cref="DemoRole"/>. Accepts the friendly
    /// player names the README uses (case-insensitive) and is the single source of truth for
    /// the role vocabulary.
    /// </summary>
    /// <param name="args">The process arguments to scan.</param>
    /// <param name="role">The parsed role when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> if a known role was found; otherwise <c>false</c>.</returns>
    public static bool TryParseRole(IReadOnlyList<string> args, out DemoRole role)
    {
        role = default;

        string? value = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("role=", StringComparison.OrdinalIgnoreCase))
            {
                value = arg["role=".Length..];
                break;
            }
        }

        if (value is null)
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "alex":
            case "friendone":
                role = DemoRole.FriendOne;
                return true;
            case "blair":
            case "friendtwo":
                role = DemoRole.FriendTwo;
                return true;
            case "casey":
            case "friendthree":
                role = DemoRole.FriendThree;
                return true;
            case "reader":
                role = DemoRole.Reader;
                return true;
            default:
                return false;
        }
    }

    private static async Task<int> RunWriterAsync(IPostBoard board, DemoRole role, CancellationToken cancellationToken)
    {
        var author = role.ToString();
        var index = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var game = Games[index % Games.Length];
                var minutes = 20 + (index * 7 % 40);
                var body = $"won {game} in {minutes} min";

                await board.AppendAsync(
                    new Post(author, "session", body, DateTimeOffset.UtcNow), cancellationToken);
                Console.WriteLine($"[{author}] {body}");

                index++;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — a clean shutdown for a demo writer.
        }

        return 0;
    }

    private static async Task<int> RunReaderAsync(IPostBoard board, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                await foreach (var post in board.ReadAsync(PostFilter.All, cancellationToken))
                {
                    counts[post.Author] = counts.TryGetValue(post.Author, out var current) ? current + 1 : 1;
                }

                Console.WriteLine("=== Leaderboard ===");
                foreach (var (player, posts) in counts)
                {
                    Console.WriteLine($"{player}: {posts.ToString(CultureInfo.InvariantCulture)} post(s)");
                }

                Console.WriteLine();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — a clean shutdown for the reader.
        }

        return 0;
    }
}
