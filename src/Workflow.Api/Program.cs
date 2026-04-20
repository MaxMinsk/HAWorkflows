using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Api.ProfilePacks;
using Workflow.Api.Settings;
using Workflow.Api.Runs;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime;
using Workflow.Engine.Runtime.Agents;
using Workflow.Engine.Runtime.Artifacts;
using Workflow.Engine.Runtime.Mcp;
using Workflow.Engine.Runtime.Nodes;
using Workflow.Engine.Runtime.Routing;
using Workflow.Engine.Validation;
using Workflow.Persistence.WorkflowDefinitions;

var builder = WebApplication.CreateBuilder(args);

const string WorkflowWebCorsPolicy = "WorkflowWebCorsPolicy";

var configuredDatabasePath = builder.Configuration["WorkflowStorage:DatabasePath"];
var workflowDatabasePath = string.IsNullOrWhiteSpace(configuredDatabasePath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "workflow.db")
    : configuredDatabasePath!;
if (!Path.IsPathRooted(workflowDatabasePath))
{
    workflowDatabasePath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, workflowDatabasePath));
}

var configuredWorkspacePath = builder.Configuration["WorkflowArtifacts:WorkspacePath"];
var workflowWorkspacePath = string.IsNullOrWhiteSpace(configuredWorkspacePath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "workspace")
    : configuredWorkspacePath!;
if (!Path.IsPathRooted(workflowWorkspacePath))
{
    workflowWorkspacePath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, workflowWorkspacePath));
}

var configuredCheckpointPath = builder.Configuration["WorkflowRuns:CheckpointPath"];
var workflowCheckpointPath = string.IsNullOrWhiteSpace(configuredCheckpointPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "run-checkpoints")
    : configuredCheckpointPath!;
if (!Path.IsPathRooted(workflowCheckpointPath))
{
    workflowCheckpointPath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, workflowCheckpointPath));
}

var configuredMcpConfigPath = builder.Configuration["WorkflowMcp:ConfigPath"];
var mcpConfigPath = string.IsNullOrWhiteSpace(configuredMcpConfigPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "mcp.json")
    : configuredMcpConfigPath!;
if (!Path.IsPathRooted(mcpConfigPath))
{
    mcpConfigPath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, mcpConfigPath));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        WorkflowWebCorsPolicy,
        policy =>
        {
            policy
                .WithOrigins("http://127.0.0.1:5191", "http://localhost:5191")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

builder.Services.AddSingleton<IWorkflowDefinitionRepository>(serviceProvider =>
    new SqliteWorkflowDefinitionRepository(
        workflowDatabasePath,
        serviceProvider.GetRequiredService<ILogger<SqliteWorkflowDefinitionRepository>>()));
builder.Services.AddSingleton<IWorkflowArtifactStore>(_ => new FileSystemWorkflowArtifactStore(workflowWorkspacePath));
builder.Services.AddSingleton<IWorkflowRunCheckpointStore>(serviceProvider =>
    new FileSystemWorkflowRunCheckpointStore(
        workflowCheckpointPath,
        serviceProvider.GetRequiredService<ILogger<FileSystemWorkflowRunCheckpointStore>>()));
builder.Services.Configure<WorkflowRunServiceOptions>(builder.Configuration.GetSection("WorkflowRuns"));
var agentExecutorOptions = builder.Configuration.GetSection("WorkflowAgents").Get<AgentExecutorOptions>()
                           ?? new AgentExecutorOptions();
builder.Services.AddSingleton(agentExecutorOptions);
builder.Services.AddSingleton<IAgentExecutor, EchoAgentExecutor>();
builder.Services.AddSingleton<IAgentExecutor, LocalAgentExecutor>();
builder.Services.AddSingleton<IAgentExecutorCatalog, AgentExecutorCatalog>();
var mcpToolInvokerOptions = builder.Configuration.GetSection("WorkflowMcp").Get<McpToolInvokerOptions>()
                            ?? new McpToolInvokerOptions();
builder.Services.AddSingleton(serviceProvider => new McpSettingsStore(
    mcpConfigPath,
    mcpToolInvokerOptions,
    serviceProvider.GetRequiredService<ILogger<McpSettingsStore>>()));
builder.Services.AddSingleton<IMcpToolProfileSource>(serviceProvider =>
    serviceProvider.GetRequiredService<McpSettingsStore>());
builder.Services.AddSingleton(mcpToolInvokerOptions);
builder.Services.AddSingleton<IMcpToolInvoker, MockMcpToolInvoker>();
builder.Services.AddSingleton<IMcpToolInvoker, StreamableHttpMcpToolInvoker>();
builder.Services.AddSingleton<IMcpToolInvokerCatalog, McpToolInvokerCatalog>();
var modelRoutingOptions = builder.Configuration.GetSection("WorkflowModelRouting").Get<WorkflowModelRoutingOptions>()
                          ?? new WorkflowModelRoutingOptions();
builder.Services.AddSingleton(modelRoutingOptions);
builder.Services.AddSingleton<IWorkflowModelRoutingPolicy, WorkflowModelRoutingPolicy>();
var workflowNodeCatalogOptions = builder.Configuration.GetSection("WorkflowNodes").Get<WorkflowNodeCatalogOptions>()
                                 ?? new WorkflowNodeCatalogOptions();
builder.Services.AddSingleton(workflowNodeCatalogOptions);
foreach (var executorType in DiscoverWorkflowNodeExecutorTypes())
{
    builder.Services.AddSingleton(typeof(IWorkflowNodeExecutor), executorType);
}
builder.Services.AddSingleton<IWorkflowNodeCatalog, WorkflowNodeCatalog>();
builder.Services.AddSingleton(serviceProvider => new WorkflowDefinitionValidator(
    serviceProvider.GetRequiredService<IWorkflowNodeCatalog>().GetDescriptors()));
builder.Services.AddSingleton<WorkflowRunMetrics>();
builder.Services.AddSingleton<IWorkflowRuntime, DeterministicWorkflowRuntime>();
builder.Services.AddSingleton<IWorkflowRunService, InMemoryWorkflowRunService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers.TryGetValue("X-Request-ID", out var requestIdHeader) &&
                    !string.IsNullOrWhiteSpace(requestIdHeader.ToString())
        ? requestIdHeader.ToString().Trim()
        : context.TraceIdentifier;

    context.Response.Headers["X-Request-ID"] = requestId;
    using (app.Logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = requestId }))
    {
        await next();
    }
});

