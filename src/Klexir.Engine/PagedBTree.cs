using System.Buffers.Binary;
using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// Page-backed B+Tree keyed by <see cref="long"/>: internal nodes hold only routing keys and child <see cref="PageId"/>s;
/// every key/value pair lives in a leaf. Reads/writes go through a <see cref="BufferPool"/>; new nodes are
/// allocated directly from the underlying <see cref="IPageStore"/> (the pool caches, it doesn't allocate).
/// </summary>
/// <remarks>
/// Insert-only for now (search, insert, splitting) — delete/rebalancing on a page-backed tree is a later
/// increment; see <see cref="BTree{TKey,TValue}"/> for the in-memory tree that already has full delete.
/// </remarks>
public sealed class PagedBTree
{
    private const int HeaderSize = 8; // [0]: IsLeaf, [1..4): reserved, [4..8): KeyCount
    private const int KeySize = sizeof(long);
    private const int ChildSize = sizeof(uint); // PageId.Value

    private readonly IPageStore _store;
    private readonly BufferPool _pool;
    private readonly int _minDegree;
    private readonly int _maxKeys;

    private PagedBTree(IPageStore store, BufferPool pool, int minDegree, PageId rootPageId)
    {
        _store = store;
        _pool = pool;
        _minDegree = minDegree;
        _maxKeys = (2 * minDegree) - 1;
        RootPageId = rootPageId;
    }

    public PageId RootPageId { get; private set; }

    /// <summary>Allocates a fresh root page and returns a new, empty tree.</summary>
    public static async Task<Result<PagedBTree>> CreateAsync(
        IPageStore store, BufferPool pool, int minDegree, CancellationToken cancellationToken = default)
    {
        var validated = ValidateCapacity(store.PageSize, minDegree);
        if (validated.IsFailure)
        {
            return Result<PagedBTree>.Failure(validated.Error);
        }

        var rootAlloc = await store.AllocatePageAsync(cancellationToken).ConfigureAwait(false);
        if (rootAlloc.IsFailure)
        {
            return Result<PagedBTree>.Failure(rootAlloc.Error);
        }

        var tree = new PagedBTree(store, pool, minDegree, rootAlloc.Value);
        var written = await tree.WriteNodeAsync(
            rootAlloc.Value, new Node { IsLeaf = true }, cancellationToken).ConfigureAwait(false);

        return written.IsFailure
            ? Result<PagedBTree>.Failure(written.Error)
            : Result<PagedBTree>.Success(tree);
    }

    /// <summary>Rebuilds a tree handle over an already-populated store, given the root page id recorded from a previous session.</summary>
    public static PagedBTree Open(IPageStore store, BufferPool pool, int minDegree, PageId rootPageId) =>
        new(store, pool, minDegree, rootPageId);

    public async Task<Result<(bool Found, long Value)>> TryGetAsync(long key, CancellationToken cancellationToken = default) =>
        await SearchAsync(RootPageId, key, cancellationToken).ConfigureAwait(false);

    public async Task<Result<Unit>> InsertAsync(long key, long value, CancellationToken cancellationToken = default)
    {
        var existing = await TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing.IsFailure)
        {
            return Result<Unit>.Failure(existing.Error);
        }

        if (existing.Value.Found)
        {
            return Result<Unit>.Failure(Error.Create($"Key '{key}' already exists."));
        }

        var rootResult = await ReadNodeAsync(RootPageId, cancellationToken).ConfigureAwait(false);
        if (rootResult.IsFailure)
        {
            return Result<Unit>.Failure(rootResult.Error);
        }

        if (rootResult.Value.Keys.Count == _maxKeys)
        {
            var newRootAlloc = await _store.AllocatePageAsync(cancellationToken).ConfigureAwait(false);
            if (newRootAlloc.IsFailure)
            {
                return Result<Unit>.Failure(newRootAlloc.Error);
            }

            var newRoot = new Node { IsLeaf = false, Children = { RootPageId } };
            var writeNewRoot = await WriteNodeAsync(newRootAlloc.Value, newRoot, cancellationToken).ConfigureAwait(false);
            if (writeNewRoot.IsFailure)
            {
                return Result<Unit>.Failure(writeNewRoot.Error);
            }

            var split = await SplitChildAsync(newRootAlloc.Value, 0, cancellationToken).ConfigureAwait(false);
            if (split.IsFailure)
            {
                return Result<Unit>.Failure(split.Error);
            }

            RootPageId = newRootAlloc.Value;
        }

