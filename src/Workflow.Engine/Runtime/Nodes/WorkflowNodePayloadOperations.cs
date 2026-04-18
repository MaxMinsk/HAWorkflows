using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Engine.Runtime.Artifacts;

namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: общие операции с payload/config для node executors.
/// Зачем: избежать дублирования Parse/Merge/SetRemove логики между нодами.
/// Как: статические helper-методы для работы с JsonObject/JsonElement.
/// </summary>
public static class WorkflowNodePayloadOperations
{
    public const string DataEnvelopeSchemaVersion = "1.0";

    public static JsonObject ParseInputPayload(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return new JsonObject();
        }

        var parsedNode = JsonNode.Parse(inputJson);
        if (parsedNode is JsonObject parsedObject)
        {
            return CloneObject(parsedObject);
        }

        return new JsonObject
        {
            ["value"] = parsedNode?.DeepClone()
        };
    }

    public static JsonObject MergePayloads(IReadOnlyList<JsonObject> payloads)
    {
        var result = new JsonObject();
        foreach (var payload in payloads)
        {
            foreach (var (key, value) in payload)
            {
                result[key] = value?.DeepClone();
            }
        }

        return result;
    }

    public static JsonObject CloneObject(JsonObject payload)
    {
        return (JsonObject)payload.DeepClone();
    }

    public static JsonObject CreateDataEnvelope(string kind, JsonObject payload)
    {
        return new JsonObject
        {
            ["kind"] = kind,
            ["schemaVersion"] = DataEnvelopeSchemaVersion,
            ["payload"] = payload.DeepClone()
        };
    }

    public static JsonObject CreateArtifactReference(WorkflowArtifactDescriptor artifact)
    {
        return new JsonObject
        {
            ["artifactId"] = artifact.ArtifactId,
            ["runId"] = artifact.RunId,
            ["nodeId"] = artifact.NodeId,
            ["name"] = artifact.Name,
            ["artifactType"] = artifact.ArtifactType,
            ["mediaType"] = artifact.MediaType,
            ["relativePath"] = artifact.RelativePath,
            ["uri"] = artifact.Uri,
            ["sizeBytes"] = artifact.SizeBytes,
            ["sha256"] = artifact.Sha256,
            ["createdAtUtc"] = artifact.CreatedAtUtc.ToString("O")
        };
    }

    public static JsonObject ReadDataEnvelopePayload(JsonObject envelope)
    {
        if (envelope.TryGetPropertyValue("payload", out var payloadNode) &&
            payloadNode is JsonObject payloadObject)
        {
            return CloneObject(payloadObject);
        }

        return CloneObject(envelope);
    }

    public static string? TryGetConfigString(JsonElement config, string propertyName)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!config.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static void ApplySetRemoveConfig(JsonElement config, JsonObject payload)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (config.TryGetProperty("set", out var setObject) && setObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in setObject.EnumerateObject())
            {
                payload[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        if (config.TryGetProperty("remove", out var removeArray) && removeArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in removeArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    payload.Remove(item.GetString() ?? string.Empty);
                }
            }
        }
    }
}
