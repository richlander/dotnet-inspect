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
            AssemblyIntegrationsEntry Entry)> entries)
    {
        _resultByPath = entries.ToDictionary(
            entry => System.IO.Path.GetFullPath(entry.Path),
            entry => new ParticipantResult(entry.Assembly, entry.Entry),
            StringComparer.OrdinalIgnoreCase);
    }

    internal AssemblyIntegrationsEntry EntryFor(string path)
        => ResultFor(path).Entry;

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
        AssemblyIntegrationsEntry Entry);
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

        var roots = inputArray
            .Select(input => ResolvedAssemblyReference.CreateFromPath(
                input.Path,
                input.Provenance))
            .ToArray();
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

        return new AssemblyContextIntegrationsBatch(
            inputArray
                .Zip(
                    retainedAssemblies,
                    static (input, assembly) => (input, assembly))
                .Zip(
                    result.Assemblies,
                    static (participant, entry) => (
                        participant.input.Path,
                        participant.assembly,
                        entry)));
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
}
