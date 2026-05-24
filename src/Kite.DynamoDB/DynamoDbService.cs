using System.Text.Json;
using System.Text.RegularExpressions;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.DynamoDB;

public class DynamoDbService
{
    private const string ContentType = "application/x-amz-json-1.0";

    private readonly IServiceRegistry _registry;
    private readonly ILogger<DynamoDbService> _logger;

    public DynamoDbService(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<DynamoDbService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var operation = target.Contains('.') ? target[(target.LastIndexOf('.') + 1)..] : target;

        _logger.LogDebug("[DEBUG] DynamoDB HandleAsync - Operation: {Operation}, Method: {Method}", operation, context.Request.Method);

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = body.Length > 0 ? JsonDocument.Parse(body) : JsonDocument.Parse("{}");
        var root = doc.RootElement.Clone();

        _logger.LogDebug("[DEBUG] DynamoDB operation: {Operation}, RequestBodyLength: {BodyLength}", operation, body.Length);

        switch (operation)
        {
            case "CreateTable":
                await HandleCreateTableAsync(context, root);
                break;
            case "DeleteTable":
                await HandleDeleteTableAsync(context, root);
                break;
            case "DescribeTable":
                await HandleDescribeTableAsync(context, root);
                break;
            case "ListTables":
                await HandleListTablesAsync(context, root);
                break;
            case "PutItem":
                await HandlePutItemAsync(context, root);
                break;
            case "GetItem":
                await HandleGetItemAsync(context, root);
                break;
            case "DeleteItem":
                await HandleDeleteItemAsync(context, root);
                break;
            case "UpdateItem":
                await HandleUpdateItemAsync(context, root);
                break;
            case "Query":
                await HandleQueryAsync(context, root);
                break;
            case "Scan":
                await HandleScanAsync(context, root);
                break;
            default:
                _logger.LogWarning("Unknown DynamoDB operation: {Operation}", operation);
                context.Response.StatusCode = 400;
                context.Response.ContentType = ContentType;
                await WriteErrorAsync(context, "UnknownOperationException", $"Unknown operation: {operation}");
                break;
        }
    }

    // ── Table operations ────────────────────────────────────────────────────────

    private async Task HandleCreateTableAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleCreateTableAsync - TableName: {TableName}", tableName ?? "<null>");
        
        if (string.IsNullOrWhiteSpace(tableName))
        {
            _logger.LogError("[ERROR] CreateTable failed - TableName is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "TableName is required.");
            return;
        }

        if (_registry.GetDynamoDbTable(tableName) is not null)
        {
            _logger.LogWarning("[ERROR] CreateTable failed - Table already exists: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceInUseException", $"Table already exists: {tableName}");
            return;
        }

        var billingMode = GetString(root, "BillingMode") ?? "PROVISIONED";
        var keySchema = ParseKeySchema(root);
        var attrDefs = ParseAttributeDefinitions(root);

        var pkElement = keySchema.FirstOrDefault(k => k.KeyType == "HASH");
        var skElement = keySchema.Cast<(string AttributeName, string KeyType)?>()
            .FirstOrDefault(k => k?.KeyType == "RANGE");

        if (string.IsNullOrEmpty(pkElement.AttributeName))
        {
            _logger.LogError("[ERROR] CreateTable failed - KeySchema must contain a HASH key");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "KeySchema must contain a HASH key.");
            return;
        }

        var pkAttr = attrDefs.FirstOrDefault(a => a.AttributeName == pkElement.AttributeName);
        var skAttr = skElement is not null
            ? attrDefs.FirstOrDefault(a => a.AttributeName == skElement.Value.AttributeName)
            : null;

        var table = new DynamoDbTable
        {
            TableName = tableName,
            BillingMode = billingMode,
            PartitionKeyName = pkElement.AttributeName,
            PartitionKeyType = pkAttr?.AttributeType ?? "S",
            SortKeyName = skElement?.AttributeName,
            SortKeyType = skAttr?.AttributeType,
            AttributeDefinitions = attrDefs
        };

        _registry.RegisterDynamoDbTable(table);
        LogHelpers.AddRequestLog(table.RequestLog, "CreateTable", $"Table={tableName}");
        table.NotifyChange();

