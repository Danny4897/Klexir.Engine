# Klexir.Engine

Storage engine and database internals experiments for Klexir, built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp) `Result<T>`.

Only `Klexir.Engine.Abstractions` is a public NuGet package (`IPageStore`, `PageId`, `SlotId`, `IWriteAheadLog`, `LockMode`, `TransactionId`).

**Page storage.** `FilePageStore` backs a single file with a configurable page size, allocates pages sequentially, and reads/writes whole pages only (a write must be exactly `PageSize` bytes); reopening an existing file resumes allocation after its last page.

**Buffer pool.** `BufferPool` caches pages from an `IPageStore` in memory with LRU eviction: dirty pages are flushed to the store before their frame is reused, `FlushAsync`/`FlushAllAsync` persist on demand, and disposing the pool flushes everything — it does not own or dispose the underlying store.

**Records.** `SlottedPage` is the variable-length record layout for one page: a 4-byte header (slot count, free-space offset), a slot directory growing forward, records growing backward from the end. `Insert`/`Read`/`Delete` operate on a page's raw bytes; delete zeroes the slot but does not yet reclaim or compact space.

**Index.** `BTree<TKey,TValue>` is a classic in-memory B-Tree (search/insert/delete, node splitting, and the full borrow/merge rebalancing on delete) — not yet page-backed; that integration (mapping nodes onto `SlottedPage`-formatted pages through `BufferPool`) is a later increment. `Insert` rejects a duplicate key rather than upserting. `InOrder()` yields every entry in ascending key order. An internal `ValidateInvariants()` checks node fill-factor bounds, ascending key order, children-count = keys-count+1, and equal leaf depth — exercised by tests that insert/delete hundreds of keys in random order.

**WAL & recovery.** `FileWriteAheadLog` is an append-only redo log (`[pageId][length][bytes]`, fsynced per record); `BufferPool` optionally logs a write before acknowledging it. `WalRecovery.ReplayAsync` replays every record onto an already-open page store, restoring pages that were dirty-but-unflushed at crash time. Simplified — no ARIES-style undo/checkpoint records, redo-only.

**Transactions.** `LockManager` grants page-granularity shared/exclusive locks with a poll-based, caller-timeout acquire (no wait-for-graph deadlock detector — a timed-out acquire means abort-and-retry). `Transaction` enforces 2PL structurally: there is no partial-release API, only `Commit`/`Abort`, which release every lock the transaction holds together.

**Query operators.** `QueryEngine.Scan/Filter/Project/Join` are the relational operator vocabulary a future planner/parser would target — `Scan` reads a `BTree` via `InOrder()`; `Filter`/`Project`/`Join` compose over any sequence. No query text, no parser, no planner yet.

Still open: page-backed B-Tree, page compaction, and a SQL parser/planner.
