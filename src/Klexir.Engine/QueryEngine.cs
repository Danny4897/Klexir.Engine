namespace Klexir.Engine;

/// <summary>
/// The relational operator vocabulary a future planner/parser would target — scan, filter, project, join — not a
/// SQL engine: there is no query text, no parser, and no planner here yet.
/// </summary>
public static class QueryEngine
{
    /// <summary>Reads every row of a table in key order.</summary>
    public static IEnumerable<TValue> Scan<TKey, TValue>(BTree<TKey, TValue> table) where TKey : IComparable<TKey> =>
        table.InOrder().Select(entry => entry.Value);

    public static IEnumerable<T> Filter<T>(IEnumerable<T> rows, Func<T, bool> predicate) => rows.Where(predicate);

    public static IEnumerable<TResult> Project<T, TResult>(IEnumerable<T> rows, Func<T, TResult> selector) => rows.Select(selector);

    public static IEnumerable<TResult> Join<TLeft, TRight, TKey, TResult>(
        IEnumerable<TLeft> left,
        IEnumerable<TRight> right,
        Func<TLeft, TKey> leftKey,
        Func<TRight, TKey> rightKey,
        Func<TLeft, TRight, TResult> combine) =>
        left.Join(right, leftKey, rightKey, combine);
}
