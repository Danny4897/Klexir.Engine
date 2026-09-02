using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// Page-granularity shared/exclusive lock table for 2PL transactions. Waiting is poll-based with a caller-supplied
/// timeout rather than a wait queue, and there is no wait-for-graph deadlock detector — a transaction that can't
/// get a lock within its timeout simply fails, which the caller should treat as "abort and retry."
/// </summary>
public sealed class LockManager
{
    private readonly Dictionary<PageId, LockState> _locks = [];
    private readonly object _gate = new();

    public async Task<Result<Unit>> AcquireAsync(
        TransactionId transactionId, PageId resource, LockMode mode, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var deadline = timeout is { } t ? DateTime.UtcNow + t : (DateTime?)null;

        while (true)
        {
            lock (_gate)
            {
                if (TryAcquireLocked(transactionId, resource, mode))
                {
                    return Result<Unit>.Success(Unit.Value);
                }
            }

            if (deadline is { } d && DateTime.UtcNow >= d)
            {
                return Result<Unit>.Failure(Error.Create($"Timed out waiting for a {mode} lock on page {resource}."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases every lock the transaction holds, all at once — the shrinking phase. Never call this and then acquire again for the same transaction; that would violate 2PL.</summary>
    public void ReleaseAll(TransactionId transactionId)
    {
        lock (_gate)
        {
            foreach (var (resource, state) in _locks.ToArray())
            {
                state.Holders.Remove(transactionId);
                if (state.Holders.Count == 0)
                {
                    _locks.Remove(resource);
                }
            }
        }
    }

    private bool TryAcquireLocked(TransactionId transactionId, PageId resource, LockMode mode)
    {
        if (!_locks.TryGetValue(resource, out var state))
        {
            _locks[resource] = new LockState(mode, [transactionId]);
            return true;
        }

        if (state.Holders.Contains(transactionId))
        {
            if (mode == LockMode.Exclusive && state.Mode == LockMode.Shared && state.Holders.Count == 1)
            {
                state.Mode = LockMode.Exclusive;
            }

            return mode == LockMode.Shared || state.Mode == LockMode.Exclusive;
        }

        if (mode == LockMode.Shared && state.Mode == LockMode.Shared)
        {
            state.Holders.Add(transactionId);
            return true;
        }

        return false;
    }

    private sealed class LockState(LockMode mode, HashSet<TransactionId> holders)
    {
        public LockMode Mode { get; set; } = mode;

        public HashSet<TransactionId> Holders { get; } = holders;
    }
}
