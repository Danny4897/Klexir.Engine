using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// A 2PL transaction: acquires locks (growing phase) via <see cref="AcquireAsync"/>, then releases every lock it
/// holds together at <see cref="Commit"/> or <see cref="Abort"/> (shrinking phase). There is no partial-release
/// API, so a caller cannot accidentally violate two-phase locking.
/// </summary>
public sealed class Transaction(LockManager lockManager)
{
    private bool _completed;

    public TransactionId Id { get; } = TransactionId.NewId();

    public Task<Result<Unit>> AcquireAsync(
        PageId resource, LockMode mode, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return Task.FromResult(Result<Unit>.Failure(Error.Create("Transaction already committed or aborted.")));
        }

        return lockManager.AcquireAsync(Id, resource, mode, timeout, cancellationToken);
    }

    public void Commit()
    {
        _completed = true;
        lockManager.ReleaseAll(Id);
    }

    public void Abort()
    {
        _completed = true;
        lockManager.ReleaseAll(Id);
    }
}
