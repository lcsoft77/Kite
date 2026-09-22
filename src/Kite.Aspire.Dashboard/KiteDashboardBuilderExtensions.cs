using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Kite.Dashboard;

namespace Kite.Aspire;

/// <summary>
/// Dashboard extension methods for <see cref="KiteResource"/>.
/// </summary>
public static class KiteDashboardBuilderExtensions
{
    /// <summary>
    /// Enables the built-in Blazor Server dashboard. The dashboard runs on a separate port
    /// and its URL is shown in the Aspire dashboard's URL column.
    /// </summary>
    /// <param name="builder">The emulator resource builder.</param>
    /// <param name="port">The HTTP port for the dashboard. Defaults to the emulator port + 1.</param>
    public static IResourceBuilder<KiteResource> WithDashboard(
        this IResourceBuilder<KiteResource> builder,
        int? port = null)
    {
        var dashboardPort = port ?? builder.Resource.Port + 1;
        if (dashboardPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), dashboardPort, "Dashboard port must be between 1 and 65535.");

        builder.Resource.DashboardEnabled = true;
        builder.Resource.DashboardPort = dashboardPort;
        builder.Resource.DashboardRunner = DashboardExtensions.RunDashboardAsync;

        builder.WithHttpEndpoint(port: dashboardPort, name: "dashboard", isProxied: false);

        return builder;
    }
}
