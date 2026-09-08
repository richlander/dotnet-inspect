using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Commands;

internal sealed record CloneCandidateSeedJson(
    CloneCandidateSeedKind Kind,
    MetadataTypeDefinitionName? Type,
    MemberAnchor? Member);

internal sealed record CloneCandidateProvenanceJson(
    string Kind,
    string? PackageId = null,
    string? PackageVersion = null,
    string? Tfm = null,
    string? Rid = null,
    string? Framework = null,
    string? FrameworkVersion = null,
    string? Project = null,
    string? ResolverSource = null,
    string? ContentRef = null,
    string? Digest = null,
    string? DeclaredName = null)
{
    internal static CloneCandidateProvenanceJson Create(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                new(
                    "Package",
                    package.PackageId,
                    package.PackageVersion,
                    package.Tfm,
                    package.Rid),
            AssemblyResolutionProvenance.PlatformAsset platform =>
                new(
                    "Platform",
                    Framework: platform.Framework,
                    FrameworkVersion: platform.FrameworkVersion,
                    ResolverSource: platform.ResolverSource),
            AssemblyResolutionProvenance.ProjectAsset project =>
                new(
                    "Project",
                    Tfm: project.Tfm,
                    Rid: project.Rid,
                    Project: project.Project),
            AssemblyResolutionProvenance.LocalAsset local =>
                new("Local", ResolverSource: local.ResolverSource),
            AssemblyResolutionProvenance.DesignatedAsset designated =>
                new(
                    "Designated",
                    ResolverSource: designated.ResolverSource),
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(
                    "Embedded",
                    ContentRef: embedded.ContentRef,
                    Digest: embedded.Digest,
                    DeclaredName: embedded.DeclaredName),
            _ => throw new InvalidOperationException(
                "Unknown assembly-resolution provenance."),
        };
}

internal sealed record CloneCandidateParticipantJson(
    int Ordinal,
    AssemblyReferenceIdentity Assembly,
    CloneCandidateProvenanceJson Provenance,
    Guid? ModuleVersionId)
{
    internal static CloneCandidateParticipantJson Create(
        CloneCandidateParticipantIdentity participant) =>
        new(
            participant.Ordinal,
            participant.Assembly,
            CloneCandidateProvenanceJson.Create(participant.Provenance),
            participant.ModuleVersionId);
}

internal sealed record CloneCandidateMethodJson(
    CloneCandidateParticipantJson Participant,
    Guid ModuleVersionId,
    int MethodDefinitionToken,
    string AddressDisplay)
{
    internal static CloneCandidateMethodJson Create(
        CloneCandidateMethodIdentity method) =>
        new(
            CloneCandidateParticipantJson.Create(method.Participant),
            method.ModuleVersionId,
            method.MethodDefinitionToken,
            method.AddressDisplay.ToString());
}

internal sealed record CloneCandidateRowJson(
    int Rank,
    CloneCandidateMethodJson Left,
    CloneCandidateMethodJson Right,
    CloneCandidateSimilarity Similarity,
    CloneCandidateNameQualification? NameQualification)
{
    internal static CloneCandidateRowJson Create(CloneCandidateRow row) =>
        new(
            row.Rank,
            CloneCandidateMethodJson.Create(row.Left),
            CloneCandidateMethodJson.Create(row.Right),
            row.Similarity,
            row.NameQualification);
}

internal sealed record CloneCandidateFailureJson(
    StructuralCloneSearchFailureKind Kind,
    AssemblyReferenceIdentity? Subject,
    string Detail)
{
    internal static CloneCandidateFailureJson Create(
        CloneCandidateFailure failure) =>
        new(failure.Kind, failure.Subject, failure.Detail.ToString());
}

internal sealed record CloneCandidateAnalysisBlockerJson(
    StructuralCloneRetrievalBlockerKind Kind,
    string Detail)
{
    internal static CloneCandidateAnalysisBlockerJson Create(
        CloneCandidateAnalysisBlocker blocker) =>
        new(blocker.Kind, blocker.Detail.ToString());
}

