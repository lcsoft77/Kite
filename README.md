# Kite

An open-source AWS cloud emulator written in C# for .NET 10, similar to [LocalStack](https://localstack.cloud/). Run and test AWS services locally with full debugging support, OpenTelemetry integration, and seamless .NET Aspire compatibility.

## ✨ Features

- **9 AWS Services** — Lambda, SQS, S3, DynamoDB, SNS, EventBridge, SSM, ECS, and API Gateway
- **In-Process Lambda Execution** — Run and debug .NET Lambda functions directly in your IDE
- **AWS SDK Compatible** — Use the official AWS SDK with endpoint override to `http://localhost:4566`
- **LocalStack Compatible** — Exposes a `/_localstack/health` endpoint for tool compatibility
- **.NET Aspire Integration** — First-class support including dashboard visibility and OpenTelemetry
- **Event-Driven Triggers** — SQS → Lambda, S3 → Lambda, EventBridge → Lambda, API Gateway → Lambda
- **Fluent Builder API** — Simple configuration through a fluent builder pattern

## 🚀 Quick Start

```csharp
using Kite.Core;

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

Then point any AWS SDK client to the emulator:

```csharp
var sqsClient = new AmazonSQSClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonSQSConfig
    {
        ServiceURL = "http://localhost:4566",
        AuthenticationRegion = "us-east-1"
    });
```

## 📦 Supported Services

| Service | Operations |
|---------|-----------|
| **Lambda** | CreateFunction, ListFunctions, GetFunction, DeleteFunction, Invoke, Event Source Mappings |
| **SQS** | CreateQueue, SendMessage, ReceiveMessage, DeleteMessage, GetQueueUrl, ListQueues |
| **S3** | CreateBucket, PutObject, GetObject, DeleteObject, ListBuckets, ListObjectsV2 |
| **DynamoDB** | CreateTable, PutItem, GetItem, DeleteItem, UpdateItem, Query, Scan |
| **SNS** | CreateTopic, Publish, Subscribe, Unsubscribe, ListTopics, ListSubscriptions |
| **EventBridge** | PutEvents, CreateEventBus, PutRule, PutTargets, ListRules |
| **SSM** | GetParameter, PutParameter, DeleteParameter, GetParametersByPath |
| **ECS** | CreateCluster, RegisterTaskDefinition, RunTask, StopTask, ListClusters |
| **API Gateway** | Route matching, Lambda proxy integration, path parameter extraction |

## 📖 Documentation

Full documentation is available in the [Wiki](../../wiki):

- [Getting Started](../../wiki/Getting-Started) — Installation and setup
- [Configuration](../../wiki/Configuration) — Builder API reference
- [Architecture](../../wiki/Architecture) — System design and internals
- [Lambda](../../wiki/Lambda) · [SQS](../../wiki/SQS) · [S3](../../wiki/S3) · [DynamoDB](../../wiki/DynamoDB) · [SNS](../../wiki/SNS) · [EventBridge](../../wiki/EventBridge) · [SSM](../../wiki/SSM) · [ECS](../../wiki/ECS) · [API Gateway](../../wiki/API-Gateway)
- [Aspire Integration](../../wiki/Aspire-Integration) — .NET Aspire guide
- [Dashboard](../../wiki/Dashboard) — Built-in monitoring UI

## 🛠️ Building

```bash
dotnet restore Kite.slnx
dotnet build Kite.slnx --configuration Release
```

## 🧪 Testing

```bash
# Unit tests
dotnet test tests/Kite.Tests.Unit/Kite.Tests.Unit.csproj

# Integration tests
dotnet test tests/Kite.Tests.Integration/Kite.Tests.Integration.csproj
```

## 📂 Examples

| Example | Description |
|---------|-------------|
| `examples/AppHost1` | Basic .NET Aspire AppHost |
| `examples/SQS/` | Standalone SQS → Lambda trigger |
| `examples/SQS.Aspire/` | SQS → Lambda with .NET Aspire |

## 📄 License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.