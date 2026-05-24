using System.Runtime.Loader;
using System.Threading;
using Kite.Core.Models;

namespace Kite.Core.Models;

public class LambdaFunction : IDisposable
{
    public string Name { get; set; } = string.Empty;
    public string Handler { get; set; } = string.Empty;
    public string DllPath { get; set; } = string.Empty;
    public int MemoryMb { get; set; } = 128;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public AssemblyLoadContext? AssemblyLoadContext { get; set; }
    public List<RequestLogEntry> RequestLog { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();

    /// <summary>
    /// Per-function exclusive lock used to coordinate Lambda invocations and restarts,
    /// preventing a restart from unloading the assembly while an invocation is in progress.
    /// </summary>
    public SemaphoreSlim FunctionLock { get; } = new(1, 1);

    public void Dispose()
    {
        FunctionLock.Dispose();
    }
}