internal sealed record CloneCandidateSeedCoverageJson(
    CloneCandidateMethodJson Seed,
    StructuralCloneRetrievalDisposition Disposition,
    int RankedPairs,
    int SuppressedPairs,
    ImmutableArray<CloneCandidateAnalysisBlockerJson> Blockers,
    ImmutableArray<CloneCandidateFailureJson> Failures,
    bool IsComplete)
{
    internal static CloneCandidateSeedCoverageJson Create(
        CloneCandidateSeedCoverage coverage) =>
        new(
            CloneCandidateMethodJson.Create(coverage.Seed),
            coverage.Disposition,
            coverage.RankedPairs,
            coverage.SuppressedPairs,
            [.. coverage.Blockers.Select(
                CloneCandidateAnalysisBlockerJson.Create)],
            [.. coverage.Failures.Select(CloneCandidateFailureJson.Create)],
            coverage.IsComplete);
}

internal sealed record CloneCandidateLibraryCoverageJson(
    CloneCandidateParticipantJson Participant,
    StructuralCloneParticipantMembership Membership,
    bool Admitted,
    int CandidateMethods,
    int DiscoveredMethods,
    long RetrievalPairs,
    long NameComparisonWork,
    ImmutableArray<CloneCandidateFailureJson> Failures,
    ImmutableArray<CloneCandidateAnalysisBlockerJson> AnalysisBlockers,
    bool IsComplete)
{
    internal static CloneCandidateLibraryCoverageJson Create(
        CloneCandidateLibraryCoverage coverage) =>
        new(
            CloneCandidateParticipantJson.Create(coverage.Participant),
            coverage.Membership,
            coverage.Admitted,
            coverage.CandidateMethods,
            coverage.DiscoveredMethods,
            coverage.RetrievalPairs,
            coverage.NameComparisonWork,
            [.. coverage.Failures.Select(CloneCandidateFailureJson.Create)],
            [.. coverage.AnalysisBlockers.Select(
                CloneCandidateAnalysisBlockerJson.Create)],
            coverage.IsComplete);
}

internal sealed record CloneCandidateOutputDocument(
    int SchemaVersion,
    CloneCandidateSeedJson Seed,
    StructuralCloneCandidateBreadth Breadth,
    StructuralCloneCandidateDiscovery Discovery,
    double NameSimilarityThreshold,
    WorkspaceStructuralCloneSearchLimits Limits,
    bool ScopeChangedDuringSearch,
    bool CoverageIsComplete,
    ImmutableArray<CloneCandidateRowJson> Rows,
    ImmutableArray<CloneCandidateSeedCoverageJson> Seeds,
    ImmutableArray<CloneCandidateLibraryCoverageJson> Libraries,
    CloneCandidateReceipt Receipt)
{
    internal static CloneCandidateOutputDocument Create(
        CloneCandidateDocument document,
        ImmutableArray<CloneCandidateRow> rows) =>
        new(
            document.SchemaVersion,
            new(
                document.Seed.Kind,
                document.Seed.Type,
                document.Seed.Member),
            document.Breadth,
            document.Discovery,
            document.NameSimilarityThreshold,
            document.Limits,
            document.ScopeChangedDuringSearch,
            document.CoverageIsComplete,
            [.. rows.Select(CloneCandidateRowJson.Create)],
            [.. document.Seeds.Select(
                CloneCandidateSeedCoverageJson.Create)],
            [.. document.Libraries.Select(
                CloneCandidateLibraryCoverageJson.Create)],
            document.Receipt);
}

internal static class CloneCandidatesCommand
{
    internal static bool IsSelected(IEnumerable<string>? sections) =>
        sections?.Contains(
            SectionNames.CloneCandidates,
            StringComparer.OrdinalIgnoreCase) == true;

    internal static bool ValidatePredicateSelection(
        CloneCandidateQueryOptions query,
        IEnumerable<string>? sections)
    {
        if (!query.HasPredicates || IsSelected(sections))
            return true;

        CommandError.Write(
            $"--where Breadth=... and Discovery=... target section '{SectionNames.CloneCandidates}'. "
            + "Omit -S or select that section.");
        return false;
    }

    internal static bool TryCreateMemberSeed(
        string assemblyPath,
        ApiType type,
        ApiMember member,
        out StructuralCloneSearchSeed.Member? seed,
        out string? error)
    {
        seed = null;
        error = null;
        if (type.DefinitionName is not { } definitionName)
        {
            error =
                $"Type '{type.FullName}' has no exact metadata definition identity for Clone Candidates.";
            return false;
        }
        if (!ApiMemberMetadataAnchor.TryResolve(
                assemblyPath,
                type,
                member,
                out MemberAnchor? anchor,
                out string? anchorError))
        {
            error = $"{anchorError} Clone Candidates requires exact metadata identity.";
            return false;
        }

        seed = new StructuralCloneSearchSeed.Member(
            definitionName,
            anchor);
        return true;
    }

