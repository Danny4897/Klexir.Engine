using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// In-memory B-Tree keyed by <typeparamref name="TKey"/>. Not yet page-backed (that integration — mapping nodes
/// onto <see cref="SlottedPage"/>-formatted pages via <see cref="BufferPool"/> — is a later increment); this
/// establishes the search/insert/delete algorithm and its invariants first.
/// </summary>
/// <remarks>
/// <paramref name="minDegree"/> (t) bounds node size: every node but the root holds between t-1 and 2t-1 keys, and
/// an internal node with k keys has exactly k+1 children. That keeps the tree height O(log n) and every leaf at
/// the same depth.
/// </remarks>
public sealed class BTree<TKey, TValue>(int minDegree = 2) where TKey : IComparable<TKey>
{
    private readonly int _minDegree = minDegree >= 2
        ? minDegree
        : throw new ArgumentOutOfRangeException(nameof(minDegree), minDegree, "Minimum degree must be at least 2.");

    private Node _root = new(isLeaf: true);

    public bool TryGet(TKey key, out TValue value) => TryGet(_root, key, out value);

    /// <summary>All entries in ascending key order — the storage-engine "table scan" primitive.</summary>
    public IEnumerable<(TKey Key, TValue Value)> InOrder() => InOrder(_root);

    private static IEnumerable<(TKey Key, TValue Value)> InOrder(Node node)
    {
        for (var i = 0; i < node.Keys.Count; i++)
        {
            if (!node.IsLeaf)
            {
                foreach (var entry in InOrder(node.Children[i]))
                {
                    yield return entry;
                }
            }

            yield return (node.Keys[i], node.Values[i]);
        }

        if (!node.IsLeaf)
        {
            foreach (var entry in InOrder(node.Children[^1]))
            {
                yield return entry;
            }
        }
    }

