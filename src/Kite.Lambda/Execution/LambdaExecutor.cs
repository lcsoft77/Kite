using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kite.Lambda.Execution;

public class LambdaExecutor
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<LambdaExecutor> _logger;

    public LambdaExecutor(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<LambdaExecutor>();
    }

    public void ValidateAndLoad(LambdaFunction function)
    {
        if (string.IsNullOrEmpty(function.DllPath))
        {
            _logger.LogWarning("Lambda {Name} has no DLL path configured - skipping validation", function.Name);
            return;
        }

        if (!File.Exists(function.DllPath))
        {
            _logger.LogWarning("Lambda {Name} DLL not found at {Path} - skipping validation", function.Name, function.DllPath);
            return;
        }

        var context = new IsolatedLambdaContext(function.DllPath);
        var assembly = context.LoadFromAssemblyPath(function.DllPath);

        var (typeName, methodName) = ParseHandler(function.Handler);
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Handler type '{typeName}' not found in assembly '{function.DllPath}'");

        var method = FindHandlerMethod(type, methodName)
            ?? throw new InvalidOperationException($"Handler method '{methodName}' not found on type '{typeName}'");

        function.AssemblyLoadContext = context;
        _logger.LogInformation("Lambda {Name} loaded successfully (handler: {Handler})", function.Name, function.Handler);
    }

    public async Task<string> InvokeAsync(string functionName, string inputJson, CancellationToken ct = default)
    {
        using var activity = KiteActivitySource.Lambda.StartActivity("lambda.invoke");
        activity?.SetTag("function.name", functionName);

        var function = _registry.GetLambda(functionName)
            ?? throw new InvalidOperationException($"Lambda function '{functionName}' not found");

        var context = new LambdaContextImpl(
            functionName,
            _registry.Region,
            _registry.AccountId,
            function.MemoryMb,
            function.Timeout,
            _logger);

        activity?.SetTag("aws.request_id", context.AwsRequestId);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(function.Timeout);

        // Acquire the per-function lock to prevent concurrent restarts from swapping
        // the AssemblyLoadContext while an invocation is in progress.
        // Use the original ct (not cts.Token) so the function timeout only applies
        // to the execution itself, not to waiting for the lock.
        await function.FunctionLock.WaitAsync(ct);
        try
        {
            var result = await ExecuteHandlerAsync(function, inputJson, context, cts.Token);
            activity?.SetTag("outcome", "success");
            LogHelpers.AddRequestLog(function.RequestLog, "Invoke", $"RequestId={context.AwsRequestId}", 200);
            function.NotifyChange();
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("outcome", "error");
            LogHelpers.AddRequestLog(function.RequestLog, "Invoke", $"RequestId={context.AwsRequestId} Error={ex.Message}", 500);
            function.NotifyChange();
            _logger.LogError(ex, "Lambda {Name} invocation failed (RequestId: {RequestId})", functionName, context.AwsRequestId);
            throw;
        }
        finally
        {
            function.FunctionLock.Release();
        }
    }

    private async Task<string> ExecuteHandlerAsync(LambdaFunction function, string inputJson, ILambdaContext context, CancellationToken ct)
    {
        if (function.AssemblyLoadContext is null)
        {
            _logger.LogWarning("Lambda {Name} has no loaded assembly context, returning empty response", function.Name);
            return "{}";
        }

        var assembly = function.AssemblyLoadContext.Assemblies
            .FirstOrDefault(a => !a.IsDynamic && a.Location == function.DllPath);

        if (assembly is null)
        {
            // Try loading from path
            assembly = function.AssemblyLoadContext.LoadFromAssemblyPath(function.DllPath);
        }

        var (typeName, methodName) = ParseHandler(function.Handler);
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Handler type '{typeName}' not found");

        var method = FindHandlerMethod(type, methodName)
            ?? throw new InvalidOperationException($"Handler method '{methodName}' not found on type '{typeName}'");

        var instance = Activator.CreateInstance(type);
        var parameters = method.GetParameters();

        // Capture stdout to logger
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        using var outWriter = new LoggingTextWriter(_logger, context.AwsRequestId);
        Console.SetOut(outWriter);
        Console.SetError(outWriter);

        try
        {
            object? result;
            if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(Stream))
            {
                var inputBytes = Encoding.UTF8.GetBytes(inputJson);
                using var stream = new MemoryStream(inputBytes);
                result = parameters.Length >= 2
                    ? method.Invoke(instance, new object[] { stream, context })
                    : method.Invoke(instance, new object[] { stream });
            }
            else
            {
                var inputType = parameters.Length >= 1 ? parameters[0].ParameterType : typeof(object);
                var input = JsonSerializer.Deserialize(inputJson, inputType);

                result = parameters.Length >= 2
                    ? method.Invoke(instance, new object?[] { input, context })
                    : parameters.Length == 1
                        ? method.Invoke(instance, new object?[] { input })
                        : method.Invoke(instance, Array.Empty<object>());
            }

            if (result is Task task)
            {
                await task.WaitAsync(ct);
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var resultProp = taskType.GetProperty("Result");
                    result = resultProp?.GetValue(task);
                }
                else
                {
                    return "null";
                }
            }

            if (result is null)
                return "null";

            if (result is string str)
                return str;

            if (result is Stream outputStream)
            {
                using var reader = new StreamReader(outputStream);
                return await reader.ReadToEndAsync(ct);
            }

            return JsonSerializer.Serialize(result);
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    public static (string TypeName, string MethodName) ParseHandler(string handler)
    {
        // Format: "Assembly::Namespace.Class::Method"
        var parts = handler.Split("::");
        if (parts.Length < 3)
            throw new InvalidOperationException($"Invalid handler format: '{handler}'. Expected 'Assembly::TypeName::MethodName'");

        return (parts[1], parts[2]);
    }

    private static MethodInfo? FindHandlerMethod(Type type, string methodName)
    {
        return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private sealed class LoggingTextWriter : TextWriter
    {
        private readonly ILogger _logger;
        private readonly string _requestId;
        private readonly StringBuilder _buffer = new();

        public LoggingTextWriter(ILogger logger, string requestId)
        {
            _logger = logger;
            _requestId = requestId;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                _logger.LogInformation("[Lambda:{RequestId}] {Line}", _requestId, _buffer.ToString());
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _buffer.Length > 0)
            {
                _logger.LogInformation("[Lambda:{RequestId}] {Line}", _requestId, _buffer.ToString());
                _buffer.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
