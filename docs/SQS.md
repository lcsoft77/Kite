# Amazon SQS

Kite provides a full Amazon Simple Queue Service (SQS) emulation supporting both the JSON and Query wire protocols.

## Overview

The SQS service supports creating queues, sending and receiving messages, message visibility timeouts, and event source mappings to trigger Lambda functions.

**Routing:**
- JSON protocol: `X-Amz-Target: AmazonSQS.*` header
- Query protocol: `Action` parameter in form-encoded body
- SigV4 service name: `sqs`

## Supported Operations

| Operation | Description |
|-----------|-------------|
| `CreateQueue` | Create a new SQS queue |
| `DeleteQueue` | Delete an existing queue |
| `GetQueueUrl` | Get the URL of a queue by name |
| `ListQueues` | List all queues |
| `SendMessage` | Send a message to a queue |
| `ReceiveMessage` | Receive messages from a queue |
| `DeleteMessage` | Delete a message from a queue |
| `GetQueueAttributes` | Get attributes of a queue |

## Configuration

### Creating a Queue via Builder

```csharp
builder.AddSQSQueue("my-queue");
```

### Creating a Queue via AWS SDK

```csharp
var sqsClient = new AmazonSQSClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonSQSConfig { ServiceURL = "http://localhost:4566" });

var response = await sqsClient.CreateQueueAsync("my-queue");
Console.WriteLine($"Queue URL: {response.QueueUrl}");
```

## Message Operations

### Sending a Message

```csharp
await sqsClient.SendMessageAsync(new SendMessageRequest
{
    QueueUrl = "http://localhost:4566/000000000000/my-queue",
    MessageBody = "{\"orderId\": 123}"
});
```

### Receiving Messages

```csharp
var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
{
    QueueUrl = "http://localhost:4566/000000000000/my-queue",
    MaxNumberOfMessages = 10,
    WaitTimeSeconds = 5
});

foreach (var message in response.Messages)
{
    Console.WriteLine($"Message: {message.Body}");

    // Delete after processing
    await sqsClient.DeleteMessageAsync(
        "http://localhost:4566/000000000000/my-queue",
        message.ReceiptHandle);
}
```

## Queue URL Format

Queue URLs follow the pattern:

```
http://localhost:4566/000000000000/{queue-name}
```

Where `000000000000` is the fixed AWS account ID used by the emulator.

## Message Visibility

The emulator supports message visibility timeouts. When a message is received, it becomes invisible to other consumers for the visibility timeout period. If not deleted within this period, the message becomes visible again.

The `SqsVisibilityTimeoutChecker` background service manages visibility timeout expiration.

## SQS → Lambda Trigger

Connect a queue to a Lambda function:

```csharp
builder
    .AddSQSQueue("order-queue")
    .AddLambda("OrderProcessor", lambda => { /* ... */ })
    .AddSQSTrigger("order-queue", "OrderProcessor", batchSize: 10);
```

The `SqsEsmPoller` background service polls the queue and invokes the Lambda function with batched messages in the standard SQS event format.

## Wire Protocols

### JSON Protocol

The SQS service supports the modern JSON protocol used by recent AWS SDKs:

```
POST / HTTP/1.1
Content-Type: application/x-amz-json-1.0
X-Amz-Target: AmazonSQS.SendMessage

{"QueueUrl": "...", "MessageBody": "..."}
```

### Query Protocol

The SQS service also supports the legacy query protocol:

```
POST / HTTP/1.1
Content-Type: application/x-www-form-urlencoded

Action=SendMessage&QueueUrl=...&MessageBody=...
```
