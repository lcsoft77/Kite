using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace S3.Lambda;

/// <summary>
/// Lambda function that processes files uploaded to an S3 bucket.
/// Each S3 event record contains information about the uploaded object.
/// </summary>
public class FileProcessorFunction
{
    public void Handle(S3Event s3Event)
    {
        Console.WriteLine($"Received batch of {s3Event.Records.Count} S3 event(s).");

        foreach (var record in s3Event.Records)
        {
            Console.WriteLine($"Event: {record.EventName}, Bucket: {record.S3.Bucket.Name}, Key: {record.S3.Object.Key}, Size: {record.S3.Object.Size}");
        }
    }
}
