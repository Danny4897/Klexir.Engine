using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>Redo recovery: replays every WAL record onto an already-opened page store. Pages a record references must already be allocated in that store (allocation itself is durable the moment it happens — see <see cref="FilePageStore"/>).</summary>
public static class WalRecovery
{
    public static async Task<Result<int>> ReplayAsync(IWriteAheadLog wal, IPageStore store, CancellationToken cancellationToken = default)
    {
        var records = await wal.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        if (records.IsFailure)
        {
            return Result<int>.Failure(records.Error);
        }

        var applied = 0;
        foreach (var (pageId, bytes) in records.Value)
        {
            var written = await store.WritePageAsync(pageId, bytes, cancellationToken).ConfigureAwait(false);
            if (written.IsFailure)
            {
                return Result<int>.Failure(written.Error);
            }

            applied++;
        }

        return Result<int>.Success(applied);
    }
}
