namespace Kite.Core.Models;

public class SnsTopic
{
    public string Name { get; set; } = string.Empty;
    public string TopicArn { get; set; } = string.Empty;
    public List<SnsSubscription> Subscriptions { get; } = new();
    public List<SentMessageEntry> PublishedMessages { get; } = new();
    public List<RequestLogEntry> RequestLog { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

public class SnsSubscription
{
    public string SubscriptionArn { get; set; } = string.Empty;
    public string TopicArn { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
}
