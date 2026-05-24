using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (SSM Parameter Store) + Dashboard
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host with pre-seeded SSM parameters visible in the Aspire dashboard.
//
// The Kite dashboard is also available on a separate port (emulator port + 1)
// and provides a real-time view of queues, buckets, topics and Lambda functions.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// SSM.Client to read the parameters.

var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithSsmParameter("/myapp/database/connection-string", "Server=db;Database=myapp;User=admin;Password=secret", "String")
    .WithSsmParameter("/myapp/api/key", "super-secret-api-key-12345", "SecureString")
    .WithSsmParameter("/myapp/feature-flags", "dark-mode,new-dashboard,beta-search", "StringList")
    .WithDashboard();

builder.Build().Run();
