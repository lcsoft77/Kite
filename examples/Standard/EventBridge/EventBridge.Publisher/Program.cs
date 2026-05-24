using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using System.Text.Json;

// Example – EventBridge Event Publisher
//
// This project sends a series of order events to the "orders" EventBridge bus
// managed by the local Kite (started by EventBridge.Emulator).
//
// Make sure EventBridge.Emulator is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string busName = "orders";

var credentials = new BasicAWSCredentials("test", "test");
using var ebClient = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

Console.WriteLine($"Publishing events to bus '{busName}'...");
Console.WriteLine();

for (int i = 1; i <= 5; i++)
{
    var orderEvent = new
    {
        OrderId = i,
        Customer = $"customer-{i}",
        Amount = Math.Round(i * 99.99, 2),
        Status = "Created",
        Timestamp = DateTime.UtcNow
    };

    var response = await ebClient.PutEventsAsync(new PutEventsRequest
    {
        Entries =
        [
            new PutEventsRequestEntry
            {
                EventBusName = busName,
                Source = "myapp.orders",
                DetailType = "OrderCreated",
                Detail = JsonSerializer.Serialize(orderEvent)
            }
        ]
    });

    var status = response.FailedEntryCount == 0 ? "OK" : "FAILED";
    Console.WriteLine($"Published order {i:D2}: [{status}]");
}

Console.WriteLine();
Console.WriteLine("All 5 events published. Check EventBridge.Emulator logs to see them being processed.");
