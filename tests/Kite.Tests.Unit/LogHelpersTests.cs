using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class LogHelpersTests
{
    [Fact]
    public void AddRequestLog_AddsEntry()
    {
        var log = new List<RequestLogEntry>();

        LogHelpers.AddRequestLog(log, "PutItem", "table=T", 200, "test");

        log.Should().HaveCount(1);
        log[0].Operation.Should().Be("PutItem");
        log[0].Details.Should().Be("table=T");
        log[0].StatusCode.Should().Be(200);
        log[0].Source.Should().Be("test");
    }

    [Fact]
    public void AddRequestLog_CapsAt200Entries()
    {
        var log = new List<RequestLogEntry>();
        for (var i = 0; i < 210; i++)
            LogHelpers.AddRequestLog(log, $"Op{i}", "details", 200);

        log.Should().HaveCount(200);
        // Oldest entries should have been removed — first entry should be Op10
        log[0].Operation.Should().Be("Op10");
    }

    [Fact]
    public void AddSentMessage_AddsEntry()
    {
        var messages = new List<SentMessageEntry>();

        LogHelpers.AddSentMessage(messages, "msg-1", "{\"body\":true}", "test");

        messages.Should().HaveCount(1);
        messages[0].MessageId.Should().Be("msg-1");
        messages[0].Body.Should().Be("{\"body\":true}");
        messages[0].Source.Should().Be("test");
    }

    [Fact]
    public void AddSentMessage_CapsAt100Entries()
    {
        var messages = new List<SentMessageEntry>();
        for (var i = 0; i < 110; i++)
            LogHelpers.AddSentMessage(messages, $"msg-{i}", "body");

        messages.Should().HaveCount(100);
        // Oldest entries should have been removed — first entry should be msg-10
        messages[0].MessageId.Should().Be("msg-10");
    }

    [Fact]
    public void RequestLogEntry_DefaultValues_AreCorrect()
    {
        var entry = new RequestLogEntry();
        entry.Operation.Should().BeEmpty();
        entry.Details.Should().BeEmpty();
        entry.StatusCode.Should().Be(200);
        entry.Source.Should().BeEmpty();
    }

    [Fact]
    public void SentMessageEntry_DefaultValues_AreCorrect()
    {
        var entry = new SentMessageEntry();
        entry.MessageId.Should().BeEmpty();
        entry.Body.Should().BeEmpty();
        entry.Source.Should().BeEmpty();
    }
}
