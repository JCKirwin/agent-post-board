using System;
using System.Collections.Generic;
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

    // Drains an async stream of posts into a list so the synchronous assertions below can work with it.
    private static async Task<List<Post>> Drain(IAsyncEnumerable<Post> posts)
    {
        var list = new List<Post>();
        await foreach (var post in posts)
        {
            list.Add(post);
        }

        return list;
    }

    // Invariant 1: posts are append-only. Appending a second post must not change the first.
    [Fact]
    public async Task Append_DoesNotMutatePreviouslyWrittenPosts()
    {
        var ct = TestContext.Current.CancellationToken;
        var board = new FileSystemPostBoard(_root);
        var first = new Post("alice", "session", "won at carcassonne", DateTimeOffset.UtcNow);
        var second = new Post("bob", "session", "lost at catan", DateTimeOffset.UtcNow.AddSeconds(1));

        await board.AppendAsync(first, ct);
        var afterFirst = await Drain(board.ReadAsync(PostFilter.All, ct));

        await board.AppendAsync(second, ct);
        var afterSecond = await Drain(board.ReadAsync(PostFilter.All, ct));

        // The first post should be byte-identical between the two reads.
        Assert.Contains(afterSecond, p =>
            p.Author == first.Author &&
            p.Topic == first.Topic &&
            p.Body == first.Body);
        Assert.Equal(afterFirst[0].Body, afterSecond.First(p => p.Author == "alice").Body);
    }

    // Invariant 2: reads remain safe while appends are happening on another thread.
    // We don't assert what gets read — only that the read completes without throwing
    // and returns a coherent view (every returned post is one we actually wrote).
    [Fact]
    public async Task Read_IsSafeUnderConcurrentAppends()
    {
        var ct = TestContext.Current.CancellationToken;
        var board = new FileSystemPostBoard(_root);
        var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                await board.AppendAsync(new Post(
                    Author: "writer",
                    Topic: "noise",
                    Body: "post-" + i++,
                    TimestampUtc: DateTimeOffset.UtcNow), ct);
            }
        }, ct);

        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await foreach (var post in board.ReadAsync(PostFilter.All, ct))
                {
                    // Every observed post must be a real, non-null post — never a torn read.
                    Assert.NotNull(post);
                    Assert.Equal("writer", post.Author);
                    Assert.StartsWith("post-", post.Body);
                }
            }
        }, ct);

        await Task.WhenAll(writer, reader);
    }

    // Invariant 3a: within a single file, posts come back in the order they were written.
    [Fact]
    public async Task Read_PreservesByteOrderWithinASingleFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var board = new FileSystemPostBoard(_root);
        var day = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);

        var posts = Enumerable.Range(0, 5)
            .Select(i => new Post("alice", "session", "body-" + i, day.AddSeconds(i)))
            .ToList();

        foreach (var p in posts)
        {
            await board.AppendAsync(p, ct);
        }

        var read = await Drain(board.ReadAsync(PostFilter.All, ct));

        var bodies = read.Select(p => p.Body).ToList();
        Assert.Equal(new[] { "body-0", "body-1", "body-2", "body-3", "body-4" }, bodies);
    }

    // Invariant 3b: across files, the global order is filename-lexicographic.
    // We force the board to use multiple files by writing posts on different days.
    [Fact]
    public async Task Read_OrdersAcrossFilesLexicographicallyByFilename()
    {
        var ct = TestContext.Current.CancellationToken;
        var board = new FileSystemPostBoard(_root);

        // Two days, two posts each. Day 1 must sort before day 2 regardless of write order.
        var day1 = new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero);

        // Write day 2 first to prove ordering doesn't depend on write order.
        await board.AppendAsync(new Post("alice", "session", "day2-first", day2), ct);
        await board.AppendAsync(new Post("alice", "session", "day2-second", day2.AddMinutes(5)), ct);
        await board.AppendAsync(new Post("alice", "session", "day1-first", day1), ct);
        await board.AppendAsync(new Post("alice", "session", "day1-second", day1.AddMinutes(5)), ct);

        var read = (await Drain(board.ReadAsync(PostFilter.All, ct))).Select(p => p.Body).ToList();

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
    public async Task Read_SkipsCorruptFileWithoutPoisoningOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        var board = new FileSystemPostBoard(_root);

        // Write one good post so a real file exists and the schema is in place.
        var good = new Post("alice", "session", "the good one", new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero));
        await board.AppendAsync(good, ct);

        // Drop a junk file alongside the real one. Filename sorts before any real date file
        // so if the reader fails fast on it, we'd lose the good post too.
        var junkPath = Path.Combine(_root, "0000-00-00.md");
        File.WriteAllText(junkPath, "this is not a valid post file at all\n###\n  garbage  \n");

        var read = await Drain(board.ReadAsync(PostFilter.All, ct));

        Assert.Contains(read, p => p.Body == "the good one");
    }
}