    internal static async Task<int> ExecuteAsync(
        ResolvedAssemblyReference assembly,
        string assemblyPath,
        StructuralCloneSearchSeed seed,
        CloneCandidateQueryOptions query,
        CloneCandidateOutputOptions output,
        CloneCandidateWorkspaceOptions workspaceOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(workspaceOptions);

        if (output.SelectedSectionCount != 1)
        {
            CommandError.Write(
                $"Section '{SectionNames.CloneCandidates}' must be selected alone.");
            return 1;
        }
        if (output.Tree || output.Mermaid)
        {
            CommandError.Write(
                $"Section '{SectionNames.CloneCandidates}' is a ranked row set and cannot render as a tree or Mermaid graph.");
            return 1;
        }
        if (output.Print || output.Value || output.Urls || output.Paths)
        {
            CommandError.Write(
                $"Section '{SectionNames.CloneCandidates}' supports rows, columns, fields, counts, and structured output, not payload extraction.");
            return 1;
        }

        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeReadResult scopeRead =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (scopeRead is WorkspaceScopeReadResult.Unavailable unavailable)
        {
            CommandError.Write(
                "The Clone Candidates Workspace scope could not be read.",
                [unavailable.RuntimeFailure.ToString()]);
            return 1;
        }
        WorkspaceScopeRevision revision =
            ((WorkspaceScopeReadResult.Available)scopeRead)
                .Snapshot.Revision;

        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(assemblyPath)
            {
                RootPackageDirectory =
                    workspaceOptions.RootPackageDirectory,
                ProjectAssetsPath = workspaceOptions.ProjectAssetsPath,
                TargetFramework = workspaceOptions.TargetFramework,
                PackageSourceOptions = workspaceOptions.SourceOptions,
                UsePackageSourcePolicy =
                    workspaceOptions.RootPackageDirectory is not null,
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        var participant = new AssemblyContextParticipant(assembly, policy);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        var snapshot = new StructuralCloneParticipantSnapshot(
            revision,
            revision,
            [
                new StructuralCloneParticipantEntry(
                    group,
                    participant,
                    StructuralCloneParticipantMembership
                        .ContainingLibrary),
            ]);
        WorkspaceStructuralCloneSearchResult result =
            WorkspaceStructuralCloneSearchQuery.Execute(
                new WorkspaceStructuralCloneSearchInput(
                    snapshot,
                    seed,
                    query.Breadth,
                    query.Discovery),
                cancellationToken);
        CloneCandidatePresentationResult presentation =
            CloneCandidatePresentation.Create(result);
        return Write(presentation, output);
    }

    static int Write(
        CloneCandidatePresentationResult result,
        CloneCandidateOutputOptions options)
    {
        if (result is not CloneCandidatePresentationResult.Available available)
        {
            WriteFailure(result);
            return 1;
        }

        CloneCandidateDocument document = available.Document;
        ImmutableArray<CloneCandidateRow> selectedRows =
            [.. RowWindow.Apply(options.Rows, document.Rows)];
        List<CloneCandidateRowView> rowViews =
            [.. document.Rows.Select(CloneCandidateRowView.Create)];
        var view = new CloneCandidateView
        {
            Breadth = document.Breadth.ToString(),
            Discovery = document.Discovery.ToString(),
            NameSimilarityThreshold =
                document.NameSimilarityThreshold,
            ParticipantCount = document.Libraries.Length,
            Coverage = document.CoverageIsComplete
                ? "Complete"
                : "Incomplete",
            ResultLimit = document.ResultLimitReached
                ? $"{document.Receipt.ReturnedPairs.ToString(CultureInfo.InvariantCulture)} returned; "
                    + $"{document.ResultLimitOmittedPairs.ToString(CultureInfo.InvariantCulture)} omitted by the result limit"
                : $"{document.Receipt.ReturnedPairs.ToString(CultureInfo.InvariantCulture)} returned",
            Work =
                $"{document.Receipt.SeedMethods.ToString(CultureInfo.InvariantCulture)} seeds; "
                + $"{document.Receipt.DiscoveredMethods.ToString(CultureInfo.InvariantCulture)} discovered candidates; "
                + $"{document.Receipt.RetrievalPairs.ToString(CultureInfo.InvariantCulture)} retrieval pairs",
            Candidates = rowViews,
        };
        var table = new CloneCandidateTableView
        {
            Candidates = rowViews,
        };

        if (options.Count)
        {
            CountOutput.WriteCount(selectedRows.Length);
        }
        else if (options.Format == OutputFormat.Json
            && options.Columns is not { Length: > 0 }
            && options.Fields is not { Length: > 0 })
        {
            CloneCandidateOutputDocument json =
                CloneCandidateOutputDocument.Create(document, selectedRows);
            JsonOutputHelper.Write(
                json,
                CloneCandidateOutputJsonContext.Default
                    .CloneCandidateOutputDocument,
                CloneCandidateOutputCompactJsonContext.Default
                    .CloneCandidateOutputDocument,
                options.CompactJson);
        }
        else if (options.Format == OutputFormat.Json)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    if (options.Fields is { Length: > 0 })
                    {
                        MarkoutWriter summaryWriter = MarkoutWriter.Create(
                            writer,
                            formatter,
                            new MarkoutWriterOptions
                            {
                                HeadingLevelOffset =
                                    writerOptions.HeadingLevelOffset,
                                Projection = writerOptions.Projection,
                            });
                        summaryWriter.WriteSectionStart(2, "Summary");
                        summaryWriter.WriteFields(
                            SummaryFields(document).AsSpan());
                        summaryWriter.WriteSectionEnd();
                        summaryWriter.Flush();
                    }
                    MarkoutSerializer.Serialize(
                        table,
                        writer,
                        formatter,
                        CloneCandidateViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson,
                options.Rows);
        }
        else if (options.Format
            is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl)
        {
            WriteTabularContext(document);
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                !options.NoHeader,
                options.Format == OutputFormat.Tsv,
                options.Format == OutputFormat.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(
                        table,
                        writer,
                        formatter,
                        CloneCandidateViewContext.Default,
                        writerOptions),
                options.Rows);
        }
        else
        {
            var writerOptions =
                OutputFormatter.CreateWindowedOptions(
                    options.Rows,
                    options.Columns,
                    options.Fields);
            IMarkoutFormatter formatter =
                options.Format == OutputFormat.PlainText
                    ? new PlainTextFormatter()
                    : new MarkdownFormatter();
            MarkoutSerializer.Serialize(
                view,
                Console.Out,
                formatter,
                CloneCandidateViewContext.Default,
                writerOptions);
        }

