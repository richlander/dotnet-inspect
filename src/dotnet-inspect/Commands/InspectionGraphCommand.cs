using System.Collections.Immutable;
using System.Text.Json;

using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using Markout;

namespace DotnetInspector.Commands;

public static class InspectionGraphCommand
{
    public const string Name = "graph";
    public const string IntegrationsName = "integrations";

    static ImmutableArray<
        InspectionGraphRelationshipDescriptor> DefaultRelationships { get; } =
    [
        InspectionGraphIntegrationsCatalog.Extension,
        InspectionGraphIntegrationsCatalog.IntegrationObserved,
        InspectionGraphIntegrationsCatalog.IntegrationOpportunity,
    ];

    public static IReadOnlyList<string> SupportedRelationshipIds { get; } =
    [
        .. InspectionGraphIntegrationsCatalog.Relationships.Select(
            static relationship => relationship.Id),
    ];

    public static Task<int> ExecuteAsync(
        InspectionGraphOptions options,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            options,
            CreateLoadOptions(options),
            cancellationToken);

    internal static async Task<int> ExecuteAsync(
        InspectionGraphOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken = default,
        Func<
            WorkspaceContextLoadOutcome.Loaded,
            InspectionGraphInducedSetRequest,
            InspectionGraphDocument>? queryExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);

        if (!TryCreateMembers(options.Packages, out var members))
            return 1;

        if (!TryResolveRelationships(
                options.Relationships,
                out ImmutableArray<
                    InspectionGraphRelationshipDescriptor> relationships))
        {
            return 1;
        }
        using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                new WorkspaceContextInput
                {
                    Framework = options.Tfm,
                    Members = members,
                },
                loadOptions,
                cancellationToken).ConfigureAwait(false);
        if (outcome is WorkspaceContextLoadOutcome.Failed failed)
        {
            CommandError.Write(
                "The Integration graph workspace could not be loaded.",
                [
                    .. failed.Failures.Select(static failure =>
                        failure.MetadataRootReason is { } reason
                            ? $"{failure.Kind} ({reason}): "
                                + failure.Message
                            : $"{failure.Kind}: {failure.Message}"),
                ]);
            return 1;
        }

        var context = (WorkspaceContextLoadOutcome.Loaded)outcome;
        InspectionGraphSubject[] subjects =
        [
            .. context.Members
                .Select(static member => member.Realized)
                .OfType<RealizedMemberCoordinate.Package>()
                .Distinct()
                .Select(InspectionGraphSubject.ForRealizedPackage),
        ];
        var request = new InspectionGraphInducedSetRequest(
            subjects,
            relationships,
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);
        InspectionGraphDocument document =
            queryExecutor is null
                ? InspectionGraphIntegrationsQuery.Execute(context, request)
                : queryExecutor(context, request);
        return Write(document, options, context);
    }

    static WorkspaceContextLoadOptions CreateLoadOptions(
        InspectionGraphOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            IncludePrerelease = options.IncludePrerelease,
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    static bool TryCreateMembers(
        IReadOnlyList<string> packageSpecs,
        out WorkspaceMemberCoordinate[] members)
    {
        var parsed = new List<WorkspaceMemberCoordinate>(
            packageSpecs.Count);
        foreach (string packageSpec in packageSpecs)
        {
            (string name, string? version) package =
                DotnetInspector.Packages.PackageExtractor
                    .ParsePackageReference(packageSpec);
            if (string.IsNullOrWhiteSpace(package.name)
                || packageSpec.EndsWith(
                    ".nupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    $"Invalid package reference '{packageSpec}'. Use name or name@version.");
                members = [];
                return false;
            }

            string? version = string.Equals(
                    package.version,
                    "latest",
                    StringComparison.OrdinalIgnoreCase)
                ? null
                : package.version;
            parsed.Add(
                WorkspaceMemberCoordinate.Package(
                    package.name,
                    version));
        }

        members = [.. parsed];
        return true;
    }

    static bool TryResolveRelationships(
        IReadOnlyList<string> requested,
        out ImmutableArray<InspectionGraphRelationshipDescriptor>
            relationships)
    {
        if (requested.Count == 0)
        {
            relationships = DefaultRelationships;
            return true;
        }

        var byId =
            InspectionGraphIntegrationsCatalog.Relationships.ToDictionary(
                static relationship => relationship.Id,
                StringComparer.Ordinal);
        var selected =
            ImmutableArray.CreateBuilder<
                InspectionGraphRelationshipDescriptor>();
        foreach (string id in requested.Distinct(StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(id, out var relationship))
            {
                CommandError.Write(
                    $"Unknown Integration graph relationship '{id}'.");
                relationships = [];
                return false;
            }
            selected.Add(relationship);
        }

        relationships = selected.ToImmutable();
        return true;
    }

    static int Write(
        InspectionGraphDocument document,
        InspectionGraphOptions options,
        WorkspaceContextLoadOutcome.Loaded context)
    {
        var output = new InspectionGraphOutputAdapter(context);
        IReadOnlyList<InspectionGraphEdgeRow> rows =
            RowWindow.Apply(
                options.Rows,
                output.EdgeRows(document));
        bool incomplete = document.Failures.Length > 0;

        if (options.Count)
        {
            CountOutput.WriteCount(rows.Count);
        }
        else if (options.Tree)
        {
            output.WriteGraph(
                document,
                rows,
                new PlainTextFormatter(),
                includeGroupInNodeLabel: true);
        }
        else
        {
            switch (options.Format)
            {
                case OutputFormat.Json:
                    Console.WriteLine(
                        JsonSerializer.Serialize(
                            output.Json(
                                document,
                                rows),
                            InspectionGraphJsonContext.Default
                                .InspectionGraphJsonDocument));
                    break;
                case OutputFormat.Table:
                case OutputFormat.Tsv:
                    output.WriteTable(
                        rows,
                        options.Format,
                        options.NoHeader);
                    break;
                case OutputFormat.Jsonl:
                    InspectionGraphOutputAdapter.WriteJsonLines(rows);
                    break;
                case OutputFormat.Mermaid:
                    output.WriteGraph(
                        document,
                        rows,
                        new MermaidFormatter(),
                        includeGroupInNodeLabel: false);
                    break;
                case OutputFormat.PlainText:
                    output.WriteGraph(
                        document,
                        rows,
                        new PlainTextFormatter(),
                        includeGroupInNodeLabel: true);
                    break;
                default:
                    output.WriteMarkdown(
                        document,
                        rows,
                        options.EmbeddedMermaid);
                    break;
            }
        }

        if (incomplete)
        {
            CommandError.Write(
                "The Integration graph is incomplete.",
                [
                    .. output.FailureRows(document)
                        .GroupBy(
                            static failure =>
                                (failure.Failure, failure.Detail))
                        .Select(static group =>
                            $"{group.Key.Failure}: {group.Key.Detail} "
                            + $"({group.Count()} graph targets)"),
                ]);
            return 1;
        }

        return 0;
    }
}
