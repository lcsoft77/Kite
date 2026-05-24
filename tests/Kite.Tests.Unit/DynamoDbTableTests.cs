using System.Text.Json;
using Kite.Core;
using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class DynamoDbTableTests
{
    private readonly InMemoryServiceRegistry _registry = new();

    // ── Registry tests ────────────────────────────────────────────────────────

    [Fact]
    public void RegisterAndGetDynamoDbTable_ShouldWork()
    {
        var table = new DynamoDbTable { TableName = "Users", PartitionKeyName = "pk" };
        _registry.RegisterDynamoDbTable(table);

        _registry.GetDynamoDbTable("Users").Should().Be(table);
    }

    [Fact]
    public void GetDynamoDbTable_NonExistent_ReturnsNull()
    {
        _registry.GetDynamoDbTable("NonExistent").Should().BeNull();
    }

    [Fact]
    public void DeleteDynamoDbTable_ShouldRemoveTable()
    {
        var table = new DynamoDbTable { TableName = "Orders", PartitionKeyName = "pk" };
        _registry.RegisterDynamoDbTable(table);
        _registry.DeleteDynamoDbTable("Orders");

        _registry.GetDynamoDbTable("Orders").Should().BeNull();
    }

    [Fact]
    public void GetAllDynamoDbTables_ReturnsAllTables()
    {
        _registry.RegisterDynamoDbTable(new DynamoDbTable { TableName = "TableA", PartitionKeyName = "pk" });
        _registry.RegisterDynamoDbTable(new DynamoDbTable { TableName = "TableB", PartitionKeyName = "id" });

        _registry.GetAllDynamoDbTables().Should().HaveCount(2);
    }

    [Fact]
    public void RegisterDynamoDbTable_IsCaseInsensitive()
    {
        var table = new DynamoDbTable { TableName = "MyTable", PartitionKeyName = "pk" };
        _registry.RegisterDynamoDbTable(table);

        _registry.GetDynamoDbTable("mytable").Should().Be(table);
        _registry.GetDynamoDbTable("MYTABLE").Should().Be(table);
    }

    // ── Builder tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Builder_AddDynamoDbTable_RegistersTable()
    {
        var emulator = KiteBuilder.Create()
            .AddDynamoDbTable("Products", "productId")
            .Build();

        var table = emulator.Registry.GetDynamoDbTable("Products");
        table.Should().NotBeNull();
        table!.TableName.Should().Be("Products");
        table.PartitionKeyName.Should().Be("productId");
        table.PartitionKeyType.Should().Be("S");
    }

    [Fact]
    public void Builder_AddDynamoDbTable_WithSortKey_RegistersCorrectly()
    {
        var emulator = KiteBuilder.Create()
            .AddDynamoDbTable("Events", "userId", "S", "timestamp", "N")
            .Build();

        var table = emulator.Registry.GetDynamoDbTable("Events");
        table.Should().NotBeNull();
        table!.SortKeyName.Should().Be("timestamp");
        table.SortKeyType.Should().Be("N");
    }

    [Fact]
    public void Builder_AddMultipleDynamoDbTables_AllRegistered()
    {
        var emulator = KiteBuilder.Create()
            .AddDynamoDbTable("TableOne", "pk")
            .AddDynamoDbTable("TableTwo", "id")
            .AddDynamoDbTable("TableThree", "userId", "S", "createdAt", "S")
            .Build();

        emulator.Registry.GetAllDynamoDbTables().Should().HaveCount(3);
    }

    // ── DynamoDbTable model tests ─────────────────────────────────────────────

    [Fact]
    public void DynamoDbTable_DefaultValues_AreCorrect()
    {
        var table = new DynamoDbTable { TableName = "Test", PartitionKeyName = "pk" };

        table.TableStatus.Should().Be("ACTIVE");
        table.BillingMode.Should().Be("PAY_PER_REQUEST");
        table.SortKeyName.Should().BeNull();
        table.AttributeDefinitions.Should().BeEmpty();
    }

    [Fact]
    public void DynamoDbTable_NotifyChange_FiresEvent()
    {
        var table = new DynamoDbTable { TableName = "Test", PartitionKeyName = "pk" };
        var fired = false;
        table.OnChange += () => fired = true;

        table.NotifyChange();

        fired.Should().BeTrue();
    }

    // ── Item operation tests ──────────────────────────────────────────────────

    [Fact]
    public void PutAndGetItem_NoSortKey_ShouldWork()
    {
        var table = new DynamoDbTable { TableName = "Users", PartitionKeyName = "pk" };
        var item = MakeItem("pk", "user-1", "name", "Alice");

        table.PutItem("user-1", "", item);

        var retrieved = table.GetItem("user-1", "");
        retrieved.Should().NotBeNull();
        retrieved!["name"].GetProperty("S").GetString().Should().Be("Alice");
    }

    [Fact]
    public void PutAndGetItem_WithSortKey_ShouldWork()
    {
        var table = new DynamoDbTable
        {
            TableName = "Events",
            PartitionKeyName = "userId",
            SortKeyName = "timestamp"
        };
        var item = MakeItem("userId", "u1", "timestamp", "2024-01-01");

        table.PutItem("u1", "2024-01-01", item);

        var retrieved = table.GetItem("u1", "2024-01-01");
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public void GetItem_NonExistent_ReturnsNull()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };

        table.GetItem("missing", "").Should().BeNull();
    }

    [Fact]
    public void PutItem_Overwrites_ExistingItem()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };
        table.PutItem("k1", "", MakeItem("pk", "k1", "val", "original"));
        table.PutItem("k1", "", MakeItem("pk", "k1", "val", "updated"));

        var item = table.GetItem("k1", "");
        item!["val"].GetProperty("S").GetString().Should().Be("updated");
    }

    [Fact]
    public void DeleteItem_ShouldRemove()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };
        table.PutItem("k1", "", MakeItem("pk", "k1"));

        table.DeleteItem("k1", "").Should().BeTrue();
        table.GetItem("k1", "").Should().BeNull();
    }

    [Fact]
    public void DeleteItem_NonExistent_ReturnsFalse()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };

        table.DeleteItem("missing", "").Should().BeFalse();
    }

    [Fact]
    public void Query_ByPartitionKey_ReturnsAllMatchingItems()
    {
        var table = new DynamoDbTable
        {
            TableName = "T",
            PartitionKeyName = "pk",
            SortKeyName = "sk"
        };
        table.PutItem("user1", "order1", MakeItem("pk", "user1", "sk", "order1"));
        table.PutItem("user1", "order2", MakeItem("pk", "user1", "sk", "order2"));
        table.PutItem("user2", "order3", MakeItem("pk", "user2", "sk", "order3"));

        var results = table.Query("user1");

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Query_WithSkCondition_FiltersCorrectly()
    {
        var table = new DynamoDbTable
        {
            TableName = "T",
            PartitionKeyName = "pk",
            SortKeyName = "sk"
        };
        table.PutItem("u1", "a", MakeItem("pk", "u1", "sk", "a"));
        table.PutItem("u1", "b", MakeItem("pk", "u1", "sk", "b"));
        table.PutItem("u1", "c", MakeItem("pk", "u1", "sk", "c"));

        var results = table.Query("u1", sk => string.Compare(sk, "b", StringComparison.Ordinal) >= 0);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Query_ScanIndexForwardFalse_ReversesOrder()
    {
        var table = new DynamoDbTable
        {
            TableName = "T",
            PartitionKeyName = "pk",
            SortKeyName = "sk"
        };
        table.PutItem("u1", "a", MakeItem("pk", "u1", "sk", "a"));
        table.PutItem("u1", "b", MakeItem("pk", "u1", "sk", "b"));
        table.PutItem("u1", "c", MakeItem("pk", "u1", "sk", "c"));

        var asc = table.Query("u1", scanIndexForward: true);
        var desc = table.Query("u1", scanIndexForward: false);

        asc[0]["sk"].GetProperty("S").GetString().Should().Be("a");
        desc[0]["sk"].GetProperty("S").GetString().Should().Be("c");
    }

    [Fact]
    public void Query_NonExistentPartition_ReturnsEmpty()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };

        table.Query("missing").Should().BeEmpty();
    }

    [Fact]
    public void Scan_ReturnsAllItems()
    {
        var table = new DynamoDbTable
        {
            TableName = "T",
            PartitionKeyName = "pk",
            SortKeyName = "sk"
        };
        table.PutItem("pk1", "sk1", MakeItem("pk", "pk1", "sk", "sk1"));
        table.PutItem("pk1", "sk2", MakeItem("pk", "pk1", "sk", "sk2"));
        table.PutItem("pk2", "sk1", MakeItem("pk", "pk2", "sk", "sk1"));

        table.Scan().Should().HaveCount(3);
    }

    [Fact]
    public void ItemCount_ReflectsStoredItems()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };
        table.PutItem("k1", "", MakeItem("pk", "k1"));
        table.PutItem("k2", "", MakeItem("pk", "k2"));

        table.ItemCount().Should().Be(2);
    }

    [Fact]
    public void Query_NumericSortKey_OrdersNumerically()
    {
        // Without numeric comparer "10" < "2" lexicographically — this test ensures correct ordering
        var table = new DynamoDbTable
        {
            TableName = "T",
            PartitionKeyName = "pk",
            SortKeyName = "sk",
            SortKeyType = "N"
        };
        table.PutItem("user1", "2", MakeNumericItem(("pk", "user1"), ("sk", "2")));
        table.PutItem("user1", "10", MakeNumericItem(("pk", "user1"), ("sk", "10")));
        table.PutItem("user1", "9", MakeNumericItem(("pk", "user1"), ("sk", "9")));

        var results = table.Query("user1", scanIndexForward: true);

        // Numeric order: 2, 9, 10
        results[0]["sk"].GetProperty("N").GetString().Should().Be("2");
        results[1]["sk"].GetProperty("N").GetString().Should().Be("9");
        results[2]["sk"].GetProperty("N").GetString().Should().Be("10");
    }

    [Fact]
    public void Builder_AddDynamoDbTable_DefaultsSortKeyTypeToS_WhenSortKeyNameProvided()
    {
        var emulator = KiteBuilder.Create()
            .AddDynamoDbTable("T", "pk", "S", "sk")  // sortKeyType not provided
            .Build();

        var table = emulator.Registry.GetDynamoDbTable("T");
        table!.SortKeyType.Should().Be("S");
        table.AttributeDefinitions.Should().Contain(a => a.AttributeName == "sk" && a.AttributeType == "S");
    }

    [Fact]
    public void Builder_AddDynamoDbTable_NoSortKey_SortKeyTypeRemainsNull()
    {
        var emulator = KiteBuilder.Create()
            .AddDynamoDbTable("T", "pk")
            .Build();

        var table = emulator.Registry.GetDynamoDbTable("T");
        table!.SortKeyType.Should().BeNull();
        table.AttributeDefinitions.Should().HaveCount(1);
    }

    [Fact]
    public void Scan_EmptyTable_ReturnsEmpty()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };

        table.Scan().Should().BeEmpty();
    }

    [Fact]
    public void ItemCount_EmptyTable_ReturnsZero()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };

        table.ItemCount().Should().Be(0);
    }

    [Fact]
    public void DeleteItem_LastItemInPartition_RemovesPartition()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk", SortKeyName = "sk" };
        table.PutItem("user1", "a", MakeItem("pk", "user1", "sk", "a"));

        table.DeleteItem("user1", "a").Should().BeTrue();

        // Partition is gone — query should return empty
        table.Query("user1").Should().BeEmpty();
        table.ItemCount().Should().Be(0);
    }

    [Fact]
    public void DeleteItem_PartialPartition_LeavesRemainingItems()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk", SortKeyName = "sk" };
        table.PutItem("user1", "a", MakeItem("pk", "user1", "sk", "a"));
        table.PutItem("user1", "b", MakeItem("pk", "user1", "sk", "b"));

        table.DeleteItem("user1", "a").Should().BeTrue();

        table.Query("user1").Should().HaveCount(1);
        table.GetItem("user1", "b").Should().NotBeNull();
    }

    [Fact]
    public void DynamoDbTable_PartitionKeyType_DefaultIsS()
    {
        var table = new DynamoDbTable { TableName = "T", PartitionKeyName = "pk" };
        table.PartitionKeyType.Should().Be("S");
    }

    [Fact]
    public void DynamoDbAttributeDefinition_DefaultValues_AreCorrect()
    {
        var attr = new DynamoDbAttributeDefinition();
        attr.AttributeName.Should().BeEmpty();
        attr.AttributeType.Should().Be("S");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> MakeItem(params string[] keyValuePairs)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i < keyValuePairs.Length - 1; i += 2)
        {
            using var doc = JsonDocument.Parse($"{{\"S\":\"{keyValuePairs[i + 1]}\"}}");
            result[keyValuePairs[i]] = doc.RootElement.Clone();
        }
        return result;
    }

    private static Dictionary<string, JsonElement> MakeNumericItem(params (string name, string value)[] pairs)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            using var doc = JsonDocument.Parse($"{{\"N\":\"{value}\"}}");
            result[name] = doc.RootElement.Clone();
        }
        return result;
    }
}
