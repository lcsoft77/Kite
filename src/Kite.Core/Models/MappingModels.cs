namespace Kite.Core.Models;

public enum EventSourceMappingType
{
    SQS,
    S3,
    EventBridge
}

public class EventSourceMapping
{
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public EventSourceMappingType Type { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 10;
    public bool Enabled { get; set; } = true;
}

public class ApiGatewayRoute
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
}
