# Architecture

This page describes the internal architecture of Kite, its design decisions, and how the various components interact.

## High-Level Overview

```
┌─────────────────────────────────────────────────────────┐
│                    HTTP Clients (AWS SDK)                │
└──────────────────────────┬──────────────────────────────┘
                           │ HTTP :4566
┌──────────────────────────▼──────────────────────────────┐
│                  ASP.NET Core Host                       │
│  ┌────────────┐  ┌─────────────┐  ┌──────────────────┐  │
│  │ SigV4 Auth │→ │UnifiedRouter│→ │ Service Handlers │  │
│  └────────────┘  └─────────────┘  └──────────────────┘  │
│                                                         │
│  Services:                                              │
│  ┌────────┐ ┌─────┐ ┌────┐ ┌────────┐ ┌─────┐ ┌─────┐ │
│  │ Lambda │ │ SQS │ │ S3 │ │DynamoDB│ │ SNS │ │ ... │ │
│  └───┬────┘ └──┬──┘ └──┬─┘ └───┬────┘ └──┬──┘ └──┬──┘ │
│      │         │       │       │         │       │     │
│  ┌───▼─────────▼───────▼───────▼─────────▼───────▼───┐ │
│  │            InMemoryServiceRegistry                 │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

## Core Components

### KiteBuilder

The entry point for configuring the emulator. Provides a fluent API to register services, Lambda functions, queues, buckets, and triggers.

**Source:** `src/Kite.Core/KiteBuilder.cs`

### InMemoryServiceRegistry

A singleton service that stores all emulated AWS resources in memory. Implements `IServiceRegistry` and manages:

- Lambda functions and event source mappings
- SQS queues
- S3 buckets and notification configurations
- DynamoDB tables
- SNS topics and subscriptions
- EventBridge buses and rules
- SSM parameters
- ECS clusters, task definitions, and tasks
- API Gateway routes

**Source:** `src/Kite.Core/InMemoryServiceRegistry.cs`

### UnifiedRouter (Middleware)

Inspects incoming HTTP requests and determines which AWS service should handle them using:

1. **Path matching** — Lambda API paths start with `/2015-03-31/`
2. **X-Amz-Target header** — JSON-protocol services (SQS, DynamoDB, EventBridge, ECS, SSM). Note that SNS uses the Query protocol (`Action=...` in a form-encoded body) but is also routed via X-Amz-Target as a routing hint.
3. **SigV4 Authorization header** — Extracts the service name from the credential scope
4. **Default fallback** — Unmatched requests route to API Gateway

The determined service name is stored in `HttpContext.Items["aws-service"]` for downstream handlers.

**Source:** `src/Kite.Core/Routing/UnifiedRouter.cs`

### SigV4 Authentication

Validates AWS Signature Version 4 headers on incoming requests. In development mode, it logs warnings for invalid credentials but allows requests through for convenience.

**Source:** `src/Kite.Core/Auth/SigV4Validator.cs`

## Service Protocol Handling

Different AWS services use different wire protocols:

| Protocol | Services | Content Type |
|----------|----------|--------------|
| **REST** | Lambda, S3 | `application/json`, `application/xml` |
| **JSON (X-Amz-Target)** | DynamoDB, EventBridge, ECS, SSM, SQS | `application/x-amz-json-1.0` / `1.1` |
| **Query (form-encoded)** | SQS (legacy), SNS | `application/x-www-form-urlencoded` |

The `UnifiedRouter` normalizes these different protocols into a unified routing system.

## Lambda Execution Model

Lambda functions run **in-process** using isolated `AssemblyLoadContext` instances:

1. Each Lambda function gets its own `AssemblyLoadContext` for assembly isolation
2. The handler assembly is loaded into this isolated context
3. A `SemaphoreSlim(1,1)` per function ensures serialized execution (simulating single-concurrency)
4. The `LambdaExecutor` manages the invocation lifecycle

This approach enables:
- Full IDE debugging with breakpoints
- Fast startup (no container or process overhead)
- Hot reload via the restart endpoint

**Source:** `src/Kite.Lambda/Execution/LambdaExecutor.cs`

## Event Source Mappings

The emulator supports event-driven triggers that connect services to Lambda:

| Trigger | Description |
|---------|-------------|
| **SQS → Lambda** | `SqsEsmPoller` background service polls queues and invokes Lambda |
| **S3 → Lambda** | S3 service sends notifications on object changes |
| **EventBridge → Lambda** | Event rules match patterns and forward to Lambda targets |
| **API Gateway → Lambda** | HTTP routes proxy requests to Lambda functions |

## Background Services

Two hosted services run continuously:

- **SqsEsmPoller** — Polls SQS queues for event source mappings and invokes Lambda
- **SqsVisibilityTimeoutChecker** — Manages message visibility timeout expiration

## OpenTelemetry Integration

The emulator exports telemetry via OTLP:

- **Endpoint:** `http://localhost:4317` (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`)
- **Tracing:** ASP.NET Core instrumentation + custom `Kite` activity source
- **Metrics:** ASP.NET Core metrics
- **Logging:** OpenTelemetry OTLP log exporter

## Project Dependencies

```
Kite.Host
├── Kite.Core (models, registry, routing, auth)
├── Kite.Lambda (function execution)
├── Kite.SQS (queue service)
├── Kite.S3 (storage service)
├── Kite.DynamoDB (NoSQL service)
├── Kite.SNS (notification service)
├── Kite.EventBridge (event bus service)
├── Kite.SSM (parameter store)
├── Kite.ECS (container service)
├── Kite.ApiGateway (API routing)
└── Kite.Dashboard (Blazor UI)

Kite.Aspire
├── Kite.Host
└── Aspire.Hosting (lifecycle hook)
```

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Lambda Runtime | .NET only | Simplifies complexity; can be expanded later |
| Execution Model | In-process isolated AssemblyLoadContext | Enables debugging, fast startup |
| IAM/Auth | Fixed static credentials | Simplifies development experience |
| Persistence | In-memory | Suitable for local testing; fast and simple |
| Port | 4566 (default) | Compatible with LocalStack tooling |
| Protocol | Multi-protocol (REST, JSON, Query) | Full AWS SDK compatibility |
