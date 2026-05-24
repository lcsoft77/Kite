using Kite.Core.Routing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kite.Tests.Unit;

public class UnifiedRouterTests
{
    private static HttpContext CreateContext(string path, string? target = null, string? contentType = null, string? authHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (target != null) context.Request.Headers["X-Amz-Target"] = target;
        if (contentType != null) context.Request.ContentType = contentType;
        if (authHeader != null) context.Request.Headers["Authorization"] = authHeader;
        return context;
    }

    [Fact]
    public void LambdaPath_RoutesToLambda()
    {
        var ctx = CreateContext("/2015-03-31/functions/my-fn/invocations");
        UnifiedRouter.DetermineService(ctx).Should().Be("lambda");
    }

    [Fact]
    public void SqsTarget_RoutesToSqs()
    {
        var ctx = CreateContext("/", target: "AmazonSQS.SendMessage");
        UnifiedRouter.DetermineService(ctx).Should().Be("sqs");
    }

    [Fact]
    public void EventBridgeTarget_RoutesToEventBridge()
    {
        var ctx = CreateContext("/", target: "AmazonEventBridge.PutEvents");
        UnifiedRouter.DetermineService(ctx).Should().Be("eventbridge");
    }

    [Fact]
    public void FormEncodedContentType_RoutesToSqs()
    {
        var ctx = CreateContext("/", contentType: "application/x-www-form-urlencoded");
        UnifiedRouter.DetermineService(ctx).Should().Be("sqs");
    }

    [Fact]
    public void S3SigV4Header_RoutesToS3()
    {
        var ctx = CreateContext("/my-bucket/key",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/s3/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("s3");
    }

    [Fact]
    public void EventsSigV4Header_RoutesToEventBridge()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/events/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("eventbridge");
    }

    [Fact]
    public void UnknownPath_RoutesToApiGateway()
    {
        var ctx = CreateContext("/api/products");
        UnifiedRouter.DetermineService(ctx).Should().Be("apigateway");
    }

    [Fact]
    public void SsmTarget_RoutesToSsm()
    {
        var ctx = CreateContext("/", target: "AmazonSSM.GetParameter");
        UnifiedRouter.DetermineService(ctx).Should().Be("ssm");
    }

    [Fact]
    public void SsmSigV4Header_RoutesToSsm()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/ssm/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("ssm");
    }

    [Fact]
    public void DynamoDbTarget_RoutesToDynamoDb()
    {
        var ctx = CreateContext("/", target: "DynamoDB_20120810.CreateTable");
        UnifiedRouter.DetermineService(ctx).Should().Be("dynamodb");
    }

    [Fact]
    public void DynamoDbSigV4Header_RoutesToDynamoDb()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/dynamodb/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("dynamodb");
    }

    [Fact]
    public void EcsTarget_RoutesToEcs()
    {
        var ctx = CreateContext("/", target: "AmazonEC2ContainerServiceV20141113.ListClusters");
        UnifiedRouter.DetermineService(ctx).Should().Be("ecs");
    }

    [Fact]
    public void SnsTarget_RoutesToSns()
    {
        var ctx = CreateContext("/", target: "AmazonSimpleNotificationService.CreateTopic");
        UnifiedRouter.DetermineService(ctx).Should().Be("sns");
    }

    [Fact]
    public void SnsSigV4Header_RoutesToSns()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/sns/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("sns");
    }

    [Fact]
    public void EcsSigV4Header_RoutesToEcs()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/ecs/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("ecs");
    }

    [Fact]
    public void SqsSigV4Header_RoutesToSqs()
    {
        var ctx = CreateContext("/",
            authHeader: "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/sqs/aws4_request, SignedHeaders=host, Signature=sig");
        UnifiedRouter.DetermineService(ctx).Should().Be("sqs");
    }

    [Fact]
    public void NoHeaders_NoPath_RoutesToApiGateway()
    {
        var ctx = CreateContext("/");
        UnifiedRouter.DetermineService(ctx).Should().Be("apigateway");
    }
}
