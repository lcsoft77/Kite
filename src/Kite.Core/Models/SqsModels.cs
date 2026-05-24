using System.Collections.Concurrent;

namespace Kite.Core.Models;

public class SqsQueue
{
    public string Name { get; set; } = string.Empty;
    public string QueueUrl { get; set; } = string.Empty;
    public ConcurrentQueue<SqsMessage> Messages { get; } = new();
    public Dictionary<string, (DateTime VisibleAt, SqsMessage Msg)> VisibilityTimeouts { get; } = new();
    public List<RequestLogEntry> RequestLog { get; } = new();
    public List<SentMessageEntry> SentMessages { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

public class SqsMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public string Body { get; set; } = string.Empty;
    public string ReceiptHandle { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
}
