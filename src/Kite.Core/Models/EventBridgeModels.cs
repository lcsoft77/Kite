namespace Kite.Core.Models;

public class EventBridgeBus
{
    public string Name { get; set; } = string.Empty;
    public List<EventBridgeRule> Rules { get; set; } = new();
    public List<RequestLogEntry> RequestLog { get; } = new();
    public List<SentMessageEntry> SentMessages { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

public class EventBridgeRule
{
    public string Name { get; set; } = string.Empty;
    public string BusName { get; set; } = "default";
    public EventPattern EventPattern { get; set; } = new();
    public List<EventBridgeTarget> Targets { get; set; } = new();
}

public class EventPattern
{
    public string[]? Source { get; set; }
    public string[]? DetailType { get; set; }
    public Dictionary<string, object>? Detail { get; set; }
}

public class EventBridgeTarget
{
    public string Id { get; set; } = string.Empty;
    public string LambdaFunctionName { get; set; } = string.Empty;
}
