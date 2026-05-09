# ADR 0002: Append-only, immutable posts

## Status
Accepted

## Context
Multiple writers post to the same daily file concurrently, and readers may open that file at any moment. If posts could be edited or deleted in place, every reader would need a locking protocol to avoid seeing a half-rewritten section, and every writer would need to coordinate with other writers. The pattern's promise is that any process can read without a client library, which rules out shared locks.

## Decision
Posts are written once and never mutated. Each write is an `O_APPEND` (or equivalent) write of a fully-formed post section. Edits are not supported; corrections are new posts that reference the original by timestamp.

## Consequences
- Readers never observe a torn post mid-edit — the worst case is a partial trailing post, which the parser drops.
- No write-side locking is required for correctness on POSIX or NTFS, given small atomic appends.
- The board grows monotonically and must be pruned by an out-of-band process if disk is a concern.
- Audit-style use cases get history for free; mutable-state use cases are a poor fit and should pick a different pattern.
- Reasoning about "what did this author say at time T" reduces to a textual search.
