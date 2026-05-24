using Aspire.Hosting.ApplicationModel;

namespace Kite.Aspire;

/// <summary>
/// Represents an SNS topic registered in the Kite as an Aspire child resource.
/// </summary>
public sealed class SnsTopicResource(string name, KiteResource parent)
    : Resource(name), IResourceWithParent<KiteResource>
{
    /// <inheritdoc/>
    public KiteResource Parent => parent;
}
