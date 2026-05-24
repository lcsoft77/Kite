using Kite.ApiGateway;
using Kite.Core;
using Kite.Core.Auth;
using OpenTelemetry;
using Kite.Core.Routing;
using Kite.DynamoDB;
using Kite.ECS;
using Kite.EventBridge;
using Kite.Lambda.Endpoints;
using Kite.Lambda.Execution;
using Kite.S3;
using Kite.SNS;
using Kite.SQS;
using Kite.SSM;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kite.Host;

public static class HostExtensions
{
    private static readonly object _localStackHealthPayload = new
    {
        services = new Dictionary<string, string>
        {
            ["sqs"] = "running",
            ["s3"] = "running",
            ["lambda"] = "running",
            ["events"] = "running",
            ["ssm"] = "running",
            ["ecs"] = "running",
            ["apigateway"] = "running"
        },
        version = KiteActivitySource.RootName
    };

     public static Core.Kite UseHost(this Core.Kite emulator,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? configureApp = null,
        Action<WebApplication>? configureEarlyMiddleware = null,
        string? serviceName = null,
        string? otlpEndpoint = null,
        string? otlpProtocol = null,
        string? otlpApiKey = null)
    {
        emulator.RunAsyncImpl = async ct =>
        {
            var registry = emulator.Registry;

            var builder = WebApplication.CreateBuilder();

            // Override URL via Kestrel options
            builder.Services.Configure<KestrelServerOptions>(opts =>
            {
                opts.ListenAnyIP(registry.Port);
            });

            // Register services
            builder.Services.AddSingleton<IServiceRegistry>(registry);
            builder.Services.AddSingleton<LambdaExecutor>(sp => new LambdaExecutor(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.LambdaName)));
            builder.Services.AddSingleton<SqsService>(sp => new SqsService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.SQSName)));
            builder.Services.AddSingleton<S3Service>(sp => new S3Service(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredService<LambdaExecutor>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.S3Name)));
            builder.Services.AddSingleton<EventBridgeService>(sp => new EventBridgeService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredService<LambdaExecutor>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.EventBridgeName)));
            builder.Services.AddSingleton<ApiGatewayService>(sp => new ApiGatewayService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredService<LambdaExecutor>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.ApiGatewayName)));
            builder.Services.AddSingleton<SsmService>(sp => new SsmService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.SSMName)));
            builder.Services.AddSingleton<EcsService>(sp => new EcsService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.ECSName)));
            builder.Services.AddSingleton<SnsService>(sp => new SnsService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.SNSName)));
            builder.Services.AddSingleton<DynamoDbService>(sp => new DynamoDbService(
                sp.GetRequiredService<IServiceRegistry>(),
                sp.GetRequiredKeyedService<ILoggerFactory>(KiteActivitySource.DynamoDBName)));

            // Background services
            builder.Services.AddHostedService<SqsEsmPoller>();
            builder.Services.AddHostedService<SqsVisibilityTimeoutChecker>();

            var resolvedServiceName = serviceName
                ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                ?? KiteActivitySource.RootName;

            var resolvedOtlpEndpoint = otlpEndpoint
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

            var resolvedOtlpProtocol = ResolveOtlpProtocol(
                    otlpProtocol ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"));

            var exporterEndpoint = string.IsNullOrWhiteSpace(resolvedOtlpEndpoint)
                ? null
                : resolvedOtlpEndpoint;

            var exporterApiKey = string.IsNullOrWhiteSpace(otlpApiKey) ? null : otlpApiKey;

            var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(resolvedServiceName));

            openTelemetryBuilder.WithTracing(t =>
            {
                // Only instrument ASP.NET Core infrastructure spans (health, lambda endpoints).
                // The anonymous "{**path}" catch-all span is suppressed here because each AWS
                // service emits its own named activity via a dedicated per-service TracerProvider.
                t.AddAspNetCoreInstrumentation(opts =>
                    opts.Filter = ctx => ctx.GetEndpoint()?.DisplayName is not "{**path}");

                if (exporterEndpoint is not null)
                {
                    t.AddOtlpExporter(opt => ConfigureOtlpExporter(opt, exporterEndpoint, resolvedOtlpProtocol, exporterApiKey));
                }
            });

            openTelemetryBuilder.WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddMeter($"{KiteActivitySource.RootName}.*");

                if (exporterEndpoint is not null)
                {
                    m.AddOtlpExporter(opt => ConfigureOtlpExporter(opt, exporterEndpoint, resolvedOtlpProtocol, exporterApiKey));
                }
            });


            builder.Logging.AddOpenTelemetry(o =>
            {
                o.IncludeFormattedMessage = true;
                o.IncludeScopes = true;

                if (exporterEndpoint is not null)
                {
                    o.AddOtlpExporter(opt => ConfigureOtlpExporter(opt, exporterEndpoint, resolvedOtlpProtocol, exporterApiKey));
                }
            });

            // Register a per-service ILoggerFactory (keyed by service name) so each AWS service
            // can emit logs under its own OTLP resource.name in Aspire/OTLP dashboards.
            foreach (var svcName in new[]
            {
                KiteActivitySource.SQSName,
                KiteActivitySource.S3Name,
                KiteActivitySource.EventBridgeName,
                KiteActivitySource.LambdaName,
                KiteActivitySource.ECSName,
                KiteActivitySource.SNSName,
                KiteActivitySource.DynamoDBName,
                KiteActivitySource.SSMName,
                KiteActivitySource.ApiGatewayName,
            })
            {
                var capturedName = svcName;
                builder.Services.AddKeyedSingleton<ILoggerFactory>(capturedName, (sp, _) =>
                    exporterEndpoint is not null
                        ? CreateForComponent(capturedName, exporterEndpoint, resolvedOtlpProtocol, exporterApiKey)
                        : sp.GetRequiredService<ILoggerFactory>());
            }

            // Allow external customisation (e.g. Aspire logger provider injection)
            configureBuilder?.Invoke(builder);

            var app = builder.Build();

            // Validate and load lambdas
            var executor = app.Services.GetRequiredService<LambdaExecutor>();
            var logger = app.Services.GetRequiredService<ILogger<Core.Kite>>();

            foreach (var lambda in registry.GetAllLambdas())
            {
                try
                {
                    executor.ValidateAndLoad(lambda);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load Lambda {Name}", lambda.Name);
                }
            }

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new
            {
                Status = "Healthy",
                Service = KiteActivitySource.RootName,
                Timestamp = DateTimeOffset.UtcNow
            }));

            // LocalStack-compatible health endpoint
            app.MapGet("/_localstack/health", () => Results.Ok(_localStackHealthPayload));

            // SigV4 middleware
            app.Use(async (context, next) =>
            {
                var authHeader = context.Request.Headers.TryGetValue("Authorization", out var auth)
                    ? auth.ToString() : null;

                if (!string.IsNullOrEmpty(authHeader))
                {
                    if (!SigV4Validator.Validate(authHeader, registry.AccessKeyId))
                    {
                        logger.LogWarning("Invalid SigV4 credentials from {RemoteIp}", context.Connection.RemoteIpAddress);
                        // Allow through anyway for dev convenience - just log the warning
                    }
                }

                await next(context);
            });

            // Routing middleware
            configureEarlyMiddleware?.Invoke(app);
            app.UseMiddleware<UnifiedRouter>();

            // Allow external customisation before the AWS catch-all route (e.g. dashboard Blazor components)
            configureApp?.Invoke(app);

            // Lambda endpoints
            app.MapLambdaEndpoints();

            // Catch-all route for SQS, S3, EventBridge, ApiGateway
            app.Map("{**path}", async (HttpContext context) =>
            {
                var service = context.Items.TryGetValue("aws-service", out var svc) ? svc?.ToString() : "apigateway";

                switch (service)
                {
                    case "sqs":
                        var sqsSvc = context.RequestServices.GetRequiredService<SqsService>();
                        await sqsSvc.HandleAsync(context);
                        break;
                    case "s3":
                        var s3Svc = context.RequestServices.GetRequiredService<S3Service>();
                        await s3Svc.HandleAsync(context);
                        break;
                    case "eventbridge":
                        var ebSvc = context.RequestServices.GetRequiredService<EventBridgeService>();
                        await ebSvc.HandleAsync(context);
                        break;
                    case "ssm":
                        var ssmSvc = context.RequestServices.GetRequiredService<SsmService>();
                        await ssmSvc.HandleAsync(context);
                        break;
                    case "ecs":
                        var ecsSvc = context.RequestServices.GetRequiredService<EcsService>();
                        await ecsSvc.HandleAsync(context);
                        break;
                    case "sns":
                        var snsSvc = context.RequestServices.GetRequiredService<SnsService>();
                        await snsSvc.HandleAsync(context);
                        break;
                    case "dynamodb":
                        var dynamoDbSvc = context.RequestServices.GetRequiredService<DynamoDbService>();
                        await dynamoDbSvc.HandleAsync(context);
                        break;
                    default:
                        var agSvc = context.RequestServices.GetRequiredService<ApiGatewayService>();
                        await agSvc.HandleAsync(context);
                        break;
                }
            });

            // Per-service TracerProviders: each AWS service reports its activities under its
            // own service.name (e.g. "Kite.SQS") so traces are separated in Aspire/OTLP.
            var perServiceProviders = new List<TracerProvider?>();
            if (exporterEndpoint is not null)
            {
                foreach (var svc in KiteActivitySource.ServiceSuffixes)
                {
                    var svcName = $"{KiteActivitySource.RootName}.{svc}";
                    perServiceProviders.Add(
                        Sdk.CreateTracerProviderBuilder()
                            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(svcName))
                            .AddSource(svcName)
                            .AddOtlpExporter(opt => ConfigureOtlpExporter(opt, exporterEndpoint, resolvedOtlpProtocol, exporterApiKey))
                            .Build());
                }
            }

            await app.StartAsync(ct);
            // Wait until the cancellation token is triggered or app stops
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetResult());
            app.Lifetime.ApplicationStopping.Register(() => tcs.TrySetResult());
            await tcs.Task;
            await app.StopAsync();

            foreach (var tp in perServiceProviders)
                tp?.Dispose();
        };

        return emulator;
    }

    private static ILoggerFactory CreateForComponent(
        string componentName,
        string? otlpEndpoint,
        OtlpExportProtocol? otlpProtocol,
        string? exporterApiKey)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(
                    ResourceBuilder.CreateEmpty()
                        .AddService(componentName)
                        .AddTelemetrySdk()
                );
                if (otlpEndpoint is not null)
                    options.AddOtlpExporter(opt => ConfigureOtlpExporter(opt, otlpEndpoint, otlpProtocol, exporterApiKey));
                options.IncludeFormattedMessage = true;
            });
        });
    }


    private static Uri CreateOtlpBaseUri(string endpoint)
    {
        if (endpoint.EndsWith('/'))
            return new Uri(endpoint);

        return new Uri($"{endpoint}/");
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions options,
        string endpoint,
        OtlpExportProtocol? protocol,
        string? apiKey = null)
    {
        if (protocol is not null)
        {
            options.Protocol = protocol.Value;
        }

        options.Endpoint = protocol == OtlpExportProtocol.HttpProtobuf
            ? CreateOtlpBaseUri(endpoint)
            : new Uri(endpoint);

        if (!string.IsNullOrEmpty(apiKey))
        {
            options.Headers = $"x-otlp-api-key={apiKey}";
        }

    }

    private static OtlpExportProtocol? ResolveOtlpProtocol(string? protocolValue) =>
        protocolValue?.Trim().ToLowerInvariant() switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => null
        };
}
