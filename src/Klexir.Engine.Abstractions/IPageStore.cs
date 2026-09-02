using MonadicSharp;

namespace Klexir.Engine.Abstractions;

/// <summary>Fixed-size page storage backed by a file. Every page is exactly <see cref="PageSize"/> bytes.</summary>
public interface IPageStore : IAsyncDisposable
{
    int PageSize { get; }

    /// <summary>Grows the store by one page and returns its id. The new page's bytes are zeroed.</summary>
    Task<Result<PageId>> AllocatePageAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a full page. Fails for a page id that was never allocated.</summary>
    Task<Result<byte[]>> ReadPageAsync(PageId pageId, CancellationToken cancellationToken = default);

    /// <summary>Overwrites a full page. <paramref name="data"/> must be exactly <see cref="PageSize"/> bytes.</summary>
    Task<Result<Unit>> WritePageAsync(PageId pageId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
