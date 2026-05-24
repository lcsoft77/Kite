using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (EventBridge → Lambda)
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  Every emulator component (Lambda, EventBridge bus) appears as its
// own resource row in the Aspire dashboard with integrated logs and a Restart button
// for hot-reloading the Lambda assembly from disk.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// EventBridge.Publisher to send order events to the bus.

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the Lambda DLL from the AppHost output directory.
// The EventBridge.Lambda project is referenced, so its DLL is copied alongside this binary.
var lambdaDll = Path.Combine(AppContext.BaseDirectory, "EventBridge.Lambda.dll");

builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithEventBridgeBus("orders")
    .WithLambdaFunction(
        name: "order-event-handler",
        dllPath: lambdaDll,
        handler: "EventBridge.Lambda::EventBridge.Lambda.OrderEventHandler::Handle")
    .WithEventBridgeRule("route-orders", rb => rb
        .OnBus("orders")
        .MatchSource("myapp.orders")
        .TargetLambda("order-event-handler"));

builder.Build().Run();
