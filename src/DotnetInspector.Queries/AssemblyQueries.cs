using System.Collections.Immutable;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Borrowed assembly content and host-issued identity for metadata queries.</summary>
public sealed record AssemblyQueryContext
{
    public AssemblyQueryContext(PdbContext metadata, FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(subject);
        Metadata = metadata;
        Subject = subject;
    }

    public PdbContext Metadata { get; }

    public FindingSubject Subject { get; }
}

public sealed record AssemblyInfoResult(AssemblyInfo Assembly);

public sealed record AssemblyPresenceResult(PresenceFlags Presence);

public sealed record AssemblyReferencesResult(
    ImmutableArray<AssemblyReference> References,
    FindingInspection<AssemblyReference> Inspection);

public static class AssemblyInfoQuery
{
    public static QueryDefinition<AssemblyQueryContext, AssemblyInfoResult> Definition { get; } =
        new(
            "assembly.info",
            QueryCost.NetworkFree,
            QueryCapabilities.None,
            static (context, _, _) => ValueTask.FromResult(
                QueryResult<AssemblyInfoResult>.Succeeded(
                    new AssemblyInfoResult(context.Metadata.ExtractAssemblyInfo()))));
}

public static class AssemblyPresenceQuery
{
    public static QueryDefinition<AssemblyQueryContext, AssemblyPresenceResult> Definition { get; } =
        new(
            "assembly.presence",
            QueryCost.NetworkFree,
            QueryCapabilities.None,
            static (context, _, _) => ValueTask.FromResult(
                QueryResult<AssemblyPresenceResult>.Succeeded(
                    new AssemblyPresenceResult(context.Metadata.ScanPresenceFlags()))));
}

public static class AssemblyReferencesQuery
{
    public static QueryDefinition<AssemblyQueryContext, AssemblyReferencesResult> Definition { get; } =
        new(
            "assembly.references",
            QueryCost.NetworkFree,
            QueryCapabilities.None,
            static (context, _, _) =>
            {
                var references = context.Metadata
                    .ExtractAssemblyReferences()
                    .ToImmutableArray();
                var inspection = MetadataFindings.InspectAssemblyReferences(
                    references,
                    context.Subject);
                return ValueTask.FromResult(
                    QueryResult<AssemblyReferencesResult>.Succeeded(
                        new AssemblyReferencesResult(references, inspection)));
            });
}

public static class AssemblyQueryCatalog
{
    public static QueryCatalog<AssemblyQueryContext> Default { get; } = Create();

    private static QueryCatalog<AssemblyQueryContext> Create()
    {
        QueryCatalogBuilder<AssemblyQueryContext> builder = new();
        return builder
            .Add(AssemblyInfoQuery.Definition)
            .Add(AssemblyPresenceQuery.Definition)
            .Add(AssemblyReferencesQuery.Definition)
            .Build();
    }
}
