using Kite.Core.Auth;
using Microsoft.AspNetCore.Http;

namespace Kite.Core.Routing;

public class UnifiedRouter
{
    private readonly RequestDelegate _next;

    public UnifiedRouter(RequestDelegate next)
    {
        _next = next;
    }

    public static string DetermineService(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var contentType = context.Request.ContentType ?? string.Empty;
        var authHeader = context.Request.Headers.TryGetValue("Authorization", out var auth) ? auth.ToString() : string.Empty;

        // Lambda paths
        if (path.StartsWith("/2015-03-31/"))
            return "lambda";

        // SQS via JSON protocol target header
        if (target.StartsWith("AmazonSQS."))
            return "sqs";

        // EventBridge via target header
        if (target.StartsWith("AmazonEventBridge."))
            return "eventbridge";

        // ECS via target header
        if (target.StartsWith("AmazonEC2ContainerServiceV20141113."))
            return "ecs";

        // SSM Parameter Store via target header
        if (target.StartsWith("AmazonSSM."))
            return "ssm";

        // SNS via target header
        if (target.StartsWith("AmazonSimpleNotificationService."))
            return "sns";

        // DynamoDB via target header
        if (target.StartsWith("DynamoDB_20120810."))
            return "dynamodb";

        // SigV4-based routing (checked before content-type to correctly distinguish SNS from SQS)
        var service = SigV4Validator.ExtractServiceName(authHeader);
        if (service == "s3") return "s3";
        if (service == "sqs") return "sqs";
        if (service == "sns") return "sns";
        if (service == "events") return "eventbridge";
        if (service == "ssm") return "ssm";
        if (service == "ecs") return "ecs";
        if (service == "dynamodb") return "dynamodb";

        // Query protocol fallback (unsigned requests default to SQS)
        if (contentType.Contains("application/x-www-form-urlencoded"))
            return "sqs";

        // Fallback to API Gateway
        return "apigateway";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Items["aws-service"] = DetermineService(context);
        await _next(context);
    }
}
