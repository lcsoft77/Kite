using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (SQS → Lambda)
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  Every emulator component (Lambda, SQS queue) appears as its own
// resource row in the Aspire dashboard with integrated logs and a Restart button for
// hot-reloading the Lambda assembly from disk.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// SQS.Sender to send order messages to the queue.

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the Lambda DLL from the AppHost output directory.
// The SQS.Lambda project is referenced, so its DLL is copied alongside this binary.
var lambdaDll = Path.Combine(AppContext.BaseDirectory, "SQS.Lambda.dll");

var kite = builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithSQSQueue("orders-queue")
    .WithLambdaFunction(
        name: "order-processor",
        dllPath: lambdaDll,
        handler: "SQS.Lambda::SQS.Lambda.OrderProcessorFunction::Handle")
    .WithSQSTrigger(queueName: "orders-queue", functionName: "order-processor", batchSize: 5);

builder.AddProject<Projects.SQS_Sender>("SQSSender")
    .WaitFor(kite);

builder.Build().Run();
