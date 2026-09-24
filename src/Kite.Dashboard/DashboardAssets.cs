using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Kite.Dashboard;

/// <summary>
/// Serves the dashboard's static files (its own CSS/JS, the Fluent UI assets and blazor.web.js),
/// which are embedded in this assembly at build time (see the EmbeddedResource items in the csproj).
/// </summary>
/// <remarks>
/// Files are exposed as literal endpoints rather than through <c>UseStaticFiles</c>: the Kite host
/// registers a catch-all route for the AWS APIs, and the static file middleware skips every request
/// that already matched an endpoint. A literal route always wins over a catch-all.
/// </remarks>
internal static class DashboardAssets
{
    private const string ResourcePrefix = "kite-assets/";

    /// <summary>
    /// The Fluent UI web components are defined by a Blazor JS initializer. Blazor learns about
    /// initializers from a "{ApplicationName}.modules.json" file in the web root.
    /// </summary>
    private const string ModulesManifestSuffix = ".modules.json";

    private const string ModulesManifestContent =
        "[\"_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js\"]";

    private static readonly Assembly Assembly = typeof(DashboardAssets).Assembly;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    // Files only change when the assembly does, so its MVID is a valid validator for all of them.
    private static readonly EntityTagHeaderValue ETag = new($"\"{Assembly.ManifestModule.ModuleVersionId:N}\"");

    /// <summary>Maps one GET/HEAD endpoint per embedded static file.</summary>
    public static IEndpointRouteBuilder MapDashboardAssets(this IEndpointRouteBuilder endpoints)
    {
        foreach (var resource in Assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            var path = resource[ResourcePrefix.Length..].Replace('\\', '/');
            if (!ContentTypes.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            var resourceName = resource;
            endpoints.MapMethods("/" + path, ["GET", "HEAD"], (HttpContext context) =>
            {
                // no-cache = always revalidate; the ETag makes that a cheap 304.
                context.Response.Headers.CacheControl = "no-cache";
                return Results.Stream(Assembly.GetManifestResourceStream(resourceName)!, contentType, entityTag: ETag);
            });
        }

        return endpoints;
    }

    /// <summary>
    /// Makes the web root answer for "{ApplicationName}.modules.json", so Blazor loads the Fluent UI
    /// initializer even though no static web asset manifest exists.
    /// </summary>
    public static void AddDashboardModulesManifest(this IWebHostEnvironment environment)
    {
        environment.WebRootFileProvider = new CompositeFileProvider(
            environment.WebRootFileProvider,
            new ModulesManifestFileProvider());
    }

    private sealed class ModulesManifestFileProvider : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath)
        {
            var name = subpath.TrimStart('/');
            return name.EndsWith(ModulesManifestSuffix, StringComparison.OrdinalIgnoreCase) && !name.Contains('/')
                ? new ModulesManifestFileInfo(name)
                : new NotFoundFileInfo(name);
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class ModulesManifestFileInfo(string name) : IFileInfo
    {
        private static readonly byte[] Content = Encoding.UTF8.GetBytes(ModulesManifestContent);

        public bool Exists => true;
        public long Length => Content.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(Content, writable: false);
    }
}