        if (!document.CoverageIsComplete)
        {
            WriteIncomplete(document);
            return 1;
        }

        return 0;
    }

    static MarkoutField[] SummaryFields(CloneCandidateDocument document) =>
    [
        new("Breadth", document.Breadth.ToString()),
        new("Discovery", document.Discovery.ToString()),
        new(
            "Name similarity threshold",
            document.NameSimilarityThreshold.ToString(
                "0.###",
                CultureInfo.InvariantCulture)),
        new(
            "Participant scope",
            document.Libraries.Length.ToString(
                CultureInfo.InvariantCulture)),
        new(
            "Coverage",
            document.CoverageIsComplete ? "Complete" : "Incomplete"),
        new(
            "Result limit",
            document.ResultLimitReached
                ? $"{document.Receipt.ReturnedPairs.ToString(CultureInfo.InvariantCulture)} returned; "
                    + $"{document.ResultLimitOmittedPairs.ToString(CultureInfo.InvariantCulture)} omitted by the result limit"
                : $"{document.Receipt.ReturnedPairs.ToString(CultureInfo.InvariantCulture)} returned"),
        new(
            "Work",
            $"{document.Receipt.SeedMethods.ToString(CultureInfo.InvariantCulture)} seeds; "
            + $"{document.Receipt.DiscoveredMethods.ToString(CultureInfo.InvariantCulture)} discovered candidates; "
            + $"{document.Receipt.RetrievalPairs.ToString(CultureInfo.InvariantCulture)} retrieval pairs"),
    ];

    static void WriteTabularContext(CloneCandidateDocument document)
    {
        CommandError.WriteNote(
            $"Clone Candidates: Breadth={document.Breadth}; "
            + $"Discovery={document.Discovery}; "
            + $"name threshold={document.NameSimilarityThreshold.ToString("0.###", CultureInfo.InvariantCulture)}.");
        CommandError.WriteNote(
            $"Workspace snapshot: {document.Libraries.Length.ToString(CultureInfo.InvariantCulture)} participant(s); "
            + "this CLI slice supplies the selected exact library only.");
        CommandError.WriteNote(
            $"Coverage: {(document.CoverageIsComplete ? "complete" : "incomplete")}; "
            + $"returned={document.Receipt.ReturnedPairs.ToString(CultureInfo.InvariantCulture)}; "
            + $"ranked={document.Receipt.RankedPairs.ToString(CultureInfo.InvariantCulture)}; "
            + $"retrieval pairs={document.Receipt.RetrievalPairs.ToString(CultureInfo.InvariantCulture)}.");
    }

    static void WriteIncomplete(CloneCandidateDocument document)
    {
        List<string> details =
        [
            .. document.Seeds
                .Where(seed => !seed.IsComplete)
                .SelectMany(seed =>
                    seed.Failures.Select(failure =>
                        $"Seed {seed.Seed.AddressDisplay}: {failure.Kind}: {failure.Detail}")),
            .. document.Seeds
                .Where(seed => !seed.IsComplete)
                .SelectMany(seed =>
                    seed.Blockers.Select(blocker =>
                        $"Seed {seed.Seed.AddressDisplay}: {blocker.Kind}: {blocker.Detail}")),
            .. document.Libraries
                .Where(library => !library.IsComplete)
                .SelectMany(library =>
                    library.Failures.Select(failure =>
                        $"Library {library.Participant.Assembly.Name}: {failure.Kind}: {failure.Detail}")),
            .. document.Libraries
                .Where(library => !library.IsComplete)
                .SelectMany(library =>
                    library.AnalysisBlockers.Select(blocker =>
                        $"Library {library.Participant.Assembly.Name}: {blocker.Kind}: {blocker.Detail}")),
        ];
        CommandError.Write(
            "The Clone Candidates result is incomplete.",
            [.. details]);
    }

    static void WriteFailure(CloneCandidatePresentationResult result)
    {
        switch (result)
        {
            case CloneCandidatePresentationResult.Rejected rejected:
                CommandError.Write(
                    $"Clone Candidates could not open '{rejected.SeedLibrary.Name}'.",
                    [$"{rejected.Kind}: {rejected.Detail}"]);
                break;
            case CloneCandidatePresentationResult.Failed failed:
                CommandError.Write(
                    $"Clone Candidates failed for '{failed.SeedLibrary.Name}'.",
                    [$"{failed.Failure.Kind}: {failed.Failure.Detail}"]);
                break;
            case CloneCandidatePresentationResult.Unrepresentable rejected:
                CommandError.Write(
                    $"Clone Candidates could not represent '{rejected.Subject.Name}'.",
                    [$"{rejected.Kind}: {rejected.Detail}"]);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Clone Candidates presentation outcome.");
        }
    }
}

