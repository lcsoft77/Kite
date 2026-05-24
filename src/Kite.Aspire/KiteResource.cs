using Aspire.Hosting.ApplicationModel;

namespace Kite.Aspire;

/// <summary>
/// Represents the Kite as a .NET Aspire resource.
/// Add this to your Aspire App Host with <see cref="KiteBuilderExtensions.AddKite"/>.
/// </summary>
public sealed class KiteResource(string name) : Resource(name), IResourceWithConnectionString, IResourceWithEndpoints
{
    internal int Port { get; set; } = 4566;
    internal string Region { get; set; } = "us-east-1";
    internal string AccountId { get; set; } = "000000000000";
    internal string AccessKeyId { get; set; } = "test";
    internal string SecretAccessKey { get; set; } = "test";
    internal bool DashboardEnabled { get; set; }
    internal int DashboardPort { get; set; }

    internal List<LambdaFunctionResource> Lambdas { get; } = [];
    internal List<SqsQueueResource> SqsQueues { get; } = [];
    internal List<S3BucketResource> S3Buckets { get; } = [];
    internal List<EventBridgeBusResource> EventBridgeBuses { get; } = [];
    internal List<EcsClusterResource> EcsClusters { get; } = [];
    internal List<EcsTaskDefinitionResource> EcsTaskDefinitionResources { get; } = [];
    internal List<SnsTopicResource> SnsTopics { get; } = [];
    internal List<DynamoDbTableResource> DynamoDbTables { get; } = [];
    internal List<(string QueueName, string FunctionName, int BatchSize)> SqsTriggers { get; } = [];
    internal List<(string BucketName, string FunctionName, Kite.Core.S3EventType EventType)> S3Triggers { get; } = [];
    internal List<(string Name, string Value, string Type)> SsmParameters { get; } = [];
    internal List<(string Family, Action<Kite.Core.EcsTaskDefinitionBuilder>? Configure)> EcsTaskDefinitions { get; } = [];
    internal List<(string TableName, string PartitionKeyName, string PartitionKeyType, string? SortKeyName, string? SortKeyType)> DynamoDbTableDefinitions { get; } = [];
    internal List<(string RuleName, string BusName, List<string> Sources, List<string> DetailTypes, Dictionary<string, object> Details, List<string> TargetFunctions)> EventBridgeRuleDefinitions { get; } = [];

    /// <summary>
    /// The base HTTP URL of the emulator endpoint (e.g. <c>http://localhost:4566</c>).
    /// </summary>
    public string ServiceUrl => $"http://localhost:{Port}";

    /// <inheritdoc/>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"http://localhost:{Port.ToString()}");
}
