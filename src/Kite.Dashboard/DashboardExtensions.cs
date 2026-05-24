using System.Reflection;
using Kite.Core;
using Kite.Dashboard.Components;
using Kite.EventBridge;
using Kite.Host;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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
        // Kite is a developer tool; set Development environment so that
        // static web assets from RCLs (_content/ paths) are served automatically.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        return emulator.UseHost(
            configureBuilder: builder =>
            {
                builder.Services.AddRazorComponents()
                    .AddInteractiveServerComponents();
                builder.Services.AddFluentUIComponents();
                builder.Services.AddSingleton<LambdaExecutor>();
                configureBuilder?.Invoke(builder);
            },
            configureApp: app =>
            {
                UseBlazorFrameworkFiles(app);
                // Serve _content/ paths (RCL static web assets) from the host's WebRootFileProvider.
                app.UseStaticFiles();
                app.UseAntiforgery();
                // MapStaticAssets serves _content/ and _framework/ paths from the static web assets manifest.
                // It must be registered before MapRazorComponents and before the catch-all route.
                // Pass the manifest path explicitly so it resolves from the Dashboard assembly's directory
                // rather than the entry assembly (which may be an Aspire AppHost).
                app.MapStaticAssets(GetDashboardManifestPath());
                app.MapRazorComponents<App>()
                    .AddInteractiveServerRenderMode();
            });
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
        // Set ASPNETCORE_ENVIRONMENT before creating the builder so that
        // UseStaticWebAssets() is enabled in the builder constructor.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        // Point ApplicationName + ContentRootPath at the Dashboard assembly so that
        // UseStaticWebAssets() (called internally for Development) discovers
        // Kite.Dashboard.staticwebassets.runtime.json instead of the
        // entry assembly's manifest (which would be the Aspire AppHost).
        var dashboardAssemblyDir = Path.GetDirectoryName(typeof(DashboardExtensions).Assembly.Location)!;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(DashboardExtensions).Assembly.GetName().Name,
            ContentRootPath = dashboardAssemblyDir
        });

        builder.Services.Configure<KestrelServerOptions>(opts =>
        {
            opts.ListenLocalhost(port);
        });

        builder.Services.AddSingleton(registry);
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddSingleton<LambdaExecutor>();
        builder.Services.AddSingleton<EventBridgeService>();

        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        // Serve _framework/ Blazor files (blazor.web.js, blazor.server.js) that are
        // not in the static assets manifest when the host is not a Blazor Web project.
        UseBlazorFrameworkFiles(app);
        // Serve root-level static files (download.js, app.css, etc.) from the Dashboard's
        // WebRootFileProvider which UseStaticWebAssets() populated from the runtime manifest.
        app.UseStaticFiles();
        // Also serve Dashboard's own files under the _content/Kite.Dashboard/ prefix
        // that App.razor references (the standard RCL static-asset path).
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = app.Environment.WebRootFileProvider,
            RequestPath = "/_content/Kite.Dashboard"
        });
        app.UseAntiforgery();
        // Pass the manifest path explicitly so it resolves from the Dashboard assembly's directory
        // rather than the entry assembly (which may be an Aspire AppHost).
        app.MapStaticAssets(GetDashboardManifestPath());
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync(ct);

        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetResult());
        app.Lifetime.ApplicationStopping.Register(() => tcs.TrySetResult());
        await tcs.Task;
        await app.StopAsync();
    }

    /// <summary>
    /// Returns the absolute path to the Dashboard assembly's static web assets endpoints
    /// manifest (<c>Kite.Dashboard.staticwebassets.endpoints.json</c>).
    /// This is passed to <c>MapStaticAssets</c> so it resolves from the Dashboard assembly's
    /// directory rather than the entry assembly (which may be an Aspire AppHost).
    /// </summary>
    private static string GetDashboardManifestPath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(DashboardExtensions).Assembly.Location)!;
        return Path.Combine(assemblyDir, "Kite.Dashboard.staticwebassets.endpoints.json");
    }

    private static void UseBlazorFrameworkFiles(WebApplication app)
    {
        var frameworkPath = FindBlazorFrameworkAssetsPath();
        if (frameworkPath is null)
        {
            var logger = app.Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?
                .CreateLogger(typeof(DashboardExtensions));
            logger?.LogWarning(
                "Could not locate Blazor framework assets (blazor.web.js). " +
                "The dashboard may not function correctly. " +
                "Ensure the ASP.NET Core SDK is installed and NuGet packages are restored.");
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(frameworkPath),
            RequestPath = "/_framework"
        });
    }

    /// <summary>
    /// Locates the Blazor framework assets directory in the NuGet global packages cache.
    /// The assets are provided by the microsoft.aspnetcore.app.internal.assets package whose
    /// version matches the current ASP.NET Core runtime.
    /// </summary>
    internal static string? FindBlazorFrameworkAssetsPath()
    {
        // Respect customised NuGet package locations via NUGET_PACKAGES env var
        var nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(nugetPackagesRoot))
        {
            nugetPackagesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
        }

        var packageDir = Path.Combine(nugetPackagesRoot, "microsoft.aspnetcore.app.internal.assets");

        if (!Directory.Exists(packageDir))
            return null;

        // Prefer a version matching the current ASP.NET Core runtime
        var runtimeVersion = typeof(WebApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0];

        if (runtimeVersion is not null)
        {
            var exactPath = Path.Combine(packageDir, runtimeVersion, "_framework");
            if (Directory.Exists(exactPath) && File.Exists(Path.Combine(exactPath, "blazor.web.js")))
                return exactPath;
        }

        // Fallback: pick the latest semantic version available
        var versions = Directory.GetDirectories(packageDir)
            .Select(d => (Dir: d, Version: ParseVersion(Path.GetFileName(d))))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version);

        foreach (var (dir, _) in versions)
        {
            var fwPath = Path.Combine(dir, "_framework");
            if (File.Exists(Path.Combine(fwPath, "blazor.web.js")))
                return fwPath;
        }

        return null;
    }

    private static Version? ParseVersion(string name)
    {
        // Strip pre-release suffixes (e.g. "10.0.5-preview.1" → "10.0.5")
        var dashIdx = name.IndexOf('-');
        var versionPart = dashIdx >= 0 ? name[..dashIdx] : name;
        return Version.TryParse(versionPart, out var v) ? v : null;
    }
}
