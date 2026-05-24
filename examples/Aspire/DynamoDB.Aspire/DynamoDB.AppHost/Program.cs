using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (DynamoDB)
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  The DynamoDB table appears as a child resource in the Aspire
// dashboard with integrated logs.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// DynamoDB.Client to perform CRUD operations against the table.

var builder = DistributedApplication.CreateBuilder(args);

var kite = builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithDynamoDbTable(
        tableName: "products",
        partitionKeyName: "ProductId",
        partitionKeyType: "S",
        sortKeyName: "Category",
        sortKeyType: "S");

builder.AddProject<Projects.DynamoDB_Client>("DynamoDBClient")
    .WaitFor(kite);

builder.Build().Run();
