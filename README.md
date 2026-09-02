# Klexir.Engine

Storage engine and database internals experiments for Klexir, built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp) `Result<T>`.

Only `Klexir.Engine.Abstractions` is a public NuGet package (`IPageStore`, `PageId`).

The first increment is fixed-size page storage: `FilePageStore` backs a single file with a configurable page size, allocates pages sequentially, and reads/writes whole pages only (a write must be exactly `PageSize` bytes). Reopening an existing file resumes allocation after its last page. Access is serialized with a gate; a concurrent buffer pool with eviction is the next increment, followed by slotted-page record layout, a B-Tree index, WAL/ARIES recovery, 2PL transactions, and a minimal query engine.
