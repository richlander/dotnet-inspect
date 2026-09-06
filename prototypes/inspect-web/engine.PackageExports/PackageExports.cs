using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

// The generated wwwroot/inspect-web-package.js module binds
// exports.PackageExports.*, so this type stays in the global namespace. Its
// helpers and wire records live in InspectWeb.Engine.PackageFacade.
using InspectWeb.Engine;
using InspectWeb.Engine.PackageFacade;

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
/// inspecting an assembly (the document and XML-documentation reads), and exports that touch no
/// artifact at all (type-name ranking and cache statistics).
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
        BrowserPackageSurface surface =
            await PackageSurfaceAsync(packageId, version, targetFramework);
        return JsonSerializer.Serialize(
            surface,
            BrowserPackageJsonContext.Default.BrowserPackageSurface);
    }

    static async Task<BrowserPackageSurface> PackageSurfaceAsync(
        string packageId,
        string version,
        string targetFramework)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        return BrowserPackageWireProjection.Project(
            BrowserPackageSurfaceProjection.ProjectSurface(scope, scope.Coordinates[0]));
    }

    /// <summary>
    /// Declared NuGet dependency groups plus the selected compile assembly's direct references.
    /// Package parsing and exact-framework selection belong to
    /// <see cref="PackageDependencyGroupsQuery"/>; the assembly-context query owns the metadata
    /// session. This method only adapts their typed results for the browser.
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

        PackageDependencyGroupsResult dependencyResult =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                coordinate.Package.Content,
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework);
        PackageDependencyGroups dependencies = dependencyResult switch
        {
            PackageDependencyGroupsResult.Available available => available.Value,
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

        string? assembly = null;
        BrowserAssemblyReference[] assemblyReferences = [];
        string? assemblyReferenceError = null;
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
                    assemblyReferences =
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
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected:
                    assemblyReferenceError =
                        $"{rejected.Failure.Kind} ({rejected.Failure.Detail})";
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Failed failed:
                    assemblyReferenceError = failed.Error.Message;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly-context reference query result.");
            }
        }
        else
        {
            assemblyReferenceError = compileLibrary.Message;
        }

        string? dependencyGroupError =
            dependencies.SelectionStatus
                == PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework
                    ? "The manifest declares no dependency group for the active target framework."
                    : null;
        return new BrowserPackageDependencies(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworkText.Active(coordinate),
                assembly,
                [
                    .. dependencies.Groups.Select((group, index) =>
                        new BrowserPackageDependencyGroup(
                            index,
                            BrowserFrameworkText.DependencyGroup(group.TargetFramework),
                            index == dependencies.SelectedGroupIndex,
                            [
                                .. group.Dependencies.Select(dependency =>
                                    new BrowserPackageDependency(
                                        dependency.Id,
                                        dependency.VersionRange)),
                            ])),
                ],
                assemblyReferences,
                dependencyGroupError,
                assemblyReferenceError,
                compileLibrary);
    }

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
    /// One member's entry from the XML documentation shipped beside the product-selected compile
    /// asset. This reads package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberDocumentation(
        string packageId,
        string version,
        string framework,
        string assemblyName,
        string documentationId)
    {
        BrowserPackageCoordinate coordinate = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            framework);
        PackageCompileAsset asset = coordinate.CompileAsset(assemblyName);
        BrowserMemberDocumentation documentation = coordinate.Package.TryReadText(
            Path.ChangeExtension(asset.Path, ".xml"),
            out byte[] xml)
                ? BrowserXmlDocumentation.Read(xml, documentationId)
                : BrowserXmlDocumentation.Empty;
        return JsonSerializer.Serialize(
            documentation,
            BrowserPackageJsonContext.Default.BrowserMemberDocumentation);
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
    /// Published package versions from the browser acquisition owner's bounded version-index
    /// reader. The JavaScript host does not fetch or parse the untrusted index independently.
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
}
