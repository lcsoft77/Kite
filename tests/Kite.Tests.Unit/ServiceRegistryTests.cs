using Kite.Core;
using Kite.Core.Auth;
using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class ServiceRegistryTests
{
    private readonly InMemoryServiceRegistry _registry = new();

    [Fact]
    public void RegisterAndGetLambda_ShouldWork()
    {
        var fn = new LambdaFunction { Name = "test-fn", Handler = "Asm::Type::Method" };
        _registry.RegisterLambda(fn);
        _registry.GetLambda("test-fn").Should().Be(fn);
    }

    [Fact]
    public void RegisterAndGetSqsQueue_ShouldWork()
    {
        var queue = new SqsQueue { Name = "my-queue", QueueUrl = "http://localhost:4566/000/my-queue" };
        _registry.RegisterSqsQueue(queue);
        _registry.GetSqsQueue("my-queue").Should().Be(queue);
    }

    [Fact]
    public void RegisterAndGetS3Bucket_ShouldWork()
    {
        var bucket = new S3Bucket { Name = "my-bucket" };
        _registry.RegisterS3Bucket(bucket);
        _registry.GetS3Bucket("my-bucket").Should().Be(bucket);
    }

    [Fact]
    public void RegisterAndGetEventBridgeBus_ShouldWork()
    {
        var bus = new EventBridgeBus { Name = "default" };
        _registry.RegisterEventBridgeBus(bus);
        _registry.GetEventBridgeBus("default").Should().Be(bus);
    }

    [Fact]
    public void RegisterAndRemoveEventSourceMapping_ShouldWork()
    {
        var mapping = new EventSourceMapping { Type = EventSourceMappingType.SQS, SourceName = "q", FunctionName = "fn" };
        _registry.RegisterEventSourceMapping(mapping);
        _registry.GetEventSourceMapping(mapping.Uuid).Should().Be(mapping);

        _registry.RemoveEventSourceMapping(mapping.Uuid);
        _registry.GetEventSourceMapping(mapping.Uuid).Should().BeNull();
    }

    [Fact]
    public void GetLambda_NonExistent_ReturnsNull()
    {
        _registry.GetLambda("non-existent").Should().BeNull();
    }

    [Fact]
    public void DefaultConfig_HasExpectedValues()
    {
        _registry.Region.Should().Be("us-east-1");
        _registry.AccountId.Should().Be("000000000000");
        _registry.Port.Should().Be(4566);
    }

    [Fact]
    public void RegisterAndGetEcsCluster_ShouldWork()
    {
        var cluster = new EcsCluster { Name = "test-cluster" };
        _registry.RegisterEcsCluster(cluster);
        _registry.GetEcsCluster("test-cluster").Should().Be(cluster);
    }

    [Fact]
    public void DeleteEcsCluster_ShouldRemoveIt()
    {
        _registry.RegisterEcsCluster(new EcsCluster { Name = "to-delete" });
        _registry.DeleteEcsCluster("to-delete");
        _registry.GetEcsCluster("to-delete").Should().BeNull();
    }

    [Fact]
    public void RegisterEcsTaskDefinition_AutoIncrementsRevision()
    {
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "web" });
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "web" });

        _registry.GetEcsTaskDefinition("web", 1).Should().NotBeNull();
        _registry.GetEcsTaskDefinition("web", 2).Should().NotBeNull();
        _registry.GetEcsTaskDefinition("web", 3).Should().BeNull();
    }

    [Fact]
    public void GetEcsTaskDefinitionsByFamily_ReturnsAllRevisions()
    {
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "batch" });
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "batch" });
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "other" });

        var batchDefs = _registry.GetEcsTaskDefinitionsByFamily("batch").ToList();
        batchDefs.Should().HaveCount(2);
    }

    [Fact]
    public void DeregisterEcsTaskDefinition_SetsStatusInactive()
    {
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "old-job" });
        _registry.DeregisterEcsTaskDefinition("old-job", 1);

        var def = _registry.GetEcsTaskDefinition("old-job", 1);
        def.Should().NotBeNull();
        def!.Status.Should().Be("INACTIVE");
    }

    [Fact]
    public void RegisterAndGetEcsTask_ShouldWork()
    {
        var task = new EcsTask
        {
            TaskArn = "arn:aws:ecs:us-east-1:000000000000:task/my-cluster/abc123",
            ClusterArn = "arn:aws:ecs:us-east-1:000000000000:cluster/my-cluster",
            TaskDefinitionArn = "arn:aws:ecs:us-east-1:000000000000:task-definition/web:1"
        };
        _registry.RegisterEcsTask(task);
        _registry.GetEcsTask(task.TaskArn).Should().Be(task);
    }

    [Fact]
    public void GetEcsTasksForCluster_ReturnsOnlyMatchingCluster()
    {
        var clusterArn = "arn:aws:ecs:us-east-1:000000000000:cluster/my-cluster";
        _registry.RegisterEcsTask(new EcsTask
        {
            TaskArn = "arn:aws:ecs:us-east-1:000000000000:task/my-cluster/t1",
            ClusterArn = clusterArn
        });
        _registry.RegisterEcsTask(new EcsTask
        {
            TaskArn = "arn:aws:ecs:us-east-1:000000000000:task/other-cluster/t2",
            ClusterArn = "arn:aws:ecs:us-east-1:000000000000:cluster/other-cluster"
        });

        var tasks = _registry.GetEcsTasksForCluster(clusterArn).ToList();
        tasks.Should().HaveCount(1);
        tasks[0].TaskArn.Should().Contain("my-cluster/t1");
    }

    // ── Additional coverage: Get*NonExistent & GetAll* ────────────────────────

    [Fact]
    public void GetAllLambdas_ReturnsAllRegistered()
    {
        _registry.RegisterLambda(new LambdaFunction { Name = "fn-a", Handler = "A::B::C" });
        _registry.RegisterLambda(new LambdaFunction { Name = "fn-b", Handler = "D::E::F" });

        _registry.GetAllLambdas().Should().HaveCount(2);
    }

    [Fact]
    public void GetSqsQueue_NonExistent_ReturnsNull()
    {
        _registry.GetSqsQueue("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetAllSqsQueues_ReturnsAllRegistered()
    {
        _registry.RegisterSqsQueue(new SqsQueue { Name = "q1" });
        _registry.RegisterSqsQueue(new SqsQueue { Name = "q2" });

        _registry.GetAllSqsQueues().Should().HaveCount(2);
    }

    [Fact]
    public void GetS3Bucket_NonExistent_ReturnsNull()
    {
        _registry.GetS3Bucket("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetAllS3Buckets_ReturnsAllRegistered()
    {
        _registry.RegisterS3Bucket(new S3Bucket { Name = "b1" });
        _registry.RegisterS3Bucket(new S3Bucket { Name = "b2" });

        _registry.GetAllS3Buckets().Should().HaveCount(2);
    }

    [Fact]
    public void GetEventBridgeBus_NonExistent_ReturnsNull()
    {
        _registry.GetEventBridgeBus("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetAllEventBridgeBuses_ReturnsAllRegistered()
    {
        _registry.RegisterEventBridgeBus(new EventBridgeBus { Name = "bus-a" });
        _registry.RegisterEventBridgeBus(new EventBridgeBus { Name = "bus-b" });

        _registry.GetAllEventBridgeBuses().Should().HaveCount(2);
    }

    [Fact]
    public void GetEventSourceMapping_NonExistent_ReturnsNull()
    {
        _registry.GetEventSourceMapping(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void GetAllEventSourceMappings_ReturnsAllRegistered()
    {
        _registry.RegisterEventSourceMapping(new EventSourceMapping { SourceName = "q1", FunctionName = "fn1" });
        _registry.RegisterEventSourceMapping(new EventSourceMapping { SourceName = "q2", FunctionName = "fn2" });

        _registry.GetAllEventSourceMappings().Should().HaveCount(2);
    }

    [Fact]
    public void RemoveEventSourceMapping_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.RemoveEventSourceMapping(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterAndGetApiGatewayRoute_ShouldWork()
    {
        _registry.RegisterApiGatewayRoute(new ApiGatewayRoute { Method = "GET", Path = "/api", FunctionName = "fn" });

        var routes = _registry.GetAllApiGatewayRoutes().ToList();
        routes.Should().HaveCount(1);
        routes[0].Method.Should().Be("GET");
    }

    [Fact]
    public void GetEcsCluster_NonExistent_ReturnsNull()
    {
        _registry.GetEcsCluster("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetAllEcsClusters_ReturnsAllRegistered()
    {
        _registry.RegisterEcsCluster(new EcsCluster { Name = "c1" });
        _registry.RegisterEcsCluster(new EcsCluster { Name = "c2" });

        _registry.GetAllEcsClusters().Should().HaveCount(2);
    }

    [Fact]
    public void GetEcsTaskDefinition_NonExistent_ReturnsNull()
    {
        _registry.GetEcsTaskDefinition("no-family", 1).Should().BeNull();
    }

    [Fact]
    public void GetAllEcsTaskDefinitions_ReturnsAllRegistered()
    {
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "web" });
        _registry.RegisterEcsTaskDefinition(new EcsTaskDefinition { Family = "worker" });

        _registry.GetAllEcsTaskDefinitions().Should().HaveCount(2);
    }

    [Fact]
    public void DeregisterEcsTaskDefinition_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeregisterEcsTaskDefinition("no-family", 99);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetEcsTask_NonExistent_ReturnsNull()
    {
        _registry.GetEcsTask("arn:aws:ecs:us-east-1:000:task/missing").Should().BeNull();
    }

    [Fact]
    public void GetEcsTasksForCluster_Empty_ReturnsEmpty()
    {
        _registry.GetEcsTasksForCluster("arn:aws:ecs:us-east-1:000:cluster/empty").Should().BeEmpty();
    }

    [Fact]
    public void UpdateEcsTask_ShouldReplaceExistingTask()
    {
        var task = new EcsTask { TaskArn = "arn:task/1", ClusterArn = "arn:cluster/1", LastStatus = "RUNNING" };
        _registry.RegisterEcsTask(task);

        var updated = new EcsTask { TaskArn = "arn:task/1", ClusterArn = "arn:cluster/1", LastStatus = "STOPPED" };
        _registry.UpdateEcsTask(updated);

        _registry.GetEcsTask("arn:task/1")!.LastStatus.Should().Be("STOPPED");
    }

    [Fact]
    public void DeleteEcsTask_ShouldRemoveTask()
    {
        var task = new EcsTask { TaskArn = "arn:task/to-delete", ClusterArn = "arn:cluster/1" };
        _registry.RegisterEcsTask(task);
        _registry.DeleteEcsTask("arn:task/to-delete");

        _registry.GetEcsTask("arn:task/to-delete").Should().BeNull();
    }

    [Fact]
    public void DeleteEcsTask_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeleteEcsTask("arn:task/non-existent");
        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteEcsCluster_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeleteEcsCluster("non-existent");
        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteDynamoDbTable_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeleteDynamoDbTable("non-existent");
        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteSsmParameter_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeleteSsmParameter("/non/existent");
        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteSnsTopic_NonExistent_DoesNotThrow()
    {
        var act = () => _registry.DeleteSnsTopic("arn:aws:sns:us-east-1:000:non-existent");
        act.Should().NotThrow();
    }

    [Fact]
    public void Lambda_CaseInsensitive_Lookup()
    {
        _registry.RegisterLambda(new LambdaFunction { Name = "MyFunc", Handler = "A::B::C" });

        _registry.GetLambda("myfunc").Should().NotBeNull();
        _registry.GetLambda("MYFUNC").Should().NotBeNull();
    }

    [Fact]
    public void SqsQueue_CaseInsensitive_Lookup()
    {
        _registry.RegisterSqsQueue(new SqsQueue { Name = "MyQueue" });

        _registry.GetSqsQueue("myqueue").Should().NotBeNull();
        _registry.GetSqsQueue("MYQUEUE").Should().NotBeNull();
    }

    [Fact]
    public void S3Bucket_CaseInsensitive_Lookup()
    {
        _registry.RegisterS3Bucket(new S3Bucket { Name = "MyBucket" });

        _registry.GetS3Bucket("mybucket").Should().NotBeNull();
        _registry.GetS3Bucket("MYBUCKET").Should().NotBeNull();
    }

    [Fact]
    public void EventBridgeBus_CaseInsensitive_Lookup()
    {
        _registry.RegisterEventBridgeBus(new EventBridgeBus { Name = "MyBus" });

        _registry.GetEventBridgeBus("mybus").Should().NotBeNull();
        _registry.GetEventBridgeBus("MYBUS").Should().NotBeNull();
    }

    [Fact]
    public void SsmParameter_CaseSensitive_Lookup()
    {
        _registry.RegisterSsmParameter(new SsmParameter { Name = "/app/Key", Value = "val" });

        _registry.GetSsmParameter("/app/Key").Should().NotBeNull();
        _registry.GetSsmParameter("/app/key").Should().BeNull();
    }

    [Fact]
    public void ConfigProperties_CanBeChanged()
    {
        _registry.Region = "eu-west-1";
        _registry.AccountId = "123456789012";
        _registry.AccessKeyId = "customKey";
        _registry.SecretAccessKey = "customSecret";
        _registry.Port = 9999;

        _registry.Region.Should().Be("eu-west-1");
        _registry.AccountId.Should().Be("123456789012");
        _registry.AccessKeyId.Should().Be("customKey");
        _registry.SecretAccessKey.Should().Be("customSecret");
        _registry.Port.Should().Be(9999);
    }

    [Fact]
    public void RegisterLambda_OverwritesExisting()
    {
        _registry.RegisterLambda(new LambdaFunction { Name = "fn", Handler = "A::B::C" });
        _registry.RegisterLambda(new LambdaFunction { Name = "fn", Handler = "D::E::F" });

        _registry.GetLambda("fn")!.Handler.Should().Be("D::E::F");
        _registry.GetAllLambdas().Should().HaveCount(1);
    }

    [Fact]
    public void RegisterSqsQueue_OverwritesExisting()
    {
        _registry.RegisterSqsQueue(new SqsQueue { Name = "q", QueueUrl = "http://old" });
        _registry.RegisterSqsQueue(new SqsQueue { Name = "q", QueueUrl = "http://new" });

        _registry.GetSqsQueue("q")!.QueueUrl.Should().Be("http://new");
        _registry.GetAllSqsQueues().Should().HaveCount(1);
    }

    [Fact]
    public void EcsCluster_CaseInsensitive_Lookup()
    {
        _registry.RegisterEcsCluster(new EcsCluster { Name = "MyCluster" });

        _registry.GetEcsCluster("mycluster").Should().NotBeNull();
        _registry.GetEcsCluster("MYCLUSTER").Should().NotBeNull();
    }
}
