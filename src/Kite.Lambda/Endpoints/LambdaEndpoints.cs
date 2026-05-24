using System.Text.Json;
using System.Text.Json.Serialization;
using Kite.Core;
using Kite.Core.Models;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kite.Lambda.Endpoints;

public static class LambdaEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapLambdaEndpoints(this IEndpointRouteBuilder app)
    {
        // Management
        app.MapPost("/2015-03-31/functions", CreateFunction);
        app.MapGet("/2015-03-31/functions", ListFunctions);
        app.MapGet("/2015-03-31/functions/{name}", GetFunction);
        app.MapDelete("/2015-03-31/functions/{name}", DeleteFunction);

        // Invocation
        app.MapPost("/2015-03-31/functions/{name}/invocations", InvokeFunction);

        // Restart
        app.MapPost("/2015-03-31/functions/{name}/restart", RestartFunction);

        // ESM
        app.MapPost("/2015-03-31/event-source-mappings", CreateEventSourceMapping);
        app.MapGet("/2015-03-31/event-source-mappings", ListEventSourceMappings);
        app.MapGet("/2015-03-31/event-source-mappings/{uuid}", GetEventSourceMapping);
        app.MapDelete("/2015-03-31/event-source-mappings/{uuid}", DeleteEventSourceMapping);
    }

    private static async Task<IResult> CreateFunction(HttpRequest request, IServiceRegistry registry, LambdaExecutor executor)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var function = new LambdaFunction
        {
            Name = root.GetProperty("FunctionName").GetString() ?? string.Empty,
            Handler = root.TryGetProperty("Handler", out var h) ? h.GetString() ?? string.Empty : string.Empty,
            DllPath = root.TryGetProperty("Code", out var code) && code.TryGetProperty("S3Key", out var key)
                ? key.GetString() ?? string.Empty : string.Empty,
            MemoryMb = root.TryGetProperty("MemorySize", out var mem) ? mem.GetInt32() : 128,
            Timeout = root.TryGetProperty("Timeout", out var timeout)
                ? TimeSpan.FromSeconds(timeout.GetInt32()) : TimeSpan.FromSeconds(30)
        };

        registry.RegisterLambda(function);

        return Results.Json(BuildFunctionConfig(function, registry), JsonOptions, statusCode: 201);
    }

    private static IResult ListFunctions(IServiceRegistry registry)
    {
        var functions = registry.GetAllLambdas()
            .Select(f => BuildFunctionConfig(f, registry))
            .ToList();

        return Results.Json(new { Functions = functions }, JsonOptions);
    }

    private static IResult GetFunction(string name, IServiceRegistry registry)
    {
        var function = registry.GetLambda(name);
        if (function is null)
            return Results.Json(new { Message = $"Function not found: {name}" }, statusCode: 404);

        return Results.Json(new { Configuration = BuildFunctionConfig(function, registry) }, JsonOptions);
    }

    private static IResult DeleteFunction(string name, IServiceRegistry registry)
    {
        var function = registry.GetLambda(name);
        if (function is null)
            return Results.Json(new { Message = $"Function not found: {name}" }, statusCode: 404);

        // Note: InMemoryServiceRegistry doesn't have a remove method for lambdas,
        // but we can re-register with a tombstone or skip – for now treat as success
        return Results.NoContent();
    }

    private static async Task<IResult> RestartFunction(string name, IServiceRegistry registry, LambdaExecutor executor, CancellationToken cancellationToken)
    {
        var function = registry.GetLambda(name);
        if (function is null)
            return Results.Json(new { Message = $"Function not found: {name}" }, statusCode: 404);

        await function.FunctionLock.WaitAsync(cancellationToken);
        try
        {
            // Capture the old AssemblyLoadContext so we can properly unload it before reloading.
            var oldContext = function.AssemblyLoadContext;

            // Detach the function from the old context.
            function.AssemblyLoadContext = null;

            // If the old context was collectible, unload it to release resources and file locks.
            if (oldContext is not null && oldContext.IsCollectible)
            {
                oldContext.Unload();

                // Trigger GC and finalizers to complete the unload promptly, important for hot-rebuilds.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            executor.ValidateAndLoad(function);
            return Results.Ok(new { FunctionName = name, State = "Active" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { Message = $"Failed to restart function '{name}': {ex.Message}" }, statusCode: 500);
        }
        finally
        {
            function.FunctionLock.Release();
        }
    }

    private static async Task<IResult> InvokeFunction(string name, HttpRequest request, IServiceRegistry registry, LambdaExecutor executor)
    {
        var function = registry.GetLambda(name);
        if (function is null)
            return Results.Json(new { Message = $"Function not found: {name}" }, statusCode: 404);

        var invocationType = request.Headers.TryGetValue("X-Amz-Invocation-Type", out var invType)
            ? invType.ToString() : "RequestResponse";

        using var reader = new StreamReader(request.Body);
        var inputJson = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(inputJson)) inputJson = "{}";

        if (invocationType == "Event")
        {
            // Fire and forget
            _ = executor.InvokeAsync(name, inputJson, CancellationToken.None);
            return Results.StatusCode(202);
        }

        try
        {
            var result = await executor.InvokeAsync(name, inputJson);
            return Results.Content(result, "application/json");
        }
        catch (Exception ex)
        {
            var errorBody = JsonSerializer.Serialize(new
            {
                errorMessage = ex.Message,
                errorType = ex.GetType().Name
            });
            return Results.Extensions.Lambda_FunctionError(errorBody);
        }
    }

    private static async Task<IResult> CreateEventSourceMapping(HttpRequest request, IServiceRegistry registry)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var mapping = new EventSourceMapping
        {
            FunctionName = root.GetProperty("FunctionName").GetString() ?? string.Empty,
            BatchSize = root.TryGetProperty("BatchSize", out var bs) ? bs.GetInt32() : 10,
            Enabled = root.TryGetProperty("Enabled", out var enabled) ? enabled.GetBoolean() : true
        };

        if (root.TryGetProperty("EventSourceArn", out var arnProp))
        {
            var arn = arnProp.GetString() ?? string.Empty;
            if (arn.Contains(":sqs:"))
            {
                mapping.Type = Core.Models.EventSourceMappingType.SQS;
                mapping.SourceName = arn.Split(':').Last();
            }
            else if (arn.Contains(":s3:::"))
            {
                mapping.Type = Core.Models.EventSourceMappingType.S3;
                mapping.SourceName = arn.Split(':').Last();
            }
        }

        registry.RegisterEventSourceMapping(mapping);

        return Results.Json(BuildMappingResponse(mapping), JsonOptions, statusCode: 202);
    }

    private static IResult ListEventSourceMappings(IServiceRegistry registry)
    {
        var mappings = registry.GetAllEventSourceMappings()
            .Select(m => BuildMappingResponse(m))
            .ToList();
        return Results.Json(new { EventSourceMappings = mappings }, JsonOptions);
    }

    private static IResult GetEventSourceMapping(string uuid, IServiceRegistry registry)
    {
        if (!Guid.TryParse(uuid, out var guid))
            return Results.BadRequest();

        var mapping = registry.GetEventSourceMapping(guid);
        if (mapping is null)
            return Results.NotFound();

        return Results.Json(BuildMappingResponse(mapping), JsonOptions);
    }

    private static IResult DeleteEventSourceMapping(string uuid, IServiceRegistry registry)
    {
        if (!Guid.TryParse(uuid, out var guid))
            return Results.BadRequest();

        var mapping = registry.GetEventSourceMapping(guid);
        if (mapping is null)
            return Results.NotFound();

        registry.RemoveEventSourceMapping(guid);
        return Results.Json(BuildMappingResponse(mapping), JsonOptions);
    }

    private static object BuildFunctionConfig(LambdaFunction f, IServiceRegistry registry) => new
    {
        FunctionName = f.Name,
        FunctionArn = $"arn:aws:lambda:{registry.Region}:{registry.AccountId}:function:{f.Name}",
        Runtime = "dotnet8",
        Handler = f.Handler,
        MemorySize = f.MemoryMb,
        Timeout = (int)f.Timeout.TotalSeconds,
        State = "Active"
    };

    private static object BuildMappingResponse(EventSourceMapping m) => new
    {
        UUID = m.Uuid.ToString(),
        FunctionArn = m.FunctionName,
        EventSourceArn = m.SourceName,
        BatchSize = m.BatchSize,
        State = m.Enabled ? "Enabled" : "Disabled"
    };
}

public static class LambdaResultExtensions
{
    public static IResult Lambda_FunctionError(this IResultExtensions _, string errorBody)
    {
        return new LambdaFunctionErrorResult(errorBody);
    }

    private sealed class LambdaFunctionErrorResult : IResult
    {
        private readonly string _errorBody;
        public LambdaFunctionErrorResult(string errorBody) => _errorBody = errorBody;

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.Headers["X-Amz-Function-Error"] = "Unhandled";
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync(_errorBody);
        }
    }
}
