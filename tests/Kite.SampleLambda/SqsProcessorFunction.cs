using Amazon.Lambda.SQSEvents;

namespace Kite.SampleLambda;

/// <summary>
/// Lambda handler that processes SQS events used for integration tests.
/// Returns the count of records processed.
/// </summary>
public class SqsProcessorFunction
{
    public int Handle(SQSEvent sqsEvent) => sqsEvent.Records.Count;
}
