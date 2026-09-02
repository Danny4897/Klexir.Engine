# Klexir.Engine

Storage engine and database internals experiments for Klexir, built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp) `Result<T>`.

Only `Klexir.Engine.Abstractions` is a public NuGet package (`IPageStore`, `PageId`).

`FilePageStore` backs a single file with a configurable page size, allocates pages sequentially, and reads/writes whole pages only (a write must be exactly `PageSize` bytes); reopening an existing file resumes allocation after its last page.

`BufferPool` caches pages from an `IPageStore` in memory with LRU eviction: dirty pages are flushed to the store before their frame is reused, `FlushAsync`/`FlushAllAsync` persist on demand, and disposing the pool flushes everything — it does not own or dispose the underlying store.

`SlottedPage` is the variable-length record layout for one page: a 4-byte header (slot count, free-space offset), a slot directory growing forward, records growing backward from the end. `Insert`/`Read`/`Delete` operate on a page's raw bytes; delete zeroes the slot but does not yet reclaim or compact space.

A B-Tree index, WAL/ARIES recovery, 2PL transactions, page compaction and a minimal query engine follow in later increments.