        _logger.LogInformation("CreateTable completed - TableName: {TableName}, PK: {PartitionKey}, SK: {SortKey}, BillingMode: {BillingMode}", 
            tableName, table.PartitionKeyName, table.SortKeyName ?? "<none>", table.BillingMode);

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            TableDescription = BuildTableDescription(table)
        }));
    }

    private async Task HandleDeleteTableAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleDeleteTableAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] DeleteTable failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        _registry.DeleteDynamoDbTable(tableName!);
        _logger.LogInformation("DeleteTable completed - TableName: {TableName}", tableName);

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            TableDescription = BuildTableDescription(table)
        }));
    }

    private async Task HandleDescribeTableAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleDescribeTableAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] DescribeTable failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        LogHelpers.AddRequestLog(table.RequestLog, "DescribeTable", $"Table={tableName}");
        _logger.LogDebug("[DEBUG] DescribeTable - TableStatus: {TableStatus}, ItemCount: {ItemCount}", table.TableStatus, table.ItemCount());
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Table = BuildTableDescription(table)
        }));
    }

    private async Task HandleListTablesAsync(HttpContext context, JsonElement root)
    {
        var limit = root.TryGetProperty("Limit", out var l) && l.TryGetInt32(out var lv) ? lv : 100;
        var exclusiveStartTableName = GetString(root, "ExclusiveStartTableName");
        _logger.LogDebug("[ENTRY] HandleListTablesAsync - Limit: {Limit}, ExclusiveStartTableName: {ExclusiveStartTableName}", limit, exclusiveStartTableName ?? "<none>");

        var allTables = _registry.GetAllDynamoDbTables()
            .OrderBy(t => t.TableName, StringComparer.Ordinal)
            .ToList();

        var startIndex = exclusiveStartTableName is null
            ? 0
            : allTables.FindIndex(t => string.Equals(t.TableName, exclusiveStartTableName, StringComparison.Ordinal)) + 1;

        var page = allTables.Skip(startIndex).Take(limit).ToList();
        var lastEvaluatedName = page.Count == limit && startIndex + limit < allTables.Count
            ? page.Last().TableName
            : null;

        _logger.LogInformation("ListTables completed - TotalTables: {TotalCount}, ReturnedCount: {ReturnedCount}, HasMore: {HasMore}", allTables.Count, page.Count, lastEvaluatedName is not null);

        context.Response.ContentType = ContentType;
        if (lastEvaluatedName is not null)
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                TableNames = page.Select(t => t.TableName).ToList(),
                LastEvaluatedTableName = lastEvaluatedName
            }));
        }
        else
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                TableNames = page.Select(t => t.TableName).ToList()
            }));
        }
    }

    // ── Item operations ──────────────────────────────────────────────────────────

    private async Task HandlePutItemAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandlePutItemAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] PutItem failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        if (!root.TryGetProperty("Item", out var itemElement))
        {
            _logger.LogError("[ERROR] PutItem failed - Item element is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "Item is required.");
            return;
        }

        var item = ParseItem(itemElement);
        var (pk, sk, error) = ExtractKeys(item, table);
        if (error is not null)
        {
            _logger.LogError("[ERROR] PutItem failed - Key extraction error: {Error}", error);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", error);
            return;
        }

        using var activity = KiteActivitySource.DynamoDB.StartActivity("dynamodb.put_item");
        activity?.SetTag("table.name", tableName);
        activity?.SetTag("partition.key", pk);

        table.PutItem(pk!, sk, item);
        LogHelpers.AddRequestLog(table.RequestLog, "PutItem", $"Table={tableName} PK={pk}");
        table.NotifyChange();

        _logger.LogInformation("PutItem completed - TableName: {TableName}, PartitionKey: {PartitionKey}, SortKey: {SortKey}, ItemSize: {ItemSize}", tableName, pk, sk ?? "<none>", item.Count);

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleGetItemAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleGetItemAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] GetItem failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        if (!root.TryGetProperty("Key", out var keyElement))
        {
            _logger.LogError("[ERROR] GetItem failed - Key element is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "Key is required.");
            return;
        }

        var keyItem = ParseItem(keyElement);
        var (pk, sk, error) = ExtractKeys(keyItem, table);
        if (error is not null)
        {
            _logger.LogError("[ERROR] GetItem failed - Key extraction error: {Error}", error);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", error);
            return;
        }

        using var activity = KiteActivitySource.DynamoDB.StartActivity("dynamodb.get_item");
        activity?.SetTag("table.name", tableName);
        activity?.SetTag("partition.key", pk);

        var item = table.GetItem(pk!, sk);
        LogHelpers.AddRequestLog(table.RequestLog, "GetItem", $"Table={tableName} PK={pk}");

        _logger.LogInformation("GetItem completed - TableName: {TableName}, PartitionKey: {PartitionKey}, SortKey: {SortKey}, ItemFound: {ItemFound}", tableName, pk, sk ?? "<none>", item is not null);

        context.Response.ContentType = ContentType;
        if (item is not null)
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { Item = item }));
        else
            await context.Response.WriteAsync("{}");
    }

    private async Task HandleDeleteItemAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleDeleteItemAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] DeleteItem failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        if (!root.TryGetProperty("Key", out var keyElement))
        {
            _logger.LogError("[ERROR] DeleteItem failed - Key element is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "Key is required.");
            return;
        }

        var keyItem = ParseItem(keyElement);
        var (pk, sk, error) = ExtractKeys(keyItem, table);
        if (error is not null)
        {
            _logger.LogError("[ERROR] DeleteItem failed - Key extraction error: {Error}", error);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", error);
            return;
        }

        using var activity = KiteActivitySource.DynamoDB.StartActivity("dynamodb.delete_item");
        activity?.SetTag("table.name", tableName);
        activity?.SetTag("partition.key", pk);

        table.DeleteItem(pk!, sk);
        LogHelpers.AddRequestLog(table.RequestLog, "DeleteItem", $"Table={tableName} PK={pk}");
        table.NotifyChange();

        _logger.LogInformation("DeleteItem completed - TableName: {TableName}, PartitionKey: {PartitionKey}, SortKey: {SortKey}", tableName, pk, sk ?? "<none>");

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleUpdateItemAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleUpdateItemAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] UpdateItem failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        if (!root.TryGetProperty("Key", out var keyElement))
        {
            _logger.LogError("[ERROR] UpdateItem failed - Key element is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "Key is required.");
            return;
        }

        var keyItem = ParseItem(keyElement);
        var (pk, sk, keyError) = ExtractKeys(keyItem, table);
        if (keyError is not null)
        {
            _logger.LogError("[ERROR] UpdateItem failed - Key extraction error: {Error}", keyError);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", keyError);
            return;
        }

        // Get or create the item
        var existingItem = table.GetItem(pk!, sk) ?? new Dictionary<string, JsonElement>(keyItem);

        // Apply UpdateExpression
        var updateExpression = GetString(root, "UpdateExpression");
        _logger.LogDebug("[DEBUG] UpdateItem - UpdateExpression: {UpdateExpression}", updateExpression ?? "<none>");
        
        if (!string.IsNullOrWhiteSpace(updateExpression))
        {
            var exprAttrNames = ParseExpressionAttributeNames(root);
            var exprAttrValues = ParseExpressionAttributeValues(root);
            ApplyUpdateExpression(existingItem, updateExpression, exprAttrNames, exprAttrValues);
            _logger.LogDebug("[DEBUG] UpdateItem - Applied update expression with {NameCount} names and {ValueCount} values", exprAttrNames.Count, exprAttrValues.Count);
        }

        table.PutItem(pk!, sk, existingItem);
        LogHelpers.AddRequestLog(table.RequestLog, "UpdateItem", $"Table={tableName} PK={pk}");
        table.NotifyChange();

        _logger.LogInformation("UpdateItem completed - TableName: {TableName}, PartitionKey: {PartitionKey}, SortKey: {SortKey}, UpdateExpression: {UpdateExpression}", 
            tableName, pk, sk ?? "<none>", updateExpression ?? "<none>");

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleQueryAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleQueryAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] Query failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        var keyCondExpr = GetString(root, "KeyConditionExpression");
        if (string.IsNullOrWhiteSpace(keyCondExpr))
        {
            _logger.LogError("[ERROR] Query failed - KeyConditionExpression is required");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException", "KeyConditionExpression is required.");
            return;
        }

        var exprAttrNames = ParseExpressionAttributeNames(root);
        var exprAttrValues = ParseExpressionAttributeValues(root);

        var resolvedExpr = ResolveAttributeNames(keyCondExpr, exprAttrNames);
        var (pkValue, skCondition) = ParseKeyConditionExpression(resolvedExpr, exprAttrValues, table);

        if (string.IsNullOrEmpty(pkValue))
        {
            _logger.LogError("[ERROR] Query failed - Invalid KeyConditionExpression, partition key not found");
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ValidationException",
                "Invalid or unsupported KeyConditionExpression. Ensure the partition key condition is present and all expression attribute values are provided.");
            return;
        }

        var scanIndexForward = !root.TryGetProperty("ScanIndexForward", out var sif) || sif.GetBoolean();
        var results = table.Query(pkValue, skCondition, scanIndexForward);

        using var activity = KiteActivitySource.DynamoDB.StartActivity("dynamodb.query");
        activity?.SetTag("table.name", tableName);
        activity?.SetTag("partition.key", pkValue);
        activity?.SetTag("result.count", results.Count);

        LogHelpers.AddRequestLog(table.RequestLog, "Query", $"Table={tableName} PK={pkValue} Count={results.Count}");
        _logger.LogInformation("Query completed - TableName: {TableName}, PartitionKey: {PartitionKey}, KeyConditionExpression: {KeyCondExpr}, ResultCount: {ResultCount}, ScanIndexForward: {ScanIndexForward}", 
            tableName, pkValue, keyCondExpr, results.Count, scanIndexForward);

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Count = results.Count,
            Items = results,
            ScannedCount = results.Count
        }));
    }

    private async Task HandleScanAsync(HttpContext context, JsonElement root)
    {
        var tableName = GetString(root, "TableName");
        _logger.LogDebug("[ENTRY] HandleScanAsync - TableName: {TableName}", tableName ?? "<null>");
        
        var table = tableName is not null ? _registry.GetDynamoDbTable(tableName) : null;

        if (table is null)
        {
            _logger.LogError("[ERROR] Scan failed - Table not found: {TableName}", tableName);
            context.Response.StatusCode = 400;
            await WriteErrorAsync(context, "ResourceNotFoundException", $"Requested resource not found: {tableName}");
            return;
        }

        using var activity = KiteActivitySource.DynamoDB.StartActivity("dynamodb.scan");
        activity?.SetTag("table.name", tableName);

        var items = table.Scan();
        activity?.SetTag("result.count", items.Count);
        LogHelpers.AddRequestLog(table.RequestLog, "Scan", $"Table={tableName} Count={items.Count}");
        _logger.LogInformation("Scan completed - TableName: {TableName}, ResultCount: {ResultCount}", tableName, items.Count);

        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Count = items.Count,
            Items = items,
            ScannedCount = items.Count
        }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private object BuildTableDescription(DynamoDbTable table)
    {
        var tableArn = $"arn:aws:dynamodb:{_registry.Region}:{_registry.AccountId}:table/{table.TableName}";

        var keySchema = new List<object>
        {
            new { AttributeName = table.PartitionKeyName, KeyType = "HASH" }
        };
        if (table.SortKeyName is not null)
            keySchema.Add(new { AttributeName = table.SortKeyName, KeyType = "RANGE" });

        return new
        {
            TableName = table.TableName,
            TableStatus = table.TableStatus,
            TableArn = tableArn,
            BillingModeSummary = new { BillingMode = table.BillingMode },
            KeySchema = keySchema,
            AttributeDefinitions = table.AttributeDefinitions.Select(a => new
            {
                a.AttributeName,
                a.AttributeType
            }).ToList(),
            ItemCount = table.ItemCount(),
            CreationDateTime = new DateTimeOffset(table.CreatedAt).ToUnixTimeSeconds()
        };
    }

    private static List<(string AttributeName, string KeyType)> ParseKeySchema(JsonElement root)
    {
        if (!root.TryGetProperty("KeySchema", out var ks) || ks.ValueKind != JsonValueKind.Array)
            return [];

        return ks.EnumerateArray()
            .Select(e => (
                AttributeName: e.TryGetProperty("AttributeName", out var an) ? an.GetString() ?? "" : "",
                KeyType: e.TryGetProperty("KeyType", out var kt) ? kt.GetString() ?? "" : ""
            ))
            .ToList();
    }

    private static List<DynamoDbAttributeDefinition> ParseAttributeDefinitions(JsonElement root)
    {
        if (!root.TryGetProperty("AttributeDefinitions", out var ad) || ad.ValueKind != JsonValueKind.Array)
            return [];

        return ad.EnumerateArray()
            .Select(e => new DynamoDbAttributeDefinition
            {
                AttributeName = e.TryGetProperty("AttributeName", out var an) ? an.GetString() ?? "" : "",
                AttributeType = e.TryGetProperty("AttributeType", out var at) ? at.GetString() ?? "S" : "S"
            })
            .ToList();
    }

    private static Dictionary<string, JsonElement> ParseItem(JsonElement element)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in element.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();

        return result;
    }

    private static (string? pk, string sk, string? error) ExtractKeys(
        Dictionary<string, JsonElement> item, DynamoDbTable table)
    {
        if (!item.TryGetValue(table.PartitionKeyName, out var pkAttr))
            return (null, "", $"Missing partition key attribute: {table.PartitionKeyName}");

        var pk = ExtractScalarValue(pkAttr);

        var sk = "";
        if (table.SortKeyName is not null)
        {
            if (!item.TryGetValue(table.SortKeyName, out var skAttr))
                return (null, "", $"Missing sort key attribute: {table.SortKeyName}");
            sk = ExtractScalarValue(skAttr);
        }

        return (pk, sk, null);
    }

    private static string ExtractScalarValue(JsonElement attrValue)
    {
        if (attrValue.TryGetProperty("S", out var s)) return s.GetString() ?? "";
        if (attrValue.TryGetProperty("N", out var n)) return n.GetString() ?? "";
        if (attrValue.TryGetProperty("B", out var b)) return b.GetString() ?? "";
        return attrValue.GetRawText();
    }

    private static Dictionary<string, string> ParseExpressionAttributeNames(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("ExpressionAttributeNames", out var names) || names.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in names.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? prop.Name;

        return result;
    }

    private static Dictionary<string, JsonElement> ParseExpressionAttributeValues(JsonElement root)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!root.TryGetProperty("ExpressionAttributeValues", out var values) || values.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in values.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();

        return result;
    }

    private static string ResolveAttributeNames(string expression, Dictionary<string, string> exprAttrNames)
    {
        foreach (var (alias, name) in exprAttrNames)
            expression = expression.Replace(alias, name, StringComparison.Ordinal);
        return expression;
    }

    private static (string pkValue, Func<string, bool>? skCondition) ParseKeyConditionExpression(
        string expression,
        Dictionary<string, JsonElement> exprAttrValues,
        DynamoDbTable table)
    {
        expression = Regex.Replace(expression.Trim(), @"\s+", " ");

        // Attribute name pattern: word chars plus common non-reserved chars (-, .)
        const string attrName = @"[\w.\-]+";
        const string placeholder = @":\w+";

        // begins_with(sk, :val)
        var beginsWithMatch = Regex.Match(expression,
            $@"^({attrName})\s*=\s*({placeholder})\s+AND\s+begins_with\s*\(\s*({attrName})\s*,\s*({placeholder})\s*\)$",
            RegexOptions.IgnoreCase);
        if (beginsWithMatch.Success)
        {
            var pkVal = GetExprValue(exprAttrValues, beginsWithMatch.Groups[2].Value);
            var prefix = GetExprValue(exprAttrValues, beginsWithMatch.Groups[4].Value);
            return (pkVal, sk => sk.StartsWith(prefix, StringComparison.Ordinal));
        }

        // BETWEEN
        var betweenMatch = Regex.Match(expression,
            $@"^({attrName})\s*=\s*({placeholder})\s+AND\s+({attrName})\s+BETWEEN\s+({placeholder})\s+AND\s+({placeholder})$",
            RegexOptions.IgnoreCase);
        if (betweenMatch.Success)
        {
            var pkVal = GetExprValue(exprAttrValues, betweenMatch.Groups[2].Value);
            var lo = GetExprValue(exprAttrValues, betweenMatch.Groups[4].Value);
            var hi = GetExprValue(exprAttrValues, betweenMatch.Groups[5].Value);
            var skType = table.SortKeyType;
            return (pkVal, sk => CompareSkValues(sk, lo, skType) >= 0 && CompareSkValues(sk, hi, skType) <= 0);
        }

        // pk = :pk AND sk op :sk
        var andMatch = Regex.Match(expression,
            $@"^({attrName})\s*=\s*({placeholder})\s+AND\s+({attrName})\s*(=|<=|>=|<|>)\s*({placeholder})$",
            RegexOptions.IgnoreCase);
        if (andMatch.Success)
        {
            var pkVal = GetExprValue(exprAttrValues, andMatch.Groups[2].Value);
            var op = andMatch.Groups[4].Value;
            var skVal = GetExprValue(exprAttrValues, andMatch.Groups[5].Value);
            var skType = table.SortKeyType;
            Func<string, bool> skCond = op switch
            {
                "=" => sk => sk == skVal,
                "<" => sk => CompareSkValues(sk, skVal, skType) < 0,
                "<=" => sk => CompareSkValues(sk, skVal, skType) <= 0,
                ">" => sk => CompareSkValues(sk, skVal, skType) > 0,
                ">=" => sk => CompareSkValues(sk, skVal, skType) >= 0,
                _ => _ => true
            };
            return (pkVal, skCond);
        }

        // pk = :pk only
        var simpleMatch = Regex.Match(expression, $@"^({attrName})\s*=\s*({placeholder})$");
        if (simpleMatch.Success)
        {
            var pkVal = GetExprValue(exprAttrValues, simpleMatch.Groups[2].Value);
            return (pkVal, null);
        }

        return ("", null);
    }

    /// <summary>
    /// Compares two sort key values using numeric ordering when the sort key type is "N",
    /// falling back to ordinal string comparison for string and binary types.
    /// </summary>
    private static int CompareSkValues(string x, string y, string? sortKeyType)
    {
        if (sortKeyType == "N" && decimal.TryParse(x, out var nx) && decimal.TryParse(y, out var ny))
            return nx.CompareTo(ny);
        return string.Compare(x, y, StringComparison.Ordinal);
    }

    private static void ApplyUpdateExpression(
        Dictionary<string, JsonElement> item,
        string updateExpression,
        Dictionary<string, string> exprAttrNames,
        Dictionary<string, JsonElement> exprAttrValues)
    {
        var resolved = ResolveAttributeNames(updateExpression, exprAttrNames);

        var setClause = ExtractClause(resolved, "SET");
        var removeClause = ExtractClause(resolved, "REMOVE");

        if (!string.IsNullOrWhiteSpace(setClause))
        {
            foreach (var assignment in setClause.Split(','))
            {
                var eqIdx = assignment.IndexOf('=');
                if (eqIdx < 0) continue;
                var attrName = assignment[..eqIdx].Trim();
                var valRef = assignment[(eqIdx + 1)..].Trim();
                if (exprAttrValues.TryGetValue(valRef, out var val))
                    item[attrName] = val;
            }
        }

        if (!string.IsNullOrWhiteSpace(removeClause))
        {
            foreach (var attr in removeClause.Split(','))
                item.Remove(attr.Trim());
        }
    }

    private static string? ExtractClause(string expression, string clauseKeyword)
    {
        var pattern = $@"\b{clauseKeyword}\b\s+(.+?)(?=\s+\b(?:SET|REMOVE|ADD|DELETE)\b|$)";
        var match = Regex.Match(expression, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string GetExprValue(Dictionary<string, JsonElement> exprAttrValues, string placeholder)
    {
        if (!exprAttrValues.TryGetValue(placeholder, out var el))
            return "";
        return ExtractScalarValue(el);
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var p) ? p.GetString() : null;

    private static Task WriteErrorAsync(HttpContext context, string code, string message)
    {
        context.Response.ContentType = ContentType;
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            __type = code,
            message
        }));
    }
}
