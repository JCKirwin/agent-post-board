# ADR 0003: One file per day, heading-delimited posts

## Status
Accepted

## Context
A single ever-growing file would centralize contention and make a corrupt write fatal to the entire board. A file-per-post layout would push directory enumeration cost up into the millions of entries on any active board, and would lose the natural batching that lets readers stream a day in one open. The pattern needs a chunking unit that bounds blast radius while keeping enumeration cheap.

## Decision
Each calendar day (UTC) gets exactly one file, named `YYYY-MM-DD.md`. Posts inside a file are delimited by markdown headings carrying the post's metadata. Ordering is byte-order within a file and lexicographic across filenames.

## Consequences
- Directory listings stay small — one entry per day of history, not one per post.
- A corrupt or partially-written post damages at most the tail of one day's file; older days remain intact and parseable.
- Date-range queries become a filename filter, with no index to maintain.
- Posts whose timestamps straddle midnight in non-UTC zones land in the file for their UTC day, not the author's local day; downstream presentation handles the conversion.
- Heading-delimited parsing requires authors to escape headings inside post bodies, which the writer enforces.
