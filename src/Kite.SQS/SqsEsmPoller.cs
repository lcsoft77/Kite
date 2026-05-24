using Amazon.Lambda.SQSEvents;
using Kite.Core;
using Kite.Lambda.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kite.SQS;

public class SqsEsmPoller : BackgroundService
{
    private readonly IServiceRegistry _registry;
    private readonly LambdaExecutor _executor;
    private readonly SqsService _sqsService;
    private readonly ILogger<SqsEsmPoller> _logger;

    public SqsEsmPoller(IServiceRegistry registry, LambdaExecutor executor, SqsService sqsService, ILogger<SqsEsmPoller> logger)
    {
        _registry = registry;
        _executor = executor;
        _sqsService = sqsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken).ConfigureAwait(false);

            var mappings = _registry.GetAllEventSourceMappings()
                .Where(m => m.Type == Core.Models.EventSourceMappingType.SQS && m.Enabled)
                .ToList();

            foreach (var mapping in mappings)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var queue = _registry.GetSqsQueue(mapping.SourceName);
                if (queue is null) continue;

                _sqsService.RequeueExpiredMessages(queue);

                if (queue.Messages.IsEmpty) continue;

                var messages = new List<Core.Models.SqsMessage>();
                for (int i = 0; i < mapping.BatchSize; i++)
                {
                    if (!queue.Messages.TryDequeue(out var msg)) break;
                    msg.ReceiptHandle = Guid.NewGuid().ToString();
                    lock (queue.VisibilityTimeouts)
                        queue.VisibilityTimeouts[msg.ReceiptHandle] = (DateTime.UtcNow.AddSeconds(30), msg);
                    messages.Add(msg);
                }

                if (messages.Count == 0) continue;

                try
                {
                    var sqsEvent = BuildSqsEvent(messages, queue);
                    var json = System.Text.Json.JsonSerializer.Serialize(sqsEvent);
                    await _executor.InvokeAsync(mapping.FunctionName, json, stoppingToken);

                    // On success, delete messages
                    foreach (var msg in messages)
                    {
                        lock (queue.VisibilityTimeouts)
                            queue.VisibilityTimeouts.Remove(msg.ReceiptHandle);
                    }
                    _logger.LogDebug("SQS ESM: processed {Count} messages for {Function}", messages.Count, mapping.FunctionName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SQS ESM: Lambda {Function} failed for queue {Queue}, messages will be requeued", mapping.FunctionName, mapping.SourceName);
                    // Messages remain in visibility timeout, will be requeued when timeout expires
                }
            }
        }
    }

    private SQSEvent BuildSqsEvent(List<Core.Models.SqsMessage> messages, Core.Models.SqsQueue queue)
    {
        return new SQSEvent
        {
            Records = messages.Select(m => new SQSEvent.SQSMessage
            {
                MessageId = m.MessageId.ToString(),
                ReceiptHandle = m.ReceiptHandle,
                Body = m.Body,
                Md5OfBody = string.Empty,
                EventSource = "aws:sqs",
                EventSourceArn = $"arn:aws:sqs:{_registry.Region}:{_registry.AccountId}:{queue.Name}",
                AwsRegion = _registry.Region,
                Attributes = m.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList()
        };
    }
}
