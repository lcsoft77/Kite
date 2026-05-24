using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

// Example 3 – SQS Message Sender
//
// This project sends a series of order messages to the "orders-queue" SQS queue
// that is managed by the local Kite (started by SQS.Emulator).
//
// Make sure SQS.Emulator is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string accountId = "000000000000";
const string queueName = "orders-queue";

var queueUrl = $"{emulatorUrl}/{accountId}/{queueName}";

var credentials = new BasicAWSCredentials("test", "test");
using var sqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

Console.WriteLine($"Sending messages to: {queueUrl}");
Console.WriteLine();

for (int i = 1; i <= 10; i++)
{
    var order = new
    {
        OrderId = i,
        Product = $"Product-{i}",
        Quantity = i * 2,
        Timestamp = DateTime.UtcNow
    };

    var body = JsonSerializer.Serialize(order);

    var response = await sqsClient.SendMessageAsync(new SendMessageRequest
    {
        QueueUrl = queueUrl,
        MessageBody = body
    });

    Console.WriteLine($"Sent order {i:D2}: MessageId = {response.MessageId}");
}

Console.WriteLine();
Console.WriteLine("All 10 messages sent. Check SQS.Emulator logs to see them being processed.");
