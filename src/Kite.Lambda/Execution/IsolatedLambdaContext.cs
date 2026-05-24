using System.Reflection;
using System.Runtime.Loader;

namespace Kite.Lambda.Execution;

public class IsolatedLambdaContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public IsolatedLambdaContext(string dllPath) : base(name: Path.GetFileNameWithoutExtension(dllPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(dllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Allow Amazon.Lambda.Core to be resolved from the default context so that
        // ILambdaContext from the host is type-compatible with the loaded handler.
        if (assemblyName.Name == "Amazon.Lambda.Core")
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