app.UseCors(WorkflowWebCorsPolicy);

await using (var startupScope = app.Services.CreateAsyncScope())
{
    var repository = startupScope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
    await repository.InitializeAsync(CancellationToken.None);
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Workflow.Api",
    status = "ok",
    storage = workflowDatabasePath,
    workspace = workflowWorkspacePath,
    checkpoints = workflowCheckpointPath,
    mcpConfig = mcpConfigPath,
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Workflow.Api",
}));

app.MapGet("/metrics", (WorkflowRunMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

app.MapGet(
    "/settings/mcp",
    async Task<IResult> (
        McpSettingsStore settingsStore,
        CancellationToken cancellationToken) =>
    {
        var settings = await settingsStore.ReadAsync(cancellationToken);
        return Results.Ok(ToMcpSettingsResponse(settings));
    });

app.MapPut(
    "/settings/mcp",
    async Task<IResult> (
        SaveMcpSettingsRequest request,
        McpSettingsStore settingsStore,
        CancellationToken cancellationToken) =>
    {
        if (request.Settings is null)
        {
            return Results.BadRequest(new { error = "settings is required." });
        }

        var settings = await settingsStore.SaveAsync(request.Settings, cancellationToken);
        return Results.Ok(ToMcpSettingsResponse(settings));
    });

app.MapPost(
    "/settings/mcp/test",
    async Task<IResult> (
        TestMcpProfileRequest request,
        McpSettingsStore settingsStore,
        IMcpToolInvokerCatalog mcpToolCatalog,
        CancellationToken cancellationToken) =>
    {
        var profile = string.IsNullOrWhiteSpace(request.ServerProfile)
            ? settingsStore.GetSnapshot().DefaultProfile
            : request.ServerProfile.Trim();
        var timeoutSeconds = request.TimeoutSeconds is > 0
            ? Math.Min(request.TimeoutSeconds.Value, 600)
            : 30;

        try
        {
            var tools = await mcpToolCatalog.ListToolsAsync(new McpToolListRequest
            {
                ServerProfile = profile,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            }, cancellationToken);

            return Results.Ok(new TestMcpProfileResponse(
                Profile: tools.ServerProfile,
                ServerType: tools.ServerType,
                ToolCount: tools.Tools.Count,
                Tools: tools.Tools,
                Metadata: tools.Metadata));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TimeoutException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

app.MapGet(
    "/node-types",
    (IWorkflowNodeCatalog nodeCatalog) =>
    {
        var nodeTypes = nodeCatalog.GetDescriptors()
            .Select(descriptor => new NodeTypeResponse(
                Type: descriptor.Type,
                Label: descriptor.Label,
                Description: descriptor.Description,
                Inputs: descriptor.Inputs,
                Outputs: descriptor.Outputs,
                Pack: descriptor.Pack,
                Source: descriptor.Source,
                UsesModel: descriptor.UsesModel,
                InputPorts: descriptor.GetInputPorts()
                    .Select(port => new NodeTypePortResponse(
                        Id: port.Id,
                        Label: port.Label,
                        Channel: port.Channel,
                        Required: port.Required,
                        AcceptedKinds: port.AcceptedKinds ?? Array.Empty<string>(),
                        ControlConditionKey: port.ControlConditionKey,
                        Description: port.Description,
                        ProducesKinds: port.ProducesKinds ?? Array.Empty<string>(),
                        FallbackDescription: port.FallbackDescription,
                        ExampleSources: port.ExampleSources ?? Array.Empty<string>(),
                        AllowMultiple: port.AllowMultiple))
                    .ToArray(),
                OutputPorts: descriptor.GetOutputPorts()
                    .Select(port => new NodeTypePortResponse(
                        Id: port.Id,
                        Label: port.Label,
                        Channel: port.Channel,
                        Required: port.Required,
                        AcceptedKinds: port.AcceptedKinds ?? Array.Empty<string>(),
                        ControlConditionKey: port.ControlConditionKey,
                        Description: port.Description,
                        ProducesKinds: port.ProducesKinds ?? Array.Empty<string>(),
                        FallbackDescription: port.FallbackDescription,
                        ExampleSources: port.ExampleSources ?? Array.Empty<string>(),
                        AllowMultiple: port.AllowMultiple))
                    .ToArray(),
                ConfigFields: (descriptor.ConfigFields ?? Array.Empty<WorkflowNodeConfigFieldDescriptor>())
                    .Select(field => new NodeTypeConfigFieldResponse(
                        Key: field.Key,
                        Label: field.Label,
                        FieldType: field.FieldType,
                        Description: field.Description,
                        Required: field.Required,
                        Multiline: field.Multiline,
                        Placeholder: field.Placeholder,
                        DefaultValue: field.DefaultValue,
                        Options: (field.Options ?? Array.Empty<WorkflowNodeConfigFieldOptionDescriptor>())
                            .Select(option => new NodeTypeConfigFieldOptionResponse(
                                Value: option.Value,
                                Label: option.Label))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        return Results.Ok(nodeTypes);
    });

app.MapGet(
    "/workflows",
    async Task<IResult> (
        IWorkflowDefinitionRepository repository,
        CancellationToken cancellationToken) =>
    {
        var workflows = await repository.GetLatestAsync(cancellationToken);
        return Results.Ok(workflows.Select(summary => new WorkflowSummaryResponse(
            WorkflowId: summary.WorkflowId,
            Name: summary.Name,
            Version: summary.Version,
            Status: summary.Status,
            UpdatedAtUtc: summary.UpdatedAtUtc,
            PublishedVersion: summary.PublishedVersion,
            PublishedAtUtc: summary.PublishedAtUtc)));
    });

app.MapGet(
    "/workflows/{workflowId}",
    async Task<IResult> (
        string workflowId,
        IWorkflowDefinitionRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Results.BadRequest(new { error = "workflowId is required." });
        }

        var workflow = await repository.GetLatestByIdAsync(workflowId.Trim(), cancellationToken);
        if (workflow is null)
        {
            return Results.NotFound(new { error = $"Workflow '{workflowId}' was not found." });
        }

        return Results.Ok(ToWorkflowResponse(workflow));
    });

app.MapGet(
    "/workflows/{workflowId}/versions/{version:int}",
    async Task<IResult> (
        string workflowId,
        int version,
        IWorkflowDefinitionRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Results.BadRequest(new { error = "workflowId is required." });
        }

        if (version <= 0)
        {
            return Results.BadRequest(new { error = "version must be positive." });
        }

        var workflow = await repository.GetByIdAndVersionAsync(workflowId.Trim(), version, cancellationToken);
        if (workflow is null)
        {
            return Results.NotFound(new { error = $"Workflow '{workflowId}' version {version} was not found." });
        }

        return Results.Ok(ToWorkflowResponse(workflow));
    });

app.MapGet(
    "/workflows/{workflowId}/profile-pack",
    async Task<IResult> (
        string workflowId,
        HttpRequest httpRequest,
        IWorkflowDefinitionRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Results.BadRequest(new { error = "workflowId is required." });
        }

        var workflowVersion = ParseOptionalVersionQuery(httpRequest, out var versionError);
        if (!string.IsNullOrWhiteSpace(versionError))
        {
            return Results.BadRequest(new { error = versionError });
        }

        StoredWorkflowDefinition? storedWorkflow;
        if (workflowVersion.HasValue)
        {
            storedWorkflow = await repository.GetByIdAndVersionAsync(
                workflowId.Trim(),
                workflowVersion.Value,
                cancellationToken);
        }
        else
        {
            storedWorkflow = await repository.GetLatestByIdAsync(workflowId.Trim(), cancellationToken);
        }

        if (storedWorkflow is null)
        {
            return workflowVersion.HasValue
                ? Results.NotFound(new { error = $"Workflow '{workflowId}' version {workflowVersion.Value} was not found." })
                : Results.NotFound(new { error = $"Workflow '{workflowId}' was not found." });
        }

        var definition = LoadWorkflowDefinition(storedWorkflow, out var deserializeError);
        if (definition is null)
        {
            return Results.BadRequest(new { error = deserializeError });
        }

        var pack = WorkflowProfilePackFactory.Create(storedWorkflow, definition);
        return Results.Ok(pack);
    });

app.MapPost(
    "/workflow-profile-packs/import",
    async Task<IResult> (
        ImportWorkflowProfilePackRequest request,
        IWorkflowDefinitionRepository repository,
        IWorkflowNodeCatalog nodeCatalog,
        WorkflowDefinitionValidator validator,
        CancellationToken cancellationToken) =>
    {
        if (request.ProfilePack.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "profilePack must be a JSON object." });
        }

        WorkflowProfilePackDocument? profilePack;
        try
        {
            profilePack = request.ProfilePack.Deserialize<WorkflowProfilePackDocument>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { error = $"profilePack cannot be deserialized: {exception.Message}" });
        }

        if (profilePack is null)
        {
            return Results.BadRequest(new { error = "profilePack cannot be deserialized." });
        }

        if (!string.Equals(
                profilePack.ProfilePackSchemaVersion,
                WorkflowProfilePackFactory.CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = $"Unsupported profilePackSchemaVersion '{profilePack.ProfilePackSchemaVersion}'."
            });
        }

        var importedName = ResolveImportedWorkflowName(profilePack, request.Name);
        var importedDefinition = new WorkflowDefinition
        {
            SchemaVersion = profilePack.Definition.SchemaVersion,
            Name = importedName,
            Nodes = profilePack.Definition.Nodes,
            Edges = profilePack.Definition.Edges
        };

        var validationResult = validator.Validate(importedDefinition);
        if (!validationResult.IsValid)
        {
            var supportedNodeTypes = nodeCatalog.GetSupportedNodeTypes().ToHashSet(StringComparer.Ordinal);
            var missingNodeTypes = importedDefinition.Nodes
                .Select(node => node.Type)
                .Where(nodeType => !supportedNodeTypes.Contains(nodeType))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(nodeType => nodeType, StringComparer.Ordinal)
                .ToArray();
            if (missingNodeTypes.Length > 0)
            {
                return Results.BadRequest(new
                {
                    error = $"Profile pack contains unsupported node types: {string.Join(", ", missingNodeTypes)}.",
                    missingNodeTypes,
                    hint = "Check WorkflowNodes:EnabledPacks/DisabledPacks. The local-first runtime no longer requires a separate local nodes mode."
                });
            }

            return Results.BadRequest(new { error = string.Join(" ", validationResult.Errors) });
        }

        var definitionJson = JsonSerializer.Serialize(
            importedDefinition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var savedWorkflow = await repository.SaveDraftAsync(
            workflowId: null,
            name: importedName,
            definitionJson: definitionJson,
            cancellationToken: cancellationToken);

        app.Logger.LogInformation(
            "Workflow profile pack imported: workflowId {WorkflowId}, version {Version}, name {WorkflowName}.",
            savedWorkflow.WorkflowId,
            savedWorkflow.Version,
            savedWorkflow.Name);

        return Results.Ok(ToWorkflowResponse(savedWorkflow));
    });

app.MapPost(
    "/workflows",
    async Task<IResult> (
        UpsertWorkflowRequest request,
        IWorkflowDefinitionRepository repository,
        WorkflowDefinitionValidator validator,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "name is required." });
        }

        if (request.Definition.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "definition must be a JSON object." });
        }

        var definition = DeserializeWorkflowDefinition(request.Definition, out var deserializeError);
        if (definition is null)
        {
            return Results.BadRequest(new { error = deserializeError });
        }

        var validationResult = validator.Validate(definition);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new { error = string.Join(" ", validationResult.Errors) });
        }

        var savedWorkflow = await repository.SaveDraftAsync(
            request.Id,
            request.Name.Trim(),
            request.Definition.GetRawText(),
            cancellationToken);

        app.Logger.LogInformation(
            "Workflow draft saved: workflowId {WorkflowId}, version {Version}, name {WorkflowName}.",
            savedWorkflow.WorkflowId,
            savedWorkflow.Version,
            savedWorkflow.Name);

        return Results.Ok(ToWorkflowResponse(savedWorkflow));
    });

