# Amazon DynamoDB

Kite provides Amazon DynamoDB emulation supporting table management, item operations, and queries.

## Overview

The DynamoDB service uses the JSON protocol via the `X-Amz-Target` header with target prefix `DynamoDB_20120810`.

**Routing:**
- Header: `X-Amz-Target: DynamoDB_20120810.*`
- Content-Type: `application/x-amz-json-1.0`
- SigV4 service name: `dynamodb`

## Supported Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `CreateTable` | `DynamoDB_20120810.CreateTable` | Create a new table |
| `DeleteTable` | `DynamoDB_20120810.DeleteTable` | Delete a table |
| `DescribeTable` | `DynamoDB_20120810.DescribeTable` | Get table metadata |
| `ListTables` | `DynamoDB_20120810.ListTables` | List all tables |
| `PutItem` | `DynamoDB_20120810.PutItem` | Insert/replace an item |
| `GetItem` | `DynamoDB_20120810.GetItem` | Get an item by key |
| `DeleteItem` | `DynamoDB_20120810.DeleteItem` | Delete an item by key |
| `UpdateItem` | `DynamoDB_20120810.UpdateItem` | Update specific attributes |
| `Query` | `DynamoDB_20120810.Query` | Query items by partition key |
| `Scan` | `DynamoDB_20120810.Scan` | Scan all items in a table |

## Data Model

DynamoDB items are stored in a nested `SortedDictionary` structure:

```
Table → Partition Key → Sort Key → Item (attributes)
```

- **Partition Key (PK):** Required hash key for item distribution
- **Sort Key (SK):** Optional range key for ordering within a partition
- Sort keys support both string and numeric ordering

## AWS SDK Usage

### Creating a Table

```csharp
var dynamoClient = new AmazonDynamoDBClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonDynamoDBConfig { ServiceURL = "http://localhost:4566" });

await dynamoClient.CreateTableAsync(new CreateTableRequest
{
    TableName = "Orders",
    KeySchema = new List<KeySchemaElement>
    {
        new("CustomerId", KeyType.HASH),
        new("OrderId", KeyType.RANGE)
    },
    AttributeDefinitions = new List<AttributeDefinition>
    {
        new("CustomerId", ScalarAttributeType.S),
        new("OrderId", ScalarAttributeType.S)
    },
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Inserting an Item

```csharp
await dynamoClient.PutItemAsync(new PutItemRequest
{
    TableName = "Orders",
    Item = new Dictionary<string, AttributeValue>
    {
        ["CustomerId"] = new() { S = "customer-1" },
        ["OrderId"] = new() { S = "order-001" },
        ["Total"] = new() { N = "99.99" },
        ["Status"] = new() { S = "PENDING" }
    }
});
```

### Getting an Item

```csharp
var response = await dynamoClient.GetItemAsync(new GetItemRequest
{
    TableName = "Orders",
    Key = new Dictionary<string, AttributeValue>
    {
        ["CustomerId"] = new() { S = "customer-1" },
        ["OrderId"] = new() { S = "order-001" }
    }
});

Console.WriteLine($"Status: {response.Item["Status"].S}");
```

### Querying Items

```csharp
var response = await dynamoClient.QueryAsync(new QueryRequest
{
    TableName = "Orders",
    KeyConditionExpression = "CustomerId = :pk",
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":pk"] = new() { S = "customer-1" }
    }
});

foreach (var item in response.Items)
{
    Console.WriteLine($"Order: {item["OrderId"].S}, Total: {item["Total"].N}");
}
```

### Scanning Items

```csharp
var response = await dynamoClient.ScanAsync(new ScanRequest
{
    TableName = "Orders",
    FilterExpression = "#s = :status",
    ExpressionAttributeNames = new Dictionary<string, string>
    {
        ["#s"] = "Status"
    },
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":status"] = new() { S = "PENDING" }
    }
});
```

### Updating an Item

```csharp
await dynamoClient.UpdateItemAsync(new UpdateItemRequest
{
    TableName = "Orders",
    Key = new Dictionary<string, AttributeValue>
    {
        ["CustomerId"] = new() { S = "customer-1" },
        ["OrderId"] = new() { S = "order-001" }
    },
    UpdateExpression = "SET #s = :newStatus",
    ExpressionAttributeNames = new Dictionary<string, string>
    {
        ["#s"] = "Status"
    },
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":newStatus"] = new() { S = "COMPLETED" }
    }
});
```

## Storage

All DynamoDB data is stored in memory and does not persist across emulator restarts. Tables must be created each time the emulator starts (either through the builder API or via SDK calls at application startup).
