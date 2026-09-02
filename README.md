# Klexir.Engine

[![CI](https://github.com/Danny4897/Klexir.Engine/actions/workflows/ci.yml/badge.svg)](https://github.com/Danny4897/Klexir.Engine/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-7c3aed.svg)](https://danny4897.github.io/Klexir.Engine/)

Storage engine and database internals, built from the file up: pages, a buffer pool, a B-Tree, a write-ahead log, 2PL transactions, and the relational operators a query planner would sit on top of. Built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/) `Result<T>` — a corrupt page, a lock timeout, or a truncated WAL record all come back as a failed `Result`, never an exception.

> **Status: public research repo, not yet published to NuGet.** These pieces are not yet wired into one cohesive database — each is independently built and tested, matching how a real engine's internals are usually studied. Reference the project directly until/unless it's published.

---

## Quick example — durable pages and an index

```csharp
await using var store = (await FilePageStore.OpenAsync("data.klx", pageSize: 4096)).Value;
await using var wal = (await FileWriteAheadLog.OpenAsync("data.wal")).Value;
await using var pool = new BufferPool(store, capacity: 256, wal); // writes are logged before they're acknowledged

var pageId = (await store.AllocatePageAsync()).Value;
await pool.WriteAsync(pageId, someRecordBytes);
// ... process exits before pool.FlushAllAsync() ever runs ...

// On restart: reopen the same files and replay whatever the WAL saw but the store never got.
var recoveredStore = (await FilePageStore.OpenAsync("data.klx", pageSize: 4096)).Value;
var recoveredWal = (await FileWriteAheadLog.OpenAsync("data.wal")).Value;
await WalRecovery.ReplayAsync(recoveredWal, recoveredStore); // → Result<int>, number of pages restored
```

```csharp
var customers = new BTree<int, Customer>();
customers.Insert(1, new Customer(1, "Alice", "Rome"));

var romans = QueryEngine.Filter(QueryEngine.Scan(customers), c => c.City == "Rome");
```

```csharp
// The page-backed index: nodes live in pages, not in memory — genuinely durable across a restart.
var index = (await PagedBTree.CreateAsync(store, pool, minDegree: 32)).Value;
await index.InsertAsync(key: 42, value: 1000);
var rootPageId = index.RootPageId; // remember this — you'll need it to reopen the tree later

// ... process restarts; reopen the same file/pool ...
var reopened = PagedBTree.Open(store, pool, minDegree: 32, rootPageId);
Result<(bool Found, long Value)> found = await reopened.TryGetAsync(42); // Success((true, 1000))
```

---

## What's in the box

| Layer | API | Notes |
|---|---|---|
| Page storage | `FilePageStore` | Fixed-size pages, sequential allocation, whole-page reads/writes only |
| Buffer pool | `BufferPool` | LRU eviction over an `IPageStore`; never owns/disposes the store |
| Records | `SlottedPage` | Variable-length records in a page: slot directory + backward-growing data |
| Index (in-memory) | `BTree<TKey,TValue>` | Full search/insert/delete with borrow/merge rebalancing; `InOrder()` for a sorted scan |
| Index (page-backed) | `PagedBTree` | `long`-keyed B+Tree — internal nodes hold only routing keys/child `PageId`s, values live only in leaves; nodes are pages, read/written through `BufferPool`. Insert-only so far |
| WAL & recovery | `FileWriteAheadLog`, `WalRecovery` | Redo-only log; `BufferPool` can log a write before acknowledging it |
| Transactions | `LockManager`, `Transaction` | Page-granularity shared/exclusive locks, 2PL enforced structurally (no partial-release API) |
| Query operators | `QueryEngine.Scan/Filter/Project/Join` | The operator vocabulary a planner would target — no query text yet |

## Not there yet

- `PagedBTree` has no delete — the in-memory `BTree` does, if you need that today
- `SlottedPage.Delete` doesn't reclaim or compact space
- No wait-for-graph deadlock detector — a `LockManager` timeout just means "abort and retry"
- No SQL text, parser, or planner — `QueryEngine` is the operator layer underneath where one would go

## Requirements

.NET 8 SDK + [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/) `Result<T>`.
