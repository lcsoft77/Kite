using Kite.Core;
using Kite.Host;

// Example – Kite Host (DynamoDB)
//
// This project starts the Kite with:
//   • A DynamoDB table named "products" with partition key "ProductId" (String)
//     and sort key "Category" (String)
//
// Run this project first, then run DynamoDB.Client to perform CRUD operations.

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddDynamoDbTable(
        tableName: "products",
        partitionKeyName: "ProductId",
        partitionKeyType: "S",
        sortKeyName: "Category",
        sortKeyType: "S")
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("DynamoDB table: products (PK=ProductId, SK=Category)");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
