using Kite.Core;
using Kite.Host;

// Example 2 – Kite Host
//
// This project starts the Kite with:
//   • An SQS queue named "orders-queue"
//   • The OrderProcessorFunction Lambda registered against that queue
//
// Run this project first, then run SQS.Sender to send messages.

var lambdaDll = Path.Combine(AppContext.BaseDirectory, "SQS.Lambda.dll");

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddSQSQueue("orders-queue")
    .AddLambda("order-processor", lb => lb
        .WithHandler("SQS.Lambda::SQS.Lambda.OrderProcessorFunction::Handle")
        .WithDll(lambdaDll))
    .AddSQSTrigger("orders-queue", "order-processor", batchSize: 5)
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("SQS queue:  http://localhost:4566/000000000000/orders-queue");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
