using Kite.Core;
using Kite.Dashboard.Components;
using Kite.EventBridge;
using Kite.Host;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Kite.Dashboard;

public static class DashboardExtensions
{
    /// <summary>
    /// Configures the Kite with a Blazor Server dashboard using Fluent UI components.
    /// The dashboard is accessible at the root path (/).
    /// </summary>
    public static Core.Kite UseDashboard(
        this Core.Kite emulator,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        return emulator.UseHost(
            configureBuilder: builder =>
            {
                AddDashboardServices(builder);
                configureBuilder?.Invoke(builder);
            },
            configureApp: MapDashboard);
    }

    /// <summary>
    /// Starts a standalone Blazor Server dashboard on the specified port.
    /// Used by the Aspire integration to run the dashboard separately from
    /// the emulator, so that it gets its own URL in the Aspire dashboard.
    /// </summary>
    /// <param name="registry">The shared service registry that provides emulator state.</param>
    /// <param name="port">The HTTP port the dashboard will listen on.</param>
    /// <param name="configureBuilder">Optional callback to customize the WebApplicationBuilder (e.g. add logging providers).</param>
    /// <param name="ct">Cancellation token to stop the dashboard.</param>
    public static async Task RunDashboardAsync(
        IServiceRegistry registry,
        int port,
        Action<WebApplicationBuilder>? configureBuilder = null,
        CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.Configure<KestrelServerOptions>(opts =>
        {
            opts.ListenLocalhost(port);
        });

        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<EventBridgeService>();
        AddDashboardServices(builder);

        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        MapDashboard(app);

        await app.StartAsync(ct);

        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetResult());
        app.Lifetime.ApplicationStopping.Register(() => tcs.TrySetResult());
        await tcs.Task;
        await app.StopAsync();
    }

    private static void AddDashboardServices(WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddSingleton<LambdaExecutor>();
        builder.Environment.AddDashboardModulesManifest();
    }

    private static void MapDashboard(WebApplication app)
    {
        app.UseAntiforgery();
        // Static files are served as endpoints (not UseStaticFiles) so they win over the host's catch-all route.
        app.MapDashboardAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
