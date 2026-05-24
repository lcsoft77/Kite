using Kite.Core;
using Kite.Host;

// Example – Kite Host (SNS)
//
// This project starts the Kite with:
//   • An SNS topic named "notifications"
//
// Run this project first, then run SNS.Publisher to publish messages to the topic.

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddSnsTopic("notifications")
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("SNS topic ARN: arn:aws:sns:eu-west-1:000000000000:notifications");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
