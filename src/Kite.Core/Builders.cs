using Kite.Core.Models;

namespace Kite.Core;

public enum S3EventType
{
    ObjectCreated,
    ObjectRemoved
}

public class LambdaBuilder
{
    internal string Handler { get; private set; } = string.Empty;
    internal string DllPath { get; private set; } = string.Empty;
    internal int MemoryMb { get; private set; } = 128;
    internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

    public LambdaBuilder WithHandler(string handler) { Handler = handler; return this; }
    public LambdaBuilder WithDll(string dllPath) { DllPath = dllPath; return this; }
    public LambdaBuilder WithMemory(int memoryMb) { MemoryMb = memoryMb; return this; }
    public LambdaBuilder WithTimeout(TimeSpan timeout) { Timeout = timeout; return this; }
}

public class EventBridgeRuleBuilder
{
    internal string BusName { get; private set; } = "default";
    internal List<string> Sources { get; } = new();
    internal List<string> DetailTypes { get; } = new();
    internal Dictionary<string, object> Details { get; } = new();
    internal List<string> TargetFunctions { get; } = new();

    public EventBridgeRuleBuilder OnBus(string busName) { BusName = busName; return this; }
    public EventBridgeRuleBuilder MatchSource(string source) { Sources.Add(source); return this; }
    public EventBridgeRuleBuilder MatchDetailType(string detailType) { DetailTypes.Add(detailType); return this; }
    public EventBridgeRuleBuilder MatchDetail(string fieldName, object value) { Details[fieldName] = value; return this; }
    public EventBridgeRuleBuilder TargetLambda(string functionName) { TargetFunctions.Add(functionName); return this; }
}

/// <summary>
/// Represents the JSON settings file format for pre-seeding SSM Parameter Store parameters.
/// </summary>
public class SsmParameterSettings
{
    public List<SsmParameterEntry> Parameters { get; set; } = [];
}

/// <summary>
/// A single SSM parameter entry in the JSON settings file.
/// </summary>
public class SsmParameterEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
}

/// <summary>
/// Builds an ECS task definition's container list.
/// </summary>
public class EcsTaskDefinitionBuilder
{
    internal List<Models.EcsContainerDefinition> Containers { get; } = new();
    internal int? Cpu { get; private set; }
    internal int? Memory { get; private set; }
    internal string NetworkMode { get; private set; } = "bridge";
    internal string? LocalProcessPath { get; private set; }

    public EcsTaskDefinitionBuilder WithNetworkMode(string mode) { NetworkMode = mode; return this; }
    public EcsTaskDefinitionBuilder WithCpu(int cpu) { Cpu = cpu; return this; }
    public EcsTaskDefinitionBuilder WithMemory(int memory) { Memory = memory; return this; }

    /// <summary>
    /// Configures a local process to launch when RunTask is called on a task using
    /// this definition. The process is terminated when StopTask is called.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path (e.g. "dotnet").</param>
    /// <param name="args">Arguments passed to the executable.</param>
    public EcsTaskDefinitionBuilder WithLocalProcess(string fileName, params string[] args)
    {
        LocalProcessFileName = fileName;
        LocalProcessArgs = args.Length > 0 ? args : null;
        return this;
    }

    /// <summary>
    /// Configures a local process to launch when RunTask is called, with an explicit working directory.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path (e.g. "dotnet").</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="args">Arguments passed to the executable.</param>
    public EcsTaskDefinitionBuilder WithLocalProcess(string fileName, string workingDirectory, params string[] args)
    {
        LocalProcessFileName = fileName;
        LocalProcessWorkingDirectory = workingDirectory;
        LocalProcessArgs = args.Length > 0 ? args : null;
        return this;
    }

    internal string? LocalProcessFileName { get; private set; }
    internal string[]? LocalProcessArgs { get; private set; }
    internal string? LocalProcessWorkingDirectory { get; private set; }
    internal Dictionary<string, string>? LocalProcessEnvironment { get; private set; }

    /// <summary>
    /// Adds an environment variable that will be injected into the local process at startup.
    /// </summary>
    public EcsTaskDefinitionBuilder WithEnvironmentVariable(string name, string value)
    {
        LocalProcessEnvironment ??= new Dictionary<string, string>();
        LocalProcessEnvironment[name] = value;
        return this;
    }

    public EcsTaskDefinitionBuilder WithContainer(string name, string image,
        Action<EcsContainerBuilder>? configure = null)
    {
        var cb = new EcsContainerBuilder { Name = name, Image = image };
        configure?.Invoke(cb);
        Containers.Add(cb.Build());
        return this;
    }
}

/// <summary>
/// Builds a single ECS container definition.
/// </summary>
public class EcsContainerBuilder
{
    internal string Name { get; set; } = string.Empty;
    internal string Image { get; set; } = string.Empty;
    internal int? Cpu { get; private set; }
    internal int? Memory { get; private set; }
    internal bool Essential { get; private set; } = true;
    internal List<Models.EcsPortMapping> PortMappings { get; } = new();
    internal List<Models.EcsKeyValuePair> Environment { get; } = new();
    internal string[]? Command { get; private set; }

    public EcsContainerBuilder WithCpu(int cpu) { Cpu = cpu; return this; }
    public EcsContainerBuilder WithMemory(int memory) { Memory = memory; return this; }
    public EcsContainerBuilder WithEssential(bool essential) { Essential = essential; return this; }
    public EcsContainerBuilder WithCommand(params string[] command) { Command = command; return this; }

    public EcsContainerBuilder WithPortMapping(int containerPort, int? hostPort = null, string protocol = "tcp")
    {
        PortMappings.Add(new Models.EcsPortMapping { ContainerPort = containerPort, HostPort = hostPort, Protocol = protocol });
        return this;
    }

    public EcsContainerBuilder WithEnvironment(string name, string value)
    {
        Environment.Add(new Models.EcsKeyValuePair { Name = name, Value = value });
        return this;
    }

    internal Models.EcsContainerDefinition Build() => new()
    {
        Name = Name,
        Image = Image,
        Cpu = Cpu,
        Memory = Memory,
        Essential = Essential,
        PortMappings = PortMappings,
        Environment = Environment,
        Command = Command
    };
}
