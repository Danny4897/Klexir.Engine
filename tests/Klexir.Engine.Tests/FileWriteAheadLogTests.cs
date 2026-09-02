using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class FileWriteAheadLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"klexir-engine-wal-{Guid.NewGuid():N}.wal");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task AppendAsync_then_ReadAllAsync_returns_records_in_append_order()
    {
        var wal = (await FileWriteAheadLog.OpenAsync(_path)).Value;
        await using (wal)
        {
            await wal.AppendAsync(new PageId(1), new byte[] { 1, 2, 3 });
            await wal.AppendAsync(new PageId(2), new byte[] { 4, 5 });

            var records = await wal.ReadAllAsync();

            records.IsSuccess.Should().BeTrue();
            records.Value.Should().HaveCount(2);
            records.Value[0].PageId.Should().Be(new PageId(1));
            records.Value[0].Bytes.Should().Equal(1, 2, 3);
            records.Value[1].PageId.Should().Be(new PageId(2));
            records.Value[1].Bytes.Should().Equal(4, 5);
        }
    }

    [Fact]
    public async Task TruncateAsync_clears_the_log()
    {
        var wal = (await FileWriteAheadLog.OpenAsync(_path)).Value;
        await using (wal)
        {
            await wal.AppendAsync(new PageId(1), new byte[] { 1 });
            await wal.TruncateAsync();

            var records = await wal.ReadAllAsync();

            records.Value.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ReadAllAsync_on_an_empty_log_returns_an_empty_list()
    {
        var wal = (await FileWriteAheadLog.OpenAsync(_path)).Value;
        await using (wal)
        {
            var records = await wal.ReadAllAsync();

            records.Value.Should().BeEmpty();
        }
    }
}
