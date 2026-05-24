using Amazon.SQS.Model;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests that send SQS messages via the official AWS SDK and verify
/// that the emulator's ESM poller triggers the connected Lambda function.
/// </summary>
[Collection("EmulatorCollection")]
public class SqsTriggerTests
{
    private readonly EmulatorFixture _fixture;

    public SqsTriggerTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendSqsMessage_TriggersLambdaViaTrigger_QueueBecomesEmpty()
    {
        // Arrange – send a message to the trigger queue
        var sendRequest = new SendMessageRequest
        {
            QueueUrl = _fixture.SqsQueueUrl,
            MessageBody = "integration-test-message"
        };

        var sendResponse = await _fixture.SqsClient.SendMessageAsync(sendRequest);
        sendResponse.MessageId.Should().NotBeNullOrEmpty();

        // Act – wait for the ESM poller (polls every 1 s) to pick up and process the message
        var queueEmpty = await PollUntilQueueEmptyAsync(_fixture.SqsQueueUrl, timeoutSeconds: 10);

        // Assert
        queueEmpty.Should().BeTrue("the SQS-to-Lambda trigger should have consumed the message");
    }

    [Fact]
    public async Task SendMultipleSqsMessages_AllProcessedByLambda()
    {
        const int messageCount = 3;

        // Arrange – enqueue several messages
        for (int i = 0; i < messageCount; i++)
        {
            await _fixture.SqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _fixture.SqsQueueUrl,
                MessageBody = $"batch-message-{i}"
            });
        }

        // Act – wait for all messages to be consumed
        var queueEmpty = await PollUntilQueueEmptyAsync(_fixture.SqsQueueUrl, timeoutSeconds: 15);

        // Assert
        queueEmpty.Should().BeTrue("all messages should have been processed by the Lambda trigger");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<bool> PollUntilQueueEmptyAsync(string queueUrl, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var attrs = await _fixture.SqsClient.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"]
            });

            if (attrs.Attributes.TryGetValue("ApproximateNumberOfMessages", out var vis) &&
                attrs.Attributes.TryGetValue("ApproximateNumberOfMessagesNotVisible", out var inFlight) &&
                vis == "0" && inFlight == "0")
            {
                return true;
            }

            await Task.Delay(500);
        }
        return false;
    }
}
