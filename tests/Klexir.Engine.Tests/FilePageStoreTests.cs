using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class FilePageStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"klexir-engine-{Guid.NewGuid():N}.page");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task OpenAsync_creates_a_new_file_with_no_pages()
    {
        var opened = await FilePageStore.OpenAsync(_path);
        opened.IsSuccess.Should().BeTrue();

        await using var store = opened.Value;
        var read = await store.ReadPageAsync(new PageId(0));

        read.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AllocatePageAsync_returns_sequential_ids_starting_at_zero()
    {
        var store = (await FilePageStore.OpenAsync(_path)).Value;
        await using (store)
        {
            var first = await store.AllocatePageAsync();
            var second = await store.AllocatePageAsync();

            first.Value.Should().Be(new PageId(0));
            second.Value.Should().Be(new PageId(1));
        }
    }

    [Fact]
    public async Task WritePageAsync_then_ReadPageAsync_roundtrips_the_bytes()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 128)).Value;
        await using (store)
        {
            var pageId = (await store.AllocatePageAsync()).Value;
            var payload = new byte[128];
            Random.Shared.NextBytes(payload);

            var written = await store.WritePageAsync(pageId, payload);
            var read = await store.ReadPageAsync(pageId);

            written.IsSuccess.Should().BeTrue();
            read.Value.Should().Equal(payload);
        }
    }

    [Fact]
    public async Task WritePageAsync_rejects_data_with_the_wrong_length()
    {
        var store = (await FilePageStore.OpenAsync(_path, pageSize: 128)).Value;
        await using (store)
        {
            var pageId = (await store.AllocatePageAsync()).Value;

            var written = await store.WritePageAsync(pageId, new byte[64]);

            written.IsFailure.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ReadPageAsync_fails_for_an_unallocated_page()
    {
        var store = (await FilePageStore.OpenAsync(_path)).Value;
        await using (store)
        {
            var read = await store.ReadPageAsync(new PageId(41));

            read.IsFailure.Should().BeTrue();
        }
    }

    [Fact]
    public async Task OpenAsync_on_an_existing_file_resumes_allocation_after_the_last_page()
    {
        var first = (await FilePageStore.OpenAsync(_path, pageSize: 64)).Value;
        await first.AllocatePageAsync();
        await first.AllocatePageAsync();
        await first.DisposeAsync();

        var reopened = (await FilePageStore.OpenAsync(_path, pageSize: 64)).Value;
        await using (reopened)
        {
            var third = await reopened.AllocatePageAsync();

            third.Value.Should().Be(new PageId(2));
        }
    }
}