app.MapPost(
    "/workflows/{workflowId}/versions/{version:int}/publish",
    async Task<IResult> (
        string workflowId,
        int version,
        IWorkflowDefinitionRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Results.BadRequest(new { error = "workflowId is required." });
        }

        if (version <= 0)
        {
            return Results.BadRequest(new { error = "version must be positive." });
        }

        var publishedWorkflow = await repository.PublishAsync(workflowId.Trim(), version, cancellationToken);
        if (publishedWorkflow is null)
        {
            return Results.NotFound(new { error = $"Workflow '{workflowId}' version {version} was not found." });
        }

        app.Logger.LogInformation(
            "Workflow published: workflowId {WorkflowId}, version {Version}.",
            publishedWorkflow.WorkflowId,
            publishedWorkflow.Version);

        return Results.Ok(ToWorkflowResponse(publishedWorkflow));
    });

app.MapPost(
    "/runtime/execute-preview",
    async Task<IResult> (
        ExecuteWorkflowPreviewRequest request,
        IWorkflowRuntime workflowRuntime,
        CancellationToken cancellationToken) =>
    {
        if (request.Definition.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "definition must be a JSON object." });
        }

        WorkflowDefinition? definition;
        try
        {
            definition = request.Definition.Deserialize<WorkflowDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception exception)
        {
            return Results.BadRequest(new { error = $"definition payload cannot be deserialized: {exception.Message}" });
        }

        if (definition is null)
        {
            return Results.BadRequest(new { error = "definition payload cannot be deserialized." });
        }

        var hasInput = request.Input.HasValue &&
                       request.Input.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
        var runRequest = new WorkflowRunRequest
        {
            InputJson = hasInput ? request.Input!.Value.GetRawText() : null
        };

        var runResult = await workflowRuntime.ExecuteAsync(
            definition,
            runRequest,
            onNodeStatusChanged: null,
            onCheckpointCreated: null,
            cancellationToken);
        return Results.Ok(runResult);
    });

