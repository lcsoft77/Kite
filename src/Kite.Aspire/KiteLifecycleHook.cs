using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Kite.Core;
using Kite.Dashboard;
using Kite.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kite.Aspire;

/// <summary>
/// An Aspire eventing subscriber that starts the Kite host in-process and
/// keeps the Aspire dashboard resource state in sync.
/// </summary>
internal sealed class KiteLifecycleHook(
    ResourceNotificationService notificationService,
    ResourceLoggerService loggerService,
    IConfiguration configuration,
    ILogger<KiteLifecycleHook> logger) : IDistributedApplicationEventingSubscriber, IAsyncDisposable
{
    private readonly List<Task> _runningTasks = [];
    private readonly Lock _linkedCancellationTokenSourcesLock = new();
    private readonly List<CancellationTokenSource> _linkedCancellationTokenSources = [];
    private readonly CancellationTokenSource _cts = new();

    public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        eventing.Subscribe<BeforeStartEvent>((@event, ct) => OnBeforeStartAsync(@event.Model, ct));
        return Task.CompletedTask;
    }

    private Task OnBeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
    {
        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        lock (_linkedCancellationTokenSourcesLock)
        {
            _linkedCancellationTokenSources.Add(linkedCancellationTokenSource);
        }

        foreach (var resource in appModel.Resources.OfType<KiteResource>())
        {
            _runningTasks.Add(RunEmulatorAsync(resource, linkedCancellationTokenSource.Token));
        }
        return Task.CompletedTask;
    }

    private async Task RunEmulatorAsync(KiteResource resource, CancellationToken ct)
    {
        await notificationService.PublishUpdateAsync(resource,
            state => state with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        var resourceLogger = loggerService.GetLogger(resource);

        try
        {
            var emulatorBuilder = KiteBuilder.Create()
                .WithPort(resource.Port)
                .WithRegion(resource.Region)
                .WithCredentials(resource.AccessKeyId, resource.SecretAccessKey);

            foreach (var lambda in resource.Lambdas)
                emulatorBuilder.AddLambda(lambda.Name, b => b
                    .WithDll(lambda.DllPath)
                    .WithHandler(lambda.Handler));

            foreach (var queue in resource.SqsQueues)
                emulatorBuilder.AddSQSQueue(queue.Name);

            foreach (var bucket in resource.S3Buckets)
                emulatorBuilder.AddS3Bucket(bucket.Name);

            foreach (var bus in resource.EventBridgeBuses)
                emulatorBuilder.AddEventBridgeBus(bus.Name);

            foreach (var (ruleName, busName, sources, detailTypes, details, targetFunctions) in resource.EventBridgeRuleDefinitions)
            {
                emulatorBuilder.AddEventBridgeRule(ruleName, rb =>
                {
                    rb.OnBus(busName);
                    foreach (var s in sources) rb.MatchSource(s);
                    foreach (var dt in detailTypes) rb.MatchDetailType(dt);
                    foreach (var (field, value) in details) rb.MatchDetail(field, value);
                    foreach (var fn in targetFunctions) rb.TargetLambda(fn);
                });
            }

            foreach (var (queueName, functionName, batchSize) in resource.SqsTriggers)
                emulatorBuilder.AddSQSTrigger(queueName, functionName, batchSize);

            foreach (var (bucketName, functionName, eventType) in resource.S3Triggers)
                emulatorBuilder.AddS3Trigger(bucketName, functionName, eventType);

            foreach (var (paramName, paramValue, paramType) in resource.SsmParameters)
                emulatorBuilder.AddSsmParameter(paramName, paramValue, paramType);

            foreach (var cluster in resource.EcsClusters)
                emulatorBuilder.AddEcsCluster(cluster.Name);

            foreach (var (family, configure) in resource.EcsTaskDefinitions)
                emulatorBuilder.AddEcsTaskDefinition(family, configure);

            foreach (var topic in resource.SnsTopics)
                emulatorBuilder.AddSnsTopic(topic.Name);

            foreach (var (tableName, pkName, pkType, skName, skType) in resource.DynamoDbTableDefinitions)
                emulatorBuilder.AddDynamoDbTable(tableName, pkName, pkType, skName, skType);

            var (resolvedOtlpEndpoint, resolvedOtlpProtocol) = ResolveOtlpSettings(configuration);

            if (string.IsNullOrWhiteSpace(resolvedOtlpEndpoint))
            {
                logger.LogWarning(
                    "No OTLP endpoint was found in the AppHost configuration for Kite resource '{Name}'. Telemetry export to the Aspire dashboard is disabled.",
                    resource.Name);
            }
            else
            {
                logger.LogInformation(
                    "Resolved OTLP endpoint {OtlpEndpoint} for Kite resource '{Name}' using protocol {OtlpProtocol}.",
                    resolvedOtlpEndpoint,
                    resource.Name,
                    resolvedOtlpProtocol ?? "grpc");
            }

            var resolvedOtplApiKey = configuration["AppHost:OtlpApiKey"];

            var emulator = emulatorBuilder.Build().UseHost(
                configureBuilder: wb =>
                {
                    // Forward emulator logs to Aspire resource log stream
                    wb.Logging.AddProvider(new ResourceLoggerProvider(resourceLogger));
                },
                serviceName: resource.Name,
                otlpEndpoint: resolvedOtlpEndpoint,
                otlpProtocol: resolvedOtlpProtocol,
                otlpApiKey: resolvedOtplApiKey);

            // Wire log/state delegates for each ECS task definition resource
            foreach (var taskDefResource in resource.EcsTaskDefinitionResources)
            {
                var taskDefLogger = loggerService.GetLogger(taskDefResource);
                var def = emulator.Registry
                    .GetEcsTaskDefinitionsByFamily(taskDefResource.Family)
                    .OrderByDescending(d => d.Revision)
                    .FirstOrDefault();
                if (def is not null)
                {
                    def.OnLogLine = line => taskDefLogger.LogInformation("{Line}", line);
                    def.OnStateChange = (taskArn, state) =>
                    {
                        var aspireState = state switch
                        {
                            "RUNNING" => new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
                            "STOPPED" => new ResourceStateSnapshot(KnownResourceStates.Finished, KnownResourceStateStyles.Info),
                            _ => new ResourceStateSnapshot(state, KnownResourceStateStyles.Info)
                        };
                        _ = notificationService.PublishUpdateAsync(taskDefResource,
                            s => s with { State = aspireState });
                    };
                }
            }

            // Start dashboard on a separate port if enabled
            Task? dashboardTask = null;
            if (resource.DashboardEnabled)
            {
                dashboardTask = DashboardExtensions.RunDashboardAsync(
                    emulator.Registry,
                    resource.DashboardPort,
                    wb => wb.Logging.AddProvider(new ResourceLoggerProvider(resourceLogger)),
                    ct);

                // Give the dashboard a moment to bind. If it fails immediately (e.g. port
                // in use), surface the error before we advertise the URL as available.
                const int dashboardStartupTimeoutMs = 2000;
                var completedTask = await Task.WhenAny(dashboardTask, Task.Delay(dashboardStartupTimeoutMs, ct));
                if (completedTask == dashboardTask && dashboardTask.IsFaulted)
                {
                    logger.LogError(dashboardTask.Exception?.InnerException ?? dashboardTask.Exception,
                        "Dashboard failed to start on port {Port}", resource.DashboardPort);
                }
            }

            var urls = new List<UrlSnapshot>
            {
                new("http", $"http://localhost:{resource.Port}", false)
            };
            if (resource.DashboardEnabled && dashboardTask is { IsFaulted: false })
            {
                urls.Add(new("Dashboard", $"http://localhost:{resource.DashboardPort}", false));
            }

            await notificationService.PublishUpdateAsync(resource,
                state => state with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
                    Urls = [.. urls]
                });

            await UpdateChildResourceStatesAsync(resource,
                KnownResourceStates.Running, KnownResourceStateStyles.Success);

            // Task definitions wait for RunTask — override their state to a custom "Waiting" state
            foreach (var taskDefResource in resource.EcsTaskDefinitionResources)
                await notificationService.PublishUpdateAsync(taskDefResource,
                    s => s with { State = new ResourceStateSnapshot("Waiting", KnownResourceStateStyles.Info) });

            await emulator.RunAsync(ct);

            if (dashboardTask is not null)
                await dashboardTask;

            await UpdateChildResourceStatesAsync(resource,
                KnownResourceStates.Finished, KnownResourceStateStyles.Info);

            await notificationService.PublishUpdateAsync(resource,
                state => state with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.Finished, KnownResourceStateStyles.Info)
                });
        }
        catch (OperationCanceledException)
        {
            await UpdateChildResourceStatesAsync(resource,
                KnownResourceStates.Finished, KnownResourceStateStyles.Info);

            await notificationService.PublishUpdateAsync(resource,
                state => state with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.Finished, KnownResourceStateStyles.Info)
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kite resource '{Name}' failed", resource.Name);

            await UpdateChildResourceStatesAsync(resource,
                KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error);

            await notificationService.PublishUpdateAsync(resource,
                state => state with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error)
                });
        }
    }

    private async Task UpdateChildResourceStatesAsync(KiteResource resource, string state, string style)
    {
        var snapshot = new ResourceStateSnapshot(state, style);

        foreach (var lambda in resource.Lambdas)
            await notificationService.PublishUpdateAsync(lambda,
                s => s with { State = snapshot });

        foreach (var queue in resource.SqsQueues)
            await notificationService.PublishUpdateAsync(queue,
                s => s with { State = snapshot });

        foreach (var bucket in resource.S3Buckets)
            await notificationService.PublishUpdateAsync(bucket,
                s => s with { State = snapshot });

        foreach (var bus in resource.EventBridgeBuses)
            await notificationService.PublishUpdateAsync(bus,
                s => s with { State = snapshot });

        foreach (var cluster in resource.EcsClusters)
            await notificationService.PublishUpdateAsync(cluster,
                s => s with { State = snapshot });

        foreach (var taskDef in resource.EcsTaskDefinitionResources)
            await notificationService.PublishUpdateAsync(taskDef,
                s => s with { State = snapshot });

        foreach (var topic in resource.SnsTopics)
            await notificationService.PublishUpdateAsync(topic,
                s => s with { State = snapshot });

        foreach (var table in resource.DynamoDbTables)
            await notificationService.PublishUpdateAsync(table,
                s => s with { State = snapshot });
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_runningTasks.Count > 0)
        {
            try { await Task.WhenAll(_runningTasks); }
            catch (OperationCanceledException) { }
        }
        List<CancellationTokenSource> linkedCancellationTokenSourcesToDispose;
        lock (_linkedCancellationTokenSourcesLock)
        {
            linkedCancellationTokenSourcesToDispose = _linkedCancellationTokenSources.ToList();
            _linkedCancellationTokenSources.Clear();
        }

        foreach (var linkedCancellationTokenSource in linkedCancellationTokenSourcesToDispose)
        {
            linkedCancellationTokenSource.Dispose();
        }
        _cts.Dispose();
    }

    private static (string? Endpoint, string? Protocol) ResolveOtlpSettings(IConfiguration configuration)
    {
        var configuredEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var configuredProtocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

        var dashboardGrpcEndpoint = configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"]
            ?? configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"];

        var dashboardHttpEndpoint = configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"]
            ?? configuration["DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"];

        var resolvedEndpoint = configuredEndpoint
            ?? dashboardGrpcEndpoint
            ?? dashboardHttpEndpoint;

        var resolvedProtocol = configuredProtocol;
        if (string.IsNullOrWhiteSpace(resolvedProtocol)
            && EndpointEquals(resolvedEndpoint, dashboardHttpEndpoint))
        {
            resolvedProtocol = "http/protobuf";
        }

        return (resolvedEndpoint, resolvedProtocol);
    }

    private static bool EndpointEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
