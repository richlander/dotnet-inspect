using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using QuerySpace;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Interop.Package;

/// <summary>
/// The browser's package and platform acquisition surface.
/// </summary>
/// <remarks>
/// <para>
/// An export that inspects an assembly resolves an exact package/version/framework identity,
/// opens a <see cref="BrowserInspectionScope"/> over it, and hands that scope's
/// <c>AssemblyContextGroup</c> to a public product query that owns the session. No export — and
/// no helper this facade owns — opens an <c>AssemblyInspectionSession</c>, a
/// <c>MetadataSource</c>, an Analysis index, or a retained image descriptor.
/// </para>
/// <para>
/// Two other categories exist and say so in place: exports that read package content without
/// inspecting an assembly (package documents), and exports that touch no artifact at all
/// (type-name ranking and cache statistics).
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    /// <summary>
    /// The package type surface for one exact package/version/framework workspace, produced by
    /// <see cref="AssemblyContextApiSurfaceQuery"/> over the workspace's own group. The query owns
    /// every session and every accessibility bucket; this method adapts its typed models and
    /// composes no evidence, no classification, and no ordering of its own.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackage(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserPackageLoadResult result =
            await PackageSurfaceAsync(packageId, version, targetFramework);
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackageLoadResult);
    }

    [JSExport]
    public static async Task<string> QueryPackageRoot(string rootRequest)
    {
        if (!PackageRootReacquisitionRequest.TryDecode(
                rootRequest,
                out PackageRootReacquisitionRequest? request))
        {
            throw new ArgumentException(
                "The package Root request is invalid.",
                nameof(rootRequest));
        }

        BrowserPackageSurface surface =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                async deadline =>
                {
                    BrowserPackageCoordinate coordinate =
                        await BrowserPackageWorkspace.ReacquireAsync(
                            request,
                            deadline.Token).ConfigureAwait(false);
                    await using BrowserScopeLease<BrowserInspectionScope>
                        scopeLease =
                            await BrowserPackageWorkspace.OpenScopeAsync(
                                [coordinate],
                                deadline.Token).ConfigureAwait(false);
                    BrowserInspectionScope scope = scopeLease.Scope;
                    return BrowserPackageWireProjection.Project(
                        BrowserPackageSurfaceProjection.ProjectSurface(
                            scope,
                            scope.Coordinates[0]));
                },
                BrowserPackageWorkspace.PackageOperationTimeout);
        return JsonSerializer.Serialize(
            surface,
            BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }

    static async Task<BrowserPackageLoadResult> PackageSurfaceAsync(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserPackageRealizationResult result =
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                targetFramework);
        if (result is BrowserPackageRealizationResult.NotSettled notSettled)
        {
            return new(
                BrowserPackageWireProjection.Project(
                    notSettled.VersionSettlement),
                PackageInfo: null,
                Surface: null);
        }

        BrowserPackageRealization realization =
            ((BrowserPackageRealizationResult.Realized)result).Realization;
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                realization,
                CancellationToken.None);
        BrowserInspectionScope scope = scopeLease.Scope;
        return new(
            BrowserPackageWireProjection.Project(
                realization.VersionSettlement),
            BrowserPackageWireProjection.Project(
                realization.PackageInfo),
            BrowserPackageWireProjection.Project(
                BrowserPackageSurfaceProjection.ProjectSurface(
                    scope,
                    scope.Coordinates[0])));
    }

    /// <summary>
    /// Bounded public API summary for one exact package compile asset.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryLibraryApi(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        BrowserExactLibraryApiInspection inspection =
            BrowserPackageWireProjection.Project(
                await LibraryApiAsync(
                    packageId,
                    version,
                    targetFramework,
                    assemblyId));
        return JsonSerializer.Serialize(
            inspection,
            BrowserPackageJsonContext
                .Default
                .BrowserExactLibraryApiInspection);
    }

    static async Task<InspectionEnvelope<ExactLibraryApiInspectionResult>>
        LibraryApiAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        var request = new ExactLibraryApiInspectionRequest(
            packageId,
            version,
            targetFramework,
            assemblyId,
            ExactLibraryApiSelectionKind.AssetId);
        ExactLibraryApiInspectionExecution execution =
            scope.UsePackageAssemblyRoles(
            coordinate,
            (package, realization) =>
                ExactLibraryApiInspectionOperation.Execute(
                    package,
                    realization,
                    request,
                    BrowserApiSurfacePolicy.Limits));
        return execution.Inspection;
    }

    /// <summary>
    /// Normalized NuGet dependency evidence plus the selected compile assembly's direct
    /// references. Package parsing and compatible dependency-group selection belong to
    /// <see cref="PackageDependencyGroupsQuery"/>, normalization belongs to
    /// <see cref="PackageDependencyEvidenceQuery"/>, and the assembly-context query owns the
    /// metadata session. This method only adapts their typed results for the browser.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageDependencies(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        BrowserPackageDependencies dependencies = await PackageDependenciesAsync(
            packageId,
            version,
            targetFramework,
            assemblyId);
        return JsonSerializer.Serialize(
            dependencies,
            BrowserPackageJsonContext.Default.BrowserPackageDependencies);
    }

    /// <summary>
    /// Qualifies the current package surface population by direct AssemblyRef
    /// simple names through the shared Library Query envelope.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryLibraries(
        string packageId,
        string version,
        string targetFramework,
        string admittedAssetIdsJson,
        string requiredReferencesJson)
    {
        BrowserLibraryQueryInspection inspection =
            await QueryLibrariesAsync(
                packageId,
                version,
                targetFramework,
                admittedAssetIdsJson,
                requiredReferencesJson);
        return JsonSerializer.Serialize(
            inspection,
            BrowserPackageJsonContext.Default
                .BrowserLibraryQueryInspection);
    }

    private static async Task<BrowserLibraryQueryInspection>
        QueryLibrariesAsync(
        string packageId,
        string version,
        string targetFramework,
        string admittedAssetIdsJson,
        string requiredReferencesJson)
    {
        string[] admittedAssetIds =
            JsonSerializer.Deserialize(
                admittedAssetIdsJson,
                BrowserPackageJsonContext.Default.StringArray)
            ?? throw new ArgumentException(
                "The admitted Browser Library roster is required.",
                nameof(admittedAssetIdsJson));
        string[] requiredReferences =
            JsonSerializer.Deserialize(
                requiredReferencesJson,
                BrowserPackageJsonContext.Default.StringArray)
            ?? throw new ArgumentException(
                "Library Query references are required.",
                nameof(requiredReferencesJson));
        LibraryQueryPlanResult planResult = LibraryQuery.Plan(
            new(
                [
                    .. requiredReferences.Select(reference =>
                        new PortableQueryTerm(
                            LibraryQuery.ReferencesTermKey,
                            PortableQueryOperator.Equal,
                            reference)),
                ]));
        if (planResult is LibraryQueryPlanResult.Rejected rejected)
        {
            throw new ArgumentException(
                $"Library Query intent was rejected: "
                + $"{rejected.Failure.Reason} at "
                + $"{rejected.Failure.Location}.",
                nameof(requiredReferencesJson));
        }

        LibraryQueryPlan plan =
            ((LibraryQueryPlanResult.Accepted)planResult).Plan;
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant[] population =
            LibraryQueryPopulation(scope, admittedAssetIds);
        InspectionEnvelope<LibraryQueryDocument> envelope =
            population.Length == 0
                ? LibraryQueryInspection.ExecuteParticipants(
                    group: null,
                    population: [],
                    plan: plan)
                : scope.UseSurface(group =>
                    LibraryQueryInspection.ExecuteParticipants(
                        group,
                        [
                            .. population.Select(participant =>
                                new LibraryQueryParticipant(
                                    participant.Participant,
                                    participant.Asset.Path,
                                    participant.Coordinate.PackageId,
                                    participant.Coordinate.Version,
                                    AssemblySetSourceKind.Package,
                                    participant.Asset.TargetFramework)),
                        ],
                        plan));
        BrowserLibraryQueryInspection inspection =
            ProjectLibraryQuery(population, envelope);
        return inspection;
    }

    private static BrowserWorkspaceParticipant[] LibraryQueryPopulation(
        BrowserInspectionScope scope,
        IReadOnlyList<string> admittedAssetIds)
    {
        var participantsById =
            scope.SurfaceParticipants.ToDictionary(
                participant => participant.Asset.Id,
                StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var population =
            new List<BrowserWorkspaceParticipant>(admittedAssetIds.Count);
        foreach (string assetId in admittedAssetIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                assetId,
                nameof(admittedAssetIds));
            if (!seen.Add(assetId))
            {
                throw new ArgumentException(
                    "The admitted Browser Library roster contains a duplicate asset ID.",
                    nameof(admittedAssetIds));
            }
            if (!participantsById.TryGetValue(
                    assetId,
                    out BrowserWorkspaceParticipant? participant))
            {
                throw new ArgumentException(
                    "The admitted Browser Library roster contains an asset outside the current package surface.",
                    nameof(admittedAssetIds));
            }
            population.Add(participant);
        }
        return [.. population];
    }

    private static BrowserLibraryQueryInspection ProjectLibraryQuery(
        IReadOnlyList<BrowserWorkspaceParticipant> population,
        InspectionEnvelope<LibraryQueryDocument> envelope)
    {
        Dictionary<string, BrowserWorkspaceParticipant> participantsByPath =
            population.ToDictionary(
                participant => participant.Asset.Path,
                StringComparer.Ordinal);

        BrowserWorkspaceParticipant Participant(string path)
        {
            if (!participantsByPath.TryGetValue(
                    path,
                    out BrowserWorkspaceParticipant? participant))
            {
                throw new InvalidOperationException(
                    "Library Query returned a path outside the current "
                    + "package surface population.");
            }

            return participant;
        }

        LibraryQueryDocument content = envelope.Content;
        return new(
            new(
                [
                    .. content.Results.Select(result =>
                    {
                        BrowserWorkspaceParticipant participant =
                            Participant(result.Path.ToString());
                        return new BrowserLibraryQueryRow(
                            participant.Asset.Id,
                            result.Library.ToString(),
                            result.Path.ToString(),
                            result.Source.ToString(),
                            result.Version?.ToString(),
                            result.SourceKind.ToString(),
                            result.TargetFramework?.ToString(),
                            [
                                .. result.MatchedReferences.Select(
                                    reference => reference.ToString()),
                            ]);
                    }),
                ],
                [
                    .. content.Failures.Select(failure =>
                        new BrowserLibraryQueryFailure(
                            failure.Path is not { } path
                                ? null
                                : Participant(path.ToString()).Asset.Id,
                            failure.Library?.ToString(),
                            failure.Path?.ToString(),
                            failure.Source?.ToString(),
                            failure.Kind.ToString(),
                            failure.Message.ToString())),
                ],
                new(
                    content.Summary.PopulationCandidates,
                    content.Summary.CandidateLimit,
                    content.Summary.Candidates,
                    content.Summary.Matches,
                    content.Summary.Failures,
                    content.Summary.IncompleteReasons.ToString(),
                    content.Summary.IsComplete)),
            BrowserPackageQueryOperations.Project(envelope.Share),
            [
                .. envelope.Diagnostics.Select(diagnostic =>
                    new BrowserInspectionDiagnostic(
                        diagnostic.Code,
                        diagnostic.Severity.ToString(),
                        diagnostic.Summary.ToString(),
                        diagnostic.Correspondence?.ToString())),
            ]);
    }

    static async Task<BrowserPackageDependencies> PackageDependenciesAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserPackageWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));

        PackageDependencyEvidenceRoot dependencyEvidence =
            await PackageDependencyEvidenceAsync(coordinate);
        PackageDependencyEvidenceDeclarationResult.Available dependencies =
            dependencyEvidence.Declaration
                as PackageDependencyEvidenceDeclarationResult.Available
                ?? throw new InvalidOperationException(
                    "The package dependency declaration projection is unavailable.");

        string? assembly = null;
        BrowserAssemblyReferenceResult assemblyReferences;
        if (coordinate.Selection.IsSelected)
        {
            PackageCompileAsset asset = coordinate.CompileAsset(assemblyId);
            assembly = asset.AssemblyName;
            BrowserWorkspaceParticipant participant =
                scope.SurfaceParticipant(coordinate, asset);
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> referenceResult =
                scope.UseSurfaceParticipant(
                    participant,
                    AssemblyContextReferencesQuery.ExecuteParticipant);

            switch (referenceResult)
            {
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Available available:
                    BrowserAssemblyReference[] references =
                    [
                        .. available.Value
                            .Select(reference => reference.ToReference())
                            .OrderBy(
                                reference => reference.Name,
                                StringComparer.OrdinalIgnoreCase)
                            .ThenBy(
                                reference => reference.Version,
                                StringComparer.Ordinal)
                            .ThenBy(
                                reference => reference.Culture,
                                StringComparer.OrdinalIgnoreCase)
                            .ThenBy(
                                reference => reference.PublicKeyToken,
                                StringComparer.OrdinalIgnoreCase)
                            .Select(reference => new BrowserAssemblyReference(
                                reference.Name,
                                reference.Version,
                                reference.Culture,
                                reference.PublicKeyToken)),
                    ];
                    assemblyReferences = new(new BrowserAssemblyReferenceList(references));
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected:
                    assemblyReferences = new(
                        $"{rejected.Failure.Kind} ({rejected.Failure.Detail})");
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Failed failed:
                    assemblyReferences = new(failed.Error.Message);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly-context reference query result.");
            }
        }
        else
        {
            assemblyReferences = new(
                compileLibrary.Message ?? "No compile library is available for this package.");
        }

        string? dependencyGroupError =
            dependencyEvidence.Selection.Status
                == PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework
                    ? "The manifest declares no dependency group for the active target framework."
                    : null;
        BrowserPackageDependencyDeclarationFailure[] declarationFailures =
            ProjectDependencyDeclarationFailures(dependencies);
        return new BrowserPackageDependencies(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworkText.Active(coordinate),
                assembly,
                [
                    .. dependencies.Groups.Select((group, index) =>
                        new BrowserPackageDependencyGroup(
                            index,
                            BrowserFrameworkText.DependencyGroup(
                                group.FrameworkScope.SourceSpelling.ToString()),
                            group.Identity
                                == dependencyEvidence.Selection.SelectedGroup,
                            [
                                .. group.Declarations.Select(dependency =>
                                    new BrowserPackageDependency(
                                        dependency.SourcePackageIdSpelling.ToString(),
                                        dependency.SourceVersionConstraintSpelling.ToString())),
                            ])),
                ],
                declarationFailures,
                assemblyReferences,
                dependencyGroupError,
                compileLibrary);
    }

    internal static async Task<PackageDependencyEvidenceRoot>
        PackageDependencyEvidenceAsync(BrowserPackageCoordinate coordinate)
    {
        PackageDependencyGroupsResult dependencyResult =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                coordinate.Package.Content,
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                allowCompatibleFallbackForRequestedTfm: true);
        PackageDependencyGroupsResult.Available available =
            dependencyResult switch
            {
                PackageDependencyGroupsResult.Available value => value,
                PackageDependencyGroupsResult.NoManifest =>
                    throw new InvalidDataException(
                        "The package contains no root manifest."),
                PackageDependencyGroupsResult.Failed failed =>
                    throw new InvalidOperationException(
                        failed.Error.Message,
                        failed.Error),
                _ => throw new InvalidOperationException(
                    "Unknown package dependency-group query result."),
            };
        PackageDependencyEvidenceInput.Package input =
            PackageDependencyEvidenceQuery.CreatePackageInput(
                available,
                PackageDependencyEvidenceAcquisitionForm.PackageArchive);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([input]));
        return outcome.Roots.Length == 1
            ? outcome.Roots[0]
            : throw new InvalidOperationException(
                "One package dependency input must produce one normalized evidence root.");
    }

    internal static BrowserPackageDependencyDeclarationFailure[]
        ProjectDependencyDeclarationFailures(
            PackageDependencyEvidenceDeclarationResult.Available declarations,
            PackageDependencyEvidenceGroupIdentity? selectedGroup = null)
    {
        string? Framework(PackageDependencyEvidenceGroupIdentity group) =>
            declarations.Groups
                .FirstOrDefault(candidate => candidate.Identity == group)
                ?.FrameworkScope.SourceSpelling.ToString();

        return
        [
            .. declarations.Failures
                .Where(failure =>
                    selectedGroup is null
                    || DependencyDeclarationFailureBelongsToGroup(
                        failure,
                        selectedGroup))
                .Select(failure => failure switch
                {
                    PackageDependencyEvidenceDeclarationFailure
                        .ConflictingPackageDeclaration conflicting =>
                            new BrowserPackageDependencyDeclarationFailure(
                                BrowserPackageDependencyDeclarationFailureKind
                                    .ConflictingPackageDeclaration,
                                Framework(conflicting.Group),
                                conflicting.CanonicalPackageId,
                                conflicting.SourceOccurrenceCount),
                    PackageDependencyEvidenceDeclarationFailure
                        .InvalidPackageDeclaration invalid =>
                            new BrowserPackageDependencyDeclarationFailure(
                                BrowserPackageDependencyDeclarationFailureKind
                                    .InvalidPackageDeclaration,
                                Framework(invalid.Group),
                                Package: null,
                                invalid.SourceOccurrenceCount),
                    PackageDependencyEvidenceDeclarationFailure
                        .RestoredProject =>
                            new BrowserPackageDependencyDeclarationFailure(
                                BrowserPackageDependencyDeclarationFailureKind
                                    .RestoredProject,
                                Framework: null,
                                Package: null,
                                SourceOccurrenceCount: null),
                    PackageDependencyEvidenceDeclarationFailure
                        .AuthoredProject =>
                            new BrowserPackageDependencyDeclarationFailure(
                                BrowserPackageDependencyDeclarationFailureKind
                                    .AuthoredProject,
                                Framework: null,
                                Package: null,
                                SourceOccurrenceCount: null),
                    PackageDependencyEvidenceDeclarationFailure
                        .AuthoredProjectUnresolvedSyntax =>
                            new BrowserPackageDependencyDeclarationFailure(
                                BrowserPackageDependencyDeclarationFailureKind
                                    .AuthoredProjectUnresolvedSyntax,
                                Framework: null,
                                Package: null,
                                SourceOccurrenceCount: null),
                    _ => throw new InvalidOperationException(
                        "Unknown dependency declaration failure."),
                }),
        ];
    }

    private static bool DependencyDeclarationFailureBelongsToGroup(
        PackageDependencyEvidenceDeclarationFailure failure,
        PackageDependencyEvidenceGroupIdentity selectedGroup) =>
        failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration conflicting =>
                    conflicting.Group == selectedGroup,
            PackageDependencyEvidenceDeclarationFailure
                .InvalidPackageDeclaration invalid =>
                    invalid.Group == selectedGroup,
            _ => true,
        };

    /// <summary>
    /// The UTF-8 text of one package-shipped Markdown document, identified by its exact package
    /// entry path. Only paths the package's own document manifest lists are served. This reads
    /// package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> GetPackageDocument(string packageId, string version, string path)
    {
        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(packageId, version);
        BrowserPackageDocumentContent document =
            BrowserPackageWireProjection.Project(package.ReadDocument(path));
        return JsonSerializer.Serialize(
            document,
            BrowserPackageJsonContext.Default.BrowserPackageDocumentContent);
    }

    /// <summary>
    /// One exact member's shared compiled-documentation outcome from the PackageHouse-selected
    /// Library and its associated XML companion.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberDocumentation(
        string packageId,
        string version,
        string framework,
        string assemblyName,
        string documentationId)
    {
        CompiledDocumentationOutcome documentation =
            await BrowserPackageWorkspace.QueryMemberDocumentationAsync(
                packageId,
                version,
                framework,
                assemblyName,
                documentationId);
        return JsonSerializer.Serialize(
            documentation,
            CompiledDocumentationQueryJsonContext.Default
                .CompiledDocumentationOutcome);
    }

    /// <summary>
    /// One exact member's shared compiled-documentation outcome from a
    /// PlatformHouse-selected reference Library and its associated XML
    /// companion.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPlatformMemberDocumentation(
        string framework,
        string platformVersion,
        string assemblyName,
        string platformPack,
        string documentationId)
    {
        CompiledDocumentationOutcome documentation =
            await BrowserPlatformWorkspace.QueryMemberDocumentationAsync(
                framework,
                platformVersion,
                assemblyName,
                platformPack,
                documentationId);
        return JsonSerializer.Serialize(
            documentation,
            CompiledDocumentationQueryJsonContext.Default
                .CompiledDocumentationOutcome);
    }

    /// <summary>
    /// Ranks loaded type candidates against an incremental query through the product's
    /// <see cref="TypeMatcher"/>: exact and namespace-suffix matches, then prefix and substring
    /// globs, then a Levenshtein "did you mean" fallback. This inspects no artifact — the
    /// candidates are names the client already holds — so it opens no workspace.
    /// </summary>
    [JSExport]
    public static string SearchTypes(string query, string candidatesJson)
    {
        BrowserTypeCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserPackageJsonContext.Default.BrowserTypeCandidateArray) ?? [];
        query = query?.Trim() ?? "";

        if (query.Length == 0)
        {
            return JsonSerializer.Serialize(
                candidates
                    .OrderBy(candidate => candidate.Name.Length)
                    .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .Select(candidate => new BrowserTypeSearchHit(candidate.Key, "all"))
                    .ToArray(),
                BrowserPackageJsonContext.Default.BrowserTypeSearchHitArray);
        }

        var hits = new List<BrowserTypeSearchHit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(string kind, Func<BrowserTypeCandidate, bool> predicate)
        {
            foreach (BrowserTypeCandidate candidate in candidates
                .Where(candidate => !used.Contains(candidate.Key) && predicate(candidate))
                .OrderBy(candidate => candidate.Name.Length)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (used.Add(candidate.Key))
                    hits.Add(new BrowserTypeSearchHit(candidate.Key, kind));
            }
        }

        AddTier("exact", candidate => TypeMatcher.Matches(candidate.Full, query));
        AddTier("prefix", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, query + "*"));
        AddTier("substring", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, "*" + query + "*"));
        AddTier("path", candidate => TypeMatcher.MatchesTypeFilter(candidate.Full, "*" + query + "*"));

        var remaining = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (BrowserTypeCandidate candidate in candidates.Where(candidate => !used.Contains(candidate.Key)))
        {
            if (!remaining.TryGetValue(candidate.Full, out List<string>? keys))
                remaining[candidate.Full] = keys = [];
            keys.Add(candidate.Key);
        }

        if (remaining.Count > 0)
        {
            foreach ((string name, _) in TypeMatcher.FindClosest(
                remaining.Keys,
                query,
                minSimilarity: 0.5,
                maxResults: 8))
            {
                if (!remaining.TryGetValue(name, out List<string>? keys))
                    continue;
                foreach (string key in keys)
                {
                    if (used.Add(key))
                        hits.Add(new BrowserTypeSearchHit(key, "fuzzy"));
                }
            }
        }

        return JsonSerializer.Serialize(
            hits.Take(40).ToArray(),
            BrowserPackageJsonContext.Default.BrowserTypeSearchHitArray);
    }

    /// <summary>Session acquisition statistics. Inspects no artifact and opens no workspace.</summary>
    [JSExport]
    public static string PackageCacheStats()
    {
        BrowserPackageCacheStats stats =
            BrowserPackageWireProjection.Project(BrowserPackageWorkspace.Stats());
        return JsonSerializer.Serialize(
            stats,
            BrowserPackageJsonContext.Default.BrowserPackageCacheStats);
    }

    /// <summary>
    /// Published package versions from the browser acquisition owner's bounded PackageHouse
    /// listing. The JavaScript host does not fetch or parse untrusted source data independently.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageVersions(
        string packageId,
        string currentVersion)
    {
        BrowserPackageVersionInventory inventory =
            await BrowserPackageWorkspace.GetVersionInventoryAsync(packageId, currentVersion);
        return JsonSerializer.Serialize(
            new BrowserPackageVersions(
                inventory.Versions,
                inventory.CurrentVersionInsertionIndex,
                inventory.PreviousVersion,
                inventory.PreviousVersionUnavailableReason),
            BrowserPackageJsonContext.Default.BrowserPackageVersions);
    }

    [JSExport]
    public static Task<string> ResolvePackageDependencyVersion(
        string packageId,
        string? declaredRange) =>
        BrowserPackageWorkspace.ResolveDependencyVersionAsync(
            packageId,
            declaredRange);

    [JSExport]
    public static string MatchPackageDependencyCoordinate(
        string packageId,
        string? declaredRange,
        string candidatesJson)
    {
        BrowserDependencyCoordinateCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateCandidateArray) ?? [];
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            candidates.Select(candidate => new PackageDependencyCoordinateCandidate(
                candidate.Key,
                candidate.Provenance switch
                {
                    BrowserDependencyCoordinateProvenance.NuGetPackage =>
                        PackageDependencyCoordinateKind.NuGetPackage,
                    BrowserDependencyCoordinateProvenance.PlatformRuntime =>
                        PackageDependencyCoordinateKind.PlatformRuntime,
                    _ => throw new InvalidOperationException(
                        "The dependency-coordinate provenance is invalid."),
                },
                candidate.PackageId,
                candidate.Version,
                candidate.TargetFramework)),
            packageId,
            declaredRange);
        var browserResult = new BrowserDependencyCoordinateMatch(
            result.Status switch
            {
                PackageDependencyCoordinateMatchStatus.NoMatch =>
                    BrowserDependencyCoordinateMatchOutcome.NoMatch,
                PackageDependencyCoordinateMatchStatus.Unique =>
                    BrowserDependencyCoordinateMatchOutcome.Unique,
                PackageDependencyCoordinateMatchStatus.Ambiguous =>
                    BrowserDependencyCoordinateMatchOutcome.Ambiguous,
                _ => throw new InvalidOperationException(
                    "The dependency-coordinate match outcome is invalid."),
            },
            result.CandidateKey);
        return JsonSerializer.Serialize(
            browserResult,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateMatch);
    }

    [JSExport]
    public static string ClassifyPackageGraphIdentities(
        string inspectedPackageId,
        string packageIdsJson)
    {
        string[] packageIds = JsonSerializer.Deserialize(
            packageIdsJson,
            BrowserPackageJsonContext.Default.StringArray)
            ?? throw new InvalidOperationException("The package graph identity batch is absent.");
        string prefix = PackageGraphFamilyPrefix(inspectedPackageId);
        BrowserPackageGraphIdentityRole[] roles = packageIds
            .Select(packageId => ClassifyPackageGraphIdentity(
                inspectedPackageId,
                prefix,
                packageId))
            .ToArray();
        return JsonSerializer.Serialize(
            roles,
            BrowserPackageJsonContext.Default.BrowserPackageGraphIdentityRoleArray);
    }

    static BrowserPackageGraphIdentityRole ClassifyPackageGraphIdentity(
        string inspectedPackageId,
        string prefix,
        string packageId)
    {
        if (string.Equals(packageId, inspectedPackageId, StringComparison.OrdinalIgnoreCase))
            return BrowserPackageGraphIdentityRole.Inspected;

        bool sharesPrefix =
            string.Equals(packageId, prefix, StringComparison.OrdinalIgnoreCase)
            || (packageId.Length > prefix.Length
                && packageId[prefix.Length] == '.'
                && packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return sharesPrefix
            ? BrowserPackageGraphIdentityRole.SamePrefix
            : BrowserPackageGraphIdentityRole.External;
    }

    static string PackageGraphFamilyPrefix(string packageId)
    {
        int firstDot = packageId.IndexOf('.');
        if (firstDot < 0)
            return packageId;
        int secondDot = packageId.IndexOf('.', firstDot + 1);
        return secondDot < 0 ? packageId : packageId[..secondDot];
    }
}
