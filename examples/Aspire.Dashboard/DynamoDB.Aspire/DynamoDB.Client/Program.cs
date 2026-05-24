using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

// Example – DynamoDB Client (Aspire + Dashboard variant)
//
// This project performs CRUD operations against the "products" DynamoDB table
// managed by the local Kite running inside the Aspire App Host (DynamoDB.AppHost).
//
// Make sure DynamoDB.AppHost is running before executing this project.
// The Kite dashboard is available at http://localhost:5051 (emulator port + 1).

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string tableName = "products";

var credentials = new BasicAWSCredentials("test", "test");
using var dynamoClient = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

Console.WriteLine($"Writing items to table '{tableName}'...");
Console.WriteLine();

// Put items
var categories = new[] { "Electronics", "Books", "Clothing" };
for (int i = 1; i <= 5; i++)
{
    var category = categories[(i - 1) % categories.Length];
    await dynamoClient.PutItemAsync(new PutItemRequest
    {
        TableName = tableName,
        Item = new Dictionary<string, AttributeValue>
        {
            ["ProductId"] = new AttributeValue { S = $"prod-{i:D3}" },
            ["Category"] = new AttributeValue { S = category },
            ["Name"] = new AttributeValue { S = $"Product {i}" },
            ["Price"] = new AttributeValue { N = (i * 9.99).ToString("F2", CultureInfo.InvariantCulture) }
        }
    });
    Console.WriteLine($"Inserted: ProductId=prod-{i:D3}, Category={category}");
}

Console.WriteLine();
Console.WriteLine("Querying prod-001...");
Console.WriteLine();

// Query items
var queryResponse = await dynamoClient.QueryAsync(new QueryRequest
{
    TableName = tableName,
    KeyConditionExpression = "ProductId = :pk",
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":pk"] = new AttributeValue { S = "prod-001" }
    }
});

foreach (var item in queryResponse.Items)
{
    var productId = item["ProductId"].S;
    var category = item["Category"].S;
    var name = item.ContainsKey("Name") ? item["Name"].S : "(no name)";
    var price = item.ContainsKey("Price") ? item["Price"].N : "(no price)";
    Console.WriteLine($"  ProductId={productId}, Category={category}, Name={name}, Price={price}");
}

Console.WriteLine();
Console.WriteLine("DynamoDB operations complete. Check the Aspire and Kite dashboard logs for more details.");
