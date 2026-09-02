using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// In-memory page cache over an <see cref="IPageStore"/> with LRU eviction. Does not own the store's lifetime —
/// disposing the pool flushes dirty pages but never disposes the underlying store. When <paramref name="wal"/> is
/// configured, every write is durably logged before it's acknowledged, so a crash before the next flush can still
/// be replayed via <see cref="WalRecovery"/>.
/// </summary>
public sealed class BufferPool(IPageStore store, int capacity, IWriteAheadLog? wal = null) : IAsyncDisposable
{
    private readonly Dictionary<PageId, Frame> _frames = [];
    private readonly LinkedList<PageId> _lruMostRecentFirst = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<byte[]>> ReadAsync(PageId pageId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(pageId, cancellationToken).ConfigureAwait(false);
            if (loaded.IsFailure)
            {
                return Result<byte[]>.Failure(loaded.Error);
            }

            Touch(pageId);
            return Result<byte[]>.Success((byte[])loaded.Value.Data.Clone());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> WriteAsync(PageId pageId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(pageId, cancellationToken).ConfigureAwait(false);
            if (loaded.IsFailure)
            {
                return Result<Unit>.Failure(loaded.Error);
            }

            if (wal is not null)
            {
                var appended = await wal.AppendAsync(pageId, data, cancellationToken).ConfigureAwait(false);
                if (appended.IsFailure)
                {
                    return appended;
                }
            }

            data.CopyTo(loaded.Value.Data);
            loaded.Value.IsDirty = true;
            Touch(pageId);
            return Result<Unit>.Success(Unit.Value);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> FlushAsync(PageId pageId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FlushFrameAsync(pageId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Unit>> FlushAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var pageId in _frames.Keys.ToArray())
            {
                var flushed = await FlushFrameAsync(pageId, cancellationToken).ConfigureAwait(false);
                if (flushed.IsFailure)
                {
                    return flushed;
                }
            }

            return Result<Unit>.Success(Unit.Value);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAllAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task<Result<Unit>> FlushFrameAsync(PageId pageId, CancellationToken cancellationToken)
    {
        if (!_frames.TryGetValue(pageId, out var frame) || !frame.IsDirty)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var written = await store.WritePageAsync(pageId, frame.Data, cancellationToken).ConfigureAwait(false);
        if (written.IsSuccess)
        {
            frame.IsDirty = false;
        }

        return written;
    }

    private async Task<Result<Frame>> LoadAsync(PageId pageId, CancellationToken cancellationToken)
    {
        if (_frames.TryGetValue(pageId, out var cached))
        {
            return Result<Frame>.Success(cached);
        }

        if (_frames.Count >= capacity && _lruMostRecentFirst.Last is { } victim)
        {
            var evicted = await FlushFrameAsync(victim.Value, cancellationToken).ConfigureAwait(false);
            if (evicted.IsFailure)
            {
                return Result<Frame>.Failure(evicted.Error);
            }

            _frames.Remove(victim.Value);
            _lruMostRecentFirst.RemoveLast();
        }

        var read = await store.ReadPageAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return Result<Frame>.Failure(read.Error);
        }

        var frame = new Frame(read.Value);
        _frames[pageId] = frame;
        _lruMostRecentFirst.AddFirst(pageId);
        return Result<Frame>.Success(frame);
    }

    private void Touch(PageId pageId)
    {
        _lruMostRecentFirst.Remove(pageId);
        _lruMostRecentFirst.AddFirst(pageId);
    }

    private sealed class Frame(byte[] data)
    {
        public byte[] Data { get; } = data;

        public bool IsDirty { get; set; }
    }
}
