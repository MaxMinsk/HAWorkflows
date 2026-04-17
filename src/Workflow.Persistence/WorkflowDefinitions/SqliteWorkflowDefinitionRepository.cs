using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Workflow.Persistence.WorkflowDefinitions;

/// <summary>
/// Что: SQLite-реализация хранилища workflow definitions.
/// Зачем: для MVP нужен простой локальный persistence слой без внешней БД.
/// Как: хранит каждое сохранение как новую версию и отдает последнюю версию для чтения.
/// </summary>
public sealed class SqliteWorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly string _databasePath;
    private readonly ILogger<SqliteWorkflowDefinitionRepository> _logger;

    public SqliteWorkflowDefinitionRepository(
        string databasePath,
        ILogger<SqliteWorkflowDefinitionRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureDatabaseDirectoryExists();
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS workflow_definitions (
                workflow_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                name TEXT NOT NULL,
                definition_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(workflow_id, version)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowDefinitionSummary>> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.workflow_id, d.name, d.version, d.updated_utc
            FROM workflow_definitions d
            INNER JOIN (
                SELECT workflow_id, MAX(version) AS max_version
                FROM workflow_definitions
                GROUP BY workflow_id
            ) latest
              ON latest.workflow_id = d.workflow_id
             AND latest.max_version = d.version
            ORDER BY d.updated_utc DESC;
            """;

        var result = new List<WorkflowDefinitionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new WorkflowDefinitionSummary(
                    WorkflowId: reader.GetString(0),
                    Name: reader.GetString(1),
                    Version: reader.GetInt32(2),
                    UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return result;
    }

    public async Task<StoredWorkflowDefinition?> GetLatestByIdAsync(string workflowId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT workflow_id, name, version, definition_json, updated_utc
            FROM workflow_definitions
            WHERE workflow_id = $workflowId
            ORDER BY version DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workflowId", workflowId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredWorkflowDefinition(
            WorkflowId: reader.GetString(0),
            Name: reader.GetString(1),
            Version: reader.GetInt32(2),
            DefinitionJson: reader.GetString(3),
            UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public async Task<StoredWorkflowDefinition> SaveAsync(
        string? workflowId,
        string name,
        string definitionJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workflow name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            throw new ArgumentException("Workflow definition JSON is required.", nameof(definitionJson));
        }

        var targetWorkflowId = string.IsNullOrWhiteSpace(workflowId)
            ? Guid.NewGuid().ToString("N")
            : workflowId.Trim();

        var updatedAtUtc = DateTimeOffset.UtcNow;
        var version = 1;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText =
                """
                SELECT MAX(version)
                FROM workflow_definitions
                WHERE workflow_id = $workflowId;
                """;
            versionCommand.Parameters.AddWithValue("$workflowId", targetWorkflowId);

            var scalar = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is long currentVersion)
            {
                version = checked((int)currentVersion + 1);
            }
        }

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO workflow_definitions (
                    workflow_id,
                    version,
                    name,
                    definition_json,
                    updated_utc
                ) VALUES (
                    $workflowId,
                    $version,
                    $name,
                    $definitionJson,
                    $updatedUtc
                );
                """;
            insertCommand.Parameters.AddWithValue("$workflowId", targetWorkflowId);
            insertCommand.Parameters.AddWithValue("$version", version);
            insertCommand.Parameters.AddWithValue("$name", name.Trim());
            insertCommand.Parameters.AddWithValue("$definitionJson", definitionJson);
            insertCommand.Parameters.AddWithValue("$updatedUtc", updatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Workflow definition saved: workflowId {WorkflowId}, version {Version}, name {Name}.",
            targetWorkflowId,
            version,
            name);

        return new StoredWorkflowDefinition(
            WorkflowId: targetWorkflowId,
            Name: name.Trim(),
            Version: version,
            DefinitionJson: definitionJson,
            UpdatedAtUtc: updatedAtUtc);
    }

    private void EnsureDatabaseDirectoryExists()
    {
        var directoryPath = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        Directory.CreateDirectory(directoryPath);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
