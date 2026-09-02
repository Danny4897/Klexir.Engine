using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class PagedBTreeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"klexir-engine-pagedbtree-{Guid.NewGuid():N}.page");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task CreateAsync_fails_when_minDegree_does_not_fit_the_page_size()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 32)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 8);
            await using (pool)
            {
                var result = await PagedBTree.CreateAsync(store, pool, minDegree: 4);

                result.IsFailure.Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task InsertAsync_then_TryGetAsync_finds_the_value()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 32);
            await using (pool)
            {
                var tree = (await PagedBTree.CreateAsync(store, pool, minDegree: 2)).Value;

                await tree.InsertAsync(5, 500);
                var found = await tree.TryGetAsync(5);

                found.IsSuccess.Should().BeTrue();
                found.Value.Found.Should().BeTrue();
                found.Value.Value.Should().Be(500);
            }
        }
    }

    [Fact]
    public async Task TryGetAsync_reports_not_found_for_a_missing_key()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 32);
            await using (pool)
            {
                var tree = (await PagedBTree.CreateAsync(store, pool, minDegree: 2)).Value;

                var found = await tree.TryGetAsync(999);

                found.IsSuccess.Should().BeTrue();
                found.Value.Found.Should().BeFalse();
            }
        }
    }

    [Fact]
    public async Task InsertAsync_rejects_a_duplicate_key()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 32);
            await using (pool)
            {
                var tree = (await PagedBTree.CreateAsync(store, pool, minDegree: 2)).Value;
                await tree.InsertAsync(1, 100);

                var result = await tree.InsertAsync(1, 999);

                result.IsFailure.Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task Inserting_many_keys_forces_leaf_internal_and_root_splits_and_every_key_stays_retrievable()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 64);
            await using (pool)
            {
                var tree = (await PagedBTree.CreateAsync(store, pool, minDegree: 2)).Value;

                var keys = Enumerable.Range(0, 200).OrderBy(_ => Guid.NewGuid()).ToArray();
                foreach (var key in keys)
                {
                    (await tree.InsertAsync(key, key * 10L)).IsSuccess.Should().BeTrue();
                }

                foreach (var key in keys)
                {
                    var found = await tree.TryGetAsync(key);
                    found.Value.Found.Should().BeTrue();
                    found.Value.Value.Should().Be(key * 10L);
                }

                var all = await tree.CollectAllAsync();
                all.Value.Should().HaveCount(200);
            }
        }
    }

    [Fact]
    public async Task The_tree_survives_reopening_the_same_file()
    {
        PageId rootPageId;
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (store)
        {
            var pool = new BufferPool(store, capacity: 32);
            await using (pool)
            {
                var tree = (await PagedBTree.CreateAsync(store, pool, minDegree: 2)).Value;
                foreach (var key in Enumerable.Range(0, 30))
                {
                    await tree.InsertAsync(key, key * 100L);
                }

                rootPageId = tree.RootPageId;
            }
        }

        // Simulates a process restart: reopen the same file and rebuild the tree handle from the remembered root.
        var reopenedStore = (await FilePageStore.OpenAsync(_path, pageSize: 256)).Value;
        await using (reopenedStore)
        {
            var reopenedPool = new BufferPool(reopenedStore, capacity: 32);
            await using (reopenedPool)
            {
                var reopenedTree = PagedBTree.Open(reopenedStore, reopenedPool, minDegree: 2, rootPageId);

                foreach (var key in Enumerable.Range(0, 30))
                {
                    var found = await reopenedTree.TryGetAsync(key);
                    found.Value.Found.Should().BeTrue();
                    found.Value.Value.Should().Be(key * 100L);
                }
            }
        }
    }
}
