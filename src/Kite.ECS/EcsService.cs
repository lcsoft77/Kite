using System.Diagnostics;
using System.Text.Json;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.ECS;

public class EcsService
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<EcsService> _logger;

    public EcsService(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<EcsService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var target = context.Request.Headers.TryGetValue("X-Amz-Target", out var t) ? t.ToString() : string.Empty;
        var operation = target.Split('.').Last();

        _logger.LogDebug("[DEBUG] ECS HandleAsync - Operation: {Operation}, Method: {Method}", operation, context.Request.Method);

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var doc = body.Length > 0 ? JsonDocument.Parse(body) : JsonDocument.Parse("{}");
        var root = doc.RootElement;

        _logger.LogDebug("[DEBUG] ECS operation: {Operation}, RequestBodyLength: {BodyLength}", operation, body.Length);

        context.Response.ContentType = "application/x-amz-json-1.1";

        switch (operation)
        {
            case "CreateCluster":
                await HandleCreateCluster(context, root);
                break;
            case "DeleteCluster":
                await HandleDeleteCluster(context, root);
                break;
            case "DescribeClusters":
                await HandleDescribeClusters(context, root);
                break;
            case "ListClusters":
                await HandleListClusters(context, root);
                break;
            case "RegisterTaskDefinition":
                await HandleRegisterTaskDefinition(context, root);
                break;
            case "DeregisterTaskDefinition":
                await HandleDeregisterTaskDefinition(context, root);
                break;
            case "DescribeTaskDefinition":
                await HandleDescribeTaskDefinition(context, root);
                break;
            case "ListTaskDefinitions":
                await HandleListTaskDefinitions(context, root);
                break;
            case "ListTaskDefinitionFamilies":
                await HandleListTaskDefinitionFamilies(context, root);
                break;
            case "RunTask":
                await HandleRunTask(context, root);
                break;
            case "StopTask":
                await HandleStopTask(context, root);
                break;
            case "DescribeTasks":
                await HandleDescribeTasks(context, root);
                break;
            case "ListTasks":
                await HandleListTasks(context, root);
                break;
            default:
                _logger.LogWarning("Unknown ECS operation: {Operation}", operation);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    __type = "UnknownOperationException",
                    message = $"Unknown ECS operation: {operation}"
                }));
                break;
        }
    }

    private async Task HandleCreateCluster(HttpContext context, JsonElement root)
    {
        var name = root.TryGetProperty("clusterName", out var n) ? n.GetString() ?? "default" : "default";
        _logger.LogDebug("[ENTRY] HandleCreateCluster - ClusterName: {ClusterName}", name);
        
        if (_registry.GetEcsCluster(name) is null)
            _registry.RegisterEcsCluster(new EcsCluster { Name = name });

        using var activity = KiteActivitySource.ECS.StartActivity("ecs.create_cluster");
        activity?.SetTag("cluster.name", name);

        var cluster = _registry.GetEcsCluster(name)!;
        LogHelpers.AddRequestLog(cluster.RequestLog, "CreateCluster", $"Name={name}");
        cluster.NotifyChange();

        _logger.LogInformation("CreateCluster completed - ClusterName: {ClusterName}", name);

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            cluster = BuildClusterObject(cluster)
        }));
    }

    private async Task HandleDeleteCluster(HttpContext context, JsonElement root)
    {
        var clusterRef = root.TryGetProperty("cluster", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        var name = ExtractClusterName(clusterRef);

        var cluster = _registry.GetEcsCluster(name);
        if (cluster is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClusterNotFoundException",
                message = $"Cluster not found: {clusterRef}"
            }));
            return;
        }

        _registry.DeleteEcsCluster(name);
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            cluster = BuildClusterObject(cluster)
        }));
    }

    private async Task HandleDescribeClusters(HttpContext context, JsonElement root)
    {
        var clusterRefs = root.TryGetProperty("clusters", out var arr)
            ? arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : new List<string>();

        var clusters = new List<object>();
        var failures = new List<object>();

        if (clusterRefs.Count == 0)
        {
            clusters.AddRange(_registry.GetAllEcsClusters().Select(c => (object)BuildClusterObject(c)));
        }
        else
        {
            foreach (var clusterRef in clusterRefs)
            {
                var name = ExtractClusterName(clusterRef);
                var cluster = _registry.GetEcsCluster(name);
                if (cluster is not null)
                    clusters.Add(BuildClusterObject(cluster));
                else
                    failures.Add(new { arn = clusterRef, reason = "MISSING" });
            }
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            clusters,
            failures
        }));
    }

    private async Task HandleListClusters(HttpContext context, JsonElement root)
    {
        var clusterArns = _registry.GetAllEcsClusters()
            .Select(c => BuildClusterArn(c.Name))
            .ToList();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            clusterArns,
            nextToken = (string?)null
        }));
    }

    private async Task HandleRegisterTaskDefinition(HttpContext context, JsonElement root)
    {
        var family = root.TryGetProperty("family", out var f) ? f.GetString() ?? string.Empty : string.Empty;
        _logger.LogDebug("[ENTRY] HandleRegisterTaskDefinition - Family: {Family}", family);
        
        if (string.IsNullOrEmpty(family))
        {
            _logger.LogError("[ERROR] RegisterTaskDefinition failed - Family is required");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClientException",
                message = "family is required"
            }));
            return;
        }

        var containers = new List<EcsContainerDefinition>();
        if (root.TryGetProperty("containerDefinitions", out var cdArr))
        {
            foreach (var cd in cdArr.EnumerateArray())
            {
                var containerDef = new EcsContainerDefinition
                {
                    Name = cd.TryGetProperty("name", out var cn) ? cn.GetString() ?? string.Empty : string.Empty,
                    Image = cd.TryGetProperty("image", out var img) ? img.GetString() ?? string.Empty : string.Empty,
                    Cpu = cd.TryGetProperty("cpu", out var cpu) ? cpu.GetInt32() : null,
                    Memory = cd.TryGetProperty("memory", out var mem) ? mem.GetInt32() : null,
                    Essential = !cd.TryGetProperty("essential", out var ess) || ess.GetBoolean(),
                };

                if (cd.TryGetProperty("portMappings", out var ports))
                {
                    foreach (var pm in ports.EnumerateArray())
                    {
                        containerDef.PortMappings.Add(new EcsPortMapping
                        {
                            ContainerPort = pm.TryGetProperty("containerPort", out var cp) ? cp.GetInt32() : 0,
                            HostPort = pm.TryGetProperty("hostPort", out var hp) ? hp.GetInt32() : null,
                            Protocol = pm.TryGetProperty("protocol", out var proto) ? proto.GetString() ?? "tcp" : "tcp"
                        });
                    }
                }

                if (cd.TryGetProperty("environment", out var envArr))
                {
                    foreach (var env in envArr.EnumerateArray())
                    {
                        containerDef.Environment.Add(new EcsKeyValuePair
                        {
                            Name = env.TryGetProperty("name", out var ename) ? ename.GetString() ?? string.Empty : string.Empty,
                            Value = env.TryGetProperty("value", out var evalue) ? evalue.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }

                if (cd.TryGetProperty("command", out var cmdArr))
                    containerDef.Command = cmdArr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

                containers.Add(containerDef);
            }
        }

        int? cpuVal = null;
        if (root.TryGetProperty("cpu", out var taskCpu))
            cpuVal = taskCpu.ValueKind == JsonValueKind.String ? int.TryParse(taskCpu.GetString(), out var cv) ? cv : null : taskCpu.GetInt32();

        int? memVal = null;
        if (root.TryGetProperty("memory", out var taskMem))
            memVal = taskMem.ValueKind == JsonValueKind.String ? int.TryParse(taskMem.GetString(), out var mv) ? mv : null : taskMem.GetInt32();

        var networkMode = root.TryGetProperty("networkMode", out var nm) ? nm.GetString() ?? "bridge" : "bridge";

        var taskDef = new EcsTaskDefinition
        {
            Family = family,
            NetworkMode = networkMode,
            Cpu = cpuVal,
            Memory = memVal,
            ContainerDefinitions = containers
        };

        using var activity = KiteActivitySource.ECS.StartActivity("ecs.register_task_definition");
        activity?.SetTag("task.family", family);
        activity?.SetTag("task.container.count", containers.Count);
        activity?.SetTag("task.cpu", cpuVal);
        activity?.SetTag("task.memory", memVal);

        _registry.RegisterEcsTaskDefinition(taskDef);

        // Inherit Aspire log/state delegates from any pre-existing revision of this family
        // so that new revisions registered via the API still forward output to the dashboard.
        var existingWithDelegates = _registry.GetEcsTaskDefinitionsByFamily(family)
            .Where(d => d.Revision != taskDef.Revision)
            .FirstOrDefault(d => d.OnLogLine is not null || d.OnStateChange is not null);
        if (existingWithDelegates is not null)
        {
            taskDef.OnLogLine = existingWithDelegates.OnLogLine;
            taskDef.OnStateChange = existingWithDelegates.OnStateChange;
        }

        _logger.LogInformation("RegisterTaskDefinition completed - Family: {Family}, ContainerCount: {ContainerCount}, CPU: {CPU}, Memory: {Memory}", 
            family, containers.Count, cpuVal, memVal);

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            taskDefinition = BuildTaskDefinitionObject(taskDef)
        }));
    }

    private async Task HandleDeregisterTaskDefinition(HttpContext context, JsonElement root)
    {
        var tdRef = root.TryGetProperty("taskDefinition", out var td) ? td.GetString() ?? string.Empty : string.Empty;
        if (!TryParseTaskDefinitionRef(tdRef, out var family, out var revision))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClientException",
                message = $"Invalid task definition: {tdRef}"
            }));
            return;
        }

        var def = _registry.GetEcsTaskDefinition(family, revision);
        if (def is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClientException",
                message = $"Task definition not found: {tdRef}"
            }));
            return;
        }

        _registry.DeregisterEcsTaskDefinition(family, revision);
        def.Status = "INACTIVE";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            taskDefinition = BuildTaskDefinitionObject(def)
        }));
    }

    private async Task HandleDescribeTaskDefinition(HttpContext context, JsonElement root)
    {
        var tdRef = root.TryGetProperty("taskDefinition", out var td) ? td.GetString() ?? string.Empty : string.Empty;

        EcsTaskDefinition? def;
        if (TryParseTaskDefinitionRef(tdRef, out var family, out var revision))
        {
            def = _registry.GetEcsTaskDefinition(family, revision);
        }
        else
        {
            // Just family name - return latest active revision
            def = _registry.GetEcsTaskDefinitionsByFamily(tdRef)
                .Where(d => d.Status == "ACTIVE")
                .OrderByDescending(d => d.Revision)
                .FirstOrDefault();
        }

        if (def is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClientException",
                message = $"Task definition not found: {tdRef}"
            }));
            return;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            taskDefinition = BuildTaskDefinitionObject(def)
        }));
    }

    private async Task HandleListTaskDefinitions(HttpContext context, JsonElement root)
    {
        var familyPrefix = root.TryGetProperty("familyPrefix", out var fp) ? fp.GetString() : null;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : "ACTIVE";

        var defs = _registry.GetAllEcsTaskDefinitions()
            .Where(d => string.IsNullOrEmpty(status) || d.Status == status)
            .Where(d => string.IsNullOrEmpty(familyPrefix) || d.Family.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Family)
            .ThenBy(d => d.Revision)
            .Select(d => BuildTaskDefinitionArn(d.Family, d.Revision))
            .ToList();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            taskDefinitionArns = defs,
            nextToken = (string?)null
        }));
    }

    private async Task HandleListTaskDefinitionFamilies(HttpContext context, JsonElement root)
    {
        var familyPrefix = root.TryGetProperty("familyPrefix", out var fp) ? fp.GetString() : null;

        var families = _registry.GetAllEcsTaskDefinitions()
            .Where(d => string.IsNullOrEmpty(familyPrefix) || d.Family.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Family)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            families,
            nextToken = (string?)null
        }));
    }

    private async Task HandleRunTask(HttpContext context, JsonElement root)
    {
        var clusterRef = root.TryGetProperty("cluster", out var c) ? c.GetString() ?? "default" : "default";
        var tdRef = root.TryGetProperty("taskDefinition", out var td) ? td.GetString() ?? string.Empty : string.Empty;
        var count = root.TryGetProperty("count", out var cnt) ? cnt.GetInt32() : 1;

        var clusterName = ExtractClusterName(clusterRef);
        var cluster = _registry.GetEcsCluster(clusterName);
        if (cluster is null)
        {
            // Auto-create default cluster if referenced
            cluster = new EcsCluster { Name = clusterName };
            _registry.RegisterEcsCluster(cluster);
        }

        EcsTaskDefinition? def;
        if (TryParseTaskDefinitionRef(tdRef, out var family, out var revision))
            def = _registry.GetEcsTaskDefinition(family, revision);
        else
            def = _registry.GetEcsTaskDefinitionsByFamily(tdRef)
                .Where(d => d.Status == "ACTIVE")
                .OrderByDescending(d => d.Revision)
                .FirstOrDefault();

        if (def is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "ClientException",
                message = $"Task definition not found: {tdRef}"
            }));
            return;
        }

        var tasks = new List<object>();
        for (var i = 0; i < count; i++)
        {
            var taskId = Guid.NewGuid().ToString();
            var clusterArn = BuildClusterArn(clusterName);
            var taskArn = $"arn:aws:ecs:{_registry.Region}:{_registry.AccountId}:task/{clusterName}/{taskId}";
            var taskDefArn = BuildTaskDefinitionArn(def.Family, def.Revision);

            var task = new EcsTask
            {
                TaskArn = taskArn,
                ClusterArn = clusterArn,
                TaskDefinitionArn = taskDefArn,
                LastStatus = "RUNNING",
                DesiredStatus = "RUNNING",
                StartedAt = DateTime.UtcNow
            };

            // Launch a local process if the task definition has one configured.
            // Priority 1: LocalProcessFileName (set via WithLocalProcess builder or <TProject> overload).
            // Priority 2: first container's Command[] array (raw SDK registration).
            string? processFileName = null;
            string? processArgs = null;
            string? processWorkingDir = null;

            if (!string.IsNullOrEmpty(def.LocalProcessFileName))
            {
                processFileName = def.LocalProcessFileName;
                processArgs = def.LocalProcessArgs is { Length: > 0 }
                    ? string.Join(' ', def.LocalProcessArgs)
                    : null;
                processWorkingDir = def.LocalProcessWorkingDirectory;
            }
            else if (def.ContainerDefinitions.Count > 0 && def.ContainerDefinitions[0].Command?.Length > 0)
            {
                var cmd = def.ContainerDefinitions[0].Command!;
                processFileName = cmd[0];
                processArgs = cmd.Length > 1 ? string.Join(' ', cmd.Skip(1)) : null;
            }

            if (processFileName is not null)
            {
                try
                {
                    var psi = new ProcessStartInfo(processFileName, processArgs ?? string.Empty)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    if (!string.IsNullOrEmpty(processWorkingDir))
                        psi.WorkingDirectory = processWorkingDir;
                    if (def.LocalProcessEnvironment is not null)
                        foreach (var (k, v) in def.LocalProcessEnvironment)
                            psi.Environment[k] = v;

                    var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    var capturedTaskArn = taskArn;
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data is not null)
                            def.OnLogLine?.Invoke(e.Data);
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data is not null)
                            def.OnLogLine?.Invoke(e.Data);
                    };
                    process.Exited += (_, _) =>
                    {
                        var exitedTask = _registry.GetEcsTask(capturedTaskArn);
                        if (exitedTask is not null && exitedTask.LastStatus == "RUNNING")
                        {
                            exitedTask.LastStatus = "STOPPED";
                            exitedTask.DesiredStatus = "STOPPED";
                            exitedTask.StoppedAt = DateTime.UtcNow;
                            exitedTask.StopCode = "EssentialContainerExited";
                            exitedTask.StoppedReason = "Process exited";
                            _registry.UpdateEcsTask(exitedTask);
                            def.OnStateChange?.Invoke(capturedTaskArn, "STOPPED");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    task.ProcessId = process.Id;
                    def.OnStateChange?.Invoke(taskArn, "RUNNING");
                    _logger.LogInformation(
                        "ECS RunTask started local process {Executable} (PID {Pid}) for task {TaskArn}",
                        processFileName, process.Id, taskArn);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ECS RunTask failed to start local process {Executable} for task {TaskArn}",
                        processFileName, taskArn);
                }
            }

            _registry.RegisterEcsTask(task);
            tasks.Add(BuildTaskObject(task));
        }

        using var activity = KiteActivitySource.ECS.StartActivity("ecs.run_task");
        activity?.SetTag("cluster.name", clusterName);
        activity?.SetTag("task.definition", $"{def.Family}:{def.Revision}");
        activity?.SetTag("task.count", count);

        LogHelpers.AddRequestLog(cluster.RequestLog, "RunTask", $"TaskDef={def.Family}:{def.Revision} Count={count}");
        cluster.NotifyChange();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            tasks,
            failures = Array.Empty<object>()
        }));
    }

    private async Task HandleStopTask(HttpContext context, JsonElement root)
    {
        var clusterRef = root.TryGetProperty("cluster", out var c) ? c.GetString() ?? "default" : "default";
        var taskRef = root.TryGetProperty("task", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty;

        var clusterName = ExtractClusterName(clusterRef);
        var clusterArn = BuildClusterArn(clusterName);

        var task = _registry.GetEcsTask(taskRef)
            ?? _registry.GetEcsTasksForCluster(clusterArn)
                .FirstOrDefault(t => t.TaskArn.EndsWith(taskRef) || t.TaskArn == taskRef);

        if (task is null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                __type = "TaskNotFoundException",
                message = $"Task not found: {taskRef}"
            }));
            return;
        }

        task.LastStatus = "STOPPED";
        task.DesiredStatus = "STOPPED";
        task.StoppedAt = DateTime.UtcNow;
        task.StopCode = "UserInitiated";
        task.StoppedReason = string.IsNullOrEmpty(reason) ? "Stopped by user request" : reason;

        // Kill the local process if one was started for this task
        if (task.ProcessId is not null)
        {
            try
            {
                var process = Process.GetProcessById(task.ProcessId.Value);
                process.Kill(entireProcessTree: true);
                _logger.LogInformation(
                    "ECS StopTask killed local process (PID {Pid}) for task {TaskArn}",
                    task.ProcessId.Value, task.TaskArn);
            }
            catch (ArgumentException)
            {
                // Process already exited – nothing to do
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ECS StopTask failed to kill local process (PID {Pid}) for task {TaskArn}",
                    task.ProcessId.Value, task.TaskArn);
            }
        }

        _registry.UpdateEcsTask(task);

        // Notify Aspire state change if delegates are wired
        if (TryParseTaskDefinitionRef(task.TaskDefinitionArn, out var stoppedFamily, out var stoppedRevision))
        {
            var stoppedDef = _registry.GetEcsTaskDefinition(stoppedFamily, stoppedRevision);
            stoppedDef?.OnStateChange?.Invoke(task.TaskArn, "STOPPED");
        }

        var cluster = _registry.GetEcsCluster(clusterName);
        if (cluster is not null)
        {
            LogHelpers.AddRequestLog(cluster.RequestLog, "StopTask", $"Task={task.TaskArn}");
            cluster.NotifyChange();
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            task = BuildTaskObject(task)
        }));
    }

    private async Task HandleDescribeTasks(HttpContext context, JsonElement root)
    {
        var clusterRef = root.TryGetProperty("cluster", out var c) ? c.GetString() ?? "default" : "default";
        var taskRefs = root.TryGetProperty("tasks", out var arr)
            ? arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : new List<string>();

        var clusterName = ExtractClusterName(clusterRef);
        var clusterArn = BuildClusterArn(clusterName);
        var clusterTasks = _registry.GetEcsTasksForCluster(clusterArn).ToList();

        var tasks = new List<object>();
        var failures = new List<object>();

        foreach (var taskRef in taskRefs)
        {
            var task = _registry.GetEcsTask(taskRef)
                ?? clusterTasks.FirstOrDefault(t => t.TaskArn.EndsWith(taskRef) || t.TaskArn == taskRef);

            if (task is not null)
                tasks.Add(BuildTaskObject(task));
            else
                failures.Add(new { arn = taskRef, reason = "MISSING" });
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { tasks, failures }));
    }

    private async Task HandleListTasks(HttpContext context, JsonElement root)
    {
        var clusterRef = root.TryGetProperty("cluster", out var c) ? c.GetString() ?? "default" : "default";
        var familyFilter = root.TryGetProperty("family", out var ff) ? ff.GetString() : null;
        var desiredStatusFilter = root.TryGetProperty("desiredStatus", out var ds) ? ds.GetString() : null;

        var clusterName = ExtractClusterName(clusterRef);
        var clusterArn = BuildClusterArn(clusterName);

        var tasks = _registry.GetEcsTasksForCluster(clusterArn)
            .Where(t => string.IsNullOrEmpty(desiredStatusFilter) || t.DesiredStatus == desiredStatusFilter)
            .Where(t => string.IsNullOrEmpty(familyFilter) || t.TaskDefinitionArn.Contains($"/{familyFilter}:"))
            .Select(t => t.TaskArn)
            .ToList();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            taskArns = tasks,
            nextToken = (string?)null
        }));
    }

    private object BuildClusterObject(EcsCluster cluster) => new
    {
        clusterArn = BuildClusterArn(cluster.Name),
        clusterName = cluster.Name,
        status = cluster.Status,
        registeredContainerInstancesCount = 0,
        runningTasksCount = _registry.GetEcsTasksForCluster(BuildClusterArn(cluster.Name)).Count(t => t.LastStatus == "RUNNING"),
        pendingTasksCount = 0,
        activeServicesCount = 0
    };

    private object BuildTaskDefinitionObject(EcsTaskDefinition def) => new
    {
        taskDefinitionArn = BuildTaskDefinitionArn(def.Family, def.Revision),
        family = def.Family,
        revision = def.Revision,
        status = def.Status,
        networkMode = def.NetworkMode,
        cpu = def.Cpu?.ToString(),
        memory = def.Memory?.ToString(),
        containerDefinitions = def.ContainerDefinitions.Select(cd => new
        {
            name = cd.Name,
            image = cd.Image,
            cpu = cd.Cpu ?? 0,
            memory = cd.Memory,
            essential = cd.Essential,
            portMappings = cd.PortMappings.Select(pm => new
            {
                containerPort = pm.ContainerPort,
                hostPort = pm.HostPort,
                protocol = pm.Protocol
            }).ToList(),
            environment = cd.Environment.Select(e => new { name = e.Name, value = e.Value }).ToList(),
            command = cd.Command
        }).ToList()
    };

    private static object BuildTaskObject(EcsTask task) => new
    {
        taskArn = task.TaskArn,
        clusterArn = task.ClusterArn,
        taskDefinitionArn = task.TaskDefinitionArn,
        lastStatus = task.LastStatus,
        desiredStatus = task.DesiredStatus,
        createdAt = task.CreatedAt,
        startedAt = task.StartedAt,
        stoppedAt = task.StoppedAt,
        stopCode = task.StopCode,
        stoppedReason = task.StoppedReason
    };

    private string BuildClusterArn(string name) =>
        $"arn:aws:ecs:{_registry.Region}:{_registry.AccountId}:cluster/{name}";

    private string BuildTaskDefinitionArn(string family, int revision) =>
        $"arn:aws:ecs:{_registry.Region}:{_registry.AccountId}:task-definition/{family}:{revision}";

    private static string ExtractClusterName(string clusterRef)
    {
        if (clusterRef.StartsWith("arn:aws:ecs:"))
        {
            var parts = clusterRef.Split('/');
            return parts.Length > 0 ? parts[^1] : clusterRef;
        }
        return clusterRef;
    }

    private bool TryParseTaskDefinitionRef(string tdRef, out string family, out int revision)
    {
        family = string.Empty;
        revision = 0;

        string candidate = tdRef;

        // Handle full ARN
        if (tdRef.StartsWith("arn:aws:ecs:"))
        {
            var parts = tdRef.Split('/');
            if (parts.Length < 2) return false;
            candidate = parts[^1];
        }

        var colonIdx = candidate.LastIndexOf(':');
        if (colonIdx < 0 || !int.TryParse(candidate.AsSpan(colonIdx + 1), out revision))
            return false;

        family = candidate[..colonIdx];
        return !string.IsNullOrEmpty(family);
    }
}
