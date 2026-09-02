using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>File-backed fixed-size page store. All access is serialized; concurrent I/O is a later increment (buffer pool).</summary>
public sealed class FilePageStore : IPageStore
{
    private readonly FileStream _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private uint _pageCount;

    public int PageSize { get; }

    public static async Task<Result<FilePageStore>> OpenAsync(
        string path, int pageSize = 4096, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            return Result<FilePageStore>.Failure(Error.Validation("Page size must be positive.", nameof(pageSize)));
        }

        return await Try.ExecuteAsync(async () =>
        {
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, useAsync: true);
            var pageCount = (uint)(file.Length / pageSize);
            return new FilePageStore(file, pageSize, pageCount);
        }).ConfigureAwait(false);
    }

    private FilePageStore(FileStream file, int pageSize, uint pageCount)
    {
        _file = file;
        PageSize = pageSize;
        _pageCount = pageCount;
    }

    public async Task<Result<PageId>> AllocatePageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Try.ExecuteAsync(async () =>
            {
                var pageId = new PageId(_pageCount);
                _file.SetLength((long)(_pageCount + 1) * PageSize);
                await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
                _pageCount++;
                return pageId;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<byte[]>> ReadPageAsync(PageId pageId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (pageId.Value >= _pageCount)
            {
                return Result<byte[]>.Failure(Error.NotFound("Page", pageId.ToString()));
            }

            return await Try.ExecuteAsync(async () =>
            {
                var buffer = new byte[PageSize];
                _file.Seek((long)pageId.Value * PageSize, SeekOrigin.Begin);
                var read = await _file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read != PageSize)
                {
                    throw new IOException($"Short read on page {pageId}: expected {PageSize} bytes, got {read}.");
                }

                return buffer;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> WritePageAsync(PageId pageId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length != PageSize)
        {
            return Result<Unit>.Failure(
                Error.Validation($"Data must be exactly {PageSize} bytes, got {data.Length}.", nameof(data)));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (pageId.Value >= _pageCount)
            {
                return Result<Unit>.Failure(Error.NotFound("Page", pageId.ToString()));
            }

            return await Try.ExecuteAsync(async () =>
            {
                _file.Seek((long)pageId.Value * PageSize, SeekOrigin.Begin);
                await _file.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
                return Unit.Value;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        await _file.DisposeAsync().ConfigureAwait(false);
    }
}
