using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;

// Example – SNS Message Publisher (Aspire variant)
//
// This project publishes a series of notification messages to the "notifications" SNS topic
// managed by the local Kite running inside the Aspire App Host (SNS.AppHost).
//
// Make sure SNS.AppHost is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string accountId = "000000000000";
const string topicName = "notifications";
const string topicArn = $"arn:aws:sns:{region}:{accountId}:{topicName}";

var credentials = new BasicAWSCredentials("test", "test");
using var snsClient = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

Console.WriteLine($"Publishing messages to topic '{topicName}'...");
Console.WriteLine();

for (int i = 1; i <= 5; i++)
{
    var notification = new
    {
        NotificationId = i,
        Subject = $"Notification #{i}",
        Body = $"This is notification number {i}, sent at {DateTime.UtcNow:O}"
    };

    var response = await snsClient.PublishAsync(new PublishRequest
    {
        TopicArn = topicArn,
        Subject = $"Notification #{i}",
        Message = JsonSerializer.Serialize(notification)
    });

    Console.WriteLine($"Published notification {i:D2}: MessageId = {response.MessageId}");
}

Console.WriteLine();
Console.WriteLine("All 5 messages published. Check the Aspire dashboard logs to see them being processed.");
