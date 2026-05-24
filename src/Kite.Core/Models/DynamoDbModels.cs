using System.Text.Json;

namespace Kite.Core.Models;

public class DynamoDbTable
{
    public string TableName { get; set; } = string.Empty;
    public string TableStatus { get; set; } = "ACTIVE";
    public string BillingMode { get; set; } = "PAY_PER_REQUEST";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string PartitionKeyName { get; set; } = string.Empty;
    public string PartitionKeyType { get; set; } = "S";
    public string? SortKeyName { get; set; }
    public string? SortKeyType { get; set; }
    public List<DynamoDbAttributeDefinition> AttributeDefinitions { get; set; } = [];

    // PK value → (SK value → item attributes)
    // For tables without a sort key the inner dict always holds exactly one entry keyed by ""
    private readonly SortedDictionary<string, SortedDictionary<string, Dictionary<string, JsonElement>>>
        _items = new(StringComparer.Ordinal);

    private readonly object _itemsLock = new();

    public List<RequestLogEntry> RequestLog { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();

    // ── Item operations ────────────────────────────────────────────────────────

    public void PutItem(string pk, string sk, Dictionary<string, JsonElement> attributes)
    {
        lock (_itemsLock)
        {
            if (!_items.TryGetValue(pk, out var partition))
            {
                partition = new SortedDictionary<string, Dictionary<string, JsonElement>>(CreateSkComparer());
                _items[pk] = partition;
            }
            partition[sk] = attributes;
        }
    }

    public Dictionary<string, JsonElement>? GetItem(string pk, string sk)
    {
        lock (_itemsLock)
        {
            return _items.TryGetValue(pk, out var partition) && partition.TryGetValue(sk, out var item)
                ? item
                : null;
        }
    }

    public bool DeleteItem(string pk, string sk)
    {
        lock (_itemsLock)
        {
            if (!_items.TryGetValue(pk, out var partition))
                return false;
            var removed = partition.Remove(sk);
            if (partition.Count == 0)
                _items.Remove(pk);
            return removed;
        }
    }

    public IList<Dictionary<string, JsonElement>> Query(
        string pk,
        Func<string, bool>? skCondition = null,
        bool scanIndexForward = true)
    {
        lock (_itemsLock)
        {
            if (!_items.TryGetValue(pk, out var partition))
                return [];

            IEnumerable<KeyValuePair<string, Dictionary<string, JsonElement>>> items = partition;
            if (skCondition is not null)
                items = items.Where(kv => skCondition(kv.Key));

            var results = items.Select(kv => kv.Value).ToList();
            if (!scanIndexForward)
                results.Reverse();
            return results;
        }
    }

    public IList<Dictionary<string, JsonElement>> Scan()
    {
        lock (_itemsLock)
        {
            return _items.Values
                .SelectMany(partition => partition.Values)
                .ToList();
        }
    }

    public int ItemCount()
    {
        lock (_itemsLock)
        {
            return _items.Values.Sum(p => p.Count);
        }
    }

    private IComparer<string> CreateSkComparer() =>
        SortKeyType == "N" ? DynamoDbNumericStringComparer.Instance : StringComparer.Ordinal;
}

public class DynamoDbAttributeDefinition
{
    public string AttributeName { get; set; } = string.Empty;
    public string AttributeType { get; set; } = "S"; // S | N | B
}

/// <summary>
/// Compares DynamoDB numeric ("N") key values stored as strings numerically so that
/// "10" sorts after "9" (unlike lexicographic ordering where "10" &lt; "9").
/// Falls back to ordinal comparison when either value is not a valid decimal.
/// </summary>
internal sealed class DynamoDbNumericStringComparer : IComparer<string>
{
    public static readonly DynamoDbNumericStringComparer Instance = new();

    private DynamoDbNumericStringComparer() { }

    public int Compare(string? x, string? y)
    {
        if (decimal.TryParse(x, out var nx) && decimal.TryParse(y, out var ny))
            return nx.CompareTo(ny);
        return StringComparer.Ordinal.Compare(x, y);
    }
}