app.MapPost(
    "/signals/{source}",
    async Task<IResult> (
        string source,
        CreateSignalRunRequest request,
        HttpRequest httpRequest,
        IWorkflowDefinitionRepository repository,
        IWorkflowRunService runService,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Results.BadRequest(new { error = "source is required." });
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            return Results.BadRequest(new { error = "workflowId is required." });
        }

        var workflowId = request.WorkflowId.Trim();
        var signalSource = source.Trim();

        var idempotencyKeyHeader = httpRequest.Headers.TryGetValue("Idempotency-Key", out var keyHeader)
            ? keyHeader.ToString()
            : null;
        var idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKeyHeader)
            ? request.IdempotencyKey
            : idempotencyKeyHeader;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new
            {
                error = "idempotencyKey is required for external signals (Idempotency-Key header or request.idempotencyKey)."
            });
        }

        var storedWorkflow = await repository.GetPublishedByIdAsync(workflowId, cancellationToken);
        if (storedWorkflow is null)
        {
            return Results.BadRequest(new { error = $"Workflow '{workflowId}' has no published version." });
        }

        var definition = LoadWorkflowDefinition(storedWorkflow, out var deserializeError);
        if (definition is null)
        {
            return Results.BadRequest(new { error = deserializeError });
        }

        var signalReceivedAtUtc = DateTimeOffset.UtcNow;
        var signalEnvelopeJson = BuildExternalSignalEnvelope(
            signalSource,
            idempotencyKey.Trim(),
            signalReceivedAtUtc,
            request.Payload);

        var runSnapshot = await runService.StartRunAsync(
            new StartWorkflowRunCommand
            {
                WorkflowId = workflowId,
                WorkflowVersion = storedWorkflow.Version,
                Definition = definition,
                InputJson = signalEnvelopeJson,
                TriggerType = WorkflowRunTriggerType.ExternalSignal,
                TriggerSource = signalSource,
                TriggerPayloadJson = signalEnvelopeJson,
                IdempotencyKey = idempotencyKey.Trim()
            },
            cancellationToken);

        var response = ToWorkflowRunResponse(runSnapshot);
        app.Logger.LogInformation(
            "External signal run request processed: source {SignalSource}, workflow {WorkflowId}, version {WorkflowVersion}, run {RunId}, deduplicated {WasDeduplicated}.",
            signalSource,
            workflowId,
            storedWorkflow.Version,
            runSnapshot.RunId,
            runSnapshot.WasDeduplicated);
        if (runSnapshot.WasDeduplicated)
        {
            return Results.Ok(response);
        }

        return Results.Accepted($"/runs/{runSnapshot.RunId}", response);
    });

