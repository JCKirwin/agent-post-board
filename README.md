# Agent Post Board

A file-backed, append-only bulletin board for loosely-coupled processes that need to share short status notes without a database, broker, or shared memory. Each post is a heading-delimited section inside a dated markdown file, so any process can read the board with nothing more than the standard library.

## What you'll learn

- How to use a directory of dated markdown files as an ordered, append-only log.
- How to keep reads safe under concurrent writes without locks or coordination.
- How filename lexicographic order plus byte-order within a file gives you "good enough" ordering for situational updates.
- How to model authors, topics, and timestamps as a post without inventing a schema.
- How to run several independent writer processes and a tailing reader process side by side from a single console app.

## Quick Start

The demo simulates a board-game night: three friends each post their session results to a shared board, and a fourth process tails the board and prints a running leaderboard. The board lives in a temp directory, so the demo is self-contained and leaves nothing behind.

### 1. Clone

```bash
git clone https://github.com/JCKirwin/agent-post-board.git
cd agent-post-board
```

### 2. Build

```bash
dotnet build
```

The demo targets `net10.0` with nullable reference types enabled and warnings treated as errors. A clean build is the green light.

### 3. Run the demo

Open four terminals in the repo root. In each one, run the demo with a different role. They all point at the same temp-dir board.

```bash
# Terminal 1 — first player
dotnet run --project src/AgentPostBoard.Demo -- role=alex

# Terminal 2 — second player
dotnet run --project src/AgentPostBoard.Demo -- role=blair

# Terminal 3 — third player
dotnet run --project src/AgentPostBoard.Demo -- role=casey

# Terminal 4 — leaderboard reader
dotnet run --project src/AgentPostBoard.Demo -- role=reader
```

The three player processes post session results (winner, duration, notes) every few seconds. The reader process tails the board directory and prints an updated leaderboard each time a new post lands.

### 4. Run the tests

```bash
dotnet test
```

The unit tests cover the append-only invariant, concurrent-read safety, and ordering rules. They do not require the bundled `samples/demo-data.json`; the sample file is there for the demo and for your own experiments.
