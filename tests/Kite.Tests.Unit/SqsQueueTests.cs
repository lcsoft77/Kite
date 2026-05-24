using System.Collections.Concurrent;
using Kite.Core.Models;
using Kite.SQS;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class SqsQueueTests
{
    [Fact]
    public void SqsQueue_EnqueueDequeue_WorksCorrectly()
    {
        var queue = new SqsQueue { Name = "test", QueueUrl = "http://localhost:5000/000/test" };
        var msg = new SqsMessage { Body = "hello world" };
        queue.Messages.Enqueue(msg);

        queue.Messages.TryDequeue(out var dequeued).Should().BeTrue();
        dequeued.Should().NotBeNull();
        dequeued!.Body.Should().Be("hello world");
    }

    [Fact]
    public void SqsMessage_HasDefaultValues()
    {
        var msg = new SqsMessage();
        msg.MessageId.Should().NotBeEmpty();
        msg.Body.Should().BeEmpty();
        msg.Attributes.Should().NotBeNull();
    }

    [Fact]
    public void SqsQueue_EmptyQueue_DequeueReturnsFalse()
    {
        var queue = new SqsQueue { Name = "empty" };
        queue.Messages.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void SqsQueue_DefaultValues_AreCorrect()
    {
        var queue = new SqsQueue();
        queue.Name.Should().BeEmpty();
        queue.QueueUrl.Should().BeEmpty();
        queue.Messages.Should().BeEmpty();
        queue.VisibilityTimeouts.Should().NotBeNull().And.BeEmpty();
        queue.SentMessages.Should().NotBeNull().And.BeEmpty();
        queue.RequestLog.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void SqsQueue_NotifyChange_FiresEvent()
    {
        var queue = new SqsQueue { Name = "test" };
        var fired = false;
        queue.OnChange += () => fired = true;

        queue.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public void SqsQueue_MultipleEnqueueDequeue_FifoOrder()
    {
        var queue = new SqsQueue { Name = "test" };
        queue.Messages.Enqueue(new SqsMessage { Body = "first" });
        queue.Messages.Enqueue(new SqsMessage { Body = "second" });
        queue.Messages.Enqueue(new SqsMessage { Body = "third" });

        queue.Messages.TryDequeue(out var msg1).Should().BeTrue();
        msg1!.Body.Should().Be("first");
        queue.Messages.TryDequeue(out var msg2).Should().BeTrue();
        msg2!.Body.Should().Be("second");
        queue.Messages.TryDequeue(out var msg3).Should().BeTrue();
        msg3!.Body.Should().Be("third");
        queue.Messages.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void SqsMessage_ReceiptHandle_DefaultEmpty()
    {
        var msg = new SqsMessage();
        msg.ReceiptHandle.Should().BeEmpty();
    }

    [Fact]
    public void SqsMessage_UniqueMessageIds()
    {
        var msg1 = new SqsMessage();
        var msg2 = new SqsMessage();

        msg1.MessageId.Should().NotBe(msg2.MessageId);
    }
}