internal sealed record CloneCandidateWorkspaceOptions(
    string? RootPackageDirectory,
    string? ProjectAssetsPath,
    string? TargetFramework,
    NuGetSourceOptions? SourceOptions);

internal sealed record CloneCandidateOutputOptions(
    OutputFormat Format,
    bool CompactJson,
    bool NoHeader,
    bool Count,
    string[]? Columns,
    string[]? Fields,
    RowWindow? Rows,
    int SelectedSectionCount,
    bool Tree,
    bool Mermaid,
    bool Print,
    bool Value,
    bool Urls,
    bool Paths)
{
    internal static CloneCandidateOutputOptions From(
        LibraryOptions options) =>
        new(
            options.Format,
            CompactJson: false,
            options.NoHeader,
            options.Count,
            options.Columns,
            options.Fields,
            options.Rows,
            options.IncludeSections?.Count ?? 0,
            options.Tree,
            Mermaid: false,
            options.Print,
            options.Value,
            options.Urls,
            options.Paths);

    internal static CloneCandidateOutputOptions From(
        ApiOptions options) =>
        new(
            options.Format,
            options.CompactJson,
            options.NoHeader,
            options.Count,
            options.Columns,
            options.Fields,
            options.Rows,
            options.IncludeSections?.Count ?? 0,
            options.Tree,
            options.MermaidOutput || options.EmbeddedMermaid,
            options.Print,
            options.Value,
            options.Urls,
            options.Paths);
}
