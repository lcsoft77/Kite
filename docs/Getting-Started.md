# Getting Started

This guide walks you through setting up and running Kite for local AWS development.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A C# IDE (Visual Studio, Visual Studio Code, or JetBrains Rider)

## Installation

Clone the repository:

```bash
git clone https://github.com/lcsoft77/Kite.git
cd Kite
```

Restore dependencies and build:

```bash
dotnet restore Kite.slnx
dotnet build Kite.slnx --configuration Release
```

## Running the Emulator

### Standalone Mode

Create a console application that references `Kite.Host` and configure the services you need:

```csharp
using Kite.Core;
using Kite.Host;

var emulator = KiteBuilder.Create()
    .WithPort(4566)
    .WithRegion("us-east-1")
    .WithCredentials("test", "test")
    .AddLambda("MyFunction", lambda => lambda
        .WithHandler("MyAssembly::MyNamespace.Function::FunctionHandler")
        .WithDll("/path/to/MyAssembly.dll"))
    .AddSQSQueue("my-queue")
    .AddS3Bucket("my-bucket")
    .Build()
    .UseHost();

await emulator.RunAsync();
```

The emulator will start on `http://localhost:4566` (the default LocalStack edge port).

### With .NET Aspire

If you are using .NET Aspire, add the `Kite.Aspire` package to your AppHost project:

```csharp
var emulator = builder.AddKite()
    .WithLambdaFunction(
        name: "MyFunction",
        dllPath: "/path/to/MyAssembly.dll",
        handler: "MyAssembly::MyNamespace.Function::FunctionHandler")
    .WithSQSQueue("my-queue")
    .WithS3Bucket("my-bucket");
```

See [Aspire Integration](Aspire-Integration) for full details.

## Configuring the AWS SDK

Point the AWS SDK to the local emulator by overriding the service URL:

```csharp
var sqsClient = new AmazonSQSClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonSQSConfig
    {
        ServiceURL = "http://localhost:4566",
        AuthenticationRegion = "us-east-1"
    });
```

This works with any AWS SDK client (SQS, S3, DynamoDB, SNS, etc.).

## Verifying the Setup

### Health Check

```bash
curl http://localhost:4566/health
```

### LocalStack-Compatible Health Check

```bash
curl http://localhost:4566/_localstack/health
```

Both endpoints return a JSON response indicating the running services.

## Running Tests

Run the unit tests:

```bash
dotnet test tests/Kite.Tests.Unit/Kite.Tests.Unit.csproj
```

Run the integration tests (requires full host setup):

```bash
dotnet test tests/Kite.Tests.Integration/Kite.Tests.Integration.csproj
```

## Examples

The repository includes ready-to-run examples in the `examples/` directory:

| Example | Description |
|---------|-------------|
| `examples/AppHost1` | Basic .NET Aspire AppHost integration |
| `examples/SQS/` | Standalone SQS → Lambda trigger example |
| `examples/SQS.Aspire/` | SQS → Lambda with .NET Aspire integration |

### Running the SQS Example

```bash
cd examples/SQS/SQS.Emulator
dotnet run
```

In another terminal, send messages:

```bash
cd examples/SQS/SQS.Sender
dotnet run
```

## Next Steps

- [Configuration](Configuration) — Learn about all builder options
- [Architecture](Architecture) — Understand how the emulator works internally
- Explore individual service pages: [Lambda](Lambda), [SQS](SQS), [S3](S3), [DynamoDB](DynamoDB), [SNS](SNS), [EventBridge](EventBridge), [SSM](SSM), [ECS](ECS), [API Gateway](API-Gateway)
