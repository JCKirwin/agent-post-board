using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentPostBoard.Demo.Tests;

// These tests pin the four invariants the post board promises:
// 1. Append-only — written posts are never mutated.
// 2. Reads are safe while writes happen concurrently.
// 3. Ordering is deterministic: byte-order within a file, filename lex across files.
// 4. A corrupt file does not poison reads of its neighbors.
//
// The board is filesystem-backed, so each test gets a fresh temp directory and cleans up after.
public sealed class PostBoardInvariantTests : IDisposable
{
    private readonly string _root;

    public PostBoardInvariantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agent-post-board-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best-effort; a leftover temp directory is not a test failure.
        }
    }

    // Invariant 1: posts are append-only. Writing a second post must not change the first.
    [Fact]
    public void Write_DoesNotMutatePreviouslyWrittenPosts()
    {
        var board = new FileSystemPostBoard(_root);
        var first = new Post("alice", "session", "won at carcassonne", DateTimeOffset.UtcNow);
        var second = new Post("bob", "session", "lost at catan", DateTimeOffset.UtcNow.AddSeconds(1));

        board.Write(first);
        var afterFirst = board.Read(PostFilter.All).ToList();

        board.Write(second);
        var afterSecond = board.Read(PostFilter.All).ToList();

        // The first post should be byte-identical between the two reads.
        Assert.Contains(afterSecond, p =>
            p.AuthorId == first.AuthorId &&
            p.Topic == first.Topic &&
            p.Body == first.Body);
        Assert.Equal(afterFirst[0].Body, afterSecond.First(p => p.AuthorId == "alice").Body);
    }

    // Invariant 2: reads remain safe while writes are happening on another thread.
    // We don't assert what gets read — only that the read completes without throwing
    // and returns a coherent view (every returned post is one we actually wrote).
    [Fact]
    public async Task Read_IsSafeUnderConcurrentWrites()
    {
        var board = new FileSystemPostBoard(_root);
        var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writer = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                board.Write(new Post(
                    authorId: "writer",
                    topic: "noise",
                    body: "post-" + i++,
                    timestamp: DateTimeOffset.UtcNow));
            }
        });

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var post in board.Read(PostFilter.All))
                {
                    // Every observed post must be a real, non-null post — never a torn read.
                    Assert.NotNull(post);
                    Assert.Equal("writer", post.AuthorId);
                    Assert.StartsWith("post-", post.Body);
                }
            }
        });

        await Task.WhenAll(writer, reader);
    }

    // Invariant 3a: within a single file, posts come back in the order they were written.
    [Fact]
    public void Read_PreservesByteOrderWithinASingleFile()
    {
        var board = new FileSystemPostBoard(_root);
        var day = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);

        var posts = Enumerable.Range(0, 5)
            .Select(i => new Post("alice", "session", "body-" + i, day.AddSeconds(i)))
            .ToList();

        foreach (var p in posts)
        {
            board.Write(p);
        }

        var read = board.Read(PostFilter.All).ToList();

        var bodies = read.Select(p => p.Body).ToList();
        Assert.Equal(new[] { "body-0", "body-1", "body-2", "body-3", "body-4" }, bodies);
    }

    // Invariant 3b: across files, the global order is filename-lexicographic.
    // We force the board to use multiple files by writing posts on different days.
    [Fact]
    public void Read_OrdersAcrossFilesLexicographicallyByFilename()
    {
        var board = new FileSystemPostBoard(_root);

        // Two days, two posts each. Day 1 must sort before day 2 regardless of write order.
        var day1 = new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero);

        // Write day 2 first to prove ordering doesn't depend on write order.
        board.Write(new Post("alice", "session", "day2-first", day2));
        board.Write(new Post("alice", "session", "day2-second", day2.AddMinutes(5)));
        board.Write(new Post("alice", "session", "day1-first", day1));
        board.Write(new Post("alice", "session", "day1-second", day1.AddMinutes(5)));

        var read = board.Read(PostFilter.All).Select(p => p.Body).ToList();

        // Day 1 file sorts before day 2 file lexicographically; within each file, write order wins.
        var day1Index = read.IndexOf("day1-first");
        var day2Index = read.IndexOf("day2-first");
        Assert.True(day1Index >= 0, "day1-first should be present");
        Assert.True(day2Index >= 0, "day2-first should be present");
        Assert.True(day1Index < day2Index, "day 1 posts should come before day 2 posts");
        Assert.True(read.IndexOf("day1-second") > day1Index);
        Assert.True(read.IndexOf("day2-second") > day2Index);
    }

    // Invariant 4: a corrupt file should not stop neighboring files from being read.
    [Fact]
    public void Read_SkipsCorruptFileWithoutPoisoningOthers()
    {
        var board = new FileSystemPostBoard(_root);

        // Write one good post so a real file exists and the schema is in place.
        var good = new Post("alice", "session", "the good one", new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero));
        board.Write(good);

        // Drop a junk file alongside the real one. Filename sorts before any real date file
        // so if the reader fails fast on it, we'd lose the good post too.
        var junkPath = Path.Combine(_root, "0000-00-00.md");
        File.WriteAllText(junkPath, "this is not a valid post file at all\n###\n  garbage  \n");

        var read = board.Read(PostFilter.All).ToList();

        Assert.Contains(read, p => p.Body == "the good one");
    }
}
