using System.Text.Json;
using Kite.Core.Models;

namespace Kite.Core;

public class KiteBuilder
{
    private readonly InMemoryServiceRegistry _registry = new();
    private readonly List<(string Name, LambdaBuilder Config)> _lambdas = new();
    private readonly List<(string RuleName, EventBridgeRuleBuilder Config)> _rules = new();
    private readonly List<(string QueueName, string FunctionName, int BatchSize)> _sqsTriggers = new();
    private readonly List<(string BucketName, string FunctionName, S3EventType EventType)> _s3Triggers = new();

    private KiteBuilder() { }

    public static KiteBuilder Create() => new();

    public KiteBuilder WithPort(int port) { _registry.Port = port; return this; }

    public KiteBuilder WithCredentials(string accessKey, string secretKey)
    {
        _registry.AccessKeyId = accessKey;
        _registry.SecretAccessKey = secretKey;
        return this;
    }

    public KiteBuilder WithRegion(string region) { _registry.Region = region; return this; }

    public KiteBuilder AddLambda(string name, Action<LambdaBuilder> configure)
    {
        var builder = new LambdaBuilder();
        configure(builder);
        _lambdas.Add((name, builder));
        return this;
    }

    public KiteBuilder AddSQSQueue(string name)
    {
        _registry.RegisterSqsQueue(new SqsQueue
        {
            Name = name,
            QueueUrl = $"http://localhost:{_registry.Port}/{_registry.AccountId}/{name}"
        });
        return this;
    }

    public KiteBuilder AddS3Bucket(string name)
    {
        _registry.RegisterS3Bucket(new S3Bucket { Name = name });
        return this;
    }

    public KiteBuilder AddEventBridgeBus(string name)
    {
        _registry.RegisterEventBridgeBus(new EventBridgeBus { Name = name });
        return this;
    }

    public KiteBuilder AddEventBridgeRule(string name, Action<EventBridgeRuleBuilder> configure)
    {
        var builder = new EventBridgeRuleBuilder();
        configure(builder);
        _rules.Add((name, builder));
        return this;
    }

    public KiteBuilder AddSQSTrigger(string queueName, string functionName, int batchSize = 10)
    {
        _sqsTriggers.Add((queueName, functionName, batchSize));
        return this;
    }

    public KiteBuilder AddS3Trigger(string bucketName, string functionName, S3EventType eventType = S3EventType.ObjectCreated)
    {
        _s3Triggers.Add((bucketName, functionName, eventType));
        return this;
    }

    public KiteBuilder AddApiGatewayRoute(string method, string path, string functionName)
    {
        _registry.RegisterApiGatewayRoute(new ApiGatewayRoute
        {
            Method = method.ToUpper(),
            Path = path,
            FunctionName = functionName
        });
        return this;
    }

    public KiteBuilder AddSsmParameter(string name, string value, string type = "String")
    {
        _registry.RegisterSsmParameter(new SsmParameter
        {
            Name = name,
            Value = value,
            Type = type
        });
        return this;
    }

    public KiteBuilder AddSnsTopic(string name)
    {
        var arn = $"arn:aws:sns:{_registry.Region}:{_registry.AccountId}:{name}";
        _registry.RegisterSnsTopic(new SnsTopic
        {
            Name = name,
            TopicArn = arn
        });
        return this;
    }

    public KiteBuilder AddDynamoDbTable(string tableName, string partitionKeyName, string partitionKeyType = "S",
        string? sortKeyName = null, string? sortKeyType = null)
    {
        var effectiveSortKeyType = sortKeyName is not null ? (sortKeyType ?? "S") : null;
        var table = new DynamoDbTable
        {
            TableName = tableName,
            PartitionKeyName = partitionKeyName,
            PartitionKeyType = partitionKeyType,
            SortKeyName = sortKeyName,
            SortKeyType = effectiveSortKeyType,
            AttributeDefinitions = BuildAttributeDefinitions(partitionKeyName, partitionKeyType, sortKeyName, effectiveSortKeyType)
        };
        _registry.RegisterDynamoDbTable(table);
        return this;
    }

    private static List<Models.DynamoDbAttributeDefinition> BuildAttributeDefinitions(
        string pkName, string pkType, string? skName, string? skType)
    {
        var defs = new List<Models.DynamoDbAttributeDefinition>
        {
            new() { AttributeName = pkName, AttributeType = pkType }
        };
        if (skName is not null && skType is not null)
            defs.Add(new() { AttributeName = skName, AttributeType = skType });
        return defs;
    }

