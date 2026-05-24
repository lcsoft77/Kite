using Aspire.Hosting.ApplicationModel;

namespace Kite.Aspire;

/// <summary>
/// Represents a DynamoDB table registered in the Kite as an Aspire child resource.
/// </summary>
public sealed class DynamoDbTableResource(string name, KiteResource parent)
    : Resource(name), IResourceWithParent<KiteResource>
{
    /// <inheritdoc/>
    public KiteResource Parent => parent;
}
