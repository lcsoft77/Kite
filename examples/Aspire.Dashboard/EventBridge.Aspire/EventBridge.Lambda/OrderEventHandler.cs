using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EventBridge.Lambda;

/// <summary>
/// Lambda function that processes order events received from an EventBridge bus.
/// The function is invoked when a rule routes a matching event to it.
/// </summary>
public class OrderEventHandler
{
    public void Handle(CloudWatchEvent<JsonElement> eventBridgeEvent, ILambdaContext context)    
    {
        context.Logger.LogLine($"Received EventBridge event:");
        context.Logger.LogLine($"  Source:      {eventBridgeEvent.Source}");
        context.Logger.LogLine($"  Detail-Type: {eventBridgeEvent.DetailType}");
        context.Logger.LogLine($"  Detail:      {eventBridgeEvent.Detail}");
    }
}