    public KiteBuilder AddEcsCluster(string name)
    {
        _registry.RegisterEcsCluster(new Models.EcsCluster { Name = name });
        return this;
    }

    public KiteBuilder AddEcsTaskDefinition(string family, Action<EcsTaskDefinitionBuilder>? configure = null)
    {
        var builder = new EcsTaskDefinitionBuilder();
        configure?.Invoke(builder);
        _registry.RegisterEcsTaskDefinition(new Models.EcsTaskDefinition
        {
            Family = family,
            NetworkMode = builder.NetworkMode,
            Cpu = builder.Cpu,
            Memory = builder.Memory,
            ContainerDefinitions = builder.Containers,
            LocalProcessFileName = builder.LocalProcessFileName,
            LocalProcessArgs = builder.LocalProcessArgs,
            LocalProcessWorkingDirectory = builder.LocalProcessWorkingDirectory,
            LocalProcessEnvironment = builder.LocalProcessEnvironment
        });
        return this;
    }

    public KiteBuilder LoadSsmParametersFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var settings = JsonSerializer.Deserialize<SsmParameterSettings>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (settings?.Parameters is { Count: > 0 })
        {
            foreach (var entry in settings.Parameters)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidOperationException(
                        $"SSM parameter entry in '{filePath}' has a null or empty Name. All parameters must have a valid Name.");

                _registry.RegisterSsmParameter(new SsmParameter
                {
                    Name = entry.Name,
                    Value = entry.Value,
                    Type = entry.Type
                });
            }
        }

        return this;
    }

    public Kite Build()
    {
        // Register lambdas
        foreach (var (name, config) in _lambdas)
        {
            _registry.RegisterLambda(new LambdaFunction
            {
                Name = name,
                Handler = config.Handler,
                DllPath = config.DllPath,
                MemoryMb = config.MemoryMb,
                Timeout = config.Timeout
            });
        }

        // Register EventBridge rules
        foreach (var (ruleName, config) in _rules)
        {
            var bus = _registry.GetEventBridgeBus(config.BusName)
                      ?? new EventBridgeBus { Name = config.BusName };

            if (_registry.GetEventBridgeBus(config.BusName) is null)
                _registry.RegisterEventBridgeBus(bus);

            var rule = new EventBridgeRule
            {
                Name = ruleName,
                BusName = config.BusName,
                EventPattern = new EventPattern
                {
                    Source = config.Sources.Count > 0 ? config.Sources.ToArray() : null,
                    DetailType = config.DetailTypes.Count > 0 ? config.DetailTypes.ToArray() : null,
                    Detail = config.Details.Count > 0 ? config.Details : null
                },
                Targets = config.TargetFunctions.Select((fn, idx) => new EventBridgeTarget
                {
                    Id = $"target-{idx}",
                    LambdaFunctionName = fn
                }).ToList()
            };

            bus.Rules.Add(rule);
        }

        // Register SQS triggers (ESM)
        foreach (var (queueName, functionName, batchSize) in _sqsTriggers)
        {
            _registry.RegisterEventSourceMapping(new EventSourceMapping
            {
                Type = EventSourceMappingType.SQS,
                SourceName = queueName,
                FunctionName = functionName,
                BatchSize = batchSize,
                Enabled = true
            });
        }

        // Register S3 triggers
        foreach (var (bucketName, functionName, eventType) in _s3Triggers)
        {
            var bucket = _registry.GetS3Bucket(bucketName);
            if (bucket is not null)
            {
                bucket.NotificationConfiguration ??= new Models.S3NotificationConfiguration();
                bucket.NotificationConfiguration.LambdaFunctionConfigurations.Add(new Models.S3LambdaNotification
                {
                    LambdaFunctionArn = functionName,
                    Events = eventType == S3EventType.ObjectCreated
                        ? new List<string> { "s3:ObjectCreated:*" }
                        : new List<string> { "s3:ObjectRemoved:*" }
                });
            }

            _registry.RegisterEventSourceMapping(new EventSourceMapping
            {
                Type = EventSourceMappingType.S3,
                SourceName = bucketName,
                FunctionName = functionName,
                Enabled = true
            });
        }

        // Update queue URLs now that port is set
        foreach (var queue in _registry.GetAllSqsQueues())
        {
            if (!queue.QueueUrl.Contains($":{_registry.Port}/"))
                queue.QueueUrl = $"http://localhost:{_registry.Port}/{_registry.AccountId}/{queue.Name}";
        }

        return new Kite(_registry);
    }
}
