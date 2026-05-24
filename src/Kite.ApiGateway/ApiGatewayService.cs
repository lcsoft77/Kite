using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Kite.Core;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.ApiGateway;

public class ApiGatewayService
{
    private readonly IServiceRegistry _registry;
    private readonly LambdaExecutor _executor;
    private readonly ILogger<ApiGatewayService> _logger;

    public ApiGatewayService(IServiceRegistry registry, LambdaExecutor executor, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _executor = executor;
        _logger = loggerFactory.CreateLogger<ApiGatewayService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var method = context.Request.Method.ToUpper();
        var path = context.Request.Path.Value ?? "/";

        using var activity = KiteActivitySource.ApiGateway.StartActivity("apigateway.request");
        activity?.SetTag("http.method", method);
        activity?.SetTag("http.url", path);

        var route = FindRoute(method, path);
        if (route is null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Not Found" }));
            return;
        }

        activity?.SetTag("lambda.function", route.FunctionName);

        using var bodyReader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await bodyReader.ReadToEndAsync();

        var queryParams = context.Request.Query.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToString());

        var headers = context.Request.Headers.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToString());

        var pathParams = ExtractPathParameters(route.Path, path);

        var proxyRequest = new APIGatewayProxyRequest
        {
            HttpMethod = method,
            Path = path,
            Body = body,
            QueryStringParameters = queryParams.Count > 0 ? queryParams : null,
            Headers = headers,
            PathParameters = pathParams.Count > 0 ? pathParams : null,
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                HttpMethod = method,
                Path = path,
                RequestId = Guid.NewGuid().ToString()
            },
            IsBase64Encoded = false
        };

        var requestJson = JsonSerializer.Serialize(proxyRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            var responseJson = await _executor.InvokeAsync(route.FunctionName, requestJson);

            APIGatewayProxyResponse? response = null;
            try
            {
                response = JsonSerializer.Deserialize<APIGatewayProxyResponse>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                // If response is not an APIGatewayProxyResponse, return as-is
            }

            if (response is not null)
            {
                context.Response.StatusCode = response.StatusCode;
                if (response.Headers is not null)
                    foreach (var (k, v) in response.Headers)
                        context.Response.Headers[k] = v;

                if (response.MultiValueHeaders is not null)
                    foreach (var (k, values) in response.MultiValueHeaders)
                        context.Response.Headers[k] = new Microsoft.Extensions.Primitives.StringValues(values.ToArray());

                var responseBody = response.IsBase64Encoded
                    ? Convert.FromBase64String(response.Body ?? string.Empty)
                    : Encoding.UTF8.GetBytes(response.Body ?? string.Empty);

                if (!context.Response.Headers.ContainsKey("Content-Type"))
                    context.Response.ContentType = "application/json";

                await context.Response.Body.WriteAsync(responseBody);
            }
            else
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(responseJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiGateway: Lambda {Function} failed for {Method} {Path}", route.FunctionName, method, path);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Internal server error" }));
        }
    }

    private Core.Models.ApiGatewayRoute? FindRoute(string method, string path)
    {
        foreach (var route in _registry.GetAllApiGatewayRoutes())
        {
            if (!route.Method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
                route.Method != "ANY")
                continue;

            if (PathMatches(route.Path, path))
                return route;
        }
        return null;
    }

    private static bool PathMatches(string pattern, string path)
    {
        // Simple pattern matching: {param} segments match any value
        var patternSegments = pattern.Split('/');
        var pathSegments = path.Split('/');

        if (patternSegments.Length != pathSegments.Length)
            return false;

        for (int i = 0; i < patternSegments.Length; i++)
        {
            if (patternSegments[i].StartsWith('{') && patternSegments[i].EndsWith('}'))
                continue;
            if (!patternSegments[i].Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static Dictionary<string, string> ExtractPathParameters(string pattern, string path)
    {
        var result = new Dictionary<string, string>();
        var patternSegments = pattern.Split('/');
        var pathSegments = path.Split('/');

        for (int i = 0; i < Math.Min(patternSegments.Length, pathSegments.Length); i++)
        {
            if (patternSegments[i].StartsWith('{') && patternSegments[i].EndsWith('}'))
            {
                var paramName = patternSegments[i][1..^1];
                result[paramName] = pathSegments[i];
            }
        }
        return result;
    }
}
