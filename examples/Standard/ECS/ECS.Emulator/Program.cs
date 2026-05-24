using Kite.Core;
using Kite.Host;

// Example – Kite Host (ECS)
//
// This project starts the Kite with:
//   • An ECS cluster named "default"
//   • A task definition "worker-task" with a single container
//
// Run this project first, then run ECS.Sender to interact with the cluster.

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddEcsCluster("default")
    .AddEcsTaskDefinition("worker-task", td => td
        .WithCpu(256)
        .WithMemory(512)
        .WithContainer("worker", "worker:latest", c => c
            .WithCpu(256)
            .WithMemory(512)
            .WithPortMapping(containerPort: 8080)))
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("ECS cluster: default");
Console.WriteLine("Task definition: worker-task");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
