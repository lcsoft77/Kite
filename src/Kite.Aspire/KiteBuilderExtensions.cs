using System.Collections.Immutable;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Kite.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kite.Aspire;

/// <summary>
/// Extension methods for integrating Kite into a .NET Aspire App Host.
/// </summary>
public static class KiteBuilderExtensions
{
    /// <summary>
    /// Adds Kite as an Aspire resource. The emulator runs in-process inside the
    /// App Host and its endpoint, logs, metrics and traces are visible in the Aspire dashboard.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="port">The HTTP port the emulator will listen on. Defaults to 4566 (LocalStack standard port).</param>
    public static IResourceBuilder<KiteResource> AddKite(
        this IDistributedApplicationBuilder builder,
        int port = 4566)
    {
        var resource = new KiteResource(KiteActivitySource.RootName) { Port = port };

        builder.Services.TryAddEventingSubscriber<KiteLifecycleHook>();
        builder.Services.AddHttpClient();

        return builder.AddResource(resource)
            .WithHttpEndpoint(port: port, name: "http", isProxied: false)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = KiteActivitySource.RootName,
                Properties = ImmutableArray<ResourcePropertySnapshot>.Empty,
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });
    }

    /// <summary>
    /// Configures the port the emulator listens on.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithPort(
        this IResourceBuilder<KiteResource> builder, int port)
    {
        builder.Resource.Port = port;
        return builder;
    }

    /// <summary>
    /// Configures the AWS region used by the emulator.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithRegion(
        this IResourceBuilder<KiteResource> builder, string region)
    {
        builder.Resource.Region = region;
        return builder;
    }

    /// <summary>
    /// Configures the fake AWS credentials used by the emulator.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithCredentials(
        this IResourceBuilder<KiteResource> builder,
        string accessKey,
        string secretKey)
    {
        builder.Resource.AccessKeyId = accessKey;
        builder.Resource.SecretAccessKey = secretKey;
        return builder;
    }

    /// <summary>
    /// Registers a Lambda function with the emulator. The function appears as a child
    /// resource in the Aspire dashboard and exposes a <em>Restart</em> command button.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="name">The Lambda function name.</param>
    /// <param name="dllPath">Path to the Lambda assembly DLL.</param>
    /// <param name="handler">The handler string in the format <c>Assembly::Namespace.Type::Method</c>.</param>
    public static IResourceBuilder<KiteResource> WithLambdaFunction(
        this IResourceBuilder<KiteResource> builder,
        string name,
        string dllPath,
        string handler)
    {
        var lambdaResource = new LambdaFunctionResource(name, builder.Resource)
        {
            DllPath = dllPath,
            Handler = handler
        };
        builder.Resource.Lambdas.Add(lambdaResource);

        var emulatorResource = builder.Resource;

        builder.ApplicationBuilder.AddResource(lambdaResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Lambda",
                Properties =
                [
                    new ResourcePropertySnapshot("Handler", handler),
                    new ResourcePropertySnapshot("DllPath", dllPath)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            })
            .WithCommand(
                name: "restart",
                displayName: "Restart",
                executeCommand: async ctx =>
                {
                    var httpClient = ctx.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
                    var url = $"http://localhost:{emulatorResource.Port}/2015-03-31/functions/{name}/restart";
                    try
                    {
                        var response = await httpClient.PostAsync(url, content: null, ctx.CancellationToken);
                        return response.IsSuccessStatusCode
                            ? new ExecuteCommandResult { Success = true }
                            : new ExecuteCommandResult { Success = false, Message = "Restart request failed" };
                    }
                    catch (Exception ex)
                    {
                        return new ExecuteCommandResult { Success = false, Message = ex.Message };
                    }
                },
                new CommandOptions { IconName = "ArrowCircleRight" });

        return builder;
    }

    /// <summary>
    /// Registers an SQS queue with the emulator. The queue appears as a child resource
    /// in the Aspire dashboard.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithSQSQueue(
        this IResourceBuilder<KiteResource> builder, string name)
    {
        var queueResource = new SqsQueueResource(name, builder.Resource);
        builder.Resource.SqsQueues.Add(queueResource);

        builder.ApplicationBuilder.AddResource(queueResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "SQS Queue",
                Properties =
                [
                    new ResourcePropertySnapshot("Queue URL", $"http://localhost:{builder.Resource.Port}/{builder.Resource.AccountId}/{name}")
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }

    /// <summary>
    /// Registers an S3 bucket with the emulator. The bucket appears as a child resource
    /// in the Aspire dashboard.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithS3Bucket(
        this IResourceBuilder<KiteResource> builder, string name)
    {
        var bucketResource = new S3BucketResource(name, builder.Resource);
        builder.Resource.S3Buckets.Add(bucketResource);

        builder.ApplicationBuilder.AddResource(bucketResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "S3 Bucket",
                Properties =
                [
                    new ResourcePropertySnapshot("Bucket Name", name)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }

    /// <summary>
    /// Registers an EventBridge event bus with the emulator. The bus appears as a child
    /// resource in the Aspire dashboard.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithEventBridgeBus(
        this IResourceBuilder<KiteResource> builder, string name)
    {
        var busResource = new EventBridgeBusResource(name, builder.Resource);
        builder.Resource.EventBridgeBuses.Add(busResource);

        builder.ApplicationBuilder.AddResource(busResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "EventBridge Bus",
                Properties =
                [
                    new ResourcePropertySnapshot("Bus Name", name)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }

    /// <summary>
    /// Configures an EventBridge rule that routes matching events to a Lambda function.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="ruleName">The EventBridge rule name.</param>
    /// <param name="configure">A delegate to configure the rule (bus, source, detail-type, detail fields and target).</param>
    public static IResourceBuilder<KiteResource> WithEventBridgeRule(
        this IResourceBuilder<KiteResource> builder,
        string ruleName,
        Action<EventBridgeRuleAspireBuilder> configure)
    {
        var ruleBuilder = new EventBridgeRuleAspireBuilder();
        configure(ruleBuilder);
        builder.Resource.EventBridgeRuleDefinitions.Add(
            (ruleName,
             ruleBuilder.BusName,
             new List<string>(ruleBuilder.Sources),
             new List<string>(ruleBuilder.DetailTypes),
             new Dictionary<string, object>(ruleBuilder.Details),
             new List<string>(ruleBuilder.TargetFunctions)));
        return builder;
    }

    /// <summary>
    /// Registers an SNS topic with the emulator. The topic appears as a child resource
    /// in the Aspire dashboard.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithSnsTopic(
        this IResourceBuilder<KiteResource> builder, string name)
    {
        var topicResource = new SnsTopicResource(name, builder.Resource);
        builder.Resource.SnsTopics.Add(topicResource);

        builder.ApplicationBuilder.AddResource(topicResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "SNS Topic",
                Properties =
                [
                    new ResourcePropertySnapshot("Topic Name", name)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }

    /// <summary>
    /// Configures an SQS-to-Lambda event source mapping trigger.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithSQSTrigger(
        this IResourceBuilder<KiteResource> builder,
        string queueName,
        string functionName,
        int batchSize = 10)
    {
        builder.Resource.SqsTriggers.Add((queueName, functionName, batchSize));
        return builder;
    }

    /// <summary>
    /// Configures an S3-to-Lambda trigger.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithS3Trigger(
        this IResourceBuilder<KiteResource> builder,
        string bucketName,
        string functionName,
        Kite.Core.S3EventType eventType = Kite.Core.S3EventType.ObjectCreated)
    {
        builder.Resource.S3Triggers.Add((bucketName, functionName, eventType));
        return builder;
    }

    /// <summary>
    /// Adds an SSM Parameter Store parameter to the emulator.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="name">The parameter name (e.g. <c>/myapp/config/key</c>).</param>
    /// <param name="value">The parameter value.</param>
    /// <param name="type">The parameter type: <c>String</c>, <c>StringList</c>, or <c>SecureString</c>. Defaults to <c>String</c>.</param>
    public static IResourceBuilder<KiteResource> WithSsmParameter(
        this IResourceBuilder<KiteResource> builder,
        string name,
        string value,
        string type = "String")
    {
        builder.Resource.SsmParameters.Add((name, value, type));
        return builder;
    }

    /// <summary>
    /// Registers an ECS cluster with the emulator. The cluster appears as a child resource
    /// in the Aspire dashboard.
    /// </summary>
    public static IResourceBuilder<KiteResource> WithEcsCluster(
        this IResourceBuilder<KiteResource> builder, string name)
    {
        var clusterResource = new EcsClusterResource(name, builder.Resource);
        builder.Resource.EcsClusters.Add(clusterResource);

        builder.ApplicationBuilder.AddResource(clusterResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "ECS Cluster",
                Properties =
                [
                    new ResourcePropertySnapshot("Cluster Name", name)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }

    /// <summary>
    /// Registers an ECS task definition with the emulator (no auto-start; tasks run only when
    /// <c>RunTask</c> is called via the SDK or API). The task definition appears as a child
    /// resource in the Aspire dashboard.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="family">The task definition family name.</param>
    /// <param name="configure">Optional builder action to configure containers, CPU, memory, etc.</param>
    public static IResourceBuilder<KiteResource> WithEcsTaskDefinition(
        this IResourceBuilder<KiteResource> builder,
        string family,
        Action<Kite.Core.EcsTaskDefinitionBuilder>? configure = null)
    {
        builder.Resource.EcsTaskDefinitions.Add((family, configure));
        RegisterEcsTaskDefinitionResource(builder, family);
        return builder;
    }

    /// <summary>
    /// Registers an ECS task definition backed by a local .NET Aspire project. When
    /// <c>RunTask</c> is called the project is launched via <c>dotnet run</c>; when
    /// <c>StopTask</c> is called the process is terminated. The project is <em>not</em>
    /// registered as a regular Aspire resource — its lifecycle is fully controlled by ECS.
    /// </summary>
    /// <typeparam name="TProject">The Aspire-generated project metadata type (e.g. <c>Projects.ECS_Task</c>).</typeparam>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="family">The task definition family name.</param>
    /// <param name="configure">Optional action to further configure the task definition.</param>
    /// <param name="environmentVariables">Optional environment variables injected into the process at startup.</param>
    public static IResourceBuilder<KiteResource> WithEcsTaskDefinition<TProject>(
        this IResourceBuilder<KiteResource> builder,
        string family,
        Action<Kite.Core.EcsTaskDefinitionBuilder>? configure = null,
        Dictionary<string, string>? environmentVariables = null)
        where TProject : IProjectMetadata, new()
    {
        var projectPath = new TProject().ProjectPath;
        var workingDir = Path.GetDirectoryName(projectPath) ?? AppContext.BaseDirectory;

        Action<Kite.Core.EcsTaskDefinitionBuilder> wrapped = td =>
        {
            configure?.Invoke(td);
            td.WithLocalProcess("dotnet", workingDir, "run", "--project", projectPath, "--", "--debug");
            if (environmentVariables is not null)
                foreach (var (k, v) in environmentVariables)
                    td.WithEnvironmentVariable(k, v);
        };
        builder.Resource.EcsTaskDefinitions.Add((family, wrapped));
        RegisterEcsTaskDefinitionResource(builder, family);
        return builder;
    }

    private static void RegisterEcsTaskDefinitionResource(
        IResourceBuilder<KiteResource> builder,
        string family)
    {
        var clusterName = builder.Resource.EcsClusters.FirstOrDefault()?.Name ?? string.Empty;
        var taskDefResource = new EcsTaskDefinitionResource(family, builder.Resource)
        {
            Family = family,
            ClusterName = clusterName
        };
        builder.Resource.EcsTaskDefinitionResources.Add(taskDefResource);

        builder.ApplicationBuilder.AddResource(taskDefResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "ECS Task Definition",
                Properties =
                [
                    new ResourcePropertySnapshot("Family", family),
                    new ResourcePropertySnapshot("Cluster", clusterName)
                ],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });
    }

    /// <summary>
    /// Enables the built-in Blazor Server dashboard. The dashboard runs on a separate port
    /// and its URL is shown in the Aspire dashboard's URL column.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="port">The HTTP port for the dashboard. Defaults to the emulator port + 1.</param>
    public static IResourceBuilder<KiteResource> WithDashboard(
        this IResourceBuilder<KiteResource> builder,
        int? port = null)
    {
        var dashboardPort = port ?? builder.Resource.Port + 1;
        if (dashboardPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), dashboardPort, "Dashboard port must be between 1 and 65535.");

        builder.Resource.DashboardEnabled = true;
        builder.Resource.DashboardPort = dashboardPort;

        builder.WithHttpEndpoint(port: dashboardPort, name: "dashboard", isProxied: false);

        return builder;
    }

    /// <summary>
    /// Registers a DynamoDB table with the emulator. The table appears as a child resource
    /// in the Aspire dashboard.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="tableName">The DynamoDB table name.</param>
    /// <param name="partitionKeyName">The partition key (HASH) attribute name.</param>
    /// <param name="partitionKeyType">The partition key attribute type: <c>S</c>, <c>N</c>, or <c>B</c>. Defaults to <c>S</c>.</param>
    /// <param name="sortKeyName">The optional sort key (RANGE) attribute name.</param>
    /// <param name="sortKeyType">The optional sort key attribute type: <c>S</c>, <c>N</c>, or <c>B</c>.</param>
    public static IResourceBuilder<KiteResource> WithDynamoDbTable(
        this IResourceBuilder<KiteResource> builder,
        string tableName,
        string partitionKeyName,
        string partitionKeyType = "S",
        string? sortKeyName = null,
        string? sortKeyType = null)
    {
        var effectiveSortKeyType = sortKeyName is not null ? (sortKeyType ?? "S") : null;

        var tableResource = new DynamoDbTableResource(tableName, builder.Resource);
        builder.Resource.DynamoDbTables.Add(tableResource);
        builder.Resource.DynamoDbTableDefinitions.Add((tableName, partitionKeyName, partitionKeyType, sortKeyName, effectiveSortKeyType));

        var properties = new List<ResourcePropertySnapshot>
        {
            new("Table Name", tableName),
            new("Partition Key", $"{partitionKeyName} ({partitionKeyType})")
        };
        if (sortKeyName is not null)
            properties.Add(new ResourcePropertySnapshot("Sort Key", $"{sortKeyName} ({effectiveSortKeyType})"));

        builder.ApplicationBuilder.AddResource(tableResource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "DynamoDB Table",
                Properties = [.. properties],
                State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info)
            });

        return builder;
    }
}
