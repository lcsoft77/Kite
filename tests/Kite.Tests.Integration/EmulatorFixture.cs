using Amazon.EventBridge;
using Amazon.Lambda;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SQS;
using Kite.Core;
using Kite.Host;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// XUnit async-lifetime fixture that starts an in-process Kite instance
/// and exposes pre-configured AWS SDK clients pointed at it.
/// </summary>
public sealed class EmulatorFixture : IAsyncLifetime
{
    public const string AccessKey = "test";
    public const string SecretKey = "test";
    public const string Region = "us-east-1";

    public const string EchoFunctionName = "echo-fn";
    public const string SqsProcessorFunctionName = "sqs-processor-fn";
    public const string TriggerQueueName = "trigger-queue";

    public const string TestBucketName = "integration-test-bucket";
    public const string EventBridgeBusName = "integration-test-bus";
    public const string SsmParameterName = "/integration/config";
    public const string SsmParameterValue = "hello-integration";

    public int Port { get; } = FreeTcpPort();

    public string ServiceUrl => $"http://localhost:{Port}";
    public string SqsQueueUrl => $"{ServiceUrl}/000000000000/{TriggerQueueName}";

    private CancellationTokenSource? _cts;
    private Task? _emulatorTask;

    // AWS SDK clients pointed at the local emulator
    public AmazonLambdaClient LambdaClient { get; private set; } = null!;
    public AmazonSQSClient SqsClient { get; private set; } = null!;
    public AmazonS3Client S3Client { get; private set; } = null!;
    public AmazonEventBridgeClient EventBridgeClient { get; private set; } = null!;
    public AmazonSimpleSystemsManagementClient SsmClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();

        // Resolve the SampleLambda DLL path from the test assembly output directory.
        var sampleLambdaDll = Path.Combine(
            AppContext.BaseDirectory,
            "Kite.SampleLambda.dll");

        var emulator = KiteBuilder.Create()
            .WithPort(Port)
            .WithRegion(Region)
            .WithCredentials(AccessKey, SecretKey)
            .AddSQSQueue(TriggerQueueName)
            .AddLambda(EchoFunctionName, lb => lb
                .WithHandler($"Kite.SampleLambda::Kite.SampleLambda.EchoFunction::Handle")
                .WithDll(sampleLambdaDll)
                .WithMemory(128)
                .WithTimeout(TimeSpan.FromSeconds(30)))
            .AddLambda(SqsProcessorFunctionName, lb => lb
                .WithHandler($"Kite.SampleLambda::Kite.SampleLambda.SqsProcessorFunction::Handle")
                .WithDll(sampleLambdaDll)
                .WithMemory(128)
                .WithTimeout(TimeSpan.FromSeconds(30)))
            .AddSQSTrigger(TriggerQueueName, SqsProcessorFunctionName, batchSize: 1)
            .AddS3Bucket(TestBucketName)
            .AddEventBridgeBus(EventBridgeBusName)
            .AddSsmParameter(SsmParameterName, SsmParameterValue)
            .Build()
            .UseHost();

        _emulatorTask = emulator.RunAsync(_cts.Token);

        // Give the host a moment to start listening
        await WaitForEmulatorAsync();

        var credentials = new BasicAWSCredentials(AccessKey, SecretKey);

        LambdaClient = new AmazonLambdaClient(credentials, new AmazonLambdaConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region
        });

        SqsClient = new AmazonSQSClient(credentials, new AmazonSQSConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region
        });

        S3Client = new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
            ForcePathStyle = true
        });

        EventBridgeClient = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region
        });

        SsmClient = new AmazonSimpleSystemsManagementClient(credentials, new AmazonSimpleSystemsManagementConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region
        });
    }

    public async Task DisposeAsync()
    {
        LambdaClient?.Dispose();
        SqsClient?.Dispose();
        S3Client?.Dispose();
        EventBridgeClient?.Dispose();
        SsmClient?.Dispose();

        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_emulatorTask is not null)
        {
            try { await _emulatorTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
    }

    private async Task WaitForEmulatorAsync()
    {
        using var httpClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await httpClient.GetAsync($"{ServiceUrl}/2015-03-31/functions");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Not ready yet
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException($"Emulator did not start on port {Port} within 15 seconds.");
    }

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
