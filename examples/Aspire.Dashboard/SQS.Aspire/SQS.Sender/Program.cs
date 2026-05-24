using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

// Example – SQS Message Sender (Aspire + Dashboard variant)
//
// This project sends a series of order messages to the "orders-queue" SQS queue
// managed by the local Kite running inside the Aspire App Host (SQS.AppHost).
//
// Make sure SQS.AppHost is running before executing this project.
// The Kite dashboard is available at http://localhost:5051 (emulator port + 1).

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
Console.WriteLine("All 10 messages sent. Check the Aspire and Kite dashboard logs to see them being processed.");
