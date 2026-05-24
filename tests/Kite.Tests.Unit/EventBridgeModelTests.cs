using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class EventBridgeModelTests
{
    [Fact]
    public void EventBridgeBus_DefaultValues_AreCorrect()
    {
        var bus = new EventBridgeBus();
        bus.Name.Should().BeEmpty();
        bus.Rules.Should().NotBeNull().And.BeEmpty();
        bus.RequestLog.Should().NotBeNull().And.BeEmpty();
        bus.SentMessages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EventBridgeBus_NotifyChange_FiresEvent()
    {
        var bus = new EventBridgeBus { Name = "default" };
        var fired = false;
        bus.OnChange += () => fired = true;

        bus.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public void EventBridgeRule_DefaultValues_AreCorrect()
    {
        var rule = new EventBridgeRule();
        rule.Name.Should().BeEmpty();
        rule.BusName.Should().Be("default");
        rule.EventPattern.Should().NotBeNull();
        rule.Targets.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void EventPattern_DefaultValues_AreCorrect()
    {
        var pattern = new EventPattern();
        pattern.Source.Should().BeNull();
        pattern.DetailType.Should().BeNull();
        pattern.Detail.Should().BeNull();
    }

    [Fact]
    public void EventBridgeTarget_DefaultValues_AreCorrect()
    {
        var target = new EventBridgeTarget();
        target.Id.Should().BeEmpty();
        target.LambdaFunctionName.Should().BeEmpty();
    }

    [Fact]
    public void EventBridgeBus_AddRule_IsRetrievable()
    {
        var bus = new EventBridgeBus { Name = "default" };
        var rule = new EventBridgeRule
        {
            Name = "my-rule",
            BusName = "default",
            Targets = [new EventBridgeTarget { Id = "t1", LambdaFunctionName = "fn" }]
        };

        bus.Rules.Add(rule);

        bus.Rules.Should().HaveCount(1);
        bus.Rules[0].Targets.Should().HaveCount(1);
        bus.Rules[0].Targets[0].LambdaFunctionName.Should().Be("fn");
    }
}
