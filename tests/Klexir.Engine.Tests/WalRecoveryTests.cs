using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class WalRecoveryTests : IDisposable
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"klexir-engine-recover-{Guid.NewGuid():N}.page");
    private readonly string _walPath = Path.Combine(Path.GetTempPath(), $"klexir-engine-recover-{Guid.NewGuid():N}.wal");

    public void Dispose()
    {
        foreach (var path in new[] { _storePath, _walPath })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ReplayAsync_restores_a_page_that_was_written_via_the_buffer_pool_but_never_flushed_before_a_simulated_crash()
    {
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);
        PageId pageId;

        // Before the "crash": allocate a page, write it through a WAL-backed buffer pool, but never flush or
        // dispose the pool — so the on-disk page store still has the page's original (zeroed) bytes.
        {
            var store = (await FilePageStore.OpenAsync(_storePath, pageSize: 64)).Value;
            var wal = (await FileWriteAheadLog.OpenAsync(_walPath)).Value;
            pageId = (await store.AllocatePageAsync()).Value;

            var pool = new BufferPool(store, capacity: 4, wal);
            await pool.WriteAsync(pageId, payload);

            await wal.DisposeAsync();
            await store.DisposeAsync();
        }

        // "After the crash": reopen the same files and replay the WAL.
        var recoveredStore = (await FilePageStore.OpenAsync(_storePath, pageSize: 64)).Value;
        await using (recoveredStore)
        {
            var recoveredWal = (await FileWriteAheadLog.OpenAsync(_walPath)).Value;
            await using (recoveredWal)
            {
                var replayed = await WalRecovery.ReplayAsync(recoveredWal, recoveredStore);

                replayed.IsSuccess.Should().BeTrue();
                replayed.Value.Should().Be(1);

                var recovered = await recoveredStore.ReadPageAsync(pageId);
                recovered.Value.Should().Equal(payload);
            }
        }
    }

    [Fact]
    public async Task ReplayAsync_on_an_empty_wal_applies_nothing()
    {
        var store = (await FilePageStore.OpenAsync(_storePath, pageSize: 64)).Value;
        await using (store)
        {
            var wal = (await FileWriteAheadLog.OpenAsync(_walPath)).Value;
            await using (wal)
            {
                var replayed = await WalRecovery.ReplayAsync(wal, store);

                replayed.Value.Should().Be(0);
            }
        }
    }
}
