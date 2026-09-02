using FluentAssertions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class QueryEngineTests
{
    [Fact]
    public void BTree_InOrder_yields_entries_in_ascending_key_order_regardless_of_insertion_order()
    {
        var tree = new BTree<int, string>();
        foreach (var key in new[] { 5, 1, 9, 3, 7 })
        {
            tree.Insert(key, $"v{key}");
        }

        var entries = tree.InOrder().ToArray();

        entries.Select(e => e.Key).Should().Equal(1, 3, 5, 7, 9);
        entries.Select(e => e.Value).Should().Equal("v1", "v3", "v5", "v7", "v9");
    }

    private sealed record Customer(int Id, string Name, string City);

    private sealed record Order(int Id, int CustomerId, decimal Total);

    [Fact]
    public void Scan_Filter_Project_and_Join_compose_over_BTree_backed_tables()
    {
        var customers = new BTree<int, Customer>();
        customers.Insert(1, new Customer(1, "Alice", "Rome"));
        customers.Insert(2, new Customer(2, "Bob", "Milan"));
        customers.Insert(3, new Customer(3, "Cara", "Rome"));

        var orders = new BTree<int, Order>();
        orders.Insert(100, new Order(100, 1, 50m));
        orders.Insert(101, new Order(101, 2, 30m));
        orders.Insert(102, new Order(102, 1, 20m));

        var romanCustomerNames = QueryEngine
            .Filter(QueryEngine.Scan(customers), c => c.City == "Rome")
            .OrderBy(c => c.Id)
            .Select(c => c.Name)
            .ToArray();
        romanCustomerNames.Should().Equal("Alice", "Cara");

        var joined = QueryEngine
            .Join(
                QueryEngine.Scan(customers), QueryEngine.Scan(orders),
                c => c.Id, o => o.CustomerId,
                (c, o) => (c.Name, o.Total))
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Total)
            .ToArray();

        joined.Should().Equal(("Alice", 20m), ("Alice", 50m), ("Bob", 30m));
    }
}
