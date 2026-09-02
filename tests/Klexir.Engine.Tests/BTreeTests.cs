using FluentAssertions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class BTreeTests
{
    [Fact]
    public void Insert_then_TryGet_finds_the_value()
    {
        var tree = new BTree<int, string>();

        tree.Insert(5, "five");
        var found = tree.TryGet(5, out var value);

        found.Should().BeTrue();
        value.Should().Be("five");
    }

    [Fact]
    public void TryGet_returns_false_for_a_missing_key()
    {
        var tree = new BTree<int, string>();
        tree.Insert(1, "one");

        tree.TryGet(99, out _).Should().BeFalse();
    }

    [Fact]
    public void Insert_rejects_a_duplicate_key()
    {
        var tree = new BTree<int, string>();
        tree.Insert(1, "one");

        var result = tree.Insert(1, "uno");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Inserting_many_keys_keeps_the_tree_balanced_and_every_key_retrievable()
    {
        var tree = new BTree<int, int>(minDegree: 2);
        var keys = Enumerable.Range(0, 200).OrderBy(_ => Guid.NewGuid()).ToArray();

        foreach (var key in keys)
        {
            tree.Insert(key, key * 10).IsSuccess.Should().BeTrue();
        }

        tree.ValidateInvariants().IsSuccess.Should().BeTrue();
        foreach (var key in keys)
        {
            tree.TryGet(key, out var value).Should().BeTrue();
            value.Should().Be(key * 10);
        }
    }

    [Fact]
    public void Delete_removes_a_leaf_key()
    {
        var tree = new BTree<int, string>();
        tree.Insert(1, "one");
        tree.Insert(2, "two");

        var deleted = tree.Delete(1);

        deleted.IsSuccess.Should().BeTrue();
        tree.TryGet(1, out _).Should().BeFalse();
        tree.TryGet(2, out _).Should().BeTrue();
    }

    [Fact]
    public void Delete_fails_for_a_missing_key()
    {
        var tree = new BTree<int, string>();
        tree.Insert(1, "one");

        tree.Delete(99).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Deleting_many_keys_keeps_the_tree_balanced_and_leaves_the_rest_retrievable()
    {
        var tree = new BTree<int, int>(minDegree: 2);
        var keys = Enumerable.Range(0, 200).ToArray();
        foreach (var key in keys)
        {
            tree.Insert(key, key);
        }

        var toDelete = keys.Where(k => k % 3 == 0).ToArray();
        foreach (var key in toDelete)
        {
            tree.Delete(key).IsSuccess.Should().BeTrue();
        }

        tree.ValidateInvariants().IsSuccess.Should().BeTrue();

        foreach (var key in toDelete)
        {
            tree.TryGet(key, out _).Should().BeFalse();
        }

        foreach (var key in keys.Except(toDelete))
        {
            tree.TryGet(key, out var value).Should().BeTrue();
            value.Should().Be(key);
        }
    }

    [Fact]
    public void Deleting_every_key_leaves_an_empty_but_still_valid_tree()
    {
        var tree = new BTree<int, int>(minDegree: 2);
        var keys = Enumerable.Range(0, 50).ToArray();
        foreach (var key in keys)
        {
            tree.Insert(key, key);
        }

        foreach (var key in keys)
        {
            tree.Delete(key).IsSuccess.Should().BeTrue();
        }

        tree.ValidateInvariants().IsSuccess.Should().BeTrue();
        tree.TryGet(0, out _).Should().BeFalse();
    }
}
