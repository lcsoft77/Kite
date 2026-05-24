using Kite.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kite.SQS;

public class SqsVisibilityTimeoutChecker : BackgroundService
{
    private readonly IServiceRegistry _registry;
    private readonly SqsService _sqsService;
    private readonly ILogger<SqsVisibilityTimeoutChecker> _logger;

    public SqsVisibilityTimeoutChecker(IServiceRegistry registry, SqsService sqsService, ILogger<SqsVisibilityTimeoutChecker> logger)
    {
        _registry = registry;
        _sqsService = sqsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken).ConfigureAwait(false);

            foreach (var queue in _registry.GetAllSqsQueues())
                _sqsService.RequeueExpiredMessages(queue);
        }
    }
}
