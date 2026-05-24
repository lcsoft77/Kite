using Amazon.EventBridge.Model;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests for EventBridge operations via the official AWS SDK,
/// pointed at the local Kite instance.
/// </summary>
[Collection("EmulatorCollection")]
public class EventBridgeTests
{
    private readonly EmulatorFixture _fixture;

    public EventBridgeTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutEvents_ToRegisteredBus_ReturnsZeroFailedEntries()
    {
        // Arrange
        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = EmulatorFixture.EventBridgeBusName,
                    Source = "integration.test",
                    DetailType = "TestEvent",
                    Detail = """{"key":"value"}"""
                }
            ]
        };

        // Act
        var response = await _fixture.EventBridgeClient.PutEventsAsync(request);

        // Assert
        response.FailedEntryCount.Should().Be(0);
        response.Entries.Should().HaveCount(1);
        response.Entries[0].EventId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PutEvents_MultipleBatchEntries_AllSucceed()
    {
        // Arrange – send 3 events in a single batch
        var entries = Enumerable.Range(1, 3)
            .Select(i => new PutEventsRequestEntry
            {
                EventBusName = EmulatorFixture.EventBridgeBusName,
                Source = $"integration.test.batch",
                DetailType = "BatchTestEvent",
                Detail = $$$"""{"index":{{{i}}}}"""
            })
            .ToList();

        // Act
        var response = await _fixture.EventBridgeClient.PutEventsAsync(new PutEventsRequest
        {
            Entries = entries
        });

        // Assert
        response.FailedEntryCount.Should().Be(0);
        response.Entries.Should().HaveCount(3);
        response.Entries.Should().AllSatisfy(e => e.EventId.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task PutEvents_ToNonExistentBus_ReportsFailedEntries()
    {
        // Arrange
        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = "non-existent-bus",
                    Source = "integration.test",
                    DetailType = "TestEvent",
                    Detail = "{}"
                }
            ]
        };

        // Act
        var response = await _fixture.EventBridgeClient.PutEventsAsync(request);

        // Assert – the emulator returns a failed entry for unknown buses
        response.FailedEntryCount.Should().Be(1);
        response.Entries[0].ErrorCode.Should().NotBeNullOrEmpty();
    }
}
