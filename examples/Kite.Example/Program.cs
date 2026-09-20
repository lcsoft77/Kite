using Kite.Core;
using Kite.Dashboard;

// Example showing how to use the fluent API with the Blazor dashboard
// The dashboard is available at http://localhost:5000/
var emulator = KiteBuilder.Create()
    .WithPort(5000)
    .WithRegion("us-east-1")
    .WithCredentials("test", "test")
    .AddSQSQueue("my-queue")
    .AddS3Bucket("my-bucket")
    .AddEventBridgeBus("default")
    .Build()
    .UseDashboard();

await emulator.RunAsync();
