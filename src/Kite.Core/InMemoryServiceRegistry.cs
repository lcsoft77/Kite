using System.Collections.Concurrent;
using Kite.Core.Models;

namespace Kite.Core;

public class InMemoryServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<string, LambdaFunction> _lambdas = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SqsQueue> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, S3Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EventBridgeBus> _buses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, EventSourceMapping> _esms = new();
    private readonly ConcurrentBag<ApiGatewayRoute> _routes = new();
    private readonly ConcurrentDictionary<string, SsmParameter> _ssmParameters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SnsTopic> _snsTopics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EcsCluster> _ecsClusters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EcsTaskDefinition> _ecsTaskDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EcsTask> _ecsTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _ecsTaskDefinitionRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DynamoDbTable> _dynamoDbTables = new(StringComparer.OrdinalIgnoreCase);

    public string Region { get; set; } = "us-east-1";
    public string AccountId { get; set; } = "000000000000";
    public string AccessKeyId { get; set; } = "test";
    public string SecretAccessKey { get; set; } = "test";
    public int Port { get; set; } = 4566;

    public void RegisterLambda(LambdaFunction function) => _lambdas[function.Name] = function;
    public LambdaFunction? GetLambda(string name) => _lambdas.TryGetValue(name, out var f) ? f : null;
    public IEnumerable<LambdaFunction> GetAllLambdas() => _lambdas.Values;

    public void RegisterSqsQueue(SqsQueue queue) => _queues[queue.Name] = queue;
    public SqsQueue? GetSqsQueue(string name) => _queues.TryGetValue(name, out var q) ? q : null;
    public IEnumerable<SqsQueue> GetAllSqsQueues() => _queues.Values;

    public void RegisterS3Bucket(S3Bucket bucket) => _buckets[bucket.Name] = bucket;
    public S3Bucket? GetS3Bucket(string name) => _buckets.TryGetValue(name, out var b) ? b : null;
    public IEnumerable<S3Bucket> GetAllS3Buckets() => _buckets.Values;

    public void RegisterEventBridgeBus(EventBridgeBus bus) => _buses[bus.Name] = bus;
    public EventBridgeBus? GetEventBridgeBus(string name) => _buses.TryGetValue(name, out var b) ? b : null;
    public IEnumerable<EventBridgeBus> GetAllEventBridgeBuses() => _buses.Values;

    public void RegisterEventSourceMapping(EventSourceMapping mapping) => _esms[mapping.Uuid] = mapping;
    public EventSourceMapping? GetEventSourceMapping(Guid uuid) => _esms.TryGetValue(uuid, out var m) ? m : null;
    public IEnumerable<EventSourceMapping> GetAllEventSourceMappings() => _esms.Values;
    public void RemoveEventSourceMapping(Guid uuid) => _esms.TryRemove(uuid, out _);

    public void RegisterApiGatewayRoute(ApiGatewayRoute route) => _routes.Add(route);
    public IEnumerable<ApiGatewayRoute> GetAllApiGatewayRoutes() => _routes;

    public void RegisterSsmParameter(SsmParameter parameter) => _ssmParameters[parameter.Name] = parameter;
    public SsmParameter? GetSsmParameter(string name) => _ssmParameters.TryGetValue(name, out var p) ? p : null;
    public IEnumerable<SsmParameter> GetAllSsmParameters() => _ssmParameters.Values;
    public void DeleteSsmParameter(string name) => _ssmParameters.TryRemove(name, out _);

    public void RegisterSnsTopic(SnsTopic topic)
    {
        _snsTopics[topic.TopicArn] = topic;
        _snsTopics[topic.Name] = topic;
    }

    public SnsTopic? GetSnsTopic(string nameOrArn) =>
        _snsTopics.TryGetValue(nameOrArn, out var t) ? t : null;

    public IEnumerable<SnsTopic> GetAllSnsTopics() =>
        _snsTopics.Values.Distinct();

    public void DeleteSnsTopic(string arn)
    {
        if (_snsTopics.TryRemove(arn, out var topic))
            _snsTopics.TryRemove(topic.Name, out _);
    }

    public void RegisterEcsCluster(EcsCluster cluster) => _ecsClusters[cluster.Name] = cluster;
    public EcsCluster? GetEcsCluster(string name) => _ecsClusters.TryGetValue(name, out var c) ? c : null;
    public IEnumerable<EcsCluster> GetAllEcsClusters() => _ecsClusters.Values;
    public void DeleteEcsCluster(string name) => _ecsClusters.TryRemove(name, out _);

    public void RegisterEcsTaskDefinition(EcsTaskDefinition definition)
    {
        var revision = _ecsTaskDefinitionRevisions.AddOrUpdate(definition.Family, 1, (_, v) => v + 1);
        definition.Revision = revision;
        _ecsTaskDefinitions[$"{definition.Family}:{revision}"] = definition;
    }

    public EcsTaskDefinition? GetEcsTaskDefinition(string family, int revision) =>
        _ecsTaskDefinitions.TryGetValue($"{family}:{revision}", out var d) ? d : null;

    public IEnumerable<EcsTaskDefinition> GetEcsTaskDefinitionsByFamily(string family) =>
        _ecsTaskDefinitions.Values.Where(d => string.Equals(d.Family, family, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<EcsTaskDefinition> GetAllEcsTaskDefinitions() => _ecsTaskDefinitions.Values;

    public void DeregisterEcsTaskDefinition(string family, int revision)
    {
        var key = $"{family}:{revision}";
        if (_ecsTaskDefinitions.TryGetValue(key, out var d))
            d.Status = "INACTIVE";
    }

    public void RegisterEcsTask(EcsTask task) => _ecsTasks[task.TaskArn] = task;
    public EcsTask? GetEcsTask(string taskArn) => _ecsTasks.TryGetValue(taskArn, out var t) ? t : null;
    public IEnumerable<EcsTask> GetEcsTasksForCluster(string clusterArn) =>
        _ecsTasks.Values.Where(t => string.Equals(t.ClusterArn, clusterArn, StringComparison.OrdinalIgnoreCase));
    public void UpdateEcsTask(EcsTask task) => _ecsTasks[task.TaskArn] = task;
    public void DeleteEcsTask(string taskArn) => _ecsTasks.TryRemove(taskArn, out _);

    public void RegisterDynamoDbTable(DynamoDbTable table) => _dynamoDbTables[table.TableName] = table;
    public DynamoDbTable? GetDynamoDbTable(string name) => _dynamoDbTables.TryGetValue(name, out var t) ? t : null;
    public IEnumerable<DynamoDbTable> GetAllDynamoDbTables() => _dynamoDbTables.Values;
    public void DeleteDynamoDbTable(string name) => _dynamoDbTables.TryRemove(name, out _);
}