app.MapPost(
    "/runs",
    async Task<IResult> (
        CreateWorkflowRunRequest request,
        IWorkflowDefinitionRepository repository,
        IWorkflowRunService runService,
        CancellationToken cancellationToken) =>
    {
        var inlineDefinitionProvided = request.Definition.HasValue;
        if (inlineDefinitionProvided && request.Definition!.Value.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "definition must be a JSON object." });
        }

        WorkflowDefinition? definition = null;
        int? workflowVersion = request.WorkflowVersion;
        var workflowId = string.IsNullOrWhiteSpace(request.WorkflowId)
            ? null
            : request.WorkflowId.Trim();

        if (inlineDefinitionProvided)
        {
            definition = DeserializeWorkflowDefinition(request.Definition!.Value, out var deserializeError);
            if (definition is null)
            {
                return Results.BadRequest(new { error = deserializeError });
            }
        }

        if (definition is null && !string.IsNullOrWhiteSpace(workflowId))
        {
            StoredWorkflowDefinition? storedWorkflow;
            if (workflowVersion.HasValue)
            {
                storedWorkflow = await repository.GetByIdAndVersionAsync(workflowId, workflowVersion.Value, cancellationToken);
            }
            else
            {
                storedWorkflow = await repository.GetPublishedByIdAsync(workflowId, cancellationToken)
                                 ?? await repository.GetLatestByIdAsync(workflowId, cancellationToken);
            }

            if (storedWorkflow is null)
            {
                return workflowVersion.HasValue
                    ? Results.NotFound(new { error = $"Workflow '{workflowId}' version {workflowVersion.Value} was not found." })
                    : Results.NotFound(new { error = $"Workflow '{workflowId}' was not found." });
            }

            definition = LoadWorkflowDefinition(storedWorkflow, out var deserializeError);
            if (definition is null)
            {
                return Results.BadRequest(new { error = deserializeError });
            }

            workflowVersion = storedWorkflow.Version;
        }

        if (definition is null)
        {
            return Results.BadRequest(new { error = "Either workflowId or definition is required." });
        }

        var hasInput = request.Input.HasValue &&
                       request.Input.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
        var runSnapshot = await runService.StartRunAsync(
            new StartWorkflowRunCommand
            {
                WorkflowId = workflowId,
                WorkflowVersion = workflowVersion,
                Definition = definition,
                InputJson = hasInput ? request.Input!.Value.GetRawText() : null,
                TriggerType = WorkflowRunTriggerType.Manual,
                TriggerSource = "api/runs",
                TriggerPayloadJson = hasInput ? request.Input!.Value.GetRawText() : null
            },
            cancellationToken);

        app.Logger.LogInformation(
            "Manual run request accepted: workflow {WorkflowId}, version {WorkflowVersion}, run {RunId}.",
            workflowId,
            workflowVersion,
            runSnapshot.RunId);

        return Results.Accepted($"/runs/{runSnapshot.RunId}", ToWorkflowRunResponse(runSnapshot));
    });

