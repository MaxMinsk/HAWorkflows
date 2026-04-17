using System.Text.Json;
using Workflow.Api.Runs;
using Workflow.Engine.Definitions;
using Workflow.Engine.Runtime;
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
builder.Services.Configure<WorkflowRunServiceOptions>(builder.Configuration.GetSection("WorkflowRuns"));
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
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Workflow.Api",
}));

app.MapGet("/metrics", (WorkflowRunMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

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
            UpdatedAtUtc: summary.UpdatedAtUtc)));
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

app.MapPost(
    "/workflows",
    async Task<IResult> (
        UpsertWorkflowRequest request,
        IWorkflowDefinitionRepository repository,
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

        var savedWorkflow = await repository.SaveAsync(
            request.Id,
            request.Name.Trim(),
            request.Definition.GetRawText(),
            cancellationToken);

        app.Logger.LogInformation(
            "Workflow saved: workflowId {WorkflowId}, version {Version}, name {WorkflowName}.",
            savedWorkflow.WorkflowId,
            savedWorkflow.Version,
            savedWorkflow.Name);

        return Results.Ok(ToWorkflowResponse(savedWorkflow));
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

        var storedWorkflow = await repository.GetLatestByIdAsync(workflowId, cancellationToken);
        if (storedWorkflow is null)
        {
            return Results.NotFound(new { error = $"Workflow '{workflowId}' was not found." });
        }

        using var document = JsonDocument.Parse(storedWorkflow.DefinitionJson);
        var definition = DeserializeWorkflowDefinition(document.RootElement, out var deserializeError);
        if (definition is null)
        {
            return Results.BadRequest(new { error = deserializeError });
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition = new WorkflowDefinition
            {
                SchemaVersion = definition.SchemaVersion,
                Name = storedWorkflow.Name,
                Nodes = definition.Nodes,
                Edges = definition.Edges
            };
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
            "External signal run request processed: source {SignalSource}, workflow {WorkflowId}, run {RunId}, deduplicated {WasDeduplicated}.",
            signalSource,
            workflowId,
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
            var storedWorkflow = await repository.GetLatestByIdAsync(workflowId, cancellationToken);
            if (storedWorkflow is null)
            {
                return Results.NotFound(new { error = $"Workflow '{workflowId}' was not found." });
            }

            using var document = JsonDocument.Parse(storedWorkflow.DefinitionJson);
            definition = DeserializeWorkflowDefinition(document.RootElement, out var deserializeError);
            if (definition is null)
            {
                return Results.BadRequest(new { error = deserializeError });
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                definition = new WorkflowDefinition
                {
                    SchemaVersion = definition.SchemaVersion,
                    Name = storedWorkflow.Name,
                    Nodes = definition.Nodes,
                    Edges = definition.Edges
                };
            }
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
                Definition = definition,
                InputJson = hasInput ? request.Input!.Value.GetRawText() : null,
                TriggerType = WorkflowRunTriggerType.Manual,
                TriggerSource = "api/runs",
                TriggerPayloadJson = hasInput ? request.Input!.Value.GetRawText() : null
            },
            cancellationToken);

        app.Logger.LogInformation(
            "Manual run request accepted: workflow {WorkflowId}, run {RunId}.",
            workflowId,
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

app.Run();

static WorkflowResponse ToWorkflowResponse(StoredWorkflowDefinition storedWorkflow)
{
    using var document = JsonDocument.Parse(storedWorkflow.DefinitionJson);
    return new WorkflowResponse(
        WorkflowId: storedWorkflow.WorkflowId,
        Name: storedWorkflow.Name,
        Version: storedWorkflow.Version,
        Definition: document.RootElement.Clone(),
        UpdatedAtUtc: storedWorkflow.UpdatedAtUtc);
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
        WorkflowName: snapshot.WorkflowName,
        TriggerType: snapshot.TriggerType,
        TriggerSource: snapshot.TriggerSource,
        TriggerPayloadJson: snapshot.TriggerPayloadJson,
        IdempotencyKey: snapshot.IdempotencyKey,
        WasDeduplicated: snapshot.WasDeduplicated,
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

public sealed record WorkflowSummaryResponse(
    string WorkflowId,
    string Name,
    int Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkflowResponse(
    string WorkflowId,
    string Name,
    int Version,
    JsonElement Definition,
    DateTimeOffset UpdatedAtUtc);

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
    public JsonElement? Definition { get; init; }
    public JsonElement? Input { get; init; }
}

public sealed class CreateSignalRunRequest
{
    public string? WorkflowId { get; init; }
    public string? IdempotencyKey { get; init; }
    public JsonElement? Payload { get; init; }
}

public sealed record WorkflowRunResponse(
    string RunId,
    string? WorkflowId,
    string WorkflowName,
    WorkflowRunTriggerType TriggerType,
    string? TriggerSource,
    string? TriggerPayloadJson,
    string? IdempotencyKey,
    bool WasDeduplicated,
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
