using ILInspector.CSharp;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal sealed record TypeDependencyScanDiagnostic(
    string Subject,
    CandidateOpenFailure Failure);

internal sealed record TypeDependencyExecutionResult(
    InspectionEnvelope<TypeDependencySectionResult>? Envelope,
    InspectionShare Share,
    IReadOnlyList<TypeDependencyScanDiagnostic> Diagnostics,
    bool IsAvailable)
{
    internal TypeDependencyResult Dependency =>
        Envelope!.Content.QueryResult.Dependency;

    internal IReadOnlyList<TypeDependencyRelationship> Relationships =>
        Envelope!.Content.RowSelection.Relationships;

    internal RowsCohortSemanticFailure<TypeDependencyRowSet>? RowSelectionFailure =>
        Envelope!.Content.RowSelection.Failure;

    internal static TypeDependencyExecutionResult Unavailable(
        InspectionShare share) =>
        new(
            Envelope: null,
            share,
            Diagnostics: [],
            IsAvailable: false);

    internal static TypeDependencyExecutionResult FromContent(
        TypeDependencySectionResult content,
        IReadOnlyList<TypeDependencyScanDiagnostic> diagnostics,
        bool isAvailable,
        InspectionShare share) =>
        Create(content, diagnostics, isAvailable, share);

    private static TypeDependencyExecutionResult Create(
        TypeDependencySectionResult content,
        IReadOnlyList<TypeDependencyScanDiagnostic> scanDiagnostics,
        bool isAvailable,
        InspectionShare share)
    {
        List<InspectionDiagnostic> diagnostics =
        [
            .. scanDiagnostics.Select(
                static diagnostic =>
                    TypeDependencyInspectionDiagnostics.ParticipantRejected(
                        diagnostic.Subject)),
        ];
        if (content.RowSelection.Failure is { } rowFailure)
        {
            diagnostics.Add(
                TypeDependencyInspectionDiagnostics.RowSelectionFailed(
                    rowFailure));
        }
        if (!isAvailable)
        {
            diagnostics.Add(TypeDependencyInspectionDiagnostics.Unavailable());
        }

        return new(
            new InspectionEnvelope<TypeDependencySectionResult>(
                content,
                share,
                diagnostics),
            share,
            scanDiagnostics,
            isAvailable);
    }
}

/// <summary>
/// Builds dependency graph data for the depends command.
/// </summary>
internal static class DependencyGraphService
{
    private const string TempDirPrefix = "inspect-depends";

