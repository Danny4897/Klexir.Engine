using MonadicSharp;

namespace Klexir.Engine.Abstractions;

/// <summary>Append-only redo log: durably records a page's new bytes before they're flushed to the page store.</summary>
public interface IWriteAheadLog : IAsyncDisposable
{
    Task<Result<Unit>> AppendAsync(PageId pageId, ReadOnlyMemory<byte> pageBytes, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<(PageId PageId, byte[] Bytes)>>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task<Result<Unit>> TruncateAsync(CancellationToken cancellationToken = default);
}
