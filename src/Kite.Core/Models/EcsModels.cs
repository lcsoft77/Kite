using System.Text.Json.Serialization;

namespace Kite.Core.Models;

public class EcsCluster
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public List<RequestLogEntry> RequestLog { get; } = new();
    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

public class EcsTaskDefinition
{
    public string Family { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public string Status { get; set; } = "ACTIVE";
    public string NetworkMode { get; set; } = "bridge";
    public int? Cpu { get; set; }
    public int? Memory { get; set; }
    public List<EcsContainerDefinition> ContainerDefinitions { get; set; } = new();

    /// <summary>
    /// Executable name (e.g. "dotnet") for the local process started by RunTask.
    /// Requires <see cref="LocalProcessArgs"/> for arguments.
    /// </summary>
    [JsonIgnore]
    public string? LocalProcessFileName { get; set; }

    /// <summary>
    /// Arguments passed to <see cref="LocalProcessFileName"/> when starting the process.
    /// </summary>
    [JsonIgnore]
    public string[]? LocalProcessArgs { get; set; }

    /// <summary>
    /// Working directory for the local process. If null, inherits the host process directory.
    /// </summary>
    [JsonIgnore]
    public string? LocalProcessWorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables injected into the local process started by RunTask.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string>? LocalProcessEnvironment { get; set; }

    /// <summary>
    /// Called with each stdout/stderr line produced by the local process.
    /// Wired by the Aspire layer to forward logs to the dashboard.
    /// </summary>
    [JsonIgnore]
    public Action<string>? OnLogLine { get; set; }

    /// <summary>
    /// Called when the task transitions to a new state (e.g. "RUNNING", "STOPPED").
    /// Arguments: taskArn, newState.
    /// Wired by the Aspire layer to publish state updates to the dashboard.
    /// </summary>
    [JsonIgnore]
    public Action<string, string>? OnStateChange { get; set; }
}

public class EcsContainerDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int? Cpu { get; set; }
    public int? Memory { get; set; }
    public bool Essential { get; set; } = true;
    public List<EcsPortMapping> PortMappings { get; set; } = new();
    public List<EcsKeyValuePair> Environment { get; set; } = new();
    public string[]? Command { get; set; }
}

public class EcsPortMapping
{
    public int ContainerPort { get; set; }
    public int? HostPort { get; set; }
    public string Protocol { get; set; } = "tcp";
}

public class EcsKeyValuePair
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class EcsTask
{
    public string TaskArn { get; set; } = string.Empty;
    public string ClusterArn { get; set; } = string.Empty;
    public string TaskDefinitionArn { get; set; } = string.Empty;
    public string LastStatus { get; set; } = "RUNNING";
    public string DesiredStatus { get; set; } = "RUNNING";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public string? StopCode { get; set; }
    public string? StoppedReason { get; set; }

    /// <summary>
    /// PID of the local process started by RunTask when
    /// <see cref="EcsTaskDefinition.LocalProcessPath"/> is configured.
    /// </summary>
    [JsonIgnore]
    public int? ProcessId { get; set; }
}
