# .NET Aspire Integration

Kite provides first-class integration with [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/), allowing the emulator to run as part of an Aspire orchestration with full dashboard visibility.

## Overview

The `Kite.Aspire` package provides:

- An `IDistributedApplicationLifecycleHook` to run the emulator in-process within the Aspire AppHost
- Builder extensions to register AWS resources as Aspire child resources
- Dashboard visibility for all emulated services
- OpenTelemetry integration (traces, metrics, and logs appear in the Aspire dashboard)
- Restart commands for Lambda functions

## Setup

### Add the Aspire Package

Reference `Kite.Aspire` in your AppHost project.

### Configure in AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var emulator = builder.AddKite()
    .WithLambdaFunction(
        name: "OrderProcessor",
        dllPath: "../Orders/bin/Debug/net10.0/Orders.dll",
        handler: "Orders::Orders.Function::ProcessOrder")
    .WithSQSQueue("order-queue")
    .WithS3Bucket("uploads")
    .WithEventBridgeBus("events")
    .WithSQSTrigger("order-queue", "OrderProcessor", batchSize: 10);

builder.Build().Run();
```

## Dashboard Resources

When configured, the following resources appear as child items in the Aspire dashboard:

| Resource Type | Dashboard Display |
|--------------|-------------------|
| Lambda Functions | Shows function name, handler, and status |
| SQS Queues | Shows queue name |
| S3 Buckets | Shows bucket name |
| SNS Topics | Shows topic name |
| EventBridge Buses | Shows bus name |
| DynamoDB Tables | Shows table name |
| ECS Clusters | Shows cluster name |

## Aspire Resource Types

The package defines Aspire-compatible resource types:

- `KiteResource` — The main emulator resource
- `LambdaFunctionResource` — Individual Lambda function
- `SqsQueueResource` — SQS queue
- `S3BucketResource` — S3 bucket
- `SnsTopicResource` — SNS topic
- `EventBridgeBusResource` — EventBridge event bus
- `DynamoDbTableResource` — DynamoDB table
- `EcsClusterResource` — ECS cluster

## Lambda Restart Command

Lambda functions in the Aspire dashboard include a restart command that allows you to reload the function assembly without restarting the entire emulator. This is useful during development when you recompile your Lambda function.

## OpenTelemetry

The emulator automatically exports telemetry to the Aspire dashboard:

- **Traces** — Request traces for all AWS API calls
- **Metrics** — ASP.NET Core request metrics
- **Logs** — Application logs from the emulator and Lambda functions

The Aspire integration includes a custom logger provider that routes emulator logs through the Aspire logging pipeline.

## Port Configuration

By default, the emulator listens on port `4566`. In the Aspire integration, this port is exposed as an HTTP endpoint on the emulator resource.

## Examples

See the `examples/` directory for complete working examples:

| Example | Description |
|---------|-------------|
| `examples/AppHost1/` | Basic Aspire AppHost with emulator |
| `examples/SQS.Aspire/` | SQS → Lambda trigger with Aspire |

### SQS.Aspire Example

```
examples/SQS.Aspire/
├── SQS.AppHost/       # Aspire AppHost (configures emulator)
├── SQS.Lambda/        # Lambda function handler
└── SQS.Sender/        # Client that sends SQS messages
```

Run the example:

```bash
cd examples/SQS.Aspire/SQS.AppHost
dotnet run
```

The Aspire dashboard will open showing the emulator and all registered resources.
