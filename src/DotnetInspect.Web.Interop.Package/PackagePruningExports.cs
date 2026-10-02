using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    private const int PackagePruningSchemaVersion = 1;
    private const string RuntimeFamily = "Microsoft.NETCore.App";
    private const string AspNetCoreFamily = "Microsoft.AspNetCore.App";

    /// <summary>
    /// Explicitly evaluates the selected normalized dependency group against one exact
    /// Browser-provided platform inventory. Candidate discovery and pruning policy remain
    /// managed operations; the browser transports the validated inventory without applying it.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackagePruning(
        string packageId,
        string version,
        string targetFramework,
        string requestJson)
    {
        BrowserPackagePruningRequest request = JsonSerializer.Deserialize(
                requestJson,
                BrowserPackageJsonContext.Default.BrowserPackagePruningRequest)
            ?? throw new InvalidOperationException(
                "The package pruning request is absent.");
        BrowserPackagePruningResult result =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => PackagePruningAsync(
                    packageId,
                    version,
                    targetFramework,
                    request,
                    deadline),
                BrowserPackageWorkspace.PackageOperationTimeout);
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackagePruningResult);
    }

    static async Task<BrowserPackagePruningResult> PackagePruningAsync(
        string packageId,
        string version,
        string targetFramework,
        BrowserPackagePruningRequest request,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        ValidatePruningRequest(request, targetFramework);
        PlatformPruneInventory inventory = CreatePruningInventory(request);
        PackageHouseTargetContext target = CreatePruningTarget(request);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework,
                deadline.Token);
        BrowserPackageCoordinate coordinate = scopeLease.Scope.Coordinates[0];
        PackageDependencyEvidenceRoot root =
            await PackageDependencyEvidenceAsync(coordinate);
        if (root.Declaration
                is not PackageDependencyEvidenceDeclarationResult.Available
                    declarations)
        {
            throw new InvalidOperationException(
                "The package dependency declaration projection is unavailable.");
        }

        PackageDependencyEvidenceGroup? selectedGroup =
            declarations.Groups.SingleOrDefault(
                group => group.Identity == root.Selection.SelectedGroup);
        if (selectedGroup is null)
        {
            return CreateNotApplicableResult(
                coordinate,
                request,
                root.Selection,
                ProjectDependencyDeclarationFailures(declarations));
        }

        var clients =
            new Dictionary<ConfiguredPackageAuthority, IPackageSourceClient>();
        PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(
                authority => GetCandidateClient(
                    authority,
                    clients,
                    deadline));
        try
        {
            var candidateSource =
                new AuthorizedPackageDependencyCandidateSource(
                    BrowserPackageWorkspace.PackageSourceAuthorization,
                    sourceLease);
            using var operation = new NuGetOperationContext(
                BrowserPackageWorkspace.GalleryOperationTimeout,
                deadline.Remaining,
                deadline.Token);
            return await EvaluatePruningAsync(
                coordinate,
                request,
                root,
                selectedGroup,
                target,
                inventory,
                candidateSource,
                operation,
                deadline.Token);
        }
        finally
        {
            try
            {
                await sourceLease.DisposeAsync();
            }
            finally
            {
                foreach (IPackageSourceClient client in clients.Values)
                    client.Dispose();
            }
        }
    }

    internal static async Task<BrowserPackagePruningResult> EvaluatePruningAsync(
        BrowserPackageCoordinate coordinate,
        BrowserPackagePruningRequest request,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceGroup selectedGroup,
        PackageHouseTargetContext target,
        PlatformPruneInventory inventory,
        IPackageDependencyCandidateSource candidateSource,
        NuGetOperationContext operation,
        CancellationToken cancellationToken)
    {
        BrowserPackageDependencyDeclarationFailure[] declarationFailures =
            ProjectDependencyDeclarationFailures(
                AssertDeclarationResult(root),
                selectedGroup.Identity);
        var rows = new List<BrowserPackagePruningRow>(
            selectedGroup.Declarations.Length);
        int evaluated = 0;
        int delegated = 0;
        int retained = 0;
        int notEvaluated = 0;
        int failed = 0;

        var inspectionRequest =
            new PackageDependencyPruningInspectionRequest(
                selectedGroup.Declarations.Select(
                    declaration =>
                        new PackageDependencyPruningInspectionSubject(
                            root,
                            declaration)),
                target,
                inventory);
        InspectionEnvelope<PackageDependencyPruningInspectionResult>
            inspection =
                await PackageDependencyPruningInspection.ExecuteAsync(
                    inspectionRequest,
                    candidateSource,
                    cancellationToken,
                    operation);
        foreach (PackageDependencyPruningInspectionOutcome outcome
                 in inspection.Content.Outcomes)
        {
            PackageDependencyEvidenceDeclaration declaration =
                outcome.Subject.Declaration;
            if (outcome
                is PackageDependencyPruningInspectionOutcome.NotEvaluated
                    notEvaluatedOutcome)
            {
                rows.Add(new BrowserPackagePruningRow(
                    declaration.SourcePackageIdSpelling.ToString(),
                    declaration.SourceVersionConstraintSpelling.ToString(),
                    CandidateVersion: null,
                    PlatformSuppliedVersion: PlatformProvidedVersion(
                        inventory,
                        declaration.CanonicalPackageId),
                    BrowserPackagePruningDisposition.NotEvaluated,
                    notEvaluatedOutcome.Applicability
                        .TargetUnavailableReason?.ToString()
                        ?? notEvaluatedOutcome.Applicability.State.ToString()));
                notEvaluated++;
                continue;
            }

            if (outcome
                is PackageDependencyPruningInspectionOutcome
                    .CandidateUnavailable candidateUnavailable)
            {
                rows.Add(new BrowserPackagePruningRow(
                    declaration.SourcePackageIdSpelling.ToString(),
                    declaration.SourceVersionConstraintSpelling.ToString(),
                    CandidateVersion: null,
                    PlatformSuppliedVersion: PlatformProvidedVersion(
                        inventory,
                        declaration.CanonicalPackageId),
                    BrowserPackagePruningDisposition.CandidateUnavailable,
                    CandidateFailure(candidateUnavailable.Candidate)));
                failed++;
                continue;
            }

            PackageDependencyPruningInspectionOutcome.Evaluated
                evaluatedOutcome =
                    outcome
                        as PackageDependencyPruningInspectionOutcome.Evaluated
                ?? throw new InvalidOperationException(
                    "Unknown package dependency pruning inspection outcome.");
            PackageHouseDependencyPruningResult.Evaluated evaluatedResult =
                evaluatedOutcome.Result;
            bool delegates =
                evaluatedResult.Pruning.Supply.DelegatesToPlatform;
            rows.Add(new BrowserPackagePruningRow(
                declaration.SourcePackageIdSpelling.ToString(),
                declaration.SourceVersionConstraintSpelling.ToString(),
                evaluatedOutcome.Candidate.Candidate.Coordinate.Version,
                evaluatedResult.Pruning.Supply.SuppliedVersion
                    ?.ToNormalizedString(),
                delegates
                    ? BrowserPackagePruningDisposition.PlatformDelegation
                    : BrowserPackagePruningDisposition.PackageRetained,
                evaluatedResult.Pruning.Supply.Subsumption.ToString()));
            evaluated++;
            if (delegates)
                delegated++;
            else
                retained++;
        }

        bool hasFailures =
            failed > 0 || declarationFailures.Length > 0;
        BrowserPackagePruningCompletion completion =
            hasFailures
                ? evaluated > 0 || notEvaluated > 0
                    ? BrowserPackagePruningCompletion.Partial
                    : BrowserPackagePruningCompletion.Failed
                : notEvaluated == rows.Count && rows.Count > 0
                    ? BrowserPackagePruningCompletion.NotApplicable
                    : BrowserPackagePruningCompletion.Complete;
        return new BrowserPackagePruningResult(
            PackagePruningSchemaVersion,
            coordinate.PackageId,
            coordinate.Version,
            request.TargetFramework,
            root.Selection.SelectedFramework?.ToString(),
            request.Family,
            request.PlatformVersion,
            completion,
            [.. rows],
            declarationFailures,
            new BrowserPackagePruningSummary(
                selectedGroup.Declarations.Length,
                evaluated,
                delegated,
                retained,
                notEvaluated,
                failed,
                declarationFailures.Length),
            Message: null);
    }

    static BrowserPackagePruningResult CreateNotApplicableResult(
        BrowserPackageCoordinate coordinate,
        BrowserPackagePruningRequest request,
        PackageDependencyEvidenceSelection selection,
        BrowserPackageDependencyDeclarationFailure[] declarationFailures)
    {
        string message = selection.Status switch
        {
            PackageDependencyEvidenceSelectionStatus.NoDependencyGroups =>
                "The manifest declares no package dependencies.",
            PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework =>
                "The manifest declares no dependency group for the active target framework.",
            _ => "The active dependency group is unavailable.",
        };
        return new BrowserPackagePruningResult(
            PackagePruningSchemaVersion,
            coordinate.PackageId,
            coordinate.Version,
            request.TargetFramework,
            selection.SelectedFramework?.ToString(),
            request.Family,
            request.PlatformVersion,
            BrowserPackagePruningCompletion.NotApplicable,
            [],
            declarationFailures,
            new BrowserPackagePruningSummary(
                0,
                0,
                0,
                0,
                0,
                0,
                declarationFailures.Length),
            message);
    }

    static PackageDependencyEvidenceDeclarationResult.Available
        AssertDeclarationResult(PackageDependencyEvidenceRoot root) =>
        root.Declaration
            as PackageDependencyEvidenceDeclarationResult.Available
            ?? throw new InvalidOperationException(
                "The package dependency declaration projection is unavailable.");

    static IPackageSourceClient GetCandidateClient(
        ConfiguredPackageAuthority authority,
        Dictionary<ConfiguredPackageAuthority, IPackageSourceClient> clients,
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
    {
        if (!authority.Source.IsNuGetOrg)
        {
            throw new InvalidOperationException(
                "The Browser dependency candidate source supports only nuget.org.");
        }
        if (clients.TryGetValue(authority, out IPackageSourceClient? existing))
            return existing;

        TimeSpan remaining = deadline.Remaining;
        var options = new NuGetFetchOptions
        {
            RequestTimeout = remaining < BrowserPackageWorkspace.GalleryOperationTimeout
                ? remaining
                : BrowserPackageWorkspace.GalleryOperationTimeout,
            OperationTimeout = remaining,
        };
        IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                authority.Association,
                options);
        clients.Add(authority, client);
        return client;
    }

    static PlatformPruneInventory CreatePruningInventory(
        BrowserPackagePruningRequest request) =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                request.Family,
                request.TargetFramework,
                NuGetVersion.Parse(request.PlatformVersion)),
            request.Supplies
                .Where(supply => string.Equals(
                    supply.Family,
                    request.Family,
                    StringComparison.Ordinal))
                .Select(supply => $"{supply.Package}|{supply.Version}"));

    static PackageHouseTargetContext CreatePruningTarget(
        BrowserPackagePruningRequest request)
    {
        PlatformFamily family = request.Family switch
        {
            RuntimeFamily => PlatformFamily.DotNetRuntime,
            AspNetCoreFamily => PlatformFamily.AspNetCore,
            _ => throw new InvalidOperationException(
                $"Unsupported platform pruning family '{request.Family}'."),
        };
        return PackageHouseTargetContext.Exact(
            request.TargetFramework,
            platformTarget: new PlatformFamilyTarget(
                family,
                PlatformTargetFramework.Parse(
                    request.TargetFramework.ToLowerInvariant()),
                PlatformVersion.Parse(request.PlatformVersion)));
    }

    static void ValidatePruningRequest(
        BrowserPackagePruningRequest request,
        string targetFramework)
    {
        if (request.SchemaVersion != PackagePruningSchemaVersion)
        {
            throw new InvalidOperationException(
                "The package pruning request schema is unsupported.");
        }
        if (!string.Equals(
                request.TargetFramework,
                targetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The package pruning target does not match the active package framework.");
        }
        if (request.Family is not RuntimeFamily and not AspNetCoreFamily)
        {
            throw new InvalidOperationException(
                $"Unsupported platform pruning family '{request.Family}'.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlatformVersion);
        ArgumentNullException.ThrowIfNull(request.Supplies);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BrowserPackagePruningSupply supply in request.Supplies)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(supply.Package);
            if (!NuGetVersion.TryParse(supply.Version, out _))
            {
                throw new InvalidOperationException(
                    "A platform package supply has an invalid version.");
            }

            string expectedFamily = supply.Pack switch
            {
                "netcore.app" => RuntimeFamily,
                "aspnetcore.app" => AspNetCoreFamily,
                _ => throw new InvalidOperationException(
                    "A platform package supply has an unsupported pack."),
            };
            if (!string.Equals(
                    supply.Family,
                    expectedFamily,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A platform package supply does not match its pack family.");
            }
            if (!identities.Add($"{supply.Pack}\0{supply.Package}"))
            {
                throw new InvalidOperationException(
                    "The platform package supply inventory contains a duplicate identity.");
            }
        }
    }

    static string? PlatformProvidedVersion(
        PlatformPruneInventory inventory,
        string packageId) =>
        inventory.TryGetEntry(packageId, out PlatformPruneEntry entry)
            ? entry.SuppliedVersion.ToNormalizedString()
            : null;

    static string CandidateFailure(PackageDependencyCandidateResult candidate) =>
        candidate switch
        {
            PackageDependencyCandidateResult.Failed failed =>
                failed.Failure.GetType().Name,
            PackageDependencyCandidateResult.Incomplete incomplete =>
                incomplete.Evidence.GetType().Name,
            _ => throw new InvalidOperationException(
                "A resolved dependency candidate is not unavailable."),
        };
}