        return await InsertNonFullAsync(RootPageId, key, value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Collects every key/value pair by recursively descending all children. For verification — not a sorted/streaming scan.</summary>
    public async Task<Result<IReadOnlyList<(long Key, long Value)>>> CollectAllAsync(CancellationToken cancellationToken = default) =>
        await CollectAsync(RootPageId, cancellationToken).ConfigureAwait(false);

    private static Result<Unit> ValidateCapacity(int pageSize, int minDegree)
    {
        if (minDegree < 2)
        {
            return Result<Unit>.Failure(Error.Create("Minimum degree must be at least 2."));
        }

        var maxKeys = (2 * minDegree) - 1;
        var leafSize = HeaderSize + (maxKeys * KeySize * 2);
        var internalSize = HeaderSize + (maxKeys * KeySize) + ((maxKeys + 1) * ChildSize);
        var required = Math.Max(leafSize, internalSize);

        return required <= pageSize
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure(Error.Create(
                $"minDegree {minDegree} needs {required} bytes per node, but pages are only {pageSize} bytes."));
    }

    private async Task<Result<(bool, long)>> SearchAsync(PageId pageId, long key, CancellationToken cancellationToken)
    {
        var nodeResult = await ReadNodeAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure)
        {
            return Result<(bool, long)>.Failure(nodeResult.Error);
        }

        var node = nodeResult.Value;

        if (node.IsLeaf)
        {
            var index = node.Keys.IndexOf(key);
            return Result<(bool, long)>.Success(index >= 0 ? (true, node.Values[index]) : (false, 0));
        }

        return await SearchAsync(node.Children[ChildIndexFor(node, key)], key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> InsertNonFullAsync(PageId pageId, long key, long value, CancellationToken cancellationToken)
    {
        var nodeResult = await ReadNodeAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure)
        {
            return Result<Unit>.Failure(nodeResult.Error);
        }

        var node = nodeResult.Value;

        if (node.IsLeaf)
        {
            var insertAt = 0;
            while (insertAt < node.Keys.Count && node.Keys[insertAt] < key)
            {
                insertAt++;
            }

            node.Keys.Insert(insertAt, key);
            node.Values.Insert(insertAt, value);
            return await WriteNodeAsync(pageId, node, cancellationToken).ConfigureAwait(false);
        }

        var childIndex = ChildIndexFor(node, key);
        var childResult = await ReadNodeAsync(node.Children[childIndex], cancellationToken).ConfigureAwait(false);
        if (childResult.IsFailure)
        {
            return Result<Unit>.Failure(childResult.Error);
        }

        if (childResult.Value.Keys.Count == _maxKeys)
        {
            var split = await SplitChildAsync(pageId, childIndex, cancellationToken).ConfigureAwait(false);
            if (split.IsFailure)
            {
                return Result<Unit>.Failure(split.Error);
            }

            // The split inserted a new separator into this node — re-read it and re-route.
            var refreshed = await ReadNodeAsync(pageId, cancellationToken).ConfigureAwait(false);
            if (refreshed.IsFailure)
            {
                return Result<Unit>.Failure(refreshed.Error);
            }

            node = refreshed.Value;
            childIndex = ChildIndexFor(node, key);
        }

        return await InsertNonFullAsync(node.Children[childIndex], key, value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits <c>parent.Children[index]</c> in place. A leaf split copies its median key/value up as a routing
    /// separator (both halves keep their own copy — the value must survive in a leaf); an internal split moves
    /// its median up (internal nodes carry no values, so nothing is lost by removing it from the child).
    /// </summary>
    private async Task<Result<Unit>> SplitChildAsync(PageId parentPageId, int index, CancellationToken cancellationToken)
    {
        var parentResult = await ReadNodeAsync(parentPageId, cancellationToken).ConfigureAwait(false);
        if (parentResult.IsFailure)
        {
            return Result<Unit>.Failure(parentResult.Error);
        }

        var parent = parentResult.Value;
        var childPageId = parent.Children[index];

        var childResult = await ReadNodeAsync(childPageId, cancellationToken).ConfigureAwait(false);
        if (childResult.IsFailure)
        {
            return Result<Unit>.Failure(childResult.Error);
        }

        var child = childResult.Value;

        var siblingAlloc = await _store.AllocatePageAsync(cancellationToken).ConfigureAwait(false);
        if (siblingAlloc.IsFailure)
        {
            return Result<Unit>.Failure(siblingAlloc.Error);
        }

        long separatorKey;
        Node sibling;
        Node updatedChild;

        if (child.IsLeaf)
        {
            var mid = _minDegree;
            sibling = new Node
            {
                IsLeaf = true,
                Keys = child.Keys.GetRange(mid, child.Keys.Count - mid),
                Values = child.Values.GetRange(mid, child.Values.Count - mid),
            };
            updatedChild = new Node
            {
                IsLeaf = true,
                Keys = child.Keys.GetRange(0, mid),
                Values = child.Values.GetRange(0, mid),
            };
            separatorKey = sibling.Keys[0];
        }
        else
        {
            var mid = _minDegree - 1;
            separatorKey = child.Keys[mid];
            sibling = new Node
            {
                IsLeaf = false,
                Keys = child.Keys.GetRange(mid + 1, child.Keys.Count - mid - 1),
                Children = child.Children.GetRange(mid + 1, child.Children.Count - mid - 1),
            };
            updatedChild = new Node
            {
                IsLeaf = false,
                Keys = child.Keys.GetRange(0, mid),
                Children = child.Children.GetRange(0, mid + 1),
            };
        }

        var writeChild = await WriteNodeAsync(childPageId, updatedChild, cancellationToken).ConfigureAwait(false);
        if (writeChild.IsFailure)
        {
            return Result<Unit>.Failure(writeChild.Error);
        }

        var writeSibling = await WriteNodeAsync(siblingAlloc.Value, sibling, cancellationToken).ConfigureAwait(false);
        if (writeSibling.IsFailure)
        {
            return Result<Unit>.Failure(writeSibling.Error);
        }

        parent.Keys.Insert(index, separatorKey);
        parent.Children.Insert(index + 1, siblingAlloc.Value);
        return await WriteNodeAsync(parentPageId, parent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IReadOnlyList<(long, long)>>> CollectAsync(PageId pageId, CancellationToken cancellationToken)
    {
        var nodeResult = await ReadNodeAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure)
        {
            return Result<IReadOnlyList<(long, long)>>.Failure(nodeResult.Error);
        }

        var node = nodeResult.Value;

        if (node.IsLeaf)
        {
            var pairs = new List<(long, long)>(node.Keys.Count);
            for (var i = 0; i < node.Keys.Count; i++)
            {
                pairs.Add((node.Keys[i], node.Values[i]));
            }

            return Result<IReadOnlyList<(long, long)>>.Success(pairs);
        }

        var all = new List<(long, long)>();
        foreach (var child in node.Children)
        {
            var childPairs = await CollectAsync(child, cancellationToken).ConfigureAwait(false);
            if (childPairs.IsFailure)
            {
                return childPairs;
            }

            all.AddRange(childPairs.Value);
        }

        return Result<IReadOnlyList<(long, long)>>.Success(all);
    }

    private static int ChildIndexFor(Node node, long key)
    {
        var index = 0;
        while (index < node.Keys.Count && key >= node.Keys[index])
        {
            index++;
        }

        return index;
    }

    private async Task<Result<Node>> ReadNodeAsync(PageId pageId, CancellationToken cancellationToken)
    {
        var read = await _pool.ReadAsync(pageId, cancellationToken).ConfigureAwait(false);
        return read.IsFailure ? Result<Node>.Failure(read.Error) : Result<Node>.Success(DecodeNode(read.Value));
    }

    private async Task<Result<Unit>> WriteNodeAsync(PageId pageId, Node node, CancellationToken cancellationToken) =>
        await _pool.WriteAsync(pageId, EncodeNode(node), cancellationToken).ConfigureAwait(false);

    private byte[] EncodeNode(Node node)
    {
        var buffer = new byte[_store.PageSize];
        buffer[0] = (byte)(node.IsLeaf ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), node.Keys.Count);

        var offset = HeaderSize;
        foreach (var key in node.Keys)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset, KeySize), key);
            offset += KeySize;
        }

        if (node.IsLeaf)
        {
            foreach (var value in node.Values)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset, KeySize), value);
                offset += KeySize;
            }
        }
        else
        {
            foreach (var child in node.Children)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, ChildSize), child.Value);
                offset += ChildSize;
            }
        }

        return buffer;
    }

    private static Node DecodeNode(byte[] bytes)
    {
        var isLeaf = bytes[0] == 1;
        var keyCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        var offset = HeaderSize;

        var keys = new List<long>(keyCount);
        for (var i = 0; i < keyCount; i++)
        {
            keys.Add(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, KeySize)));
            offset += KeySize;
        }

        var node = new Node { IsLeaf = isLeaf, Keys = keys };

        if (isLeaf)
        {
            for (var i = 0; i < keyCount; i++)
            {
                node.Values.Add(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, KeySize)));
                offset += KeySize;
            }
        }
        else
        {
            for (var i = 0; i <= keyCount; i++)
            {
                node.Children.Add(new PageId(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, ChildSize))));
                offset += ChildSize;
            }
        }

        return node;
    }

    /// <summary>In-memory decoding of one page's bytes, valid only for the duration of one operation — nothing here is cached across calls.</summary>
    private sealed class Node
    {
        public bool IsLeaf { get; init; }

        public List<long> Keys { get; init; } = [];

        public List<long> Values { get; init; } = [];

        public List<PageId> Children { get; init; } = [];
    }
}
