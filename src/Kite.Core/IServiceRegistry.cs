using Kite.Core.Models;

namespace Kite.Core;

public interface IServiceRegistry
{
    // Lambda
    void RegisterLambda(LambdaFunction function);
    LambdaFunction? GetLambda(string name);
    IEnumerable<LambdaFunction> GetAllLambdas();

    // SQS
    void RegisterSqsQueue(SqsQueue queue);
    SqsQueue? GetSqsQueue(string name);
    IEnumerable<SqsQueue> GetAllSqsQueues();

    // S3
    void RegisterS3Bucket(S3Bucket bucket);
    S3Bucket? GetS3Bucket(string name);
    IEnumerable<S3Bucket> GetAllS3Buckets();

    // EventBridge
    void RegisterEventBridgeBus(EventBridgeBus bus);
    EventBridgeBus? GetEventBridgeBus(string name);
    IEnumerable<EventBridgeBus> GetAllEventBridgeBuses();

    // ESM
    void RegisterEventSourceMapping(EventSourceMapping mapping);
    EventSourceMapping? GetEventSourceMapping(Guid uuid);
    IEnumerable<EventSourceMapping> GetAllEventSourceMappings();
    void RemoveEventSourceMapping(Guid uuid);

    // ECS
    void RegisterEcsCluster(EcsCluster cluster);
    EcsCluster? GetEcsCluster(string name);
    IEnumerable<EcsCluster> GetAllEcsClusters();
    void DeleteEcsCluster(string name);

    void RegisterEcsTaskDefinition(EcsTaskDefinition definition);
    EcsTaskDefinition? GetEcsTaskDefinition(string family, int revision);
    IEnumerable<EcsTaskDefinition> GetEcsTaskDefinitionsByFamily(string family);
    IEnumerable<EcsTaskDefinition> GetAllEcsTaskDefinitions();
    void DeregisterEcsTaskDefinition(string family, int revision);

    void RegisterEcsTask(EcsTask task);
    EcsTask? GetEcsTask(string taskArn);
    IEnumerable<EcsTask> GetEcsTasksForCluster(string clusterArn);
    void UpdateEcsTask(EcsTask task);
    void DeleteEcsTask(string taskArn);

    // API Gateway routes
    void RegisterApiGatewayRoute(ApiGatewayRoute route);
    IEnumerable<ApiGatewayRoute> GetAllApiGatewayRoutes();

    // SNS
    void RegisterSnsTopic(SnsTopic topic);
    SnsTopic? GetSnsTopic(string nameOrArn);
    IEnumerable<SnsTopic> GetAllSnsTopics();
    void DeleteSnsTopic(string arn);

    // SSM Parameter Store
    void RegisterSsmParameter(SsmParameter parameter);
    SsmParameter? GetSsmParameter(string name);
    IEnumerable<SsmParameter> GetAllSsmParameters();
    void DeleteSsmParameter(string name);

    // DynamoDB
    void RegisterDynamoDbTable(DynamoDbTable table);
    DynamoDbTable? GetDynamoDbTable(string name);
    IEnumerable<DynamoDbTable> GetAllDynamoDbTables();
    void DeleteDynamoDbTable(string name);

    // Config
    string Region { get; set; }
    string AccountId { get; set; }
    string AccessKeyId { get; set; }
    string SecretAccessKey { get; set; }
    int Port { get; set; }
}