app.MapGet(
    "/runs/{runId}",
    (string runId, IWorkflowRunService runService) =>
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Results.BadRequest(new { error = "runId is required." });
        }

        var runSnapshot = runService.GetRun(runId.Trim());
        if (runSnapshot is null)
        {
            return Results.NotFound(new { error = $"Run '{runId}' was not found." });
        }

        return Results.Ok(ToWorkflowRunResponse(runSnapshot));
    });

app.MapPost(
    "/runs/{runId}/resume",
    async Task<IResult> (
        string runId,
        IWorkflowRunService runService,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Results.BadRequest(new { error = "runId is required." });
        }

        var runSnapshot = await runService.ResumeRunAsync(runId.Trim(), cancellationToken);
        if (runSnapshot is null)
        {
            return Results.NotFound(new { error = $"Run '{runId}' was not found." });
        }

        return Results.Accepted($"/runs/{runSnapshot.RunId}", ToWorkflowRunResponse(runSnapshot));
    });

app.MapGet(
    "/runs/{runId}/nodes",
    (string runId, IWorkflowRunService runService) =>
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Results.BadRequest(new { error = "runId is required." });
        }

        var nodeResults = runService.GetRunNodes(runId.Trim());
        if (nodeResults is null)
        {
            return Results.NotFound(new { error = $"Run '{runId}' was not found." });
        }

        return Results.Ok(nodeResults);
    });

app.MapGet(
    "/runs/{runId}/artifacts",
    (string runId, IWorkflowArtifactStore artifactStore) =>
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Results.BadRequest(new { error = "runId is required." });
        }

        return Results.Ok(artifactStore.ListRunArtifacts(runId.Trim()));
    });

app.MapGet(
    "/runs/{runId}/artifacts/{artifactId}",
    (string runId, string artifactId, IWorkflowArtifactStore artifactStore) =>
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Results.BadRequest(new { error = "runId is required." });
        }

        if (string.IsNullOrWhiteSpace(artifactId))
        {
            return Results.BadRequest(new { error = "artifactId is required." });
        }

        var artifact = artifactStore.TryReadArtifact(runId.Trim(), artifactId.Trim());
        if (artifact is null)
        {
            return Results.NotFound(new { error = $"Artifact '{artifactId}' was not found for run '{runId}'." });
        }

        return Results.Ok(artifact);
    });

app.Run();

