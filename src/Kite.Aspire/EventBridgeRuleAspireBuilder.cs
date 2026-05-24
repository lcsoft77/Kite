namespace Kite.Aspire;

/// <summary>
/// A fluent builder for configuring an EventBridge rule inside the Aspire App Host.
/// </summary>
public sealed class EventBridgeRuleAspireBuilder
{
    internal string BusName { get; private set; } = "default";
    internal List<string> Sources { get; } = [];
    internal List<string> DetailTypes { get; } = [];
    internal Dictionary<string, object> Details { get; } = [];
    internal List<string> TargetFunctions { get; } = [];

    /// <summary>Sets the EventBridge bus this rule listens on.</summary>
    public EventBridgeRuleAspireBuilder OnBus(string busName) { BusName = busName; return this; }

    /// <summary>Filters events by the given source.</summary>
    public EventBridgeRuleAspireBuilder MatchSource(string source) { Sources.Add(source); return this; }

    /// <summary>Filters events by the given detail-type.</summary>
    public EventBridgeRuleAspireBuilder MatchDetailType(string detailType) { DetailTypes.Add(detailType); return this; }

    /// <summary>Filters events by a field inside the event detail JSON object.</summary>
    public EventBridgeRuleAspireBuilder MatchDetail(string fieldName, object value) { Details[fieldName] = value; return this; }

    /// <summary>Routes matching events to the specified Lambda function.</summary>
    public EventBridgeRuleAspireBuilder TargetLambda(string functionName) { TargetFunctions.Add(functionName); return this; }
}
