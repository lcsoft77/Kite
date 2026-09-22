# Dashboard

Kite includes a built-in Blazor Server dashboard for monitoring and interacting with emulated AWS services.

## Overview

The dashboard provides a web-based UI for viewing the status and details of all emulated AWS resources. It is built with Blazor Server and uses Microsoft's Fluent UI components.

## Accessing the Dashboard

The dashboard is enabled via the `UseDashboard()` extension method in the host configuration. Once running, it is accessible through the emulator's web interface.

### Packages

| Scenario | Package | Call |
|---|---|---|
| Standalone host | `Kite.Dashboard` | `KiteBuilder.Create()...Build().UseDashboard()` |
| .NET Aspire AppHost | `Kite.Aspire.Dashboard` | `builder.AddKite()...WithDashboard()` (runs on the emulator port + 1 by default) |

`WithDashboard()` lives in `Kite.Aspire.Dashboard` (namespace `Kite.Aspire`), so `Kite.Aspire` itself does not depend on the dashboard.

The dashboard is self-contained: its CSS, JavaScript, the Fluent UI assets and `blazor.web.js` are embedded in `Kite.Dashboard.dll` and served through endpoints. It works with a plain `Microsoft.NET.Sdk` project and does not require `Microsoft.NET.Sdk.Web`, static web asset manifests or a populated NuGet cache at runtime. It also no longer changes `ASPNETCORE_ENVIRONMENT`.

## Dashboard Pages

### Main Dashboard

The main dashboard page provides an overview of all registered AWS resources:

- Lambda Functions count and status
- SQS Queues count
- S3 Buckets count
- SNS Topics count
- EventBridge Buses count
- ECS Clusters count

### Lambda Function Detail

View detailed information about a specific Lambda function:

- Function name and handler
- Runtime configuration (memory, timeout)
- Environment variables
- Invocation history
- Request/response payloads

### SQS Queue Detail

View SQS queue information:

- Queue name and URL
- Message count
- Queue attributes
- Recent messages

### S3 Bucket Detail

View S3 bucket information:

- Bucket name
- Object count
- Object listing
- Notification configurations

### SNS Topic Detail

View SNS topic information:

- Topic name and ARN
- Subscriptions
- Topic attributes

### EventBridge Bus Detail

View EventBridge information:

- Event bus name
- Rules and their event patterns
- Targets for each rule

### ECS Cluster Detail

View ECS cluster information:

- Cluster name and ARN
- Running tasks
- Task definitions

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Framework | Blazor Server (.NET 10) |
| UI Components | Microsoft Fluent UI for ASP.NET Core |
| Rendering | Server-side interactive rendering |
| Styling | Fluent Design System |

## Architecture

The dashboard reads data directly from the `IServiceRegistry` singleton, providing real-time visibility into the in-memory state of all emulated services.

```
Browser  ←──SignalR──→  Blazor Server  ←──DI──→  IServiceRegistry
```

All updates are reflected in real-time through the Blazor Server SignalR connection.
