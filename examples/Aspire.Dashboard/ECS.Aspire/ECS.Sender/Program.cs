using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;

// Example – ECS Sender (Aspire)
//
// When run via ECS.AppHost, the emulator endpoint is available through the
// AWS__ServiceURL environment variable set by Aspire.
// When run standalone, it falls back to http://localhost:4566.

var emulatorUrl = Environment.GetEnvironmentVariable("AWS__ServiceURL") ?? "http://localhost:4566";
const string region = "eu-west-1";
const string clusterName = "default";
const string taskFamily = "worker-task";

Console.WriteLine($"Connecting to ECS emulator at {emulatorUrl}");

var credentials = new BasicAWSCredentials("test", "test");
using var ecsClient = new AmazonECSClient(credentials, new AmazonECSConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

var clustersResponse = await ecsClient.ListClustersAsync(new ListClustersRequest());
Console.WriteLine($"Clusters in emulator: {clustersResponse.ClusterArns.Count}");
foreach (var arn in clustersResponse.ClusterArns)
    Console.WriteLine($"  {arn}");

// --- 1. Describe cluster ---
Console.WriteLine("\n=== Describing cluster ===");
var describeClustersResponse = await ecsClient.DescribeClustersAsync(new DescribeClustersRequest
{
    Clusters = [clusterName]
});
foreach (var cluster in describeClustersResponse.Clusters)
    Console.WriteLine($"  Cluster: {cluster.ClusterName} | Status: {cluster.Status}");

var tasksResponse = await ecsClient.ListTasksAsync(new ListTasksRequest
{
    Cluster = clusterName
});
Console.WriteLine($"\nTasks in cluster '{clusterName}': {tasksResponse.TaskArns.Count}");
foreach (var arn in tasksResponse.TaskArns)
    Console.WriteLine($"  {arn}");

// --- 2. Look up task definition registered by the AppHost ---
Console.WriteLine("\n=== Looking up task definition ===");
Amazon.ECS.Model.TaskDefinition taskDef;
try
{
    var describeDefResponse = await ecsClient.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest
    {
        TaskDefinition = taskFamily
    });
    taskDef = describeDefResponse.TaskDefinition;
    Console.WriteLine($"  Found: {taskDef.Family}:{taskDef.Revision} | Status: {taskDef.Status}");
}
catch (Exception ex)
{
    Console.WriteLine($"  Task definition '{taskFamily}' not found: {ex.Message}");
    return;
}

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
Console.WriteLine($"  TaskArn:    {describedTask.TaskArn}");
Console.WriteLine($"  LastStatus: {describedTask.LastStatus}");
Console.WriteLine($"  StartedAt:  {describedTask.StartedAt}");

// --- 5. Stop task ---
/*Console.WriteLine("\n=== Stopping task ===");
var stopResponse = await ecsClient.StopTaskAsync(new StopTaskRequest
{
    Cluster = clusterName,
    Task = taskArn,
    Reason = "Demo task stopped by ECS.Sender"
});
var stoppedTask = stopResponse.Task;
Console.WriteLine($"  Status: {stoppedTask.LastStatus}");
Console.WriteLine($"  Reason: {stoppedTask.StoppedReason}");*/

Console.WriteLine("\nDone.");
