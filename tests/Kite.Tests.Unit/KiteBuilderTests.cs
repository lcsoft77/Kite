using Kite.Core;
using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class KiteBuilderTests
{
    [Fact]
    public void Build_WithBasicConfig_CreatesEmulator()
    {
        var emulator = KiteBuilder.Create()
            .WithPort(5001)
            .WithRegion("eu-west-1")
            .WithCredentials("mykey", "mysecret")
            .Build();

        emulator.Should().NotBeNull();
        emulator.Registry.Port.Should().Be(5001);
        emulator.Registry.Region.Should().Be("eu-west-1");
        emulator.Registry.AccessKeyId.Should().Be("mykey");
        emulator.Registry.SecretAccessKey.Should().Be("mysecret");
    }

    [Fact]
    public void Build_WithSqsQueue_RegistersQueue()
    {
        var emulator = KiteBuilder.Create()
            .WithPort(5002)
            .AddSQSQueue("test-queue")
            .Build();

        var queue = emulator.Registry.GetSqsQueue("test-queue");
        queue.Should().NotBeNull();
        queue!.Name.Should().Be("test-queue");
        queue.QueueUrl.Should().Contain("test-queue");
    }

    [Fact]
    public void Build_WithS3Bucket_RegistersBucket()
    {
        var emulator = KiteBuilder.Create()
            .AddS3Bucket("my-bucket")
            .Build();

        emulator.Registry.GetS3Bucket("my-bucket").Should().NotBeNull();
    }

    [Fact]
    public void Build_WithLambda_RegistersFunction()
    {
        var emulator = KiteBuilder.Create()
            .AddLambda("my-fn", lb => lb
                .WithHandler("Asm::Type::Method")
                .WithMemory(256)
                .WithTimeout(TimeSpan.FromSeconds(60)))
            .Build();

        var fn = emulator.Registry.GetLambda("my-fn");
        fn.Should().NotBeNull();
        fn!.Handler.Should().Be("Asm::Type::Method");
        fn.MemoryMb.Should().Be(256);
        fn.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Build_WithSqsTrigger_CreatesEventSourceMapping()
    {
        var emulator = KiteBuilder.Create()
            .AddSQSQueue("trigger-queue")
            .AddLambda("trigger-fn", lb => lb.WithHandler("A::B::C"))
            .AddSQSTrigger("trigger-queue", "trigger-fn", batchSize: 5)
            .Build();

        var mappings = emulator.Registry.GetAllEventSourceMappings().ToList();
        mappings.Should().HaveCount(1);
        mappings[0].Type.Should().Be(EventSourceMappingType.SQS);
        mappings[0].SourceName.Should().Be("trigger-queue");
        mappings[0].FunctionName.Should().Be("trigger-fn");
        mappings[0].BatchSize.Should().Be(5);
    }

    [Fact]
    public void Build_WithApiGatewayRoute_RegistersRoute()
    {
        var emulator = KiteBuilder.Create()
            .AddLambda("api-fn", lb => lb.WithHandler("A::B::C"))
            .AddApiGatewayRoute("GET", "/api/items", "api-fn")
            .Build();

        var routes = emulator.Registry.GetAllApiGatewayRoutes().ToList();
        routes.Should().HaveCount(1);
        routes[0].Method.Should().Be("GET");
        routes[0].Path.Should().Be("/api/items");
        routes[0].FunctionName.Should().Be("api-fn");
    }

    [Fact]
    public void Build_WithEcsCluster_RegistersCluster()
    {
        var emulator = KiteBuilder.Create()
            .AddEcsCluster("my-cluster")
            .Build();

        var cluster = emulator.Registry.GetEcsCluster("my-cluster");
        cluster.Should().NotBeNull();
        cluster!.Name.Should().Be("my-cluster");
        cluster.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public void Build_WithEcsTaskDefinition_RegistersDefinition()
    {
        var emulator = KiteBuilder.Create()
            .AddEcsTaskDefinition("web-server", t => t
                .WithContainer("nginx", "nginx:latest", c => c
                    .WithPortMapping(80)
                    .WithMemory(512)))
            .Build();

        var defs = emulator.Registry.GetEcsTaskDefinitionsByFamily("web-server").ToList();
        defs.Should().HaveCount(1);
        defs[0].Family.Should().Be("web-server");
        defs[0].Revision.Should().Be(1);
        defs[0].Status.Should().Be("ACTIVE");
        defs[0].ContainerDefinitions.Should().HaveCount(1);
        defs[0].ContainerDefinitions[0].Name.Should().Be("nginx");
        defs[0].ContainerDefinitions[0].Image.Should().Be("nginx:latest");
    }

    [Fact]
    public void Build_MultipleRevisions_IncrementsRevision()
    {
        var emulator = KiteBuilder.Create()
            .AddEcsTaskDefinition("worker")
            .AddEcsTaskDefinition("worker")
            .Build();

        var defs = emulator.Registry.GetEcsTaskDefinitionsByFamily("worker")
            .OrderBy(d => d.Revision)
            .ToList();
        defs.Should().HaveCount(2);
        defs[0].Revision.Should().Be(1);
        defs[1].Revision.Should().Be(2);
    }

    [Fact]
    public void Build_WithEcsClusterAndTaskDefinition_NeitherAutoStarts()
    {
        var emulator = KiteBuilder.Create()
            .AddEcsCluster("prod-cluster")
            .AddEcsTaskDefinition("batch-job", t => t
                .WithContainer("runner", "my-image:latest"))
            .Build();

        // Cluster exists but no tasks are running
        var cluster = emulator.Registry.GetEcsCluster("prod-cluster");
        cluster.Should().NotBeNull();

        var clusterArn = $"arn:aws:ecs:{emulator.Registry.Region}:{emulator.Registry.AccountId}:cluster/prod-cluster";
        var tasks = emulator.Registry.GetEcsTasksForCluster(clusterArn).ToList();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithEventBridgeBus_RegistersBus()
    {
        var emulator = KiteBuilder.Create()
            .AddEventBridgeBus("my-bus")
            .Build();

        var bus = emulator.Registry.GetEventBridgeBus("my-bus");
        bus.Should().NotBeNull();
        bus!.Name.Should().Be("my-bus");
    }

    [Fact]
    public void Build_WithEventBridgeRule_RegistersRuleOnBus()
    {
        var emulator = KiteBuilder.Create()
            .AddEventBridgeBus("default")
            .AddEventBridgeRule("my-rule", r => r
                .OnBus("default")
                .MatchSource("my-app")
                .MatchDetailType("order.created")
                .TargetLambda("my-fn"))
            .AddLambda("my-fn", lb => lb.WithHandler("A::B::C"))
            .Build();

        var bus = emulator.Registry.GetEventBridgeBus("default");
        bus.Should().NotBeNull();
        bus!.Rules.Should().HaveCount(1);
        bus.Rules[0].Name.Should().Be("my-rule");
        bus.Rules[0].Targets.Should().HaveCount(1);
        bus.Rules[0].Targets[0].LambdaFunctionName.Should().Be("my-fn");
        bus.Rules[0].EventPattern.Source.Should().Contain("my-app");
        bus.Rules[0].EventPattern.DetailType.Should().Contain("order.created");
    }

    [Fact]
    public void Build_WithEventBridgeRule_AutoCreatesBusIfMissing()
    {
        var emulator = KiteBuilder.Create()
            .AddEventBridgeRule("auto-rule", r => r
                .OnBus("auto-bus")
                .TargetLambda("fn"))
            .Build();

        var bus = emulator.Registry.GetEventBridgeBus("auto-bus");
        bus.Should().NotBeNull();
        bus!.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void Build_WithS3Trigger_CreatesNotificationAndEsm()
    {
        var emulator = KiteBuilder.Create()
            .AddS3Bucket("my-bucket")
            .AddLambda("s3-fn", lb => lb.WithHandler("A::B::C"))
            .AddS3Trigger("my-bucket", "s3-fn", S3EventType.ObjectCreated)
            .Build();

        var bucket = emulator.Registry.GetS3Bucket("my-bucket");
        bucket!.NotificationConfiguration.Should().NotBeNull();
        bucket.NotificationConfiguration!.LambdaFunctionConfigurations.Should().HaveCount(1);
        bucket.NotificationConfiguration.LambdaFunctionConfigurations[0].Events.Should().Contain("s3:ObjectCreated:*");

        var mappings = emulator.Registry.GetAllEventSourceMappings().ToList();
        mappings.Should().Contain(m => m.Type == EventSourceMappingType.S3 && m.SourceName == "my-bucket");
    }

    [Fact]
    public void Build_WithS3Trigger_ObjectRemoved()
    {
        var emulator = KiteBuilder.Create()
            .AddS3Bucket("remove-bucket")
            .AddLambda("fn", lb => lb.WithHandler("A::B::C"))
            .AddS3Trigger("remove-bucket", "fn", S3EventType.ObjectRemoved)
            .Build();

        var bucket = emulator.Registry.GetS3Bucket("remove-bucket");
        bucket!.NotificationConfiguration!.LambdaFunctionConfigurations[0].Events
            .Should().Contain("s3:ObjectRemoved:*");
    }

    [Fact]
    public void Build_DefaultLambdaValues_AreApplied()
    {
        var emulator = KiteBuilder.Create()
            .AddLambda("default-fn", lb => lb
                .WithHandler("A::B::C")
                .WithDll("/some/path.dll"))
            .Build();

        var fn = emulator.Registry.GetLambda("default-fn");
        fn!.MemoryMb.Should().Be(128);
        fn.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        fn.DllPath.Should().Be("/some/path.dll");
    }

    [Fact]
    public void Build_WithDefaults_HasCorrectRegistryValues()
    {
        var emulator = KiteBuilder.Create().Build();

        emulator.Registry.Region.Should().Be("us-east-1");
        emulator.Registry.AccountId.Should().Be("000000000000");
        emulator.Registry.Port.Should().Be(4566);
        emulator.Registry.AccessKeyId.Should().Be("test");
        emulator.Registry.SecretAccessKey.Should().Be("test");
    }

    [Fact]
    public void Build_WithMultipleSqsQueues_AllRegistered()
    {
        var emulator = KiteBuilder.Create()
            .AddSQSQueue("q1")
            .AddSQSQueue("q2")
            .AddSQSQueue("q3")
            .Build();

        emulator.Registry.GetAllSqsQueues().Should().HaveCount(3);
    }

    [Fact]
    public void Build_WithApiGatewayRoute_MethodConvertedToUppercase()
    {
        var emulator = KiteBuilder.Create()
            .AddApiGatewayRoute("post", "/api/items", "fn")
            .Build();

        var routes = emulator.Registry.GetAllApiGatewayRoutes().ToList();
        routes[0].Method.Should().Be("POST");
    }

    [Fact]
    public void Build_SqsQueueUrl_ContainsPort()
    {
        var emulator = KiteBuilder.Create()
            .WithPort(8888)
            .AddSQSQueue("test-queue")
            .Build();

        var queue = emulator.Registry.GetSqsQueue("test-queue");
        queue!.QueueUrl.Should().Contain(":8888/");
    }

    [Fact]
    public void Build_WithMultipleS3Buckets_AllRegistered()
    {
        var emulator = KiteBuilder.Create()
            .AddS3Bucket("b1")
            .AddS3Bucket("b2")
            .Build();

        emulator.Registry.GetAllS3Buckets().Should().HaveCount(2);
    }

    [Fact]
    public void Build_WithEcsTaskDefinitionBuilder_ConfiguresNetworkAndResources()
    {
        var emulator = KiteBuilder.Create()
            .AddEcsTaskDefinition("my-task", t => t
                .WithNetworkMode("awsvpc")
                .WithCpu(256)
                .WithMemory(512)
                .WithContainer("app", "my-app:latest", c => c
                    .WithCpu(128)
                    .WithMemory(256)
                    .WithEssential(true)
                    .WithCommand("dotnet", "run")
                    .WithEnvironment("ENV", "production")
                    .WithPortMapping(8080, 80, "tcp")))
            .Build();

        var defs = emulator.Registry.GetEcsTaskDefinitionsByFamily("my-task").ToList();
        defs.Should().HaveCount(1);
        defs[0].NetworkMode.Should().Be("awsvpc");
        defs[0].Cpu.Should().Be(256);
        defs[0].Memory.Should().Be(512);

        var container = defs[0].ContainerDefinitions[0];
        container.Name.Should().Be("app");
        container.Image.Should().Be("my-app:latest");
        container.Cpu.Should().Be(128);
        container.Memory.Should().Be(256);
        container.Essential.Should().BeTrue();
        container.Command.Should().BeEquivalentTo("dotnet", "run");
        container.Environment.Should().ContainSingle(e => e.Name == "ENV" && e.Value == "production");
        container.PortMappings.Should().ContainSingle(p => p.ContainerPort == 8080 && p.HostPort == 80);
    }
}
