# AWS Systems Manager (SSM) Parameter Store

Kite provides AWS Systems Manager Parameter Store emulation for storing and retrieving configuration data.

## Overview

The SSM service uses the JSON protocol via the `X-Amz-Target` header with target prefix `AmazonSSM`.

**Routing:**
- Header: `X-Amz-Target: AmazonSSM.*`
- Content-Type: `application/x-amz-json-1.1`
- SigV4 service name: `ssm`

## Supported Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `GetParameter` | `AmazonSSM.GetParameter` | Get a single parameter |
| `GetParameters` | `AmazonSSM.GetParameters` | Get multiple parameters |
| `PutParameter` | `AmazonSSM.PutParameter` | Create or update a parameter |
| `DeleteParameter` | `AmazonSSM.DeleteParameter` | Delete a parameter |
| `GetParametersByPath` | `AmazonSSM.GetParametersByPath` | Get parameters by path prefix |
| `DescribeParameters` | `AmazonSSM.DescribeParameters` | Describe available parameters |

## Configuration

### Pre-loading Parameters via Builder

```csharp
builder
    .AddSsmParameter("/config/database/host", "localhost", "String")
    .AddSsmParameter("/config/database/port", "5432", "String")
    .AddSsmParameter("/config/api-key", "my-secret-key", "SecureString");
```

### Parameter Types

| Type | Description |
|------|-------------|
| `String` | A plain text string |
| `StringList` | A comma-separated list of strings |
| `SecureString` | An encrypted string (stored as plain text in the emulator) |

## AWS SDK Usage

### Putting a Parameter

```csharp
var ssmClient = new AmazonSimpleSystemsManagementClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonSimpleSystemsManagementConfig { ServiceURL = "http://localhost:4566" });

await ssmClient.PutParameterAsync(new PutParameterRequest
{
    Name = "/myapp/config/api-url",
    Value = "https://api.example.com",
    Type = ParameterType.String,
    Overwrite = true
});
```

### Getting a Parameter

```csharp
var response = await ssmClient.GetParameterAsync(new GetParameterRequest
{
    Name = "/myapp/config/api-url",
    WithDecryption = true
});

Console.WriteLine($"Value: {response.Parameter.Value}");
```

### Getting Parameters by Path

```csharp
var response = await ssmClient.GetParametersByPathAsync(new GetParametersByPathRequest
{
    Path = "/myapp/config/",
    Recursive = true
});

foreach (var param in response.Parameters)
{
    Console.WriteLine($"{param.Name} = {param.Value}");
}
```

### Deleting a Parameter

```csharp
await ssmClient.DeleteParameterAsync(new DeleteParameterRequest
{
    Name = "/myapp/config/api-url"
});
```

## Parameter Path Conventions

Parameters use hierarchical paths separated by `/`:

```
/environment/service/setting
```

For example:
```
/production/database/connection-string
/production/database/max-connections
/staging/api/key
```

Use `GetParametersByPath` to retrieve all parameters under a specific prefix.

## Storage

All parameters are stored in memory and do not persist across emulator restarts.