    public static async Task<TypeDependencyExecutionResult>
        BuildTypeDependencyTreeAsync(
        HttpClient httpClient,
        DependsOptions options,
        VerboseLogger logger,
        CancellationToken cancellationToken = default,
        Func<string, InspectionShare>? shareProjection = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InspectionShare ShareFor(string typeName) =>
            shareProjection is null
                ? new InspectionShare.NonProjectable(
                    "share",
                    "Share projection was not requested.")
                : shareProjection(typeName);
        RowQueryResolutionResult<TypeDependencyRelationship>
            rowQueryResolution =
                TypeDependencyVocabulary.Resolve(
                    options.QueryPlan?.RelationshipRows
                        ?? options.TypeDependencyRowQuery
                        ?? RowQueryIntent.Empty);
        TypeDependencySectionPlan plan =
            new(
                options.TargetType,
                rowQueryResolution.Plan
                    ?? throw new InvalidOperationException(
                        "The canonical Type Dependency row query "
                            + "did not resolve."),
                options.QueryPlan?.MaximumDepth
                    ?? options.Depth);
        AssemblySetRequest request =
            options.ToAssemblySetRequest(TempDirPrefix);
        if (ConfiguredPackageSearchWorkspace.IsEligible(
                options.SourceSelection,
                request,
                options.Tfm))
        {
            await using ConfiguredPackageSearchWorkspace? workspace =
                await ConfiguredPackageSearchWorkspace.OpenAsync(
                    httpClient,
                    request,
                    options.Tfm!,
                    logger.Log,
                    cancellationToken).ConfigureAwait(false);
            if (workspace is null)
                return TypeDependencyExecutionResult.Unavailable(
                    ShareFor(options.TargetType));

            ConfiguredPackageSearchQueryResult<
                TypeDependencySectionResult>? execution =
                    await workspace.QuerySurfaceAsync(
                        context =>
                            TypeDependencySectionExecutor.Execute(
                                context.Group,
                                plan),
                        cancellationToken).ConfigureAwait(false);
            if (execution is null)
                return TypeDependencyExecutionResult.Unavailable(
                    ShareFor(options.TargetType));
            if (execution.Result is null)
            {
                return TypeDependencyExecutionResult.FromContent(
                    TypeDependencySectionResult.NotFound(),
                    [],
                    isAvailable: true,
                    ShareFor(options.TargetType));
            }

            PackageSearchQuerySources sources =
                execution.Sources
                ?? throw new InvalidOperationException(
                    "A package Root dependency result requires source correspondence.");
            IReadOnlyList<TypeDependencyScanDiagnostic> diagnostics =
                execution.Result.QueryResult.Participants
                    .OfType<
                        AssemblyContextTypeDependencyEntry.Rejected>()
                    .Select(
                        rejected =>
                            new TypeDependencyScanDiagnostic(
                                sources.SourceFor(
                                    rejected.Subject)
                                    .DiagnosticSubject,
                                rejected.Failure))
                    .ToArray();
            return TypeDependencyExecutionResult.FromContent(
                execution.Result,
                diagnostics,
                execution.Result.QueryResult.HasSurvivingParticipant,
                ShareFor(
                    execution.Result.QueryResult.Dependency.MatchedType
                        ?? options.TargetType));
        }

        using AssemblySet assemblySet =
            await AssemblySetResolver.CollectAsync(
                httpClient,
                request,
                logger.Log).ConfigureAwait(false);
        AssemblySetDiagnosticWriter.Write(assemblySet);

        cancellationToken.ThrowIfCancellationRequested();
        logger.Log(
            $"Scanning {assemblySet.Assemblies.Count} libraries for type {options.TargetType}");
        TypeDependencySectionResult content =
            TypeDependencySectionResult.NotFound();
        var fallbackDiagnostics =
            new List<TypeDependencyScanDiagnostic>();
        await using var inspectionWorkspace =
            new AssemblySetInspectionWorkspace();
        inspectionWorkspace.RunGroupWithTypedFailures(
            assemblySet,
            (group, entries) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                content =
                    TypeDependencySectionExecutor.Execute(
                        group,
                        plan);
                fallbackDiagnostics.AddRange(
                    content.QueryResult.Participants
                        .OfType<
                            AssemblyContextTypeDependencyEntry.Rejected>()
                        .Select(
                            rejected =>
                                new TypeDependencyScanDiagnostic(
                                    SearchAssemblySource
                                        .FromAssemblySet(
                                            entries.EntryFor(
                                                rejected.Subject))
                                        .DiagnosticSubject,
                                    rejected.Failure)));
                cancellationToken.ThrowIfCancellationRequested();
            },
            (entry, failure) =>
                fallbackDiagnostics.Add(
                    new TypeDependencyScanDiagnostic(
                        SearchAssemblySource
                            .FromAssemblySet(entry)
                            .DiagnosticSubject,
                        failure)));
        return TypeDependencyExecutionResult.FromContent(
            content,
            fallbackDiagnostics,
            assemblySet.Assemblies.Count == 0
                || content.QueryResult.HasSurvivingParticipant,
            ShareFor(
                content.QueryResult.Dependency.MatchedType
                    ?? options.TargetType));
    }

    public static Task<LibraryDependencyGraphResult> BuildLibraryDependencyTreeAsync(
        HttpClient httpClient,
        string libraryName,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        int? maxDepth = null,
        string? requestedTfm = null) =>
        BuildLibraryDependencyTreeAsync(
            httpClient,
            libraryName,
            sourceOptions,
            logger,
            maxDepth,
            CancellationToken.None,
            requestedTfm: requestedTfm);

    public static async Task<LibraryDependencyGraphResult> BuildLibraryDependencyTreeAsync(
        HttpClient httpClient,
        string libraryName,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        int? maxDepth,
        CancellationToken cancellationToken,
        bool traverseReferences = true,
        string? requestedTfm = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? assemblyPath = null;
        AssemblySetEntry? selectedAssembly = null;
        AssemblySet? ownedAssemblySet = null;

        try
        {
            if (File.Exists(libraryName)
                && !libraryName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest
                    {
                        Assemblies = [libraryName],
                        TempDirPrefix = TempDirPrefix,
                        CancellationToken = cancellationToken,
                    },
                    logger.Log);
                AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
                selectedAssembly =
                    ownedAssemblySet.Assemblies.FirstOrDefault();
                assemblyPath = selectedAssembly?.Path;
            }
            else if (EcosystemPackCatalog.ShouldSearchPlatform(libraryName))
            {
                if (requestedTfm is not null
                    && !PlatformResolver
                        .TryGetFrameworkSpecsForTargetFramework(
                            requestedTfm,
                            out _))
                {
                    return new LibraryDependencyGraphResult.Error(
                        $"Target framework '{requestedTfm}' cannot select an installed platform library.",
                        libraryName);
                }

                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest
                    {
                        PlatformAssemblies = [libraryName],
                        Tfm = requestedTfm,
                        TempDirPrefix = TempDirPrefix,
                        CancellationToken = cancellationToken,
                    },
                    logger.Log);
                if (ownedAssemblySet.Assemblies.Count > 0)
                {
                    AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
                    selectedAssembly = ownedAssemblySet.Assemblies[0];
                    assemblyPath = selectedAssembly.Path;
                }
                else
                {
                    ownedAssemblySet.Dispose();
                    ownedAssemblySet = null;
                }
            }

            if (assemblyPath == null)
            {
                logger.Log($"Resolving package: {libraryName}");
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest
                    {
                        Packages = [libraryName],
                        SourceOptions = sourceOptions,
                        Tfm = requestedTfm,
                        TempDirPrefix = TempDirPrefix,
                        PackageSelectionMode = requestedTfm is null
                            ? AssemblySetPackageSelectionMode
                                .LibAssembliesDescending
                            : AssemblySetPackageSelectionMode
                                .TargetFramework,
                        CancellationToken = cancellationToken,
                    },
                    logger.Log);

                selectedAssembly =
                    ownedAssemblySet.Assemblies.FirstOrDefault();
                assemblyPath = selectedAssembly?.Path;
                if (assemblyPath == null)
                {
                    AssemblySetDiagnosticWriter.Write(ownedAssemblySet, includeErrors: false);
                    var errorDiagnostic = ownedAssemblySet.Diagnostics
                        .FirstOrDefault(static d => d.Severity == AssemblySetDiagnosticSeverity.Error);
                    return new LibraryDependencyGraphResult.Error(
                        errorDiagnostic?.Message
                            ?? $"Could not resolve '{libraryName}' as a file, platform library, or NuGet package.",
                        libraryName);
                }

                AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ManagedMetadataIdentity? rootIdentity =
                AssemblyInspector.ExtractManagedMetadataIdentity(assemblyPath);
            var assemblyName = rootIdentity switch
            {
                ManagedMetadataIdentity.Assembly assembly =>
                    assembly.Identity.Name,
                ManagedMetadataIdentity.Module module =>
                    module.Name,
                _ => Path.GetFileNameWithoutExtension(assemblyPath),
            };

            if (!traverseReferences)
            {
                return rootIdentity is null
                    ? new LibraryDependencyGraphResult.NoMetadata(
                        assemblyName,
                        selectedAssembly?.SourceKind
                            ?? AssemblySetSourceKind.Assembly)
                    : new LibraryDependencyGraphResult.Empty(
                        assemblyName,
                        rootIdentity,
                        selectedAssembly?.SourceKind
                            ?? AssemblySetSourceKind.Assembly);
            }

            var (refs, _) =
                AssemblyInspector.ExtractReferenceIdentitiesAndCompany(
                    assemblyPath);
            if (refs.Count == 0)
            {
                if (rootIdentity is null)
                {
                    return new LibraryDependencyGraphResult.NoMetadata(
                        assemblyName,
                        selectedAssembly?.SourceKind
                            ?? AssemblySetSourceKind.Assembly);
                }

                return new LibraryDependencyGraphResult.Empty(
                    assemblyName,
                    rootIdentity,
                    selectedAssembly?.SourceKind
                        ?? AssemblySetSourceKind.Assembly);
            }
            if (rootIdentity is null)
            {
                throw new InvalidOperationException(
                    "An image without managed metadata cannot declare assembly references.");
            }
            LibraryMetadataService.AssemblyReferenceGraph referenceGraph =
                LibraryMetadataService.BuildTransitiveReferenceGraph(
                    refs,
                    assemblyPath,
                    rootIdentity,
                    logger,
                    maxDepth,
                    failOnReadError: false,
                    cancellationToken: cancellationToken);

            return new LibraryDependencyGraphResult.Graph(
                assemblyName,
                referenceGraph,
                selectedAssembly?.SourceKind
                    ?? AssemblySetSourceKind.Assembly);
        }
        finally
        {
            ownedAssemblySet?.Dispose();
        }
    }

}

