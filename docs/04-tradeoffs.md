# Trade-offs

This section exists so you can decide, before you adopt the pattern, whether a directory of markdown files is the right substrate for your problem — and where it stops being right.

## What you give up

A filesystem-backed board is not a message bus. The board has no acknowledgements, no consumer offsets, no replay protocol. A reader that crashes mid-scan re-scans on restart and re-emits everything it sees; nothing on the board remembers that it was already read. If your workers need exactly-once delivery, or need to know when every consumer has caught up, you want a broker, not this.

You also give up cross-machine reach. The pattern leans on the local filesystem's ordering guarantees — atomic file creation, monotonically advancing modification times within a single OS, byte-order reads inside a single file. SMB shares, sync clients, and most network filesystems weaken at least one of those guarantees. Treat the board as single-host by default; if you need multi-host, you are outside the pattern.

## What you gain

In return you get a substrate every developer on the team already understands. There is no client library to version, no schema migration, no broker to babysit. A human can `cat` a post. A failed process leaves its last post behind as a forensic artifact. Onboarding is "look in this folder."

Append-only writes mean concurrency is mostly a non-issue. Two processes writing two posts cannot corrupt each other because they write to different files. Two processes writing to the same dated file is the only contention point, and the heading-delimited format is forgiving — a torn write at the end of a post is recoverable by the reader, which simply skips to the next heading.

## Alternatives you should consider first

- **A real message broker** (RabbitMQ, NATS, Azure Service Bus). Pick this when you need delivery guarantees, fan-out to many consumers, or cross-machine reach. The operational cost is real but so is what you get.
- **A single append-only log file** (one file, not a directory). Pick this when you have exactly one writer or you can serialize writers behind a lock. Simpler than the directory form, but loses the per-file isolation that keeps a corrupt write from poisoning other days.
- **A small SQLite database**. Pick this when you want SQL-style filtering (by author, by topic, by time window) and you are willing to add a dependency. SQLite handles concurrent readers and a single writer well, and it gives you indexes the directory form cannot.
- **An in-memory channel** (`System.Threading.Channels`). Pick this when the workers live in the same process. The board pattern only earns its keep across process boundaries.

## Non-goals, restated

The pattern explicitly does not try to be:

- A subscription system. There is no push, no webhook, no callback. Readers poll.
- A delivery guarantee. "The post is readable after the write call returns" is the entire contract.
- A synchronization primitive. Two workers coordinating on shared state should use a lock, not a bulletin board.
- A long-term archive. Old dated files accumulate and you are expected to prune them. The pattern says nothing about retention policy.

## When to walk away

If two of these are true, this pattern is the wrong shape for your problem:

- You need to know whether a specific reader saw a specific post.
- Your writers and readers live on different machines.
- Your post volume is high enough that a single day's file grows past a few megabytes.
- You need structured queries — "all posts by author X in the last hour where topic = Y" — at interactive latency.

The pattern is designed for low-volume, single-host, human-readable status sharing between loosely-coupled processes. Inside that envelope it is hard to beat. Outside it, you are bending the substrate against its grain.
