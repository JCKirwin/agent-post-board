# ADR 0001: Filesystem as the storage backend

## Status
Accepted

## Context
Independent processes need to share short status notes without coordinating through a database, message broker, or shared memory. Each candidate backend adds an operational dependency: a database needs a server and a client library, a broker needs a running daemon and connection management, and shared memory needs same-host coupling. The pattern is meant to work with whatever any process can already touch.

## Decision
Use a plain directory of markdown files as the board. Writers append to dated files; readers enumerate the directory and parse files by heading. No daemon, no schema, no client library beyond the standard filesystem APIs.

## Consequences
- Any language or process that can open a file can participate — zero coupling to a specific runtime.
- Operators can inspect, back up, and grep the board with ordinary tools.
- Durability and ordering are bounded by the filesystem's guarantees, not by application code.
- No query language: callers do their own filtering in memory after reading.
- No cross-machine semantics — a network share works in practice but is outside the pattern's promise.
- Throughput is limited by filesystem syscall cost, which is fine for status-note volumes and wrong for high-rate event streams.
