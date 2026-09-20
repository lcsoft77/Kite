# Packaging

Kite ships as a set of NuGet packages so applications can reference the emulator
instead of the source tree. This page covers the package layout, how to build the
packages locally, and how a release is published.

## Packages

| Package | Contents |
|---------|----------|
| `Kite.Core` | Abstractions, in-memory service registry, `KiteBuilder` |
| `Kite.Lambda` | In-process Lambda execution and endpoints |
| `Kite.ApiGateway` | Route matching and Lambda proxy integration |
| `Kite.SQS` | SQS emulation and SQS → Lambda triggers |
| `Kite.S3` | S3 emulation and S3 → Lambda triggers |
| `Kite.DynamoDB` | DynamoDB emulation |
| `Kite.SNS` | SNS emulation |
| `Kite.EventBridge` | EventBridge emulation and EventBridge → Lambda triggers |
| `Kite.SSM` | Parameter Store emulation |
| `Kite.ECS` | ECS emulation |
| `Kite.Host` | ASP.NET Core host exposing every service on one endpoint |
| `Kite.Dashboard` | Blazor Server monitoring UI |
| `Kite.Aspire` | .NET Aspire hosting integration |

Most applications reference a single package:

```bash
# Standalone emulator
dotnet add package Kite.Host

# Inside a .NET Aspire AppHost
dotnet add package Kite.Aspire
```

`Kite.Host` transitively brings in every emulated service, and `Kite.Aspire` brings
in `Kite.Host` and `Kite.Dashboard`. The per-service packages are there for anyone
who wants to compose a narrower dependency set by hand.

## Build configuration

Shared build and package settings live in two files rather than in each project:

- `Directory.Build.props` (repo root) — target framework, nullable settings, the
  shared package metadata (version, authors, license, repository URL) and Source Link.
  It also sets `IsPackable=false` so nothing is published by accident.
- `src/Directory.Build.props` — opts every library under `src/` back into packing,
  turns on XML documentation and symbol packages, and adds `LICENSE` and `README.md`
  to each package.

To change the version for a local build, override the property:

```bash
dotnet pack Kite.slnx --configuration Release -p:Version=0.2.0-preview.1 --output ./artifacts
```

## Releasing

`.github/workflows/release.yml` publishes to nuget.org. It runs on a `v*` tag and
derives the package version from the tag (`v1.2.3` → `1.2.3`):

```bash
git tag v0.1.0
git push origin v0.1.0
```

It can also be started manually from the Actions tab, where the version is entered
by hand and pushing to nuget.org is opt-in — useful for validating the packages
before a real release.

Publishing requires a `NUGET_API_KEY` repository secret. The workflow fails with a
clear error if it is missing rather than silently skipping the push.

`.github/workflows/build.yml` also runs `dotnet pack` on every push and pull request,
so packaging problems surface immediately instead of at release time.

## Known limitation: dashboard assets in published applications

`Kite.Dashboard` serves the Blazor framework scripts (`blazor.web.js`,
`blazor.server.js`) that the Razor SDK does not bundle into a Razor class library.
At runtime `DashboardExtensions.FindBlazorFrameworkAssetsPath()` looks for them in
two places, in order:

1. an `_framework` directory next to the application;
2. the NuGet global packages cache, under
   `microsoft.aspnetcore.app.internal.assets`.

The second path is why `Microsoft.AspNetCore.App.Internal.Assets` is a public
dependency of `Kite.Dashboard` rather than a private one: consumers need it in
their restore graph for the lookup to succeed.

This covers the normal development workflow (`dotnet run` on a machine that has
restored the package). It does **not** cover an application published to a machine
with no NuGet cache — there the dashboard logs a warning and loads without its
scripts. The workaround today is to copy the package's `_framework` directory next
to the published application, which the first lookup path picks up. Bundling those
files into the package itself would remove the limitation.

Kite is a development-time tool, so this mainly affects publishing the emulator
into a container image.

## Licensing note

Kite is licensed under the **GNU GPL v3**, and the packages declare it by shipping
the `LICENSE` file. GPL-3.0 is a copyleft license: an application that references
these packages is expected to be distributed under GPL-3.0 as well. That is a
significant constraint for a library distributed on nuget.org, and many
organizations block GPL dependencies by policy.

If the project ever moves to a permissive license, the change is confined to the
`LICENSE` file and the `PackageLicenseFile` property in `Directory.Build.props`
(which would become, for example, `<PackageLicenseExpression>MIT</PackageLicenseExpression>`).
