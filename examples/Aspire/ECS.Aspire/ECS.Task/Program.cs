
using System.Diagnostics;

Console.WriteLine($"Environment variable ENV_VAR_1: {Environment.GetEnvironmentVariable("ENV_VAR_1")}");

if (args.Contains("--debug"))
{
    Console.WriteLine($"[ECS.Task] Waiting for debugger to attach... PID={Environment.ProcessId}");
    Console.WriteLine("[ECS.Task] In VS Code: Run > Attach to Process, then select this PID.");
    while (!Debugger.IsAttached)
        Thread.Sleep(100);
    Console.WriteLine("[ECS.Task] Debugger attached.");
}

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


for (var i = 0; i < 500000; i++)
{
    Console.WriteLine($"Executing task {i}");
    Thread.Sleep(1000);
}
