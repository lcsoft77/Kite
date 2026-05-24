# Configuration

Kite uses a fluent builder pattern for configuration. All resources and settings are configured through `KiteBuilder`.

## Builder Basics

```csharp
using Kite.Core;

var builder = KiteBuilder.Create();
```

## General Settings

### Port

Set the HTTP port the emulator listens on (default: `4566`):

```csharp
builder.WithPort(4566);
```

### AWS Credentials

Set fake AWS credentials for SDK authentication (default: `test` / `test`):

```csharp
builder.WithCredentials("my-access-key", "my-secret-key");
```

### AWS Region

Set the AWS region (default: `us-east-1`):

```csharp
builder.WithRegion("eu-west-1");
```

## Adding Resources

### Lambda Functions

```csharp
builder.AddLambda("MyFunction", lambda => lambda
    .WithHandler("AssemblyName::Namespace.ClassName::MethodName")
    .WithDll("/path/to/assembly.dll")
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithMemory(256));
```

### SQS Queues

```csharp
builder.AddSQSQueue("my-queue");
```

### S3 Buckets

```csharp
builder.AddS3Bucket("my-bucket");
```

### DynamoDB Tables

```csharp
builder.AddDynamoDbTable("Orders", "CustomerId", "S", sortKeyName: "OrderId", sortKeyType: "S");
```

DynamoDB tables can also be created dynamically at runtime via the DynamoDB API (CreateTable).

### SNS Topics

```csharp
builder.AddSnsTopic("my-topic");
```

SNS topics can also be created dynamically at runtime via the SNS API (CreateTopic).

### EventBridge Event Buses

```csharp
builder.AddEventBridgeBus("my-bus");
```

### EventBridge Rules

```csharp
builder.AddEventBridgeRule("my-rule", rule =>
{
    rule.EventBusName = "my-bus";
    rule.EventPattern = "{\"source\": [\"my.source\"]}";
});
```

### SSM Parameters

```csharp
builder.AddSsmParameter("/my/parameter", "my-value", "String");
```

Parameter types: `String`, `StringList`, `SecureString`.

### API Gateway Routes

```csharp
builder.AddApiGatewayRoute("GET", "/api/users/{id}", "MyFunction");
```

## Event Triggers

### SQS → Lambda Trigger

```csharp
builder.AddSQSTrigger("my-queue", "MyFunction", batchSize: 10);
```

### S3 → Lambda Trigger

```csharp
builder.AddS3Trigger("my-bucket", "MyFunction", S3EventType.ObjectCreated);
```

Supported event types:
- `S3EventType.ObjectCreated` — Triggers on any object creation event
- `S3EventType.ObjectRemoved` — Triggers on any object removal event

## Building and Running

### Run with UseHost

```csharp
var emulator = builder.Build().UseHost();
await emulator.RunAsync();
```

### Run with Dashboard

```csharp
var emulator = builder.Build().UseDashboard();
await emulator.RunAsync();
```

## Full Configuration Example

```csharp
using Kite.Core;

var builder = KiteBuilder.Create()
    .WithPort(4566)
    .WithRegion("us-east-1")
    .WithCredentials("test", "test")

    // Lambda functions
    .AddLambda("OrderProcessor", lambda => lambda
        .WithHandler("Orders::Orders.Function::ProcessOrder")
        .WithDll("./Orders/bin/Debug/net10.0/Orders.dll")
        .WithTimeout(TimeSpan.FromSeconds(30)))
    .AddLambda("NotificationSender", lambda => lambda
        .WithHandler("Notifications::Notifications.Function::Send")
        .WithDll("./Notifications/bin/Debug/net10.0/Notifications.dll"))

    // SQS queues
    .AddSQSQueue("order-queue")
    .AddSQSQueue("notification-queue")

    // S3 buckets
    .AddS3Bucket("uploads")

    // SSM parameters
    .AddSsmParameter("/config/api-key", "my-api-key", "SecureString")

    // EventBridge
    .AddEventBridgeBus("orders")

    // Triggers
    .AddSQSTrigger("order-queue", "OrderProcessor", batchSize: 5)
    .AddS3Trigger("uploads", "OrderProcessor", S3EventType.ObjectCreated)

    // API Gateway
    .AddApiGatewayRoute("POST", "/orders", "OrderProcessor")
    .AddApiGatewayRoute("GET", "/orders/{id}", "OrderProcessor");

await builder.Build().UseHost().RunAsync();
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | OpenTelemetry OTLP exporter endpoint |

## AWS SDK Configuration

To use the emulator with the AWS SDK, override the `ServiceURL`:

```csharp
var config = new AmazonSQSConfig
{
    ServiceURL = "http://localhost:4566",
    AuthenticationRegion = "us-east-1"
};
var client = new AmazonSQSClient(
    new BasicAWSCredentials("test", "test"),
    config);
```

This pattern works for all AWS SDK service clients (SQS, S3, DynamoDB, SNS, etc.).
