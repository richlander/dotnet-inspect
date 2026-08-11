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
            AssemblyIntegrationsEntry? Entry)> entries)
    {
        _resultByPath = entries.ToDictionary(
            entry => System.IO.Path.GetFullPath(entry.Path),
            entry => new ParticipantResult(entry.Assembly, entry.Entry),
            StringComparer.OrdinalIgnoreCase);
    }

    internal AssemblyIntegrationsEntry? EntryFor(string path)
        => ResultFor(path).Entry;

    internal ResolvedAssemblyReference? AssemblyForInspection(string path)
        => ResultFor(path).Assembly;

    internal bool IntegrationEvidenceUnavailableFor(string path)
        => ResultFor(path).Entry is null;

    ParticipantResult ResultFor(string path)
        => _resultByPath.TryGetValue(
            System.IO.Path.GetFullPath(path),
            out ParticipantResult? result)
                ? result
                : throw new InspectionQueryException(
                    $"Assembly context integrations did not produce a result for '{path}'.");

    sealed record ParticipantResult(
        ResolvedAssemblyReference? Assembly,
        AssemblyIntegrationsEntry? Entry);
}

internal static class AssemblyContextIntegrationsRunner
{
    internal static AssemblyContextIntegrationsBatch? RunIfRequested(
        HashSet<InspectionQueryDefinition>? requestedQueries,
        InspectionQueryRegistry<AssemblyContextGroup> queryRegistry,
        IEnumerable<AssemblyContextIntegrationsInput> inputs,
        InspectionTrace? trace = null,
        AssemblyContextGroupOptions? groupOptions = null)
    {
        ArgumentNullException.ThrowIfNull(queryRegistry);
        if (requestedQueries?.Remove(
                AssemblyContextIntegrationsQuery.Definition) != true)
        {
            return null;
        }

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
                    Entry: (AssemblyIntegrationsEntry?)null)));
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
        HashSet<InspectionQueryDefinition> requested =
            [AssemblyContextIntegrationsQuery.Definition];
        HashSet<InspectionQueryDefinition> closure =
            queryRegistry.ExpandRequired(requested);
        trace?.RecordQueryClosure(closure);
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution =
            trace is null ? null : trace.RecordQueryExecution;
        InspectionQueryResults queryResults = queryRegistry.Run(
            requested,
            group,
            recordExecution);
        AssemblyContextIntegrationsResult result = queryResults.Get(
            AssemblyContextIntegrationsQuery.Definition);
        ResolvedAssemblyReference?[] retainedAssemblies = roots
            .Zip(
                result.Assemblies,
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
                        Entry: (AssemblyIntegrationsEntry?)null);
                }

                int index = managedIndex++;
                return (
                    Path: candidate.Input.Path,
                    Assembly: (ResolvedAssemblyReference?)
                        retainedAssemblies[index],
                    Entry: (AssemblyIntegrationsEntry?)
                        result.Assemblies[index]);
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