    public Result<Unit> Insert(TKey key, TValue value)
    {
        if (TryGet(key, out _))
        {
            return Result<Unit>.Failure(Error.Create($"Key '{key}' already exists."));
        }

        if (_root.Keys.Count == (2 * _minDegree) - 1)
        {
            var newRoot = new Node(isLeaf: false);
            newRoot.Children.Add(_root);
            SplitChild(newRoot, 0);
            _root = newRoot;
        }

        InsertNonFull(_root, key, value);
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Delete(TKey key)
    {
        if (!TryGet(key, out _))
        {
            return Result<Unit>.Failure(Error.NotFound("Key", key?.ToString() ?? "null"));
        }

        Remove(_root, key);

        if (_root.Keys.Count == 0 && !_root.IsLeaf)
        {
            _root = _root.Children[0];
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>Checks node fill-factor bounds, ascending key order, children-count = keys-count+1, and equal leaf depth.</summary>
    internal Result<Unit> ValidateInvariants()
    {
        var leafDepths = new List<int>();
        var result = ValidateNode(_root, isRoot: true, depth: 0, leafDepths);
        if (result.IsFailure)
        {
            return result;
        }

        return leafDepths.Distinct().Count() <= 1
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure(Error.Create("Leaves are not all at the same depth."));
    }

    private Result<Unit> ValidateNode(Node node, bool isRoot, int depth, List<int> leafDepths)
    {
        if (!isRoot && (node.Keys.Count < _minDegree - 1 || node.Keys.Count > (2 * _minDegree) - 1))
        {
            return Result<Unit>.Failure(
                Error.Create($"Node has {node.Keys.Count} keys, outside [{_minDegree - 1}, {(2 * _minDegree) - 1}]."));
        }

        for (var i = 1; i < node.Keys.Count; i++)
        {
            if (node.Keys[i - 1].CompareTo(node.Keys[i]) >= 0)
            {
                return Result<Unit>.Failure(Error.Create("Keys within a node are not strictly ascending."));
            }
        }

        if (node.IsLeaf)
        {
            leafDepths.Add(depth);
            return Result<Unit>.Success(Unit.Value);
        }

        if (node.Children.Count != node.Keys.Count + 1)
        {
            return Result<Unit>.Failure(
                Error.Create($"Internal node has {node.Children.Count} children but {node.Keys.Count} keys."));
        }

        foreach (var child in node.Children)
        {
            var childResult = ValidateNode(child, isRoot: false, depth + 1, leafDepths);
            if (childResult.IsFailure)
            {
                return childResult;
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private bool TryGet(Node node, TKey key, out TValue value)
    {
        var i = FindKeyIndex(node, key);
        if (i < node.Keys.Count && key.CompareTo(node.Keys[i]) == 0)
        {
            value = node.Values[i];
            return true;
        }

        if (node.IsLeaf)
        {
            value = default!;
            return false;
        }

        return TryGet(node.Children[i], key, out value);
    }

    private void SplitChild(Node parent, int index)
    {
        var t = _minDegree;
        var child = parent.Children[index];
        var sibling = new Node(child.IsLeaf);

        sibling.Keys.AddRange(child.Keys.GetRange(t, t - 1));
        sibling.Values.AddRange(child.Values.GetRange(t, t - 1));

        if (!child.IsLeaf)
        {
            sibling.Children.AddRange(child.Children.GetRange(t, t));
            child.Children.RemoveRange(t, t);
        }

        var medianKey = child.Keys[t - 1];
        var medianValue = child.Values[t - 1];

        child.Keys.RemoveRange(t - 1, t);
        child.Values.RemoveRange(t - 1, t);

        parent.Children.Insert(index + 1, sibling);
        parent.Keys.Insert(index, medianKey);
        parent.Values.Insert(index, medianValue);
    }

    private void InsertNonFull(Node node, TKey key, TValue value)
    {
        var i = node.Keys.Count - 1;

        if (node.IsLeaf)
        {
            node.Keys.Add(key);
            node.Values.Add(value);

            while (i >= 0 && key.CompareTo(node.Keys[i]) < 0)
            {
                (node.Keys[i], node.Keys[i + 1]) = (node.Keys[i + 1], node.Keys[i]);
                (node.Values[i], node.Values[i + 1]) = (node.Values[i + 1], node.Values[i]);
                i--;
            }

            return;
        }

        while (i >= 0 && key.CompareTo(node.Keys[i]) < 0)
        {
            i--;
        }

        i++;

        if (node.Children[i].Keys.Count == (2 * _minDegree) - 1)
        {
            SplitChild(node, i);
            if (key.CompareTo(node.Keys[i]) > 0)
            {
                i++;
            }
        }

        InsertNonFull(node.Children[i], key, value);
    }

    private static int FindKeyIndex(Node node, TKey key)
    {
        var idx = 0;
        while (idx < node.Keys.Count && node.Keys[idx].CompareTo(key) < 0)
        {
            idx++;
        }

        return idx;
    }

    private void Remove(Node node, TKey key)
    {
        var t = _minDegree;
        var idx = FindKeyIndex(node, key);

        if (idx < node.Keys.Count && key.CompareTo(node.Keys[idx]) == 0)
        {
            if (node.IsLeaf)
            {
                node.Keys.RemoveAt(idx);
                node.Values.RemoveAt(idx);
            }
            else
            {
                RemoveFromInternalNode(node, idx);
            }

            return;
        }

        if (node.IsLeaf)
        {
            return;
        }

        var isLastChild = idx == node.Keys.Count;

        if (node.Children[idx].Keys.Count < t)
        {
            Fill(node, idx);
        }

        if (isLastChild && idx > node.Keys.Count)
        {
            Remove(node.Children[idx - 1], key);
        }
        else
        {
            Remove(node.Children[idx], key);
        }
    }

    private void RemoveFromInternalNode(Node node, int idx)
    {
        var t = _minDegree;
        var key = node.Keys[idx];

        if (node.Children[idx].Keys.Count >= t)
        {
            var (predKey, predValue) = GetPredecessor(node, idx);
            node.Keys[idx] = predKey;
            node.Values[idx] = predValue;
            Remove(node.Children[idx], predKey);
        }
        else if (node.Children[idx + 1].Keys.Count >= t)
        {
            var (succKey, succValue) = GetSuccessor(node, idx);
            node.Keys[idx] = succKey;
            node.Values[idx] = succValue;
            Remove(node.Children[idx + 1], succKey);
        }
        else
        {
            Merge(node, idx);
            Remove(node.Children[idx], key);
        }
    }

    private static (TKey Key, TValue Value) GetPredecessor(Node node, int idx)
    {
        var current = node.Children[idx];
        while (!current.IsLeaf)
        {
            current = current.Children[^1];
        }

        return (current.Keys[^1], current.Values[^1]);
    }

    private static (TKey Key, TValue Value) GetSuccessor(Node node, int idx)
    {
        var current = node.Children[idx + 1];
        while (!current.IsLeaf)
        {
            current = current.Children[0];
        }

        return (current.Keys[0], current.Values[0]);
    }

    private void Fill(Node node, int idx)
    {
        var t = _minDegree;

        if (idx != 0 && node.Children[idx - 1].Keys.Count >= t)
        {
            BorrowFromPrev(node, idx);
        }
        else if (idx != node.Keys.Count && node.Children[idx + 1].Keys.Count >= t)
        {
            BorrowFromNext(node, idx);
        }
        else if (idx != node.Keys.Count)
        {
            Merge(node, idx);
        }
        else
        {
            Merge(node, idx - 1);
        }
    }

    private static void BorrowFromPrev(Node node, int idx)
    {
        var child = node.Children[idx];
        var sibling = node.Children[idx - 1];

        child.Keys.Insert(0, node.Keys[idx - 1]);
        child.Values.Insert(0, node.Values[idx - 1]);

        if (!child.IsLeaf)
        {
            child.Children.Insert(0, sibling.Children[^1]);
            sibling.Children.RemoveAt(sibling.Children.Count - 1);
        }

        node.Keys[idx - 1] = sibling.Keys[^1];
        node.Values[idx - 1] = sibling.Values[^1];
        sibling.Keys.RemoveAt(sibling.Keys.Count - 1);
        sibling.Values.RemoveAt(sibling.Values.Count - 1);
    }

    private static void BorrowFromNext(Node node, int idx)
    {
        var child = node.Children[idx];
        var sibling = node.Children[idx + 1];

        child.Keys.Add(node.Keys[idx]);
        child.Values.Add(node.Values[idx]);

        if (!child.IsLeaf)
        {
            child.Children.Add(sibling.Children[0]);
            sibling.Children.RemoveAt(0);
        }

        node.Keys[idx] = sibling.Keys[0];
        node.Values[idx] = sibling.Values[0];
        sibling.Keys.RemoveAt(0);
        sibling.Values.RemoveAt(0);
    }

    private static void Merge(Node node, int idx)
    {
        var child = node.Children[idx];
        var sibling = node.Children[idx + 1];

        child.Keys.Add(node.Keys[idx]);
        child.Values.Add(node.Values[idx]);
        child.Keys.AddRange(sibling.Keys);
        child.Values.AddRange(sibling.Values);

        if (!child.IsLeaf)
        {
            child.Children.AddRange(sibling.Children);
        }

        node.Keys.RemoveAt(idx);
        node.Values.RemoveAt(idx);
        node.Children.RemoveAt(idx + 1);
    }

    private sealed class Node(bool isLeaf)
    {
        public List<TKey> Keys { get; } = [];

        public List<TValue> Values { get; } = [];

        public List<Node> Children { get; } = [];

        public bool IsLeaf { get; } = isLeaf;
    }
}
