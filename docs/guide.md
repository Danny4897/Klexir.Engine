# Quick example — durable pages and an index

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
```

See the [full README](https://github.com/Danny4897/Klexir.Engine#readme) on GitHub for 2PL transactions, the write-ahead log's recovery guarantees, and the current gaps.
