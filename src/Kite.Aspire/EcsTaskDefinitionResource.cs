using Aspire.Hosting.ApplicationModel;

namespace Kite.Aspire;

/// <summary>
/// Represents an ECS task definition registered in the Kite as an Aspire child resource.
/// Its state reflects the lifecycle of the most-recent running task instance:
/// Waiting → Running → Finished.
/// </summary>
public sealed class EcsTaskDefinitionResource(string name, KiteResource parent)
    : Resource(name), IResourceWithParent<KiteResource>
{
    /// <inheritdoc/>
    public KiteResource Parent => parent;

    /// <summary>The ECS task definition family name.</summary>
    internal string Family { get; init; } = name;

    /// <summary>The ECS cluster this task definition is associated with (informational).</summary>
    internal string ClusterName { get; init; } = string.Empty;
}
