using Kite.Core;
using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class SnsTopicTests
{
    private readonly InMemoryServiceRegistry _registry = new();

    [Fact]
    public void RegisterAndGetSnsTopic_ByArn_ShouldWork()
    {
        var topic = new SnsTopic
        {
            Name = "my-topic",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:my-topic"
        };
        _registry.RegisterSnsTopic(topic);

        _registry.GetSnsTopic("arn:aws:sns:us-east-1:000000000000:my-topic").Should().Be(topic);
    }

    [Fact]
    public void RegisterAndGetSnsTopic_ByName_ShouldWork()
    {
        var topic = new SnsTopic
        {
            Name = "my-topic",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:my-topic"
        };
        _registry.RegisterSnsTopic(topic);

        _registry.GetSnsTopic("my-topic").Should().Be(topic);
    }

    [Fact]
    public void GetSnsTopic_NonExistent_ReturnsNull()
    {
        _registry.GetSnsTopic("non-existent").Should().BeNull();
    }

    [Fact]
    public void DeleteSnsTopic_ShouldRemoveTopic()
    {
        var arn = "arn:aws:sns:us-east-1:000000000000:my-topic";
        var topic = new SnsTopic { Name = "my-topic", TopicArn = arn };
        _registry.RegisterSnsTopic(topic);

        _registry.DeleteSnsTopic(arn);

        _registry.GetSnsTopic(arn).Should().BeNull();
        _registry.GetSnsTopic("my-topic").Should().BeNull();
    }

    [Fact]
    public void GetAllSnsTopics_ShouldReturnDistinctTopics()
    {
        _registry.RegisterSnsTopic(new SnsTopic
        {
            Name = "topic-a",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:topic-a"
        });
        _registry.RegisterSnsTopic(new SnsTopic
        {
            Name = "topic-b",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:topic-b"
        });

        _registry.GetAllSnsTopics().Should().HaveCount(2);
    }

    [Fact]
    public void Builder_AddSnsTopic_RegistersTopic()
    {
        var emulator = KiteBuilder.Create()
            .AddSnsTopic("my-notifications")
            .Build();

        var topic = emulator.Registry.GetSnsTopic("my-notifications");
        topic.Should().NotBeNull();
        topic!.Name.Should().Be("my-notifications");
        topic.TopicArn.Should().Contain("my-notifications");
    }

    [Fact]
    public void Builder_AddSnsTopic_ArnContainsRegionAndAccountId()
    {
        var emulator = KiteBuilder.Create()
            .WithRegion("eu-west-1")
            .AddSnsTopic("alerts")
            .Build();

        var topic = emulator.Registry.GetSnsTopic("alerts");
        topic.Should().NotBeNull();
        topic!.TopicArn.Should().Be("arn:aws:sns:eu-west-1:000000000000:alerts");
    }

    [Fact]
    public void SnsTopic_Subscriptions_InitiallyEmpty()
    {
        var topic = new SnsTopic { Name = "test", TopicArn = "arn:aws:sns:us-east-1:000000000000:test" };
        topic.Subscriptions.Should().BeEmpty();
    }

    [Fact]
    public void SnsTopic_AddSubscription_ShouldBeRetrievable()
    {
        var topic = new SnsTopic
        {
            Name = "test",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test"
        };
        _registry.RegisterSnsTopic(topic);

        var subscription = new SnsSubscription
        {
            SubscriptionArn = "arn:aws:sns:us-east-1:000000000000:test:abc123",
            TopicArn = topic.TopicArn,
            Protocol = "lambda",
            Endpoint = "arn:aws:lambda:us-east-1:000000000000:function:my-fn",
            Owner = "000000000000"
        };
        topic.Subscriptions.Add(subscription);

        topic.Subscriptions.Should().HaveCount(1);
        topic.Subscriptions[0].Protocol.Should().Be("lambda");
        topic.Subscriptions[0].Endpoint.Should().Contain("my-fn");
    }

    [Fact]
    public void SnsTopic_NotifyChange_FiresEvent()
    {
        var topic = new SnsTopic { Name = "test", TopicArn = "arn:aws:sns:us-east-1:000000000000:test" };
        var fired = false;
        topic.OnChange += () => fired = true;

        topic.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public void Builder_AddMultipleSnsTopics_AllRegistered()
    {
        var emulator = KiteBuilder.Create()
            .AddSnsTopic("topic-one")
            .AddSnsTopic("topic-two")
            .AddSnsTopic("topic-three")
            .Build();

        emulator.Registry.GetAllSnsTopics().Should().HaveCount(3);
    }
}