static WorkflowResponse ToWorkflowResponse(StoredWorkflowDefinition storedWorkflow)
{
    using var document = JsonDocument.Parse(storedWorkflow.DefinitionJson);
    return new WorkflowResponse(
        WorkflowId: storedWorkflow.WorkflowId,
        Name: storedWorkflow.Name,
        Version: storedWorkflow.Version,
        Status: storedWorkflow.Status,
        Definition: document.RootElement.Clone(),
        UpdatedAtUtc: storedWorkflow.UpdatedAtUtc,
        PublishedAtUtc: storedWorkflow.PublishedAtUtc);
}

static WorkflowDefinition? LoadWorkflowDefinition(
    StoredWorkflowDefinition storedWorkflow,
    out string? error)
{
    using var document = JsonDocument.Parse(storedWorkflow.DefinitionJson);
    var definition = DeserializeWorkflowDefinition(document.RootElement, out error);
    if (definition is null)
    {
        return null;
    }

    if (!string.IsNullOrWhiteSpace(definition.Name))
    {
        return definition;
    }

    return new WorkflowDefinition
    {
        SchemaVersion = definition.SchemaVersion,
        Name = storedWorkflow.Name,
        Nodes = definition.Nodes,
        Edges = definition.Edges
    };
}

static WorkflowDefinition? DeserializeWorkflowDefinition(
    JsonElement definitionElement,
    out string? error)
{
    try
    {
        var definition = definitionElement.Deserialize<WorkflowDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (definition is null)
        {
            error = "definition payload cannot be deserialized.";
            return null;
        }

        error = null;
        return definition;
    }
    catch (Exception exception)
    {
        error = $"definition payload cannot be deserialized: {exception.Message}";
        return null;
    }
}

static int? ParseOptionalVersionQuery(HttpRequest httpRequest, out string? error)
{
    error = null;
    if (!httpRequest.Query.TryGetValue("version", out var rawVersion) ||
        string.IsNullOrWhiteSpace(rawVersion.ToString()))
    {
        return null;
    }

    if (!int.TryParse(rawVersion.ToString(), out var parsedVersion) || parsedVersion <= 0)
    {
        error = "version query parameter must be a positive integer.";
        return null;
    }

    return parsedVersion;
}

static string ResolveImportedWorkflowName(
    WorkflowProfilePackDocument profilePack,
    string? requestedName)
{
    var name = requestedName?.Trim();
    if (!string.IsNullOrWhiteSpace(name))
    {
        return name;
    }

    name = profilePack.Metadata.Name?.Trim();
    if (!string.IsNullOrWhiteSpace(name))
    {
        return name;
    }

    name = profilePack.Definition.Name?.Trim();
    return string.IsNullOrWhiteSpace(name)
        ? "Imported Workflow Profile"
        : name;
}

static WorkflowRunResponse ToWorkflowRunResponse(WorkflowRunSnapshot snapshot)
{
    var totalNodes = snapshot.NodeResults.Count;
    var succeededNodes = snapshot.NodeResults.Count(node => node.Status == WorkflowNodeRunStatus.Succeeded);
    var failedNodes = snapshot.NodeResults.Count(node => node.Status == WorkflowNodeRunStatus.Failed);
    var pendingNodes = snapshot.NodeResults.Count(node => node.Status == WorkflowNodeRunStatus.Pending);
    var runningNodes = snapshot.NodeResults.Count(node => node.Status == WorkflowNodeRunStatus.Running);
    var skippedNodes = snapshot.NodeResults.Count(node => node.Status == WorkflowNodeRunStatus.Skipped);

    return new WorkflowRunResponse(
        RunId: snapshot.RunId,
        WorkflowId: snapshot.WorkflowId,
        WorkflowVersion: snapshot.WorkflowVersion,
        WorkflowName: snapshot.WorkflowName,
        TriggerType: snapshot.TriggerType,
        TriggerSource: snapshot.TriggerSource,
        TriggerPayloadJson: snapshot.TriggerPayloadJson,
        IdempotencyKey: snapshot.IdempotencyKey,
        WasDeduplicated: snapshot.WasDeduplicated,
        CanResume: snapshot.CanResume,
        CheckpointedAtUtc: snapshot.CheckpointedAtUtc,
        Status: snapshot.Status,
        CreatedAtUtc: snapshot.CreatedAtUtc,
        StartedAtUtc: snapshot.StartedAtUtc,
        CompletedAtUtc: snapshot.CompletedAtUtc,
        Error: snapshot.Error,
        OutputJson: snapshot.OutputJson,
        TotalNodes: totalNodes,
        SucceededNodes: succeededNodes,
        FailedNodes: failedNodes,
        PendingNodes: pendingNodes,
        RunningNodes: runningNodes,
        SkippedNodes: skippedNodes,
        Logs: snapshot.Logs);
}

