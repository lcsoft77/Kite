# Amazon EventBridge

Kite provides Amazon EventBridge emulation supporting event buses, rules, targets, and event delivery to Lambda functions.

## Overview

The EventBridge service uses the JSON protocol via the `X-Amz-Target` header with target prefix `AmazonEventBridge`.

**Routing:**
- Header: `X-Amz-Target: AmazonEventBridge.*`
- Content-Type: `application/x-amz-json-1.1`
- SigV4 service name: `events`

## Supported Operations

| Operation | X-Amz-Target | Description |
|-----------|-------------|-------------|
| `PutEvents` | `AmazonEventBridge.PutEvents` | Send events to an event bus |
| `CreateEventBus` | `AmazonEventBridge.CreateEventBus` | Create a new event bus |
| `DeleteEventBus` | `AmazonEventBridge.DeleteEventBus` | Delete an event bus |
| `ListEventBuses` | `AmazonEventBridge.ListEventBuses` | List all event buses |
| `PutRule` | `AmazonEventBridge.PutRule` | Create or update a rule |
| `DeleteRule` | `AmazonEventBridge.DeleteRule` | Delete a rule |
| `ListRules` | `AmazonEventBridge.ListRules` | List rules on an event bus |
| `PutTargets` | `AmazonEventBridge.PutTargets` | Add targets to a rule |
| `RemoveTargets` | `AmazonEventBridge.RemoveTargets` | Remove targets from a rule |
| `ListTargetsByRule` | `AmazonEventBridge.ListTargetsByRule` | List targets for a rule |

## Configuration

### Creating an Event Bus via Builder

```csharp
builder.AddEventBridgeBus("my-bus");
```

### Creating a Rule via Builder

```csharp
builder.AddEventBridgeRule("order-rule", rule =>
{
    rule.EventBusName = "my-bus";
    rule.EventPattern = "{\"source\": [\"order.service\"]}";
});
```

## AWS SDK Usage

### Creating an Event Bus

```csharp
var ebClient = new AmazonEventBridgeClient(
    new BasicAWSCredentials("test", "test"),
    new AmazonEventBridgeConfig { ServiceURL = "http://localhost:4566" });

await ebClient.CreateEventBusAsync(new CreateEventBusRequest
{
    Name = "my-bus"
});
```

### Creating a Rule

```csharp
await ebClient.PutRuleAsync(new PutRuleRequest
{
    Name = "order-created-rule",
    EventBusName = "my-bus",
    EventPattern = "{\"source\": [\"order.service\"], \"detail-type\": [\"OrderCreated\"]}"
});
```

### Adding Targets

```csharp
await ebClient.PutTargetsAsync(new PutTargetsRequest
{
    Rule = "order-created-rule",
    EventBusName = "my-bus",
    Targets = new List<Target>
    {
        new Target
        {
            Id = "lambda-target",
            Arn = "arn:aws:lambda:us-east-1:000000000000:function:OrderProcessor"
        }
    }
});
```

### Publishing Events

```csharp
await ebClient.PutEventsAsync(new PutEventsRequest
{
    Entries = new List<PutEventsRequestEntry>
    {
        new PutEventsRequestEntry
        {
            Source = "order.service",
            DetailType = "OrderCreated",
            Detail = "{\"orderId\": \"123\", \"amount\": 99.99}",
            EventBusName = "my-bus"
        }
    }
});
```

When an event matches a rule's event pattern, it is delivered to the configured targets (Lambda functions).

### Listing Rules

```csharp
var response = await ebClient.ListRulesAsync(new ListRulesRequest
{
    EventBusName = "my-bus"
});

foreach (var rule in response.Rules)
{
    Console.WriteLine($"Rule: {rule.Name}, Pattern: {rule.EventPattern}");
}
```

## Event Routing

When events are published via `PutEvents`:

1. The event bus receives the events
2. Each rule on the bus evaluates its event pattern against the event
3. Matching rules forward the event to their configured targets
4. Lambda function targets are invoked with the event payload

## Storage

All event buses, rules, and targets are stored in memory and do not persist across emulator restarts.
