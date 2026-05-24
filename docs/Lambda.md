# AWS Lambda

Kite provides full AWS Lambda emulation with in-process execution, enabling debugging directly from your IDE.

## Overview

The Lambda service emulates the AWS Lambda API (version `2015-03-31`), supporting function lifecycle management, invocation, and event source mappings.

**Key Features:**
- In-process execution with isolated `AssemblyLoadContext` per function
- Full IDE debugging with breakpoint support
- Event source mappings (SQS, S3, EventBridge triggers)
- Function restart without emulator restart
- OpenTelemetry tracing for invocations

## Supported Operations

| Operation | HTTP Method | Path |
|-----------|-------------|------|
| Create Function | `POST` | `/2015-03-31/functions` |
| List Functions | `GET` | `/2015-03-31/functions` |
| Get Function | `GET` | `/2015-03-31/functions/{name}` |
| Delete Function | `DELETE` | `/2015-03-31/functions/{name}` |
| Invoke Function | `POST` | `/2015-03-31/functions/{name}/invocations` |
| Restart Function | `POST` | `/2015-03-31/functions/{name}/restart` |
| Create Event Source Mapping | `POST` | `/2015-03-31/event-source-mappings` |
| List Event Source Mappings | `GET` | `/2015-03-31/event-source-mappings` |
| Get Event Source Mapping | `GET` | `/2015-03-31/event-source-mappings/{uuid}` |
| Delete Event Source Mapping | `DELETE` | `/2015-03-31/event-source-mappings/{uuid}` |

## Configuration

### Registering a Lambda Function

```csharp
builder.AddLambda("MyFunction", lambda => lambda
    .WithHandler("AssemblyName::Namespace.ClassName::MethodName")
    .WithDll("/path/to/assembly.dll")
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithMemory(256));
```

### Handler Format

The handler string follows the AWS Lambda convention:

```
AssemblyName::Namespace.ClassName::MethodName
```

- **AssemblyName** — The .NET assembly name (without `.dll`)
- **Namespace.ClassName** — Fully qualified class name
- **MethodName** — The handler method name

### Lambda Function Properties

| Property | Builder Method | Type | Description |
|----------|---------------|------|-------------|
| `Handler` | `WithHandler(string)` | `string` | Handler string in AWS format |
| `DllPath` | `WithDll(string)` | `string` | Path to the compiled assembly DLL |
| `Timeout` | `WithTimeout(TimeSpan)` | `TimeSpan` | Function timeout (default: 30s) |
| `MemoryMb` | `WithMemory(int)` | `int` | Memory allocation in MB (default: 128) |

## Execution Model

Each Lambda function runs in an isolated `AssemblyLoadContext`:

1. The handler assembly is loaded into an isolated context
2. A `SemaphoreSlim(1,1)` ensures serialized execution per function
3. The handler method is invoked with the event payload and `ILambdaContext`
4. Results are returned as the HTTP response body

### Concurrency

The emulator uses a per-function `SemaphoreSlim(1,1)` lock, simulating single-concurrency behavior. If a function is already executing, subsequent invocations wait for the lock.

### Restarting a Function

To reload a function's assembly (e.g., after recompilation):

```bash
curl -X POST http://localhost:4566/2015-03-31/functions/MyFunction/restart
```

This unloads the current `AssemblyLoadContext` and creates a new one, loading the latest assembly.

## Event Source Mappings

### SQS Trigger

```csharp
builder.AddSQSTrigger("my-queue", "MyFunction", batchSize: 10);
```

The `SqsEsmPoller` background service continuously polls SQS queues and invokes the mapped Lambda function with batched messages.

### S3 Trigger

```csharp
builder.AddS3Trigger("my-bucket", "MyFunction", S3EventType.ObjectCreated);
```

The S3 service sends notification events to Lambda when objects are created or deleted. Supported event types are `S3EventType.ObjectCreated` and `S3EventType.ObjectRemoved`.

## AWS SDK Usage

```csharp
var lambdaClient = new AmazonLambdaClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonLambdaConfig
    {
        ServiceURL = "http://localhost:4566"
    });

// Invoke a function
var response = await lambdaClient.InvokeAsync(new InvokeRequest
{
    FunctionName = "MyFunction",
    Payload = "{\"key\": \"value\"}"
});

var result = new StreamReader(response.Payload).ReadToEnd();
```

## Debugging

Since Lambda functions run in-process, you can set breakpoints directly in your Lambda handler code. Simply attach your IDE debugger to the emulator process (or run the emulator project in debug mode) and breakpoints will be hit when the function is invoked.
