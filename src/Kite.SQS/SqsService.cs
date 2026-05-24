using System.Text;
using System.Text.Json;
using System.Web;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.SQS;

public class SqsService
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<SqsService> _logger;

    public SqsService(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<SqsService>();
    }
    public async Task HandleAsync(HttpContext context)
    {
        var contentType = context.Request.ContentType ?? string.Empty;
        bool isJson = contentType.Contains("application/x-amz-json");

        _logger.LogDebug("[DEBUG] SQS HandleAsync - Protocol: {Protocol}, Method: {Method}", isJson ? "JSON" : "Query", context.Request.Method);

        if (isJson)
            await HandleJsonProtocolAsync(context);
        else
            await HandleQueryProtocolAsync(context);
    }

    private async Task HandleJsonProtocolAsync(HttpContext context)
    {
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var operation = target.Split('.').Last();

        _logger.LogDebug("[DEBUG] SQS JSON Protocol - Operation: {Operation}", operation);

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = body.Length > 0 ? JsonDocument.Parse(body) : JsonDocument.Parse("{}");
        var root = doc.RootElement;

        switch (operation)
        {
            case "CreateQueue":
                await HandleCreateQueueJson(context, root);
                break;
            case "GetQueueUrl":
                await HandleGetQueueUrlJson(context, root);
                break;
            case "SendMessage":
                await HandleSendMessageJson(context, root);
                break;
            case "ReceiveMessage":
                await HandleReceiveMessageJson(context, root);
                break;
            case "DeleteMessage":
                await HandleDeleteMessageJson(context, root);
                break;
            case "ListQueues":
                await HandleListQueuesJson(context, root);
                break;
            case "DeleteQueue":
                await HandleDeleteQueueJson(context, root);
                break;
            case "GetQueueAttributes":
                await HandleGetQueueAttributesJson(context, root);
                break;
            default:
                _logger.LogWarning("Unknown SQS JSON operation: {Operation}", operation);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync($"{{\"Error\":\"Unknown operation: {operation}\"}}");
                break;
        }
    }

    private async Task HandleQueryProtocolAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var form = HttpUtility.ParseQueryString(body);
        var action = form["Action"] ?? string.Empty;

        switch (action)
        {
            case "CreateQueue":
                await HandleCreateQueueQuery(context, form);
                break;
            case "GetQueueUrl":
                await HandleGetQueueUrlQuery(context, form);
                break;
            case "SendMessage":
                await HandleSendMessageQuery(context, form);
                break;
            case "ReceiveMessage":
                await HandleReceiveMessageQuery(context, form);
                break;
            case "DeleteMessage":
                await HandleDeleteMessageQuery(context, form);
                break;
            case "ListQueues":
                await HandleListQueuesQuery(context, form);
                break;
            case "DeleteQueue":
                await HandleDeleteQueueQuery(context, form);
                break;
            case "GetQueueAttributes":
                await HandleGetQueueAttributesQuery(context, form);
                break;
            default:
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/xml";
                await context.Response.WriteAsync(XmlError("InvalidAction", $"Unknown action: {action}"));
                break;
        }
    }

    // JSON Protocol handlers
    private async Task HandleCreateQueueJson(HttpContext context, JsonElement root)
    {
        var name = root.GetProperty("QueueName").GetString() ?? string.Empty;
        var queue = GetOrCreateQueue(name);
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { QueueUrl = queue.QueueUrl }));
    }

    private async Task HandleGetQueueUrlJson(HttpContext context, JsonElement root)
    {
        var name = root.GetProperty("QueueName").GetString() ?? string.Empty;
        var queue = _registry.GetSqsQueue(name);
        if (queue is null) { await WriteQueueNotFoundJson(context, name); return; }
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { QueueUrl = queue.QueueUrl }));
    }

    private async Task HandleSendMessageJson(HttpContext context, JsonElement root)
    {
        _logger.LogInformation("Messaggio ricevuto Received SendMessage request: {RequestBody}", root.ToString());
        var queueUrl = root.GetProperty("QueueUrl").GetString() ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { await WriteQueueNotFoundJson(context, queueUrl); return; }

        using var activity = KiteActivitySource.SQS.StartActivity("sqs.send");
        activity?.SetTag("queue.name", queue.Name);

        var msg = new SqsMessage
        {
            Body = root.TryGetProperty("MessageBody", out var mb) ? mb.GetString() ?? string.Empty : string.Empty,
            ReceiptHandle = Guid.NewGuid().ToString()
        };
        queue.Messages.Enqueue(msg);
        LogHelpers.AddSentMessage(queue.SentMessages, msg.MessageId.ToString(), msg.Body);
        LogHelpers.AddRequestLog(queue.RequestLog, "SendMessage", $"MessageId={msg.MessageId}");
        queue.NotifyChange();

        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            MessageId = msg.MessageId.ToString(),
            MD5OfMessageBody = ComputeMd5(msg.Body)
        }));
    }

    private async Task HandleReceiveMessageJson(HttpContext context, JsonElement root)
    {
        _logger.LogInformation("Messaggio ricevuto request: {Request}", root.ToString());
        var queueUrl = root.GetProperty("QueueUrl").GetString() ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { await WriteQueueNotFoundJson(context, queueUrl); return; }

        var maxMessages = root.TryGetProperty("MaxNumberOfMessages", out var max) ? max.GetInt32() : 1;
        var visibilityTimeout = root.TryGetProperty("VisibilityTimeout", out var vt) ? vt.GetInt32() : 30;

        using var activity = KiteActivitySource.SQS.StartActivity("sqs.receive");
        activity?.SetTag("queue.name", queue.Name);

        var messages = DequeueMessages(queue, maxMessages, visibilityTimeout);
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Messages = messages.Select(m => new
            {
                MessageId = m.MessageId.ToString(),
                ReceiptHandle = m.ReceiptHandle,
                Body = m.Body,
                MD5OfBody = ComputeMd5(m.Body),
                Attributes = m.Attributes
            })
        }));
    }

    private async Task HandleDeleteMessageJson(HttpContext context, JsonElement root)
    {
        var queueUrl = root.GetProperty("QueueUrl").GetString() ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { await WriteQueueNotFoundJson(context, queueUrl); return; }

        var receiptHandle = root.GetProperty("ReceiptHandle").GetString() ?? string.Empty;

        using var activity = KiteActivitySource.SQS.StartActivity("sqs.delete");
        activity?.SetTag("queue.name", queue.Name);

        DeleteMessage(queue, receiptHandle);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleListQueuesJson(HttpContext context, JsonElement root)
    {
        var urls = _registry.GetAllSqsQueues().Select(q => q.QueueUrl).ToList();
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { QueueUrls = urls }));
    }

    private async Task HandleDeleteQueueJson(HttpContext context, JsonElement root)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync("{}");
    }

    private async Task HandleGetQueueAttributesJson(HttpContext context, JsonElement root)
    {
        var queueUrl = root.GetProperty("QueueUrl").GetString() ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { await WriteQueueNotFoundJson(context, queueUrl); return; }

        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Attributes = new Dictionary<string, string>
            {
                ["ApproximateNumberOfMessages"] = queue.Messages.Count.ToString(),
                ["ApproximateNumberOfMessagesNotVisible"] = queue.VisibilityTimeouts.Count.ToString(),
                ["QueueArn"] = $"arn:aws:sqs:{_registry.Region}:{_registry.AccountId}:{queue.Name}"
            }
        }));
    }

    // Query Protocol handlers
    private async Task HandleCreateQueueQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var name = form["QueueName"] ?? string.Empty;
        var queue = GetOrCreateQueue(name);
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<CreateQueueResponse>
  <CreateQueueResult>
    <QueueUrl>{queue.QueueUrl}</QueueUrl>
  </CreateQueueResult>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</CreateQueueResponse>");
    }

    private async Task HandleGetQueueUrlQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var name = form["QueueName"] ?? string.Empty;
        var queue = _registry.GetSqsQueue(name);
        if (queue is null) { context.Response.StatusCode = 400; context.Response.ContentType = "application/xml"; await context.Response.WriteAsync(XmlError("AWS.SimpleQueueService.NonExistentQueue", $"Queue {name} not found")); return; }
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<GetQueueUrlResponse>
  <GetQueueUrlResult>
    <QueueUrl>{queue.QueueUrl}</QueueUrl>
  </GetQueueUrlResult>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</GetQueueUrlResponse>");
    }

    private async Task HandleSendMessageQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var queueUrl = form["QueueUrl"] ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { context.Response.StatusCode = 400; context.Response.ContentType = "application/xml"; await context.Response.WriteAsync(XmlError("AWS.SimpleQueueService.NonExistentQueue", "Queue not found")); return; }

        var msg = new SqsMessage
        {
            Body = form["MessageBody"] ?? string.Empty,
            ReceiptHandle = Guid.NewGuid().ToString()
        };
        queue.Messages.Enqueue(msg);
        LogHelpers.AddSentMessage(queue.SentMessages, msg.MessageId.ToString(), msg.Body);
        LogHelpers.AddRequestLog(queue.RequestLog, "SendMessage", $"MessageId={msg.MessageId}");
        queue.NotifyChange();

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<SendMessageResponse>
  <SendMessageResult>
    <MessageId>{msg.MessageId}</MessageId>
    <MD5OfMessageBody>{ComputeMd5(msg.Body)}</MD5OfMessageBody>
  </SendMessageResult>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</SendMessageResponse>");
    }

    private async Task HandleReceiveMessageQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var queueUrl = form["QueueUrl"] ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { context.Response.StatusCode = 400; context.Response.ContentType = "application/xml"; await context.Response.WriteAsync(XmlError("AWS.SimpleQueueService.NonExistentQueue", "Queue not found")); return; }

        var maxMessages = int.TryParse(form["MaxNumberOfMessages"], out var m) ? m : 1;
        var visibilityTimeout = int.TryParse(form["VisibilityTimeout"], out var vt) ? vt : 30;
        var messages = DequeueMessages(queue, maxMessages, visibilityTimeout);

        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine("<ReceiveMessageResponse>");
        sb.AppendLine("  <ReceiveMessageResult>");
        foreach (var msg in messages)
        {
            sb.AppendLine("    <Message>");
            sb.AppendLine($"      <MessageId>{msg.MessageId}</MessageId>");
            sb.AppendLine($"      <ReceiptHandle>{msg.ReceiptHandle}</ReceiptHandle>");
            sb.AppendLine($"      <MD5OfBody>{ComputeMd5(msg.Body)}</MD5OfBody>");
            sb.AppendLine($"      <Body>{System.Security.SecurityElement.Escape(msg.Body)}</Body>");
            sb.AppendLine("    </Message>");
        }
        sb.AppendLine("  </ReceiveMessageResult>");
        sb.AppendLine($"  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>");
        sb.AppendLine("</ReceiveMessageResponse>");

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleDeleteMessageQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var queueUrl = form["QueueUrl"] ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is not null)
            DeleteMessage(queue, form["ReceiptHandle"] ?? string.Empty);

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<DeleteMessageResponse>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</DeleteMessageResponse>");
    }

    private async Task HandleListQueuesQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine("<ListQueuesResponse>");
        sb.AppendLine("  <ListQueuesResult>");
        foreach (var q in _registry.GetAllSqsQueues())
            sb.AppendLine($"    <QueueUrl>{q.QueueUrl}</QueueUrl>");
        sb.AppendLine("  </ListQueuesResult>");
        sb.AppendLine($"  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>");
        sb.AppendLine("</ListQueuesResponse>");

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleDeleteQueueQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<DeleteQueueResponse>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</DeleteQueueResponse>");
    }

    private async Task HandleGetQueueAttributesQuery(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var queueUrl = form["QueueUrl"] ?? string.Empty;
        var queue = GetQueueByUrl(queueUrl);
        if (queue is null) { context.Response.StatusCode = 400; context.Response.ContentType = "application/xml"; await context.Response.WriteAsync(XmlError("AWS.SimpleQueueService.NonExistentQueue", "Queue not found")); return; }

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<GetQueueAttributesResponse>
  <GetQueueAttributesResult>
    <Attribute>
      <Name>ApproximateNumberOfMessages</Name>
      <Value>{queue.Messages.Count}</Value>
    </Attribute>
    <Attribute>
      <Name>ApproximateNumberOfMessagesNotVisible</Name>
      <Value>{queue.VisibilityTimeouts.Count}</Value>
    </Attribute>
    <Attribute>
      <Name>QueueArn</Name>
      <Value>arn:aws:sqs:{_registry.Region}:{_registry.AccountId}:{queue.Name}</Value>
    </Attribute>
  </GetQueueAttributesResult>
  <ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>
</GetQueueAttributesResponse>");
    }

    // Helpers
    private SqsQueue GetOrCreateQueue(string name)
    {
        var existing = _registry.GetSqsQueue(name);
        if (existing is not null) return existing;

        var queue = new SqsQueue
        {
            Name = name,
            QueueUrl = $"http://localhost:{_registry.Port}/{_registry.AccountId}/{name}"
        };
        _registry.RegisterSqsQueue(queue);
        return queue;
    }

    public SqsQueue? GetQueueByUrl(string queueUrl)
    {
        // URL format: http://localhost:{port}/{accountId}/{queueName}
        // or just the name
        var name = queueUrl.Split('/').Last();
        return _registry.GetSqsQueue(name);
    }

    private List<SqsMessage> DequeueMessages(SqsQueue queue, int maxMessages, int visibilityTimeoutSeconds)
    {
        // First, re-enqueue any expired visibility timeouts
        RequeueExpiredMessages(queue);

        var result = new List<SqsMessage>();
        for (int i = 0; i < maxMessages; i++)
        {
            if (queue.Messages.TryDequeue(out var msg))
            {
                msg.ReceiptHandle = Guid.NewGuid().ToString();
                var visibleAt = DateTime.UtcNow.AddSeconds(visibilityTimeoutSeconds);
                lock (queue.VisibilityTimeouts)
                    queue.VisibilityTimeouts[msg.ReceiptHandle] = (visibleAt, msg);
                result.Add(msg);
            }
        }
        return result;
    }

    private void DeleteMessage(SqsQueue queue, string receiptHandle)
    {
        lock (queue.VisibilityTimeouts)
            queue.VisibilityTimeouts.Remove(receiptHandle);
    }

    public void RequeueExpiredMessages(SqsQueue queue)
    {
        var now = DateTime.UtcNow;
        List<string> expired;
        lock (queue.VisibilityTimeouts)
        {
            expired = queue.VisibilityTimeouts
                .Where(kv => kv.Value.VisibleAt <= now)
                .Select(kv => kv.Key)
                .ToList();
        }

        foreach (var handle in expired)
        {
            lock (queue.VisibilityTimeouts)
            {
                if (queue.VisibilityTimeouts.TryGetValue(handle, out var entry))
                {
                    queue.VisibilityTimeouts.Remove(handle);
                    queue.Messages.Enqueue(entry.Msg);
                }
            }
        }
    }

    private static string ComputeMd5(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string XmlError(string code, string message) =>
        $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<ErrorResponse>
  <Error>
    <Code>{code}</Code>
    <Message>{message}</Message>
  </Error>
  <RequestId>{Guid.NewGuid()}</RequestId>
</ErrorResponse>";

    private static async Task WriteQueueNotFoundJson(HttpContext context, string identifier)
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/x-amz-json-1.0";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            __type = "AWS.SimpleQueueService.NonExistentQueue",
            message = $"Queue {identifier} not found"
        }));
    }
}
