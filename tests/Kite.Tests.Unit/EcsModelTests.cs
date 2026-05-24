using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class EcsModelTests
{
    [Fact]
    public void EcsCluster_DefaultValues_AreCorrect()
    {
        var cluster = new EcsCluster();
        cluster.Name.Should().BeEmpty();
        cluster.Status.Should().Be("ACTIVE");
        cluster.RequestLog.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EcsCluster_NotifyChange_FiresEvent()
    {
        var cluster = new EcsCluster { Name = "test" };
        var fired = false;
        cluster.OnChange += () => fired = true;

        cluster.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public void EcsTaskDefinition_DefaultValues_AreCorrect()
    {
        var def = new EcsTaskDefinition();
        def.Family.Should().BeEmpty();
        def.Revision.Should().Be(1);
        def.Status.Should().Be("ACTIVE");
        def.NetworkMode.Should().Be("bridge");
        def.Cpu.Should().BeNull();
        def.Memory.Should().BeNull();
        def.ContainerDefinitions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EcsContainerDefinition_DefaultValues_AreCorrect()
    {
        var container = new EcsContainerDefinition();
        container.Name.Should().BeEmpty();
        container.Image.Should().BeEmpty();
        container.Cpu.Should().BeNull();
        container.Memory.Should().BeNull();
        container.Essential.Should().BeTrue();
        container.PortMappings.Should().NotBeNull().And.BeEmpty();
        container.Environment.Should().NotBeNull().And.BeEmpty();
        container.Command.Should().BeNull();
    }

    [Fact]
    public void EcsPortMapping_DefaultValues_AreCorrect()
    {
        var mapping = new EcsPortMapping();
        mapping.ContainerPort.Should().Be(0);
        mapping.HostPort.Should().BeNull();
        mapping.Protocol.Should().Be("tcp");
    }

    [Fact]
    public void EcsKeyValuePair_DefaultValues_AreCorrect()
    {
        var kvp = new EcsKeyValuePair();
        kvp.Name.Should().BeEmpty();
        kvp.Value.Should().BeEmpty();
    }

    [Fact]
    public void EcsTask_DefaultValues_AreCorrect()
    {
        var task = new EcsTask();
        task.TaskArn.Should().BeEmpty();
        task.ClusterArn.Should().BeEmpty();
        task.TaskDefinitionArn.Should().BeEmpty();
        task.LastStatus.Should().Be("RUNNING");
        task.DesiredStatus.Should().Be("RUNNING");
        task.StartedAt.Should().BeNull();
        task.StoppedAt.Should().BeNull();
        task.StopCode.Should().BeNull();
        task.StoppedReason.Should().BeNull();
    }
}
