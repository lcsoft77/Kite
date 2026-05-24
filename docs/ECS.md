# Amazon ECS

Kite provides Amazon Elastic Container Service (ECS) emulation supporting cluster management, task definitions, and task execution.

## Overview

The ECS service uses the JSON protocol via the `X-Amz-Target` header with target prefix `AmazonEC2ContainerServiceV20141113`.

**Routing:**
- Header: `X-Amz-Target: AmazonEC2ContainerServiceV20141113.*`
- Content-Type: `application/x-amz-json-1.1`
- SigV4 service name: `ecs`

## Supported Operations

### Cluster Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `CreateCluster` | `...CreateCluster` | Create a new ECS cluster |
| `DeleteCluster` | `...DeleteCluster` | Delete a cluster |
| `DescribeClusters` | `...DescribeClusters` | Describe one or more clusters |
| `ListClusters` | `...ListClusters` | List all clusters |

### Task Definition Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `RegisterTaskDefinition` | `...RegisterTaskDefinition` | Register a new task definition |
| `DeregisterTaskDefinition` | `...DeregisterTaskDefinition` | Deregister a task definition |
| `DescribeTaskDefinition` | `...DescribeTaskDefinition` | Describe a task definition |
| `ListTaskDefinitions` | `...ListTaskDefinitions` | List task definitions |
| `ListTaskDefinitionFamilies` | `...ListTaskDefinitionFamilies` | List task definition families |

### Task Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `RunTask` | `...RunTask` | Run a task on a cluster |
| `StopTask` | `...StopTask` | Stop a running task |
| `DescribeTasks` | `...DescribeTasks` | Describe one or more tasks |
| `ListTasks` | `...ListTasks` | List tasks in a cluster |

## AWS SDK Usage

### Creating a Cluster

```csharp
var ecsClient = new AmazonECSClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonECSConfig { ServiceURL = "http://localhost:4566" });

var response = await ecsClient.CreateClusterAsync(new CreateClusterRequest
{
    ClusterName = "my-cluster"
});

Console.WriteLine($"Cluster ARN: {response.Cluster.ClusterArn}");
```

### Registering a Task Definition

```csharp
var response = await ecsClient.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
{
    Family = "my-web-app",
    ContainerDefinitions = new List<ContainerDefinition>
    {
        new ContainerDefinition
        {
            Name = "web",
            Image = "nginx:latest",
            Memory = 512,
            PortMappings = new List<PortMapping>
            {
                new PortMapping { ContainerPort = 80, HostPort = 8080 }
            }
        }
    }
});

Console.WriteLine($"Task Definition: {response.TaskDefinition.TaskDefinitionArn}");
```

Task definitions use auto-incrementing revision numbers per family (e.g., `my-web-app:1`, `my-web-app:2`).

### Running a Task

```csharp
var response = await ecsClient.RunTaskAsync(new RunTaskRequest
{
    Cluster = "my-cluster",
    TaskDefinition = "my-web-app",
    Count = 1
});

foreach (var task in response.Tasks)
{
    Console.WriteLine($"Task ARN: {task.TaskArn}");
    Console.WriteLine($"Status: {task.LastStatus}");
}
```

### Stopping a Task

```csharp
await ecsClient.StopTaskAsync(new StopTaskRequest
{
    Cluster = "my-cluster",
    Task = "arn:aws:ecs:us-east-1:000000000000:task/my-cluster/abc123",
    Reason = "Scaling down"
});
```

### Listing Tasks

```csharp
var response = await ecsClient.ListTasksAsync(new ListTasksRequest
{
    Cluster = "my-cluster"
});

foreach (var taskArn in response.TaskArns)
{
    Console.WriteLine($"Task: {taskArn}");
}
```

### Describing Tasks

```csharp
var response = await ecsClient.DescribeTasksAsync(new DescribeTasksRequest
{
    Cluster = "my-cluster",
    Tasks = new List<string> { "arn:aws:ecs:us-east-1:000000000000:task/my-cluster/abc123" }
});

foreach (var task in response.Tasks)
{
    Console.WriteLine($"Task: {task.TaskArn}, Status: {task.LastStatus}");
}
```

## Task Definition Versioning

Task definitions use automatic revision numbering:
- First registration of family `my-app` creates `my-app:1`
- Next registration creates `my-app:2`
- Revisions are stored with key format `{family}:{revision}`

## Storage

All ECS clusters, task definitions, and tasks are stored in memory and do not persist across emulator restarts.
