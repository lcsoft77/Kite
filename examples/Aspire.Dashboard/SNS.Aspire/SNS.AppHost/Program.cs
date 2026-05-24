using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (SNS) + Dashboard
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  The SNS topic appears as a child resource in the Aspire dashboard
// with integrated logs.
//
// The Kite dashboard is also available on a separate port (emulator port + 1)
// and provides a real-time view of queues, buckets, topics and Lambda functions.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// SNS.Publisher to publish messages to the topic.

var builder = DistributedApplication.CreateBuilder(args);


var kite = builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithSnsTopic("notifications")
    .WithDashboard();

builder.AddProject<Projects.SNS_Publisher>("SNSPublisher")
    .WaitFor(kite);   

builder.Build().Run();