static McpSettingsResponse ToMcpSettingsResponse(McpSettingsReadResult result)
{
    return new McpSettingsResponse(
        ConfigPath: result.ConfigPath,
        Exists: result.Exists,
        Settings: result.Settings);
}

static string BuildExternalSignalEnvelope(
    string source,
    string idempotencyKey,
    DateTimeOffset receivedAtUtc,
    JsonElement? payload)
{
    JsonElement? payloadValue = null;
    if (payload.HasValue &&
        payload.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
    {
        payloadValue = payload.Value;
    }

    return JsonSerializer.Serialize(new
    {
        triggerType = "external_signal",
        source,
        idempotencyKey,
        receivedAtUtc,
        payload = payloadValue
    });
}

static IReadOnlyList<Type> DiscoverWorkflowNodeExecutorTypes()
{
    var executorInterface = typeof(IWorkflowNodeExecutor);
    return typeof(Program).Assembly
        .GetReferencedAssemblies()
        .Select(Assembly.Load)
        .Prepend(typeof(IWorkflowNodeExecutor).Assembly)
        .DistinctBy(assembly => assembly.FullName)
        .SelectMany(SafeGetTypes)
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => executorInterface.IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();
}

static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException exception)
    {
        return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
    }
}

public sealed record WorkflowSummaryResponse(
    string WorkflowId,
    string Name,
    int Version,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    int? PublishedVersion,
    DateTimeOffset? PublishedAtUtc);

public sealed record WorkflowResponse(
    string WorkflowId,
    string Name,
    int Version,
    string Status,
    JsonElement Definition,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc);

public sealed record NodeTypeResponse(
    string Type,
    string Label,
    string Description,
    int Inputs,
    int Outputs,
    string Pack,
    string Source,
    bool UsesModel,
    IReadOnlyList<NodeTypePortResponse> InputPorts,
    IReadOnlyList<NodeTypePortResponse> OutputPorts,
    IReadOnlyList<NodeTypeConfigFieldResponse> ConfigFields);

public sealed record NodeTypePortResponse(
    string Id,
    string Label,
    string Channel,
    bool Required,
    IReadOnlyList<string> AcceptedKinds,
    string? ControlConditionKey,
    string? Description,
    IReadOnlyList<string> ProducesKinds,
    string? FallbackDescription,
    IReadOnlyList<string> ExampleSources,
    bool AllowMultiple);

public sealed record NodeTypeConfigFieldResponse(
    string Key,
    string Label,
    string FieldType,
    string? Description,
    bool Required,
    bool Multiline,
    string? Placeholder,
    string? DefaultValue,
    IReadOnlyList<NodeTypeConfigFieldOptionResponse> Options);

public sealed record NodeTypeConfigFieldOptionResponse(
    string Value,
    string Label);

public sealed class UpsertWorkflowRequest
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public JsonElement Definition { get; init; }
}

public sealed class ExecuteWorkflowPreviewRequest
{
    public JsonElement Definition { get; init; }
    public JsonElement? Input { get; init; }
}

public sealed class CreateWorkflowRunRequest
{
    public string? WorkflowId { get; init; }
    public int? WorkflowVersion { get; init; }
    public JsonElement? Definition { get; init; }
    public JsonElement? Input { get; init; }
}

public sealed class CreateSignalRunRequest
{
    public string? WorkflowId { get; init; }
    public string? IdempotencyKey { get; init; }
    public JsonElement? Payload { get; init; }
}

public sealed class ImportWorkflowProfilePackRequest
{
    public JsonElement ProfilePack { get; init; }

    public string? Name { get; init; }
}

public sealed class SaveMcpSettingsRequest
{
    public McpToolInvokerOptions? Settings { get; init; }
}

public sealed class TestMcpProfileRequest
{
    public string? ServerProfile { get; init; }
    public int? TimeoutSeconds { get; init; }
}

public sealed record McpSettingsResponse(
    string ConfigPath,
    bool Exists,
    McpToolInvokerOptions Settings);

public sealed record TestMcpProfileResponse(
    string Profile,
    string ServerType,
    int ToolCount,
    IReadOnlyList<McpToolDescriptor> Tools,
    JsonObject Metadata);

public sealed record WorkflowRunResponse(
    string RunId,
    string? WorkflowId,
    int? WorkflowVersion,
    string WorkflowName,
    WorkflowRunTriggerType TriggerType,
    string? TriggerSource,
    string? TriggerPayloadJson,
    string? IdempotencyKey,
    bool WasDeduplicated,
    bool CanResume,
    DateTimeOffset? CheckpointedAtUtc,
    WorkflowRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Error,
    string? OutputJson,
    int TotalNodes,
    int SucceededNodes,
    int FailedNodes,
    int PendingNodes,
    int RunningNodes,
    int SkippedNodes,
    IReadOnlyList<WorkflowExecutionLogItem> Logs);
