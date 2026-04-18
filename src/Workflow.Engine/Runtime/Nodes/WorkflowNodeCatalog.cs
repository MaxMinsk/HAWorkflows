namespace Workflow.Engine.Runtime.Nodes;

/// <summary>
/// Что: runtime-каталог node executors.
/// Зачем: дать auto-discovery + pack-based отбор нод (shared/local).
/// Как: принимает все executors из DI, фильтрует по Profile/EnabledPacks/DisabledPacks и строит словарь type->executor.
/// </summary>
public sealed class WorkflowNodeCatalog : IWorkflowNodeCatalog
{
    private readonly IReadOnlyDictionary<string, IWorkflowNodeExecutor> _executorsByType;
    private readonly IReadOnlyList<WorkflowNodeDescriptor> _descriptors;

    public WorkflowNodeCatalog(IEnumerable<IWorkflowNodeExecutor> executors, WorkflowNodeCatalogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(executors);
        options ??= new WorkflowNodeCatalogOptions();

        var isLocalProfile = string.Equals(options.Profile, "local", StringComparison.OrdinalIgnoreCase);
        var includeLocal = options.IncludeLocalNodes || isLocalProfile;
        var enabledPacks = NormalizePacks(options.EnabledPacks);
        var disabledPacks = NormalizePacks(options.DisabledPacks);
        var hasExplicitEnabledPacks = enabledPacks.Count > 0;
        if (hasExplicitEnabledPacks &&
            includeLocal &&
            !disabledPacks.Contains(WorkflowNodePacks.LocalDevelopment))
        {
            enabledPacks.Add(WorkflowNodePacks.LocalDevelopment);
        }

        var dictionary = new Dictionary<string, IWorkflowNodeExecutor>(StringComparer.Ordinal);
        foreach (var executor in executors)
        {
            var descriptor = executor.Descriptor;
            var descriptorPack = NormalizePack(descriptor.Pack);
            if (disabledPacks.Contains(descriptorPack))
            {
                continue;
            }

            if (hasExplicitEnabledPacks)
            {
                if (!enabledPacks.Contains(descriptorPack))
                {
                    continue;
                }
            }
            else if (descriptor.IsLocal && !includeLocal)
            {
                continue;
            }

            if (dictionary.ContainsKey(descriptor.Type))
            {
                throw new InvalidOperationException(
                    $"Duplicate workflow node type '{descriptor.Type}' registered in node catalog.");
            }

            dictionary[descriptor.Type] = executor;
        }

        _executorsByType = dictionary;
        _descriptors = dictionary.Values
            .Select(executor => executor.Descriptor)
            .OrderBy(descriptor => descriptor.IsLocal)
            .ThenBy(descriptor => descriptor.Pack, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Type, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<WorkflowNodeDescriptor> GetDescriptors() => _descriptors;

    public IReadOnlyCollection<string> GetSupportedNodeTypes() => _executorsByType.Keys.ToArray();

    public bool TryGetExecutor(string nodeType, out IWorkflowNodeExecutor executor)
    {
        return _executorsByType.TryGetValue(nodeType, out executor!);
    }

    private static HashSet<string> NormalizePacks(IEnumerable<string>? packs)
    {
        return packs?
                   .Select(NormalizePack)
                   .Where(pack => !string.IsNullOrWhiteSpace(pack))
                   .ToHashSet(StringComparer.Ordinal)
               ?? [];
    }

    private static string NormalizePack(string? pack)
    {
        return string.IsNullOrWhiteSpace(pack)
            ? WorkflowNodePacks.Core
            : pack.Trim();
    }
}
