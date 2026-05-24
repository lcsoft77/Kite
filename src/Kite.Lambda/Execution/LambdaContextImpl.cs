using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging;

namespace Kite.Lambda.Execution;

internal class LambdaLoggerAdapter : ILambdaLogger
{
    private readonly ILogger _logger;
    private readonly string _requestId;

    public LambdaLoggerAdapter(ILogger logger, string requestId)
    {
        _logger = logger;
        _requestId = requestId;
    }

    public void Log(string message) => _logger.LogInformation("[{RequestId}] {Message}", _requestId, message);
    public void LogLine(string message) => _logger.LogInformation("[{RequestId}] {Message}", _requestId, message);
}

public class LambdaContextImpl : ILambdaContext
{
    private readonly DateTime _deadline;
    private readonly ILogger _logger;

    public string AwsRequestId { get; }
    public IClientContext? ClientContext => null;
    public string FunctionName { get; }
    public string FunctionVersion => "$LATEST";
    public ICognitoIdentity? Identity => null;
    public string InvokedFunctionArn { get; }
    public ILambdaLogger Logger { get; }
    public string LogGroupName { get; }
    public string LogStreamName { get; }
    public int MemoryLimitInMB { get; }
    public TimeSpan RemainingTime => _deadline - DateTime.UtcNow;

    public LambdaContextImpl(string functionName, string region, string accountId, int memoryMb, TimeSpan timeout, ILogger logger)
    {
        AwsRequestId = Guid.NewGuid().ToString();
        FunctionName = functionName;
        InvokedFunctionArn = $"arn:aws:lambda:{region}:{accountId}:function:{functionName}";
        LogGroupName = $"/aws/lambda/{functionName}";
        LogStreamName = $"{DateTime.UtcNow:yyyy/MM/dd}/[$LATEST]{AwsRequestId}";
        MemoryLimitInMB = memoryMb;
        _deadline = DateTime.UtcNow.Add(timeout);
        _logger = logger;
        Logger = new LambdaLoggerAdapter(logger, AwsRequestId);
    }
}
