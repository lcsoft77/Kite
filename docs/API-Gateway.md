# Amazon API Gateway

Kite provides Amazon API Gateway emulation with Lambda proxy integration, supporting route matching and path parameter extraction.

## Overview

The API Gateway service handles HTTP requests that don't match any other AWS service endpoint. It matches incoming requests against registered routes and proxies them to the configured Lambda function.

**Routing:**
- Default fallback when no other AWS service matches
- Routes are matched by HTTP method and path pattern

## Configuration

### Registering Routes via Builder

```csharp
builder
    .AddLambda("UserHandler", lambda => { /* ... */ })
    .AddApiGatewayRoute("GET", "/api/users", "UserHandler")
    .AddApiGatewayRoute("GET", "/api/users/{id}", "UserHandler")
    .AddApiGatewayRoute("POST", "/api/users", "UserHandler")
    .AddApiGatewayRoute("PUT", "/api/users/{id}", "UserHandler")
    .AddApiGatewayRoute("DELETE", "/api/users/{id}", "UserHandler");
```

### Path Parameters

Routes support path parameters using `{paramName}` syntax:

```csharp
builder.AddApiGatewayRoute("GET", "/api/orders/{orderId}/items/{itemId}", "OrderHandler");
```

When a request matches, path parameters are extracted and included in the Lambda event.

## How It Works

1. An HTTP request arrives that doesn't match any AWS service (SQS, S3, DynamoDB, etc.)
2. The API Gateway service attempts to match the request method and path against registered routes
3. If a match is found, the request is converted to a Lambda proxy integration event
4. The mapped Lambda function is invoked with the proxy event
5. The Lambda response is returned to the client

## Lambda Proxy Integration Event

The API Gateway sends a proxy integration event to Lambda with this structure:

```json
{
    "httpMethod": "GET",
    "path": "/api/users/123",
    "pathParameters": {
        "id": "123"
    },
    "queryStringParameters": {
        "limit": "10"
    },
    "headers": {
        "Content-Type": "application/json"
    },
    "body": null,
    "isBase64Encoded": false
}
```

## Lambda Response Format

The Lambda function should return a response in the proxy integration format:

```json
{
    "statusCode": 200,
    "headers": {
        "Content-Type": "application/json"
    },
    "body": "{\"id\": \"123\", \"name\": \"John Doe\"}",
    "isBase64Encoded": false
}
```

## Example

### Emulator Setup

```csharp
var emulator = KiteBuilder.Create()
    .WithPort(4566)
    .AddLambda("ApiHandler", lambda => lambda
        .WithHandler("MyApi::MyApi.Function::Handler")
        .WithDll("./MyApi/bin/Debug/net10.0/MyApi.dll"))
    .AddApiGatewayRoute("GET", "/api/hello", "ApiHandler")
    .AddApiGatewayRoute("GET", "/api/hello/{name}", "ApiHandler")
    .Build()
    .UseHost();

await emulator.RunAsync();
```

### Calling the API

```bash
# Simple route
curl http://localhost:4566/api/hello

# Route with path parameter
curl http://localhost:4566/api/hello/World

# Route with query parameters
curl "http://localhost:4566/api/hello?greeting=Hi"
```

## Route Matching

Routes are matched using regex-based path patterns:
- Exact paths: `/api/users` matches only `/api/users`
- Path parameters: `/api/users/{id}` matches `/api/users/123`, `/api/users/abc`
- Multiple parameters: `/api/{resource}/{id}` matches `/api/orders/456`

The HTTP method must also match the registered method.
