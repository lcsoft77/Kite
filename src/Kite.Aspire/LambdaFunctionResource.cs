using Aspire.Hosting.ApplicationModel;

namespace Kite.Aspire;

/// <summary>
/// Represents a Lambda function registered in the Kite as an Aspire child resource.
/// </summary>
public sealed class LambdaFunctionResource(string name, KiteResource parent)
    : Resource(name), IResourceWithParent<KiteResource>
{
    /// <inheritdoc/>
    public KiteResource Parent => parent;

    internal string DllPath { get; set; } = string.Empty;
    internal string Handler { get; set; } = string.Empty;
}
