# Kite

**Kite** is an open-source AWS cloud emulator written in C# targeting .NET 10, similar in concept to [LocalStack](https://localstack.cloud/). It allows developers to run and test AWS services locally with full debugging support, OpenTelemetry integration, and seamless .NET Aspire compatibility.

## Key Features

- **9 AWS Services** — Lambda, SQS, S3, DynamoDB, SNS, EventBridge, SSM, ECS, and API Gateway
- **In-Process Lambda Execution** — Run and debug .NET Lambda functions directly in your IDE with full breakpoint support
- **AWS SDK Compatible** — Use the official AWS SDK by overriding the service endpoint to `http://localhost:4566`
- **LocalStack Compatible** — Exposes a `/_localstack/health` endpoint for tool compatibility
- **.NET Aspire Integration** — First-class integration with .NET Aspire including dashboard visibility
- **OpenTelemetry** — Built-in tracing, metrics, and logging via OTLP export
- **Event-Driven Triggers** — SQS → Lambda, S3 → Lambda, EventBridge → Lambda, API Gateway → Lambda
- **Fluent Configuration** — Builder pattern API for easy resource setup
- **Built-in Dashboard** — Blazor Server UI for monitoring all emulated services

## Wiki Pages

### Getting Started
- [Getting Started](Getting-Started) — Installation, setup, and first run
- [Configuration](Configuration) — Builder pattern API and configuration options
- [Architecture](Architecture) — System design and technical decisions

### AWS Services
- [Lambda](Lambda) — AWS Lambda function execution and management
- [SQS](SQS) — Amazon Simple Queue Service
- [S3](S3) — Amazon Simple Storage Service
- [DynamoDB](DynamoDB) — Amazon DynamoDB NoSQL database
- [SNS](SNS) — Amazon Simple Notification Service
- [EventBridge](EventBridge) — Amazon EventBridge event bus
- [SSM](SSM) — AWS Systems Manager Parameter Store
- [ECS](ECS) — Amazon Elastic Container Service
- [API Gateway](API-Gateway) — Amazon API Gateway

### Integration & Tools
- [Aspire Integration](Aspire-Integration) — .NET Aspire integration guide
- [Dashboard](Dashboard) — Built-in monitoring dashboard
- [Packaging](Packaging) — NuGet package layout and release process

## Quick Example

```csharp
var emulator = KiteBuilder.Create()
    .WithPort(4566)
    .AddLambda("MyFunction", lambda => lambda
        .WithHandler("MyProject::MyNamespace.Function::FunctionHandler")
        .WithDll("path/to/MyProject.dll"))
    .AddSQSQueue("my-queue")
    .AddSQSTrigger("my-queue", "MyFunction", batchSize: 10)
    .AddS3Bucket("my-bucket")
    .Build()
    .UseHost();

await emulator.RunAsync();
```

## Supported AWS Operations

| Service | Operations |
|---------|-----------|
| Lambda | CreateFunction, ListFunctions, GetFunction, DeleteFunction, Invoke, Event Source Mappings |
| SQS | CreateQueue, SendMessage, ReceiveMessage, DeleteMessage, GetQueueUrl, ListQueues, DeleteQueue |
| S3 | CreateBucket, PutObject, GetObject, DeleteObject, ListBuckets, ListObjectsV2, HeadObject |
| DynamoDB | CreateTable, PutItem, GetItem, DeleteItem, UpdateItem, Query, Scan, ListTables |
| SNS | CreateTopic, Publish, Subscribe, Unsubscribe, ListTopics, ListSubscriptions |
| EventBridge | PutEvents, CreateEventBus, PutRule, PutTargets, ListRules, ListEventBuses |
| SSM | GetParameter, PutParameter, DeleteParameter, GetParametersByPath, DescribeParameters |
| ECS | CreateCluster, RegisterTaskDefinition, RunTask, StopTask, DescribeTasks, ListClusters |
| API Gateway | Route matching, Lambda proxy integration, path parameter extraction |

## License

This project is licensed under the terms specified in the [LICENSE](https://github.com/lcsoft77/Kite/blob/main/LICENSE) file.
