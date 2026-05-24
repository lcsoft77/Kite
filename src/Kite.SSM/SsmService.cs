using System.Text.Json;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.SSM;

public class SsmService
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<SsmService> _logger;

    public SsmService(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<SsmService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var operation = target.Split('.').Last();

        _logger.LogDebug("[DEBUG] SSM HandleAsync - Operation: {Operation}, Method: {Method}", operation, context.Request.Method);

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = body.Length > 0 ? JsonDocument.Parse(body) : JsonDocument.Parse("{}");
        var root = doc.RootElement.Clone();

        _logger.LogDebug("[DEBUG] SSM operation: {Operation}, RequestBodyLength: {BodyLength}", operation, body.Length);

        switch (operation)
        {
            case "GetParameter":
                await HandleGetParameterAsync(context, root);
                break;
            case "GetParameters":
                await HandleGetParametersAsync(context, root);
                break;
            case "PutParameter":
                await HandlePutParameterAsync(context, root);
                break;
            case "DeleteParameter":
                await HandleDeleteParameterAsync(context, root);
                break;
            case "GetParametersByPath":
                await HandleGetParametersByPathAsync(context, root);
                break;
            case "DescribeParameters":
                await HandleDescribeParametersAsync(context, root);
                break;
            default:
                _logger.LogWarning("Unknown SSM operation: {Operation}", operation);
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/x-amz-json-1.1";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    __type = "InvalidAction",
                    message = $"Unknown operation: {operation}"
                }));
                break;
        }
    }

    private async Task HandleGetParameterAsync(HttpContext context, JsonElement root)
    {
        var name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        _logger.LogDebug("[ENTRY] HandleGetParameterAsync - Name: {ParameterName}", name);
        
        var param = _registry.GetSsmParameter(name);

        if (param is null)
        {
            _logger.LogError("[ERROR] GetParameter failed - Parameter not found: {ParameterName}", name);
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ParameterNotFound",
                message = $"Parameter {name} not found."
            }));
            return;
        }

        using var activity = KiteActivitySource.SSM.StartActivity("ssm.get_parameter");
        activity?.SetTag("parameter.name", name);
        activity?.SetTag("parameter.type", param.Type);

        _logger.LogInformation("GetParameter completed - Name: {ParameterName}, Type: {Type}", name, param.Type);

        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Parameter = BuildParameterObject(param)
        }));
    }

    private async Task HandleGetParametersAsync(HttpContext context, JsonElement root)
    {
        var names = root.TryGetProperty("Names", out var n)
            ? n.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : new List<string>();

        var found = new List<object>();
        var invalidParameters = new List<string>();

        foreach (var name in names)
        {
            var param = _registry.GetSsmParameter(name);
            if (param is not null)
                found.Add(BuildParameterObject(param));
            else
                invalidParameters.Add(name);
        }

        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Parameters = found,
            InvalidParameters = invalidParameters
        }));
    }

    private async Task HandlePutParameterAsync(HttpContext context, JsonElement root)
    {
        var name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var value = root.TryGetProperty("Value", out var v) ? v.GetString() ?? string.Empty : string.Empty;
        var type = root.TryGetProperty("Type", out var t) ? t.GetString() ?? "String" : "String";
        var overwrite = root.TryGetProperty("Overwrite", out var ow) && ow.GetBoolean();

        _logger.LogDebug("[ENTRY] HandlePutParameterAsync - Name: {ParameterName}, Type: {Type}, Overwrite: {Overwrite}", name, type, overwrite);

        // Validate parameter name: must be present, non-empty, and start with '/'
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogError("[ERROR] PutParameter failed - Parameter name is required");
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ValidationException",
                message = "Parameter name is required and must not be null, empty, or blank."
            }));
            return;
        }

        if (!name.StartsWith("/"))
        {
            _logger.LogError("[ERROR] PutParameter failed - Parameter name must start with '/': {ParameterName}", name);
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ValidationException",
                message = "Parameter name must start with '/'."
            }));
            return;
        }

        var existing = _registry.GetSsmParameter(name);
        if (existing is not null && !overwrite)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ParameterAlreadyExists",
                message = $"Parameter {name} already exists."
            }));
            return;
        }

        var version = existing is not null ? existing.Version + 1 : 1;
        
        using var activity = KiteActivitySource.SSM.StartActivity("ssm.put_parameter");
        activity?.SetTag("parameter.name", name);
        activity?.SetTag("parameter.type", type);
        activity?.SetTag("overwrite", overwrite);
        activity?.SetTag("parameter.version", version);
        
        _registry.RegisterSsmParameter(new SsmParameter
        {
            Name = name,
            Value = value,
            Type = type,
            Version = version,
            LastModifiedDate = DateTime.UtcNow
        });

        _logger.LogInformation("PutParameter completed - Name: {ParameterName}, Type: {Type}, Version: {Version}, Overwrite: {Overwrite}", name, type, version, overwrite);

        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Version = version,
            Tier = "Standard"
        }));
    }

    private async Task HandleDeleteParameterAsync(HttpContext context, JsonElement root)
    {
        var name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var param = _registry.GetSsmParameter(name);

        if (param is null)
        {
            _logger.LogDebug("SSM DeleteParameter: parameter {Name} not found", name);
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ParameterNotFound",
                message = $"Parameter {name} not found."
            }));
            return;
        }

        using var activity = KiteActivitySource.SSM.StartActivity("ssm.delete_parameter");
        activity?.SetTag("parameter.name", name);

        _registry.DeleteSsmParameter(name);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleGetParametersByPathAsync(HttpContext context, JsonElement root)
    {
        var path = root.TryGetProperty("Path", out var p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(path))
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ValidationException",
                message = "Path is required and cannot be null or empty."
            }));
            return;
        }

        // Normalize trailing slash so "/myapp" and "/myapp/" behave the same
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        var recursive = root.TryGetProperty("Recursive", out var r) && r.GetBoolean();

        var parameters = _registry.GetAllSsmParameters()
            .Where(param =>
            {
                if (!param.Name.StartsWith(path, StringComparison.Ordinal))
                    return false;

                var prefixLength = path.Length;

                // Enforce path boundary: exact match or next character is '/'
                if (param.Name.Length > prefixLength && param.Name[prefixLength] != '/')
                    return false;

                if (!recursive)
                {
                    // Non-recursive: only direct children (no further slashes after the path prefix)
                    var remainder = param.Name[prefixLength..].TrimStart('/');
                    return !remainder.Contains('/');
                }

                return true;
            })
            .Select(BuildParameterObject)
            .ToList();

        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Parameters = parameters
        }));
    }

    private async Task HandleDescribeParametersAsync(HttpContext context, JsonElement root)
    {
        var allParams = _registry.GetAllSsmParameters().ToList();

        var parameterMetadata = allParams.Select(p => new
        {
            Name = p.Name,
            Type = p.Type,
            Version = p.Version,
            LastModifiedDate = p.LastModifiedDate,
            ARN = BuildArn(p.Name)
        }).ToList();

        context.Response.ContentType = "application/x-amz-json-1.1";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Parameters = parameterMetadata
        }));
    }

    private object BuildParameterObject(SsmParameter param) => new
    {
        Name = param.Name,
        Value = param.Value,
        Type = param.Type,
        Version = param.Version,
        LastModifiedDate = param.LastModifiedDate,
        ARN = BuildArn(param.Name)
    };

    private string BuildArn(string name) =>
        $"arn:aws:ssm:{_registry.Region}:{_registry.AccountId}:parameter{name}";
}