internal abstract record LibraryDependencyGraphResult
{
    public sealed record Graph(
        string AssemblyName,
        LibraryMetadataService.AssemblyReferenceGraph ReferenceGraph,
        AssemblySetSourceKind SourceKind = AssemblySetSourceKind.Assembly) :
        LibraryDependencyGraphResult
    {
        public IReadOnlyList<AssemblyReferenceNode> References =>
            ReferenceGraph.Nodes;
    }

    public sealed record Empty(
        string AssemblyName,
        ManagedMetadataIdentity Identity,
        AssemblySetSourceKind SourceKind = AssemblySetSourceKind.Assembly) :
        LibraryDependencyGraphResult;

    public sealed record NoMetadata(
        string AssemblyName,
        AssemblySetSourceKind SourceKind = AssemblySetSourceKind.Assembly) :
        LibraryDependencyGraphResult;
    /// <summary>
    /// A resolution failure whose message embeds the caller's subject.
    /// </summary>
    /// <remarks>
    /// The subject is untrusted: an agent composes a <c>depends</c> invocation
    /// from a type or package name it read out of metadata, so a name carrying
    /// a bidi override or line separator reaches this message and then stderr.
    /// Containment lives on the record rather than at each writer, so a new
    /// call site cannot reopen it. <see cref="HintInput"/> stays raw: it is
    /// matched against namespace prefixes, not rendered (issue #3319).
    /// </remarks>
    public sealed record Error(string Message, string? HintInput = null) : LibraryDependencyGraphResult
    {
        public string Message { get; init; } = CSharpIdentifier.ContainRenderedText(Message);

        /// <inheritdoc cref="Error"/>
        public string? HintInput { get; init; } = HintInput;
    }
}
