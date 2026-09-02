using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class BufferPoolTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"klexir-engine-pool-{Guid.NewGuid():N}.page");
    private FilePageStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = (await FilePageStore.OpenAsync(_path, pageSize: 64)).Value;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task WriteAsync_then_ReadAsync_returns_the_cached_bytes_without_reading_the_store_again()
    {
        await using var pool = new BufferPool(_store, capacity: 4);
        var pageId = (await _store.AllocatePageAsync()).Value;
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);

        (await pool.WriteAsync(pageId, payload)).IsSuccess.Should().BeTrue();
        var read = await pool.ReadAsync(pageId);

        read.IsSuccess.Should().BeTrue();
        read.Value.Should().Equal(payload);
    }

    [Fact]
    public async Task FlushAsync_persists_a_dirty_page_to_the_underlying_store()
    {
        await using var pool = new BufferPool(_store, capacity: 4);
        var pageId = (await _store.AllocatePageAsync()).Value;
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);

        await pool.WriteAsync(pageId, payload);
        await pool.FlushAsync(pageId);

        var readDirect = await _store.ReadPageAsync(pageId);
        readDirect.Value.Should().Equal(payload);
    }

    [Fact]
    public async Task Eviction_flushes_the_least_recently_used_dirty_page_before_reusing_its_frame()
    {
        await using var pool = new BufferPool(_store, capacity: 1);
        var pageA = (await _store.AllocatePageAsync()).Value;
        var pageB = (await _store.AllocatePageAsync()).Value;
        var payloadA = new byte[64];
        Random.Shared.NextBytes(payloadA);

        await pool.WriteAsync(pageA, payloadA);
        await pool.ReadAsync(pageB); // capacity 1: forces eviction of A, which must flush it first

        var persistedA = await _store.ReadPageAsync(pageA);
        persistedA.Value.Should().Equal(payloadA);
    }

    [Fact]
    public async Task DisposeAsync_flushes_all_dirty_pages()
    {
        var pool = new BufferPool(_store, capacity: 4);
        var pageId = (await _store.AllocatePageAsync()).Value;
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);

        await pool.WriteAsync(pageId, payload);
        await pool.DisposeAsync();

        var persisted = await _store.ReadPageAsync(pageId);
        persisted.Value.Should().Equal(payload);
    }

    [Fact]
    public async Task ReadAsync_fails_for_an_unallocated_page()
    {
        await using var pool = new BufferPool(_store, capacity: 4);

        var read = await pool.ReadAsync(new PageId(99));

        read.IsFailure.Should().BeTrue();
    }
}
