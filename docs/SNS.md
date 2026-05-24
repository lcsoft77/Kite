# Amazon SNS

Kite provides Amazon Simple Notification Service (SNS) emulation supporting topics, subscriptions, and message publishing.

## Overview

The SNS service uses the Query protocol (form-encoded), similar to the legacy AWS API style.

**Routing:**
- Query protocol: `Action` parameter in form-encoded body
- SigV4 service name: `sns`
- Optional routing hint (not required): `X-Amz-Target: AmazonSimpleNotificationService.*`

## Supported Operations

| Operation | Description |
|-----------|-------------|
| `CreateTopic` | Create a new SNS topic |
| `DeleteTopic` | Delete a topic |
| `ListTopics` | List all topics |
| `GetTopicAttributes` | Get topic attributes |
| `SetTopicAttributes` | Set topic attributes |
| `Subscribe` | Subscribe an endpoint to a topic |
| `Unsubscribe` | Remove a subscription |
| `ListSubscriptions` | List all subscriptions |
| `ListSubscriptionsByTopic` | List subscriptions for a specific topic |
| `Publish` | Publish a message to a topic |

## AWS SDK Usage

### Creating a Topic

```csharp
var snsClient = new AmazonSimpleNotificationServiceClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonSimpleNotificationServiceConfig { ServiceURL = "http://localhost:4566" });

var response = await snsClient.CreateTopicAsync("order-notifications");
Console.WriteLine($"Topic ARN: {response.TopicArn}");
```

### Subscribing to a Topic

```csharp
await snsClient.SubscribeAsync(new SubscribeRequest
{
    TopicArn = "arn:aws:sns:us-east-1:000000000000:order-notifications",
    Protocol = "sqs",
    Endpoint = "arn:aws:sqs:us-east-1:000000000000:notification-queue"
});
```

### Publishing a Message

```csharp
await snsClient.PublishAsync(new PublishRequest
{
    TopicArn = "arn:aws:sns:us-east-1:000000000000:order-notifications",
    Message = "{\"orderId\": 123, \"status\": \"shipped\"}",
    Subject = "Order Update"
});
```

### Listing Topics

```csharp
var response = await snsClient.ListTopicsAsync();
foreach (var topic in response.Topics)
{
    Console.WriteLine($"Topic: {topic.TopicArn}");
}
```

### Listing Subscriptions

```csharp
var response = await snsClient.ListSubscriptionsByTopicAsync(
    "arn:aws:sns:us-east-1:000000000000:order-notifications");

foreach (var sub in response.Subscriptions)
{
    Console.WriteLine($"Subscription: {sub.SubscriptionArn} ({sub.Protocol})");
}
```

## Topic ARN Format

```
arn:aws:sns:{region}:000000000000:{topic-name}
```

## Storage

All SNS topics and subscriptions are stored in memory and do not persist across emulator restarts.
