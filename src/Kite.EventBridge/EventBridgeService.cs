using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using Kite.Core;
using Kite.Core.Models;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.EventBridge;

public class EventBridgeService
{
    private readonly IServiceRegistry _registry;
    private readonly LambdaExecutor _executor;
    private readonly ILogger<EventBridgeService> _logger;

    public EventBridgeService(IServiceRegistry registry, LambdaExecutor executor, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _executor = executor;
        _logger = loggerFactory.CreateLogger<EventBridgeService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var operation = target.Split('.').Last();

        _logger.LogDebug("[DEBUG] EventBridge HandleAsync - Operation: {Operation}, Method: {Method}", operation, context.Request.Method);

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = body.Length > 0 ? JsonDocument.Parse(body) : JsonDocument.Parse("{}");
        var root = doc.RootElement;

        _logger.LogDebug("[DEBUG] EventBridge operation: {Operation}, RequestBodyLength: {BodyLength}", operation, body.Length);

        context.Response.ContentType = "application/x-amz-json-1.1";

        switch (operation)
        {
            case "PutEvents":
                await HandlePutEvents(context, root);
                break;
            case "CreateEventBus":
                await HandleCreateEventBus(context, root);
                break;
            case "DeleteEventBus":
                await HandleDeleteEventBus(context, root);
                break;
            case "ListEventBuses":
                await HandleListEventBuses(context, root);
                break;
            case "PutRule":
                await HandlePutRule(context, root);
                break;
            case "PutTargets":
                await HandlePutTargets(context, root);
                break;
            case "DeleteRule":
                await HandleDeleteRule(context, root);
                break;
            case "ListRules":
                await HandleListRules(context, root);
                break;
            case "ListTargetsByRule":
                await HandleListTargetsByRule(context, root);
                break;
            case "RemoveTargets":
                await HandleRemoveTargets(context, root);
                break;
            default:
                _logger.LogWarning("Unknown EventBridge operation: {Operation}", operation);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { __type = "UnknownOperationException", message = $"Unknown operation: {operation}" }));
                break;
        }
    }

    private async Task HandlePutEvents(HttpContext context, JsonElement root)
    {
        _logger.LogDebug("[ENTRY] HandlePutEvents");
        
        using var activity = KiteActivitySource.EventBridge.StartActivity("eventbridge.put_events");

        var entries = root.GetProperty("Entries");
        int failedCount = 0;
        var resultEntries = new List<object>();

        foreach (var entry in entries.EnumerateArray())
        {
            var busName = entry.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";
            var source = entry.TryGetProperty("Source", out var src) ? src.GetString() ?? string.Empty : string.Empty;
            var detailType = entry.TryGetProperty("DetailType", out var dt) ? dt.GetString() ?? string.Empty : string.Empty;
            var detail = entry.TryGetProperty("Detail", out var d) ? d.GetString() ?? "{}" : "{}";

            var eventBus = _registry.GetEventBridgeBus(busName);
            if (eventBus is null)
            {
                _logger.LogError("[ERROR] PutEvents failed - EventBus not found: {BusName}", busName);
                resultEntries.Add(new { ErrorCode = "InvalidEventBus", ErrorMessage = $"Event bus {busName} not found" });
                failedCount++;
                continue;
            }

            var eventId = Guid.NewGuid().ToString();
            LogHelpers.AddSentMessage(eventBus.SentMessages, eventId, detail, source);
            LogHelpers.AddRequestLog(eventBus.RequestLog, "PutEvents", $"Source={source} DetailType={detailType}");
            eventBus.NotifyChange();

            var matchedRules = MatchRules(eventBus.Rules, source, detailType, detail);
            foreach (var rule in matchedRules)
            {
                foreach (var target in rule.Targets)
                {
                    _ = InvokeEventBridgeTargetAsync(target.LambdaFunctionName, source, detailType, detail, busName);
                }
            }

            resultEntries.Add(new { EventId = eventId });
        }

        activity?.SetTag("events.failed", failedCount);
        _logger.LogInformation("PutEvents completed - EventsProcessed: {EventCount}, FailedCount: {FailedCount}", entries.GetArrayLength(), failedCount);
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { FailedEntryCount = failedCount, Entries = resultEntries }));
    }

    /// <summary>
    /// Publishes a single event directly into the EventBridge pipeline (bypassing HTTP).
    /// Matches rules, invokes Lambda targets, and updates the in-memory request/sent-message logs.
    /// </summary>
    public async Task<string> PutEventAsync(string busName, string source, string detailType, string detail)
    {
        using var activity = KiteActivitySource.EventBridge.StartActivity("eventbridge.put_events");

        var eventBus = _registry.GetEventBridgeBus(busName);
        if (eventBus is null)
            throw new InvalidOperationException($"Event bus '{busName}' not found.");

        var eventId = Guid.NewGuid().ToString();
        LogHelpers.AddSentMessage(eventBus.SentMessages, eventId, detail, source);
        LogHelpers.AddRequestLog(eventBus.RequestLog, "PutEvents", $"Source={source} DetailType={detailType}");
        eventBus.NotifyChange();

        var matchedRules = MatchRules(eventBus.Rules, source, detailType, detail);
        foreach (var rule in matchedRules)
        {
            foreach (var target in rule.Targets)
            {
                _ = InvokeEventBridgeTargetAsync(target.LambdaFunctionName, source, detailType, detail, busName);
            }
        }

        activity?.SetTag("events.failed", 0);
        return eventId;
    }

    private async Task HandleCreateEventBus(HttpContext context, JsonElement root)
    {
        var name = root.GetProperty("Name").GetString() ?? string.Empty;
        _logger.LogDebug("[ENTRY] HandleCreateEventBus - Name: {BusName}", name);
        
        if (_registry.GetEventBridgeBus(name) is null)
            _registry.RegisterEventBridgeBus(new EventBridgeBus { Name = name });

        _logger.LogInformation("CreateEventBus completed - BusName: {BusName}", name);

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            EventBusArn = $"arn:aws:events:{_registry.Region}:{_registry.AccountId}:event-bus/{name}"
        }));
    }

    private async Task HandleDeleteEventBus(HttpContext context, JsonElement root)
    {
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleListEventBuses(HttpContext context, JsonElement root)
    {
        var buses = _registry.GetAllEventBridgeBuses().Select(b => new
        {
            Name = b.Name,
            Arn = $"arn:aws:events:{_registry.Region}:{_registry.AccountId}:event-bus/{b.Name}"
        }).ToList();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { EventBuses = buses }));
    }

    private async Task HandlePutRule(HttpContext context, JsonElement root)
    {
        var ruleName = root.GetProperty("Name").GetString() ?? string.Empty;
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";

        EventPattern pattern = new();
        if (root.TryGetProperty("EventPattern", out var patternJson))
        {
            var patternStr = patternJson.GetString() ?? "{}";
            pattern = ParseEventPattern(patternStr);
        }

        var eventBus = _registry.GetEventBridgeBus(busName);
        if (eventBus is null)
        {
            eventBus = new EventBridgeBus { Name = busName };
            _registry.RegisterEventBridgeBus(eventBus);
        }

        var existing = eventBus.Rules.FirstOrDefault(r => r.Name == ruleName);
        if (existing is not null)
        {
            existing.EventPattern = pattern;
        }
        else
        {
            eventBus.Rules.Add(new EventBridgeRule { Name = ruleName, BusName = busName, EventPattern = pattern });
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            RuleArn = $"arn:aws:events:{_registry.Region}:{_registry.AccountId}:rule/{ruleName}"
        }));
    }

    private async Task HandlePutTargets(HttpContext context, JsonElement root)
    {
        var ruleName = root.GetProperty("Rule").GetString() ?? string.Empty;
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";

        var eventBus = _registry.GetEventBridgeBus(busName);
        var rule = eventBus?.Rules.FirstOrDefault(r => r.Name == ruleName);
        if (rule is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { __type = "ResourceNotFoundException", message = $"Rule {ruleName} not found" }));
            return;
        }

        var targets = root.GetProperty("Targets");
        foreach (var t in targets.EnumerateArray())
        {
            var id = t.GetProperty("Id").GetString() ?? string.Empty;
            var arn = t.GetProperty("Arn").GetString() ?? string.Empty;
            // Extract function name from ARN
            var functionName = arn.Split(':').Last();

            var existing = rule.Targets.FirstOrDefault(x => x.Id == id);
            if (existing is not null)
                existing.LambdaFunctionName = functionName;
            else
                rule.Targets.Add(new EventBridgeTarget { Id = id, LambdaFunctionName = functionName });
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { FailedEntryCount = 0, FailedEntries = Array.Empty<object>() }));
    }

    private async Task HandleDeleteRule(HttpContext context, JsonElement root)
    {
        var ruleName = root.GetProperty("Name").GetString() ?? string.Empty;
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";

        var eventBus = _registry.GetEventBridgeBus(busName);
        if (eventBus is not null)
        {
            var rule = eventBus.Rules.FirstOrDefault(r => r.Name == ruleName);
            if (rule is not null) eventBus.Rules.Remove(rule);
        }

        await context.Response.WriteAsync("{}");
    }

    private async Task HandleListRules(HttpContext context, JsonElement root)
    {
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";
        var eventBus = _registry.GetEventBridgeBus(busName);
        var rules = eventBus?.Rules.Select(r => new
        {
            Name = r.Name,
            EventBusName = r.BusName,
            Arn = $"arn:aws:events:{_registry.Region}:{_registry.AccountId}:rule/{r.Name}",
            State = "ENABLED"
        }).ToList() ?? new();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { Rules = rules }));
    }

    private async Task HandleListTargetsByRule(HttpContext context, JsonElement root)
    {
        var ruleName = root.GetProperty("Rule").GetString() ?? string.Empty;
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";

        var eventBus = _registry.GetEventBridgeBus(busName);
        var rule = eventBus?.Rules.FirstOrDefault(r => r.Name == ruleName);
        var targets = rule?.Targets.Select(t => new
        {
            Id = t.Id,
            Arn = $"arn:aws:lambda:{_registry.Region}:{_registry.AccountId}:function:{t.LambdaFunctionName}"
        }).ToList() ?? new();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { Targets = targets }));
    }

    private async Task HandleRemoveTargets(HttpContext context, JsonElement root)
    {
        var ruleName = root.GetProperty("Rule").GetString() ?? string.Empty;
        var busName = root.TryGetProperty("EventBusName", out var bus) ? bus.GetString() ?? "default" : "default";
        var ids = root.GetProperty("Ids").EnumerateArray().Select(i => i.GetString()).ToHashSet();

        var eventBus = _registry.GetEventBridgeBus(busName);
        var rule = eventBus?.Rules.FirstOrDefault(r => r.Name == ruleName);
        if (rule is not null)
            rule.Targets.RemoveAll(t => ids.Contains(t.Id));

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { FailedEntryCount = 0, FailedEntries = Array.Empty<object>() }));
    }

    private List<EventBridgeRule> MatchRules(List<EventBridgeRule> rules, string source, string detailType, string detail)
    {
        var matched = new List<EventBridgeRule>();
        JsonDocument? detailDoc = null;

        try { detailDoc = JsonDocument.Parse(detail); } catch { }

        foreach (var rule in rules)
        {
            var pattern = rule.EventPattern;

            if (pattern.Source is { Length: > 0 } && !pattern.Source.Contains(source))
                continue;

            if (pattern.DetailType is { Length: > 0 } && !pattern.DetailType.Contains(detailType))
                continue;

            if (pattern.Detail is { Count: > 0 } && detailDoc is not null)
            {
                var detailMatches = true;
                foreach (var (field, expected) in pattern.Detail)
                {
                    if (!detailDoc.RootElement.TryGetProperty(field, out var actual))
                    {
                        detailMatches = false;
                        break;
                    }

                    var expectedJson = JsonSerializer.Serialize(expected);
                    var actualJson = actual.GetRawText();
                    if (expectedJson != actualJson && !MatchesArray(expected, actual))
                    {
                        detailMatches = false;
                        break;
                    }
                }
                if (!detailMatches) continue;
            }

            matched.Add(rule);
        }

        return matched;
    }

    private static bool MatchesArray(object expected, JsonElement actual)
    {
        if (expected is not System.Text.Json.JsonElement[] and not object[])
            return false;

        var actualStr = actual.ToString();
        var expectedStr = JsonSerializer.Serialize(expected);

        // Check if actual value is in the expected array
        try
        {
            var expectedArray = JsonDocument.Parse(expectedStr);
            if (expectedArray.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in expectedArray.RootElement.EnumerateArray())
                {
                    if (item.GetRawText() == actual.GetRawText())
                        return true;
                }
            }
        }
        catch { }

        return false;
    }

    private async Task InvokeEventBridgeTargetAsync(string functionName, string source, string detailType, string detail, string busName)
    {
        try
        {
            using var activity = KiteActivitySource.EventBridge.StartActivity("eventbridge.dispatch");
            activity?.SetTag("function.name", functionName);
            activity?.SetTag("trigger.source", source);

            var cloudWatchEvent = new
            {
                Version = "0",
                Id = Guid.NewGuid().ToString(),
                Source = source,
                Account = _registry.AccountId,
                Time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Region = _registry.Region,
                DetailType = detailType,
                Detail = JsonDocument.Parse(detail).RootElement,
                Resources = Array.Empty<string>()
            };

            var json = JsonSerializer.Serialize(cloudWatchEvent);

            await _executor.InvokeAsync(functionName, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EventBridge: failed to invoke {Function}", functionName);
        }
    }

    private static EventPattern ParseEventPattern(string patternJson)
    {
        var pattern = new EventPattern();
        try
        {
            var doc = JsonDocument.Parse(patternJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Array)
                pattern.Source = src.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            if (root.TryGetProperty("detail-type", out var dt) && dt.ValueKind == JsonValueKind.Array)
                pattern.DetailType = dt.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                pattern.Detail = new Dictionary<string, object>();
                foreach (var prop in detail.EnumerateObject())
                    pattern.Detail[prop.Name] = prop.Value.Clone();
            }
        }
        catch { }

        return pattern;
    }
}
