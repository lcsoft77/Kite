using System.Diagnostics;

namespace Kite.Core;

public static class KiteActivitySource
{
    /// <summary>
    /// Single source-of-truth for the emulator's telemetry name prefix.
    /// All ActivitySource names and OTLP service.name values are derived from this constant.
    /// </summary>
    public const string RootName = "Kite";

    public const string SQSName         = RootName + ".SQS";
    public const string S3Name          = RootName + ".S3";
    public const string EventBridgeName = RootName + ".EventBridge";
    public const string LambdaName      = RootName + ".Lambda";
    public const string ECSName         = RootName + ".ECS";
    public const string SNSName         = RootName + ".SNS";
    public const string DynamoDBName    = RootName + ".DynamoDB";
    public const string SSMName         = RootName + ".SSM";
    public const string ApiGatewayName  = RootName + ".ApiGateway";

    public static readonly ActivitySource SQS         = new($"{RootName}.SQS",         "1.0.0");
    public static readonly ActivitySource S3          = new($"{RootName}.S3",          "1.0.0");
    public static readonly ActivitySource EventBridge = new($"{RootName}.EventBridge", "1.0.0");
    public static readonly ActivitySource Lambda      = new($"{RootName}.Lambda",      "1.0.0");
    public static readonly ActivitySource ECS         = new($"{RootName}.ECS",         "1.0.0");
    public static readonly ActivitySource SNS         = new($"{RootName}.SNS",         "1.0.0");
    public static readonly ActivitySource DynamoDB    = new($"{RootName}.DynamoDB",    "1.0.0");
    public static readonly ActivitySource SSM         = new($"{RootName}.SSM",         "1.0.0");
    public static readonly ActivitySource ApiGateway  = new($"{RootName}.ApiGateway",  "1.0.0");

    /// <summary>Service suffixes used when registering per-service TracerProviders.</summary>
    internal static readonly string[] ServiceSuffixes =
        ["SQS", "S3", "EventBridge", "Lambda", "ECS", "SNS", "DynamoDB", "SSM", "ApiGateway"];
}
