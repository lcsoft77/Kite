namespace Kite.Core.Models;

public class RequestLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Operation { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public string Source { get; set; } = string.Empty;
}

public class SentMessageEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string MessageId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
