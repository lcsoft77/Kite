using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;

// Example – ECS Sender
//
// This project interacts with the ECS emulator (started by ECS.Emulator):
//   1. Describe the "default" cluster
//   2. Register a new task definition (or use the existing "worker-task")
//   3. Run a task on the cluster
//   4. Describe the running task
//   5. Stop the task
//
// Make sure ECS.Emulator is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string clusterName = "default";
const string taskFamily = "worker-task";

var credentials = new BasicAWSCredentials("test", "test");
using var ecsClient = new AmazonECSClient(credentials, new AmazonECSConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

// --- 1. Describe cluster ---
Console.WriteLine("=== Describing cluster ===");
var describeClustersResponse = await ecsClient.DescribeClustersAsync(new DescribeClustersRequest
{
    Clusters = [clusterName]
});
foreach (var cluster in describeClustersResponse.Clusters)
    Console.WriteLine($"  Cluster: {cluster.ClusterName} | Status: {cluster.Status}");

// --- 2. Register a task definition ---
Console.WriteLine("\n=== Registering task definition ===");
var registerResponse = await ecsClient.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
{
    Family = taskFamily,
    Cpu = "256",
    Memory = "512",
    ContainerDefinitions =
    [
        new ContainerDefinition
        {
            Name = "worker",
            Image = "worker:latest",
            Cpu = 256,
            Memory = 512,
            Essential = true,
            PortMappings = [new PortMapping { ContainerPort = 8080 }],
            Environment =
            [
                new Amazon.ECS.Model.KeyValuePair { Name = "ENV", Value = "dev" },
                new Amazon.ECS.Model.KeyValuePair { Name = "LOG_LEVEL", Value = "info" }
            ]
        }
    ]
});
var taskDef = registerResponse.TaskDefinition;
Console.WriteLine($"  Registered: {taskDef.Family}:{taskDef.Revision} | Status: {taskDef.Status}");

// --- 3. Run task ---
Console.WriteLine("\n=== Running task ===");
var runResponse = await ecsClient.RunTaskAsync(new RunTaskRequest
{
    Cluster = clusterName,
    TaskDefinition = $"{taskFamily}:{taskDef.Revision}",
    Count = 1
});

if (runResponse.Failures.Count > 0)
{
    Console.WriteLine("  Run task failed:");
    foreach (var failure in runResponse.Failures)
        Console.WriteLine($"    {failure.Reason}");
    return;
}

var task = runResponse.Tasks[0];
var taskArn = task.TaskArn;
Console.WriteLine($"  Task started: {taskArn}");
Console.WriteLine($"  Status: {task.LastStatus}");

// --- 4. Describe running task ---
Console.WriteLine("\n=== Describing task ===");
var describeResponse = await ecsClient.DescribeTasksAsync(new DescribeTasksRequest
{
    Cluster = clusterName,
    Tasks = [taskArn]
});
var describedTask = describeResponse.Tasks[0];
Console.WriteLine($"  TaskArn:     {describedTask.TaskArn}");
Console.WriteLine($"  LastStatus:  {describedTask.LastStatus}");
Console.WriteLine($"  StartedAt:   {describedTask.StartedAt}");

// --- 5. Stop task ---
Console.WriteLine("\n=== Stopping task ===");
var stopResponse = await ecsClient.StopTaskAsync(new StopTaskRequest
{
    Cluster = clusterName,
    Task = taskArn,
    Reason = "Demo task stopped by ECS.Sender"
});
var stoppedTask = stopResponse.Task;
Console.WriteLine($"  Status: {stoppedTask.LastStatus}");
Console.WriteLine($"  Reason: {stoppedTask.StoppedReason}");

Console.WriteLine("\nDone.");
