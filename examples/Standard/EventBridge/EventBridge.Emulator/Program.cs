using Kite.Core;
using Kite.Host;

// Example – Kite Host (EventBridge → Lambda)
//
// This project starts the Kite with:
//   • An EventBridge bus named "orders"
//   • A rule that routes events with source "myapp.orders" to the OrderEventHandler Lambda
//
// Run this project first, then run EventBridge.Publisher to send events.

var lambdaDll = Path.Combine(AppContext.BaseDirectory, "EventBridge.Lambda.dll");

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddEventBridgeBus("orders")
    .AddLambda("order-event-handler", lb => lb
        .WithHandler("EventBridge.Lambda::EventBridge.Lambda.OrderEventHandler::Handle")
        .WithDll(lambdaDll))
    .AddEventBridgeRule("route-orders", rb => rb
        .OnBus("orders")
        .MatchSource("myapp.orders")
        .TargetLambda("order-event-handler"))
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("EventBridge bus: orders");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
