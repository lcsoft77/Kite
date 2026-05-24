using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (ECS)
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  The ECS cluster and task definition appear as child resource rows
// in the Aspire dashboard with integrated logs.
//
// Run this project (it starts the Aspire dashboard automatically), then ECS.Sender
// will be launched automatically by Aspire and will interact with the cluster.

var builder = DistributedApplication.CreateBuilder(args);

var kite = builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithEcsCluster("default")
    .WithEcsTaskDefinition<Projects.ECS_Task>("worker-task", td => td
        .WithCpu(256)
        .WithMemory(512)
        .WithEnvironmentVariable("ENV_VAR_1", "value1"));

builder.AddProject<Projects.ECS_Sender>("ecs-sender")
    .WaitFor(kite);

builder.Build().Run();
