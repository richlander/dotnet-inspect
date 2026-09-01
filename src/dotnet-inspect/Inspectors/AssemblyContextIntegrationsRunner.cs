using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record AssemblyContextIntegrationsInput(
    string Path,
    AssemblyResolutionProvenance Provenance);

internal sealed class AssemblyContextIntegrationsBatch
{
    readonly Dictionary<string, ParticipantResult> _resultByPath;

    internal AssemblyContextIntegrationsBatch(
        IEnumerable<(
            string Path,
            ResolvedAssemblyReference? Assembly,
            AssemblyIntegrationsEntry? IntegrationsEntry,
            AssemblyIntegrationOpportunitiesEntry? OpportunitiesEntry)> entries)
    {
        _resultByPath = entries.ToDictionary(
            entry => System.IO.Path.GetFullPath(entry.Path),
            entry => new ParticipantResult(
                entry.Assembly,
                entry.IntegrationsEntry,
                entry.OpportunitiesEntry),
            StringComparer.OrdinalIgnoreCase);
    }

    internal AssemblyIntegrationsEntry? EntryFor(string path)
        => ResultFor(path).IntegrationsEntry;

    internal AssemblyIntegrationOpportunitiesEntry?
        OpportunitiesEntryFor(string path)
        => ResultFor(path).OpportunitiesEntry;

    internal ResolvedAssemblyReference? AssemblyForInspection(string path)
        => ResultFor(path).Assembly;

    ParticipantResult ResultFor(string path)
        => _resultByPath.TryGetValue(
            System.IO.Path.GetFullPath(path),
            out ParticipantResult? result)
                ? result
                : throw new InspectionQueryException(
                    $"Assembly context integrations did not produce a result for '{path}'.");

    sealed record ParticipantResult(
        ResolvedAssemblyReference? Assembly,
        AssemblyIntegrationsEntry? IntegrationsEntry,
        AssemblyIntegrationOpportunitiesEntry? OpportunitiesEntry);
}

internal static class AssemblyContextIntegrationsRunner
{
    internal static AssemblyContextIntegrationsBatch? RunIfRequested(
        HashSet<InspectionQueryDefinition>? requestedQueries,
        InspectionQueryCatalog<AssemblyContextGroup> queryCatalog,
        IEnumerable<AssemblyContextIntegrationsInput> inputs,
        InspectionTrace? trace = null,
        AssemblyContextGroupOptions? groupOptions = null)
    {
        ArgumentNullException.ThrowIfNull(queryCatalog);
        HashSet<InspectionQueryDefinition> requested = requestedQueries?
            .Where(
                query =>
                    queryCatalog.RegisteredQueries.Contains(query))
            .ToHashSet() ?? [];
        if (requested.Count == 0)
        {
            return null;
        }
        requestedQueries!.ExceptWith(requested);

        AssemblyContextIntegrationsInput[] inputArray = [.. inputs];
        if (inputArray.Length == 0)
        {
            throw new InspectionQueryException(
                "Assembly context integrations requires at least one assembly.");
        }

        var candidates = inputArray
            .Select(input => (
                Input: input,
                Assembly: TryCreateManagedParticipant(input)))
            .ToArray();
        var roots = candidates
            .Where(candidate => candidate.Assembly is not null)
            .Select(candidate => candidate.Assembly!)
            .ToArray();
        if (roots.Length == 0)
        {
            return new AssemblyContextIntegrationsBatch(
                inputArray.Select(input => (
                    input.Path,
                    Assembly: (ResolvedAssemblyReference?)null,
                    IntegrationsEntry: (AssemblyIntegrationsEntry?)null,
                    OpportunitiesEntry:
                        (AssemblyIntegrationOpportunitiesEntry?)null)));
        }

        var sourcePolicies = roots
            .Select(root => (
                Assembly: root,
                Policy: (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(root.Path!))))
            .ToArray();
        var groupPolicy =
            new SourceRelativeAssemblyGroupBindingPolicy(sourcePolicies);
        var participants = roots
            .Select(root => new AssemblyContextParticipant(root, groupPolicy))
            .ToArray();

        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(participants, groupOptions);
        InspectionQueryPlan<AssemblyContextGroup> plan =
            queryCatalog.Plan(requested);
        trace?.RecordQueryClosure(plan.Queries);
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution =
            trace is null ? null : trace.RecordQueryExecution;
        InspectionQueryResults queryResults = plan.Run(
            group,
            recordExecution);
        AssemblyContextIntegrationsResult integrationsResult = queryResults.Get(
            AssemblyContextIntegrationsQuery.Definition);
        AssemblyContextIntegrationOpportunitiesResult? opportunitiesResult =
            queryResults.TryGet(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                out AssemblyContextIntegrationOpportunitiesResult?
                    producedOpportunities)
                ? producedOpportunities
                : null;
        ResolvedAssemblyReference?[] retainedAssemblies = roots
            .Zip(
                integrationsResult.Assemblies,
                (root, entry) => entry
                    is AssemblyIntegrationsEntry.Rejected
                        ? null
                        : RetainAcquiredAssembly(group, root))
            .ToArray();

        int managedIndex = 0;
        return new AssemblyContextIntegrationsBatch(
            candidates.Select(candidate =>
            {
                if (candidate.Assembly is null)
                {
                    return (
                        Path: candidate.Input.Path,
                        Assembly: (ResolvedAssemblyReference?)null,
                        IntegrationsEntry:
                            (AssemblyIntegrationsEntry?)null,
                        OpportunitiesEntry:
                            (AssemblyIntegrationOpportunitiesEntry?)null);
                }

                int index = managedIndex++;
                return (
                    Path: candidate.Input.Path,
                    Assembly: (ResolvedAssemblyReference?)
                        retainedAssemblies[index],
                    IntegrationsEntry: (AssemblyIntegrationsEntry?)
                        integrationsResult.Assemblies[index],
                    OpportunitiesEntry:
                        opportunitiesResult is null
                            ? null
                            : opportunitiesResult.Assemblies[index]);
            }));
    }

    static ResolvedAssemblyReference RetainAcquiredAssembly(
        AssemblyContextGroup group,
        ResolvedAssemblyReference assembly)
    {
        AssemblyImageAccessResult<ResolvedAssemblyReference> retained =
            group.RetainAssemblyReference(assembly);
        return retained
            is AssemblyImageAccessResult<
                ResolvedAssemblyReference>.Available available
                    ? available.Value
                    : throw new InspectionQueryException(
                        $"Integrations participant '{assembly.Identity.Name}' could not retain its acquired image.");
    }

    static ResolvedAssemblyReference? TryCreateManagedParticipant(
        AssemblyContextIntegrationsInput input)
    {
        try
        {
            return ResolvedAssemblyReference.CreateFromPathIfManaged(
                input.Path,
                input.Provenance);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or UnsupportedMetadataFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            // The owning per-library inspection path reports the artifact
            // failure; it must not prevent valid group participants from
            // producing evidence.
            return null;
        }
    }
}
