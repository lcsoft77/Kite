using Kite.Aspire;
using Kite.Core;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class AspireResourceTests
{
    [Fact]
    public void KiteResource_DefaultValues_AreCorrect()
    {
        var resource = new KiteResource("my-emulator");

        resource.Name.Should().Be("my-emulator");
        resource.Port.Should().Be(4566);
        resource.Region.Should().Be("us-east-1");
        resource.AccountId.Should().Be("000000000000");
        resource.AccessKeyId.Should().Be("test");
        resource.SecretAccessKey.Should().Be("test");
    }

    [Fact]
    public void KiteResource_ServiceUrl_ReflectsPort()
    {
        var resource = new KiteResource("emulator") { Port = 7777 };

        resource.ServiceUrl.Should().Be("http://localhost:7777");
    }

    [Fact]
    public async Task KiteResource_ConnectionStringExpression_ReturnsLocalhostUrl()
    {
        var resource = new KiteResource("emulator") { Port = 9090 };

        var value = await resource.ConnectionStringExpression.GetValueAsync(default);
        value.Should().Be("http://localhost:9090");
    }

    [Fact]
    public void LambdaFunctionResource_DefaultValues_AreCorrect()
    {
        var parent = new KiteResource("emulator");
        var lambda = new LambdaFunctionResource("my-lambda", parent)
        {
            DllPath = "/some/path/handler.dll",
            Handler = "Assembly::Type::Method"
        };

        lambda.Name.Should().Be("my-lambda");
        lambda.Parent.Should().BeSameAs(parent);
        lambda.DllPath.Should().Be("/some/path/handler.dll");
        lambda.Handler.Should().Be("Assembly::Type::Method");
    }

    [Fact]
    public void SqsQueueResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var queue = new SqsQueueResource("my-queue", parent);

        queue.Name.Should().Be("my-queue");
        queue.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void S3BucketResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var bucket = new S3BucketResource("my-bucket", parent);

        bucket.Name.Should().Be("my-bucket");
        bucket.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void EventBridgeBusResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var bus = new EventBridgeBusResource("my-bus", parent);

        bus.Name.Should().Be("my-bus");
        bus.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void KiteResource_ChildCollections_AreInitiallyEmpty()
    {
        var resource = new KiteResource("emulator");

        resource.Lambdas.Should().BeEmpty();
        resource.SqsQueues.Should().BeEmpty();
        resource.S3Buckets.Should().BeEmpty();
        resource.EventBridgeBuses.Should().BeEmpty();
        resource.SqsTriggers.Should().BeEmpty();
        resource.S3Triggers.Should().BeEmpty();
    }

    [Fact]
    public void KiteResource_ChildCollections_CanBePopulated()
    {
        var resource = new KiteResource("emulator");
        var lambda = new LambdaFunctionResource("fn", resource);
        var queue = new SqsQueueResource("q", resource);
        var bucket = new S3BucketResource("b", resource);
        var bus = new EventBridgeBusResource("eb", resource);

        resource.Lambdas.Add(lambda);
        resource.SqsQueues.Add(queue);
        resource.S3Buckets.Add(bucket);
        resource.EventBridgeBuses.Add(bus);
        resource.SqsTriggers.Add(("q", "fn", 10));
        resource.S3Triggers.Add(("b", "fn", S3EventType.ObjectCreated));

        resource.Lambdas.Should().ContainSingle().Which.Name.Should().Be("fn");
        resource.SqsQueues.Should().ContainSingle().Which.Name.Should().Be("q");
        resource.S3Buckets.Should().ContainSingle().Which.Name.Should().Be("b");
        resource.EventBridgeBuses.Should().ContainSingle().Which.Name.Should().Be("eb");
        resource.SqsTriggers.Should().ContainSingle().Which.Should().Be(("q", "fn", 10));
        resource.S3Triggers.Should().ContainSingle();
    }

    [Fact]
    public void SnsTopicResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var topic = new SnsTopicResource("my-topic", parent);

        topic.Name.Should().Be("my-topic");
        topic.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void DynamoDbTableResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var table = new DynamoDbTableResource("my-table", parent);

        table.Name.Should().Be("my-table");
        table.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void EcsClusterResource_Parent_IsSet()
    {
        var parent = new KiteResource("emulator");
        var cluster = new EcsClusterResource("my-cluster", parent);

        cluster.Name.Should().Be("my-cluster");
        cluster.Parent.Should().BeSameAs(parent);
    }

    [Fact]
    public void KiteResource_AdditionalCollections_AreInitiallyEmpty()
    {
        var resource = new KiteResource("emulator");

        resource.SnsTopics.Should().BeEmpty();
        resource.EcsClusters.Should().BeEmpty();
        resource.DynamoDbTables.Should().BeEmpty();
        resource.SsmParameters.Should().BeEmpty();
        resource.EcsTaskDefinitions.Should().BeEmpty();
        resource.DynamoDbTableDefinitions.Should().BeEmpty();
        resource.EventBridgeRuleDefinitions.Should().BeEmpty();
    }

    [Fact]
    public void KiteResource_EventBridgeRuleDefinitions_CanBePopulated()
    {
        var resource = new KiteResource("emulator");

        resource.EventBridgeRuleDefinitions.Add((
            "rule-1",
            "orders",
            new List<string> { "myapp.orders" },
            new List<string> { "OrderCreated" },
            new Dictionary<string, object> { ["status"] = "pending" },
            new List<string> { "order-handler" }));

        resource.EventBridgeRuleDefinitions.Should().ContainSingle();
        var rule = resource.EventBridgeRuleDefinitions[0];
        rule.RuleName.Should().Be("rule-1");
        rule.BusName.Should().Be("orders");
        rule.Sources.Should().ContainSingle().Which.Should().Be("myapp.orders");
        rule.DetailTypes.Should().ContainSingle().Which.Should().Be("OrderCreated");
        rule.Details.Should().ContainKey("status").WhoseValue.Should().Be("pending");
        rule.TargetFunctions.Should().ContainSingle().Which.Should().Be("order-handler");
    }

    [Fact]
    public void KiteResource_CustomCredentials_AreSet()
    {
        var resource = new KiteResource("emulator")
        {
            AccessKeyId = "custom-key",
            SecretAccessKey = "custom-secret",
            Region = "eu-west-1",
            AccountId = "123456789012"
        };

        resource.AccessKeyId.Should().Be("custom-key");
        resource.SecretAccessKey.Should().Be("custom-secret");
        resource.Region.Should().Be("eu-west-1");
        resource.AccountId.Should().Be("123456789012");
    }

    [Fact]
    public void KiteResource_DashboardEnabled_DefaultsFalse()
    {
        var resource = new KiteResource("emulator");

        resource.DashboardEnabled.Should().BeFalse();
        resource.DashboardPort.Should().Be(0);
    }

    [Fact]
    public void KiteResource_DashboardEnabled_CanBeSet()
    {
        var resource = new KiteResource("emulator")
        {
            DashboardEnabled = true,
            DashboardPort = 4567
        };

        resource.DashboardEnabled.Should().BeTrue();
        resource.DashboardPort.Should().Be(4567);
    }

    [Fact]
    public void KiteResource_DashboardPort_ReflectsCustomValue()
    {
        var resource = new KiteResource("emulator")
        {
            DashboardPort = 9999
        };

        resource.DashboardPort.Should().Be(9999);
        resource.DashboardEnabled.Should().BeFalse("setting only the port should not implicitly enable the dashboard");
    }
}
