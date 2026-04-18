using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Workflow.Persistence.WorkflowDefinitions;

/// <summary>
/// Что: SQLite-реализация хранилища workflow definitions.
/// Зачем: для MVP нужен простой локальный persistence слой без внешней БД.
/// Как: хранит каждое сохранение как draft-версию, а publish помечает одну версию workflow как активную.
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
                status TEXT NOT NULL DEFAULT 'draft',
                definition_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                published_utc TEXT NULL,
                PRIMARY KEY(workflow_id, version)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await EnsureColumnAsync(connection, "status", "TEXT NOT NULL DEFAULT 'draft'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "published_utc", "TEXT NULL", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowDefinitionSummary>> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                d.workflow_id,
                d.name,
                d.version,
                d.status,
                d.updated_utc,
                published.version,
                published.published_utc
            FROM workflow_definitions d
            INNER JOIN (
                SELECT workflow_id, MAX(version) AS max_version
                FROM workflow_definitions
                GROUP BY workflow_id
            ) latest
              ON latest.workflow_id = d.workflow_id
             AND latest.max_version = d.version
            LEFT JOIN (
                SELECT workflow_id, MAX(version) AS version, MAX(published_utc) AS published_utc
                FROM workflow_definitions
                WHERE status = 'published'
                GROUP BY workflow_id
            ) published
              ON published.workflow_id = d.workflow_id
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
                    Status: reader.GetString(3),
                    UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    PublishedVersion: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    PublishedAtUtc: reader.IsDBNull(6)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
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
            SELECT workflow_id, name, version, status, definition_json, updated_utc, published_utc
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

        return ReadStoredWorkflow(reader);
    }

    public async Task<StoredWorkflowDefinition?> GetByIdAndVersionAsync(
        string workflowId,
        int version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Workflow version must be positive.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT workflow_id, name, version, status, definition_json, updated_utc, published_utc
            FROM workflow_definitions
            WHERE workflow_id = $workflowId
              AND version = $version
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workflowId", workflowId);
        command.Parameters.AddWithValue("$version", version);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadStoredWorkflow(reader)
            : null;
    }

    public async Task<StoredWorkflowDefinition?> GetPublishedByIdAsync(string workflowId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT workflow_id, name, version, status, definition_json, updated_utc, published_utc
            FROM workflow_definitions
            WHERE workflow_id = $workflowId
              AND status = 'published'
            ORDER BY version DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workflowId", workflowId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadStoredWorkflow(reader)
            : null;
    }

    public async Task<StoredWorkflowDefinition> SaveDraftAsync(
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
                    status,
                    definition_json,
                    updated_utc,
                    published_utc
                ) VALUES (
                    $workflowId,
                    $version,
                    $name,
                    $status,
                    $definitionJson,
                    $updatedUtc,
                    NULL
                );
                """;
            insertCommand.Parameters.AddWithValue("$workflowId", targetWorkflowId);
            insertCommand.Parameters.AddWithValue("$version", version);
            insertCommand.Parameters.AddWithValue("$name", name.Trim());
            insertCommand.Parameters.AddWithValue("$status", WorkflowDefinitionStatuses.Draft);
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
            Status: WorkflowDefinitionStatuses.Draft,
            DefinitionJson: definitionJson,
            UpdatedAtUtc: updatedAtUtc,
            PublishedAtUtc: null);
    }

    public async Task<StoredWorkflowDefinition?> PublishAsync(
        string workflowId,
        int version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Workflow version must be positive.");
        }

        var publishedAtUtc = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.Transaction = transaction;
            existsCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM workflow_definitions
                WHERE workflow_id = $workflowId
                  AND version = $version;
                """;
            existsCommand.Parameters.AddWithValue("$workflowId", workflowId.Trim());
            existsCommand.Parameters.AddWithValue("$version", version);
            var scalar = await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is not long count || count == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        await using (var resetCommand = connection.CreateCommand())
        {
            resetCommand.Transaction = transaction;
            resetCommand.CommandText =
                """
                UPDATE workflow_definitions
                SET status = 'draft',
                    published_utc = NULL
                WHERE workflow_id = $workflowId;
                """;
            resetCommand.Parameters.AddWithValue("$workflowId", workflowId.Trim());
            await resetCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var publishCommand = connection.CreateCommand())
        {
            publishCommand.Transaction = transaction;
            publishCommand.CommandText =
                """
                UPDATE workflow_definitions
                SET status = 'published',
                    published_utc = $publishedUtc
                WHERE workflow_id = $workflowId
                  AND version = $version;
                """;
            publishCommand.Parameters.AddWithValue("$workflowId", workflowId.Trim());
            publishCommand.Parameters.AddWithValue("$version", version);
            publishCommand.Parameters.AddWithValue("$publishedUtc", publishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            await publishCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Workflow definition published: workflowId {WorkflowId}, version {Version}.",
            workflowId.Trim(),
            version);

        return await GetByIdAndVersionAsync(workflowId.Trim(), version, cancellationToken).ConfigureAwait(false);
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

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(workflow_definitions);";

        await using (var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE workflow_definitions ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StoredWorkflowDefinition ReadStoredWorkflow(SqliteDataReader reader)
    {
        return new StoredWorkflowDefinition(
            WorkflowId: reader.GetString(0),
            Name: reader.GetString(1),
            Version: reader.GetInt32(2),
            Status: reader.GetString(3),
            DefinitionJson: reader.GetString(4),
            UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            PublishedAtUtc: reader.IsDBNull(6)
                ? null
                : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
