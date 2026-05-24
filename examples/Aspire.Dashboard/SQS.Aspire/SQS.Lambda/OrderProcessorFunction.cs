using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SQS.Lambda;

/// <summary>
/// Lambda function that processes orders received from an SQS queue.
/// Each SQS message body is expected to contain an order payload (JSON or plain text).
/// </summary>
public class OrderProcessorFunction
{
    public void Handle(SQSEvent sqsEvent, ILambdaContext context)
    {
        context.Logger.LogLine($"Received batch of {sqsEvent.Records.Count} order(s).");

        foreach (var record in sqsEvent.Records)
        {
            context.Logger.LogLine($"Processing order: {record.Body}");
        }
    }
}
