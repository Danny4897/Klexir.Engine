using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class LockManagerTests
{
    [Fact]
    public async Task AcquireAsync_grants_a_shared_lock_immediately_when_uncontended()
    {
        var manager = new LockManager();
        var txn = new Transaction(manager);

        var result = await txn.AcquireAsync(new PageId(1), LockMode.Shared);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_allows_multiple_shared_locks_on_the_same_resource()
    {
        var manager = new LockManager();
        var txn1 = new Transaction(manager);
        var txn2 = new Transaction(manager);
        var page = new PageId(1);

        var first = await txn1.AcquireAsync(page, LockMode.Shared);
        var second = await txn2.AcquireAsync(page, LockMode.Shared, TimeSpan.FromMilliseconds(200));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_times_out_requesting_exclusive_while_another_transaction_holds_shared()
    {
        var manager = new LockManager();
        var holder = new Transaction(manager);
        var waiter = new Transaction(manager);
        var page = new PageId(1);
        await holder.AcquireAsync(page, LockMode.Shared);

        var result = await waiter.AcquireAsync(page, LockMode.Exclusive, TimeSpan.FromMilliseconds(150));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Commit_releases_all_locks_so_a_waiting_transaction_can_then_acquire()
    {
        var manager = new LockManager();
        var holder = new Transaction(manager);
        var waiter = new Transaction(manager);
        var page = new PageId(1);
        await holder.AcquireAsync(page, LockMode.Exclusive);

        var waiting = waiter.AcquireAsync(page, LockMode.Exclusive, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        holder.Commit();

        var result = await waiting;
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task The_same_transaction_can_upgrade_from_shared_to_exclusive_when_it_is_the_sole_holder()
    {
        var manager = new LockManager();
        var txn = new Transaction(manager);
        var page = new PageId(1);
        await txn.AcquireAsync(page, LockMode.Shared);

        var upgraded = await txn.AcquireAsync(page, LockMode.Exclusive, TimeSpan.FromMilliseconds(200));

        upgraded.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_fails_after_the_transaction_has_committed()
    {
        var manager = new LockManager();
        var txn = new Transaction(manager);
        txn.Commit();

        var result = await txn.AcquireAsync(new PageId(1), LockMode.Shared);

        result.IsFailure.Should().BeTrue();
    }
}
