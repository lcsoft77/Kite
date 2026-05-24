namespace Kite.Core;

public class Kite
{
    public IServiceRegistry Registry { get; }

    // This is set by Host when building. Allows the Host project to inject
    // the RunAsync implementation without circular dependencies.
    internal Func<CancellationToken, Task>? RunAsyncImpl { get; set; }

    internal Kite(IServiceRegistry registry)
    {
        Registry = registry;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (RunAsyncImpl is null)
            throw new InvalidOperationException(
                "RunAsync is not configured. Use Kite.Host to create and run the emulator.");
        await RunAsyncImpl(ct);
    }
}
