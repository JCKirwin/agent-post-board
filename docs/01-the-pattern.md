# The Pattern

This section explains what the post board *is* before any code shows up. Read it once, and the rest of the repo — the demo, the tests, the trade-off notes — will sit on a foundation you can argue with.

## The one-line shape

A post board is a directory of dated markdown files. Each file holds a stream of heading-delimited posts. Independent processes append posts; any process can read them without asking permission.

That is the whole pattern. Everything else is a consequence.

## Why this exists

You have several long-running processes that need to share short situational updates. Think of three friends recording results from a board-game night: each one sits at a different table, plays a different game, and wants to tell the others what happened — who won, how long it ran, what was weird about the session. There is no host machine. There is no shared database. Spinning up a broker for three console apps would be silly.

The constraints that push you toward a post board look like this:

- The writers do not coordinate with each other.
- The readers do not need to be notified the instant a post lands.
- The data is small, append-only, and human-readable on its own.
- You want any process — including a `cat` from a terminal — to be a valid reader.

When all four hold, a directory is enough. The filesystem already gives you durability, ordering, and concurrent-read safety. You do not need to add infrastructure on top of it.

## What a post is

A post is four things bundled together:

- An **author id** — who wrote it.
- A **topic** — what it is about.
- A **body** — the message itself.
- A **timestamp** — when it was written.

The body can be free-form markdown. The other three are metadata you will want to filter on later.

## What the board is

The board is a directory. Inside it, posts live in dated markdown files — one file per day is a natural choice, but the pattern does not care about the exact slicing. Within a file, each post is a section with a heading. The heading carries enough metadata for a reader to filter without parsing the body.

```
board/
├── 2026-04-21.md
├── 2026-04-22.md
└── 2026-04-23.md
```

Two ordering rules fall out of this layout:

1. **Within a file**, posts are ordered by byte position. The one written earlier appears earlier.
2. **Across files**, posts are ordered by filename. Lexicographic sort on `YYYY-MM-DD.md` gives you chronological order for free.

You will lean on both rules in the demo.

## The invariants

Four rules hold, and the pattern depends on all of them.

- **Posts are never mutated after they are written.** The board only grows. Editing a past post is not part of the contract; if you need to retract something, you append a new post that says so.
- **Reading is safe under concurrent writes.** A reader that opens a file while a writer is mid-append will see a consistent prefix. It may miss the in-flight post, but it will not see a garbled one.
- **Ordering is deterministic.** Within a file, byte order. Across files, filename order. No clocks to reconcile.
- **A corrupt or partial post in one file does not affect reads of other files.** The blast radius of a crashed writer is one file, and usually one post within that file.

If any of these slip, the pattern stops being honest. The trade-off section spells out where they bend.

## What it does not do

Be careful what you ask a post board to be. It is *not*:

- **A delivery system.** There is no acknowledgement. The contract is "the post is readable after the write call returns" — nothing about whether anyone read it.
- **A subscription bus.** No fan-out, no push, no callbacks. Readers poll or tail.
- **A cross-machine sync.** One filesystem, one board. If you need replication, you put a sync tool underneath — that is a different pattern.

People will ask you to add these things. The right answer is usually "use a different tool," not "make the board do it."

## The shape of the demo

This repo includes a small console app that drives all of the above. Three writer processes record their board-game session results — winner, duration, notes. A fourth reader process tails the board and prints a running leaderboard. All four run via `dotnet run -- role=<role>` against a temp-directory board, so the demo is self-contained: clone, run, see it work.

That demo is not the pattern. It is one honest example of the pattern. The next section, `02-architecture.md`, walks through the components that make it work.
