using System.Collections.Concurrent;

namespace Kite.Core.Models;

public class S3Bucket
{
    public string Name { get; set; } = string.Empty;
    public ConcurrentDictionary<string, S3Object> Objects { get; } = new();
    public S3NotificationConfiguration? NotificationConfiguration { get; set; }
    public List<RequestLogEntry> RequestLog { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

public class S3Object
{
    public string Key { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public string ETag { get; set; } = string.Empty;
}

public class S3NotificationConfiguration
{
    public List<S3LambdaNotification> LambdaFunctionConfigurations { get; set; } = new();
}

public class S3LambdaNotification
{
    public string LambdaFunctionArn { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();
}
