# Kite

[![NuGet](https://img.shields.io/nuget/v/Kite.Host.svg?logo=nuget)](https://www.nuget.org/packages/Kite.Host)
[![NuGet downloads](https://img.shields.io/nuget/dt/Kite.Host.svg?logo=nuget)](https://www.nuget.org/packages/Kite.Host)
[![Release](https://github.com/lcsoft77/Kite/actions/workflows/release.yml/badge.svg)](https://github.com/lcsoft77/Kite/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/github/license/lcsoft77/Kite.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

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

## 📥 NuGet Packages

| Package | Version | Downloads | Description |
|---|---|---|---|
| [Kite.Host](https://www.nuget.org/packages/Kite.Host) | [![NuGet](https://img.shields.io/nuget/v/Kite.Host.svg)](https://www.nuget.org/packages/Kite.Host) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Host.svg)](https://www.nuget.org/packages/Kite.Host) | Run the emulator in-process on ASP.NET Core |
| [Kite.Core](https://www.nuget.org/packages/Kite.Core) | [![NuGet](https://img.shields.io/nuget/v/Kite.Core.svg)](https://www.nuget.org/packages/Kite.Core) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Core.svg)](https://www.nuget.org/packages/Kite.Core) | Core abstractions and fluent builder API |
| [Kite.Lambda](https://www.nuget.org/packages/Kite.Lambda) | [![NuGet](https://img.shields.io/nuget/v/Kite.Lambda.svg)](https://www.nuget.org/packages/Kite.Lambda) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Lambda.svg)](https://www.nuget.org/packages/Kite.Lambda) | AWS Lambda |
| [Kite.SQS](https://www.nuget.org/packages/Kite.SQS) | [![NuGet](https://img.shields.io/nuget/v/Kite.SQS.svg)](https://www.nuget.org/packages/Kite.SQS) | [![Downloads](https://img.shields.io/nuget/dt/Kite.SQS.svg)](https://www.nuget.org/packages/Kite.SQS) | Amazon SQS |
| [Kite.S3](https://www.nuget.org/packages/Kite.S3) | [![NuGet](https://img.shields.io/nuget/v/Kite.S3.svg)](https://www.nuget.org/packages/Kite.S3) | [![Downloads](https://img.shields.io/nuget/dt/Kite.S3.svg)](https://www.nuget.org/packages/Kite.S3) | Amazon S3 |
| [Kite.DynamoDB](https://www.nuget.org/packages/Kite.DynamoDB) | [![NuGet](https://img.shields.io/nuget/v/Kite.DynamoDB.svg)](https://www.nuget.org/packages/Kite.DynamoDB) | [![Downloads](https://img.shields.io/nuget/dt/Kite.DynamoDB.svg)](https://www.nuget.org/packages/Kite.DynamoDB) | Amazon DynamoDB |
| [Kite.SNS](https://www.nuget.org/packages/Kite.SNS) | [![NuGet](https://img.shields.io/nuget/v/Kite.SNS.svg)](https://www.nuget.org/packages/Kite.SNS) | [![Downloads](https://img.shields.io/nuget/dt/Kite.SNS.svg)](https://www.nuget.org/packages/Kite.SNS) | Amazon SNS |
| [Kite.EventBridge](https://www.nuget.org/packages/Kite.EventBridge) | [![NuGet](https://img.shields.io/nuget/v/Kite.EventBridge.svg)](https://www.nuget.org/packages/Kite.EventBridge) | [![Downloads](https://img.shields.io/nuget/dt/Kite.EventBridge.svg)](https://www.nuget.org/packages/Kite.EventBridge) | Amazon EventBridge |
| [Kite.SSM](https://www.nuget.org/packages/Kite.SSM) | [![NuGet](https://img.shields.io/nuget/v/Kite.SSM.svg)](https://www.nuget.org/packages/Kite.SSM) | [![Downloads](https://img.shields.io/nuget/dt/Kite.SSM.svg)](https://www.nuget.org/packages/Kite.SSM) | AWS SSM Parameter Store |
| [Kite.ECS](https://www.nuget.org/packages/Kite.ECS) | [![NuGet](https://img.shields.io/nuget/v/Kite.ECS.svg)](https://www.nuget.org/packages/Kite.ECS) | [![Downloads](https://img.shields.io/nuget/dt/Kite.ECS.svg)](https://www.nuget.org/packages/Kite.ECS) | Amazon ECS |
| [Kite.ApiGateway](https://www.nuget.org/packages/Kite.ApiGateway) | [![NuGet](https://img.shields.io/nuget/v/Kite.ApiGateway.svg)](https://www.nuget.org/packages/Kite.ApiGateway) | [![Downloads](https://img.shields.io/nuget/dt/Kite.ApiGateway.svg)](https://www.nuget.org/packages/Kite.ApiGateway) | API Gateway |
| [Kite.Aspire](https://www.nuget.org/packages/Kite.Aspire) | [![NuGet](https://img.shields.io/nuget/v/Kite.Aspire.svg)](https://www.nuget.org/packages/Kite.Aspire) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Aspire.svg)](https://www.nuget.org/packages/Kite.Aspire) | `.NET Aspire` hosting integration |
| [Kite.Aspire.Dashboard](https://www.nuget.org/packages/Kite.Aspire.Dashboard) | [![NuGet](https://img.shields.io/nuget/v/Kite.Aspire.Dashboard.svg)](https://www.nuget.org/packages/Kite.Aspire.Dashboard) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Aspire.Dashboard.svg)](https://www.nuget.org/packages/Kite.Aspire.Dashboard) | Aspire `WithDashboard` extension |
| [Kite.Dashboard](https://www.nuget.org/packages/Kite.Dashboard) | [![NuGet](https://img.shields.io/nuget/v/Kite.Dashboard.svg)](https://www.nuget.org/packages/Kite.Dashboard) | [![Downloads](https://img.shields.io/nuget/dt/Kite.Dashboard.svg)](https://www.nuget.org/packages/Kite.Dashboard) | Blazor Server dashboard |

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