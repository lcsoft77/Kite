using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class MappingModelTests
{
    [Fact]
    public void EventSourceMapping_DefaultValues_AreCorrect()
    {
        var mapping = new EventSourceMapping();
        mapping.Uuid.Should().NotBeEmpty();
        mapping.SourceName.Should().BeEmpty();
        mapping.FunctionName.Should().BeEmpty();
        mapping.BatchSize.Should().Be(10);
        mapping.Enabled.Should().BeTrue();
    }

    [Fact]
    public void EventSourceMapping_UniqueUuids()
    {
        var m1 = new EventSourceMapping();
        var m2 = new EventSourceMapping();

        m1.Uuid.Should().NotBe(m2.Uuid);
    }

    [Fact]
    public void ApiGatewayRoute_DefaultValues_AreCorrect()
    {
        var route = new ApiGatewayRoute();
        route.Method.Should().BeEmpty();
        route.Path.Should().BeEmpty();
        route.FunctionName.Should().BeEmpty();
    }
}
