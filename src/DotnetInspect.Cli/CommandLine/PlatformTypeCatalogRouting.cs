using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using CSharpText;
using DotnetInspect.Cli.Commands;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Installed;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal enum CliPlatformTypeCatalogFailureKind
{
    HouseUnavailable,
    HouseAmbiguous,
    HouseRejected,
    HouseIncomplete,
    HouseFailed,
    CatalogIncomplete,
    CatalogRejected,
    CatalogFailed,
    CleanupFailed,
}

internal abstract class CliPlatformTypeCatalogOutcome
{
    private protected CliPlatformTypeCatalogOutcome()
    {
    }

    internal sealed class Completed : CliPlatformTypeCatalogOutcome
    {
        internal Completed(PlatformTypeCatalog catalog) =>
            Catalog = catalog;

        internal PlatformTypeCatalog Catalog { get; }
    }

    internal sealed class NotCompleted : CliPlatformTypeCatalogOutcome
    {
        internal NotCompleted(
            CliPlatformTypeCatalogFailureKind kind,
            PlatformHouseReceipt? houseReceipt = null,
            PlatformTypeCatalogDerivationOutcome? derivation = null,
            IReadOnlyList<PlatformHouseFailureKind>? cleanupFailures = null)
        {
            Kind = kind;
            HouseReceipt = houseReceipt;
            Derivation = derivation;
            CleanupFailures = cleanupFailures ?? [];
        }

        internal CliPlatformTypeCatalogFailureKind Kind { get; }
        internal PlatformHouseReceipt? HouseReceipt { get; }
        internal PlatformTypeCatalogDerivationOutcome? Derivation { get; }
        internal IReadOnlyList<PlatformHouseFailureKind> CleanupFailures
        { get; }
    }
}

internal abstract class CliPlatformTypeRouteOutcome
{
    private protected CliPlatformTypeRouteOutcome()
    {
    }

    internal sealed class Resolved : CliPlatformTypeRouteOutcome
    {
        internal Resolved(
            PlatformTypeCatalogQueryOutcome.Resolved query,
            string typeName,
            string? memberSelector,
            bool inputWasFullName)
        {
            Query = query;
            TypeName = typeName;
            MemberSelector = memberSelector;
            InputWasFullName = inputWasFullName;
        }

        internal PlatformTypeCatalogQueryOutcome.Resolved Query { get; }
        internal string TypeName { get; }
        internal string? MemberSelector { get; }
        internal bool InputWasFullName { get; }
        internal string AssemblyName =>
            Query.Candidate.ApiContent.AssemblyIdentity!.Identity.Name;
        internal string Framework =>
            $"runtime@{Query.Catalog.Target.Version.Value}";
    }

    internal sealed class Ambiguous : CliPlatformTypeRouteOutcome
    {
        internal Ambiguous(
            PlatformTypeCatalogQueryOutcome.Ambiguous query,
            string pattern)
        {
            Query = query;
            Pattern = pattern;
        }

        internal PlatformTypeCatalogQueryOutcome.Ambiguous Query { get; }
        internal string Pattern { get; }
    }

    internal sealed class Rejected : CliPlatformTypeRouteOutcome
    {
        internal Rejected(
            PlatformTypeCatalogQueryOutcome.Rejected query) =>
            Query = query;

        internal PlatformTypeCatalogQueryOutcome.Rejected Query { get; }
    }

    internal sealed class Missing : CliPlatformTypeRouteOutcome
    {
        internal Missing(PlatformTypeCatalog catalog) =>
            Catalog = catalog;

        internal PlatformTypeCatalog Catalog { get; }
    }
}

internal static class PlatformTypeCatalogRouting
{
    private const int MaxTypeMemberBoundaryProbes = 64;
    private static readonly PlatformTypeCatalogDerivationBounds
        CatalogBounds = new(
            new LibraryTypeDeclarationInventoryInspectionBounds(
                maximumAssemblyBytes: 512 * 1024 * 1024,
                maximumRetainedDeclarations: 500_000),
            maximumAssemblies: 512,
            maximumAggregateAssemblyBytes: 2L * 1024 * 1024 * 1024,
            maximumRetainedEntries: 1_500_000,
            maximumDuration: TimeSpan.FromMinutes(2));

    internal static ValueTask<CliPlatformTypeCatalogOutcome> LoadAsync(
        CommandContext context,
        NuGetSourceOptions sourceOptions,
        CancellationToken cancellationToken) =>
        LoadAsync(
            FindActiveDotnetRoot(),
            context,
            sourceOptions,
            cancellationToken);

    internal static async ValueTask<CliPlatformTypeCatalogOutcome> LoadAsync(
        string? dotnetRoot,
        CommandContext context,
        NuGetSourceOptions sourceOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceOptions);
        cancellationToken.ThrowIfCancellationRequested();

        InstalledPlatformHouseAdapter? installed =
            dotnetRoot is null
                ? null
                : CreateInstalledAdapter(dotnetRoot);
        await using var packageRuntime =
            new DesktopPlatformPackageSourceRuntime(
                context.CreatePackageSourceComposition,
                sourceOptions,
                "inspect-cli-platform");
        PackagePlatformHouseAdapter package =
            packageRuntime.CreateAdapter("cli-platform-type-routing-package");
        PlatformHouseRequest request =
            CreateRequest(installed, package, cancellationToken);

        List<PlatformTargetDiscoverySource> discoverySources = [];
        List<PlatformReferencePopulationRealizationSource>
            realizationSources = [];
        if (installed is not null)
        {
            discoverySources.Add(
                InstalledPlatformTargetDiscovery.CreateSource(installed));
            realizationSources.Add(
                InstalledPlatformSelectedReferencePopulationRealization
                    .CreateSource(
                        installed,
                        PlatformHouseCandidateIdentity.Create(
                            "cli-platform-installed-reference-population")));
        }
        discoverySources.Add(
            PackagePlatformTargetDiscovery.CreateSource(
                package,
                packageRuntime.IssueOperation));
        realizationSources.Add(
            PackagePlatformSelectedReferencePopulationRealization
                .CreateSource(
                    package,
                    packageRuntime.IssueOperation,
                    PlatformHouseCandidateIdentity.Create(
                        "cli-platform-package-reference-population")));

        PlatformPopulationArtifactMaterializationOutcome realization =
            await PlatformHouseSelectedReferencePopulationExecutor
                .ExecuteAsync(
                    request,
                    discoverySources,
                    realizationSources,
                    "cli-platform-type-routing")
                .ConfigureAwait(false);
        if (realization
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            return HouseFailure(terminal);
        }

        var completed =
            (PlatformPopulationArtifactMaterializationOutcome.Completed)
                realization;
        PlatformTypeCatalogDerivationOutcome? derivation = null;
        ExceptionDispatchInfo? derivationFailure = null;
        try
        {
            derivation = PlatformTypeCatalogDerivation.Execute(
                completed.Population,
                CatalogBounds,
                cancellationToken);
        }
        catch (Exception ex)
        {
            derivationFailure = ExceptionDispatchInfo.Capture(ex);
        }

        ImmutableArray<PlatformHouseFailureKind> cleanupFailures =
            await RetireAsync(completed).ConfigureAwait(false);
        if (!cleanupFailures.IsDefaultOrEmpty)
        {
            return new CliPlatformTypeCatalogOutcome.NotCompleted(
                CliPlatformTypeCatalogFailureKind.CleanupFailed,
                completed.Population.Receipt.HouseReceipt,
                derivation,
                cleanupFailures);
        }
        derivationFailure?.Throw();

        return derivation switch
        {
            PlatformTypeCatalogDerivationOutcome.Completed catalog =>
                new CliPlatformTypeCatalogOutcome.Completed(
                    catalog.Catalog),
            PlatformTypeCatalogDerivationOutcome.Incomplete =>
                new CliPlatformTypeCatalogOutcome.NotCompleted(
                    CliPlatformTypeCatalogFailureKind.CatalogIncomplete,
                    completed.Population.Receipt.HouseReceipt,
                    derivation),
            PlatformTypeCatalogDerivationOutcome.Rejected =>
                new CliPlatformTypeCatalogOutcome.NotCompleted(
                    CliPlatformTypeCatalogFailureKind.CatalogRejected,
                    completed.Population.Receipt.HouseReceipt,
                    derivation),
            PlatformTypeCatalogDerivationOutcome.Failed =>
                new CliPlatformTypeCatalogOutcome.NotCompleted(
                    CliPlatformTypeCatalogFailureKind.CatalogFailed,
                    completed.Population.Receipt.HouseReceipt,
                    derivation),
            _ => throw new InvalidOperationException(
                "Unknown Platform type catalog derivation outcome."),
        };
    }

    internal static CliPlatformTypeRouteOutcome Resolve(
        PlatformTypeCatalog catalog,
        string target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(target);

        PlatformTypeCatalogQueryOutcome full =
            PlatformTypeCatalogQuery.Execute(
                catalog,
                target,
                cancellationToken);
        switch (full)
        {
            case PlatformTypeCatalogQueryOutcome.Resolved resolved:
                return Resolved(
                    resolved,
                    target,
                    memberSelector: null);
            case PlatformTypeCatalogQueryOutcome.Ambiguous ambiguous:
                return new CliPlatformTypeRouteOutcome.Ambiguous(
                    ambiguous,
                    target);
            case PlatformTypeCatalogQueryOutcome.Rejected rejected:
                return new CliPlatformTypeRouteOutcome.Rejected(rejected);
        }

        int probes = 0;
        int genericDepth = 0;
        for (int index = target.Length - 1;
            index > 0 && probes < MaxTypeMemberBoundaryProbes;
            index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (target[index])
            {
                case '>':
                    genericDepth++;
                    continue;
                case '<':
                    genericDepth--;
                    continue;
                case '.' when genericDepth == 0
                    && index < target.Length - 1:
                    break;
                default:
                    continue;
            }

            probes++;
            PlatformTypeCatalogQueryOutcome prefix =
                PlatformTypeCatalogQuery.Execute(
                    catalog,
                    target[..index],
                    cancellationToken);
            switch (prefix)
            {
                case PlatformTypeCatalogQueryOutcome.Resolved resolved:
                    return Resolved(
                        resolved,
                        target[..index],
                        target[(index + 1)..]);
                case PlatformTypeCatalogQueryOutcome.Ambiguous ambiguous:
                    return new CliPlatformTypeRouteOutcome.Ambiguous(
                        ambiguous,
                        target[..index]);
                case PlatformTypeCatalogQueryOutcome.Rejected rejected:
                    return new CliPlatformTypeRouteOutcome.Rejected(
                        rejected);
            }
        }

        return new CliPlatformTypeRouteOutcome.Missing(catalog);
    }

    internal static string? FindActiveDotnetRoot()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "DOTNET_ROOT");
        if (HasPacks(configured))
            return Path.GetFullPath(configured!);

        string runtimeDirectory =
            RuntimeEnvironment.GetRuntimeDirectory()
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        DirectoryInfo? version = new DirectoryInfo(runtimeDirectory);
        DirectoryInfo? framework = version.Parent;
        DirectoryInfo? shared = framework?.Parent;
        DirectoryInfo? root = shared?.Parent;
        return framework is not null
            && shared is not null
            && root is not null
            && framework.Name.Equals(
                "Microsoft.NETCore.App",
                StringComparison.OrdinalIgnoreCase)
            && shared.Name.Equals(
                "shared",
                StringComparison.OrdinalIgnoreCase)
            && HasPacks(root.FullName)
                ? root.FullName
                : null;
    }

    private static CliPlatformTypeRouteOutcome.Resolved Resolved(
        PlatformTypeCatalogQueryOutcome.Resolved query,
        string inputTypeName,
        string? memberSelector) =>
        new(
            query,
            MetadataTypeNameFormatter.FormatGenericTypeName(
                query.Candidate.Name.ToMetadataFullName()),
            memberSelector,
            query.Candidate.Name
                .ToMetadataFullName()
                .Replace('+', '.')
                .Equals(
                    FqnParser.NormalizeTypeName(inputTypeName)
                        .Replace('+', '.'),
                    StringComparison.OrdinalIgnoreCase));

    private static InstalledPlatformHouseAdapter CreateInstalledAdapter(
        string dotnetRoot)
    {
        InstalledDotnetHiveIdentity hive =
            InstalledDotnetHiveIdentity.Create(
                "cli-platform-type-routing");
        return new(
            new InstalledReferencePackSource(hive, dotnetRoot),
            new InstalledImplementationPlatformSource(hive, dotnetRoot),
            "cli-platform-type-routing-installed");
    }

    private static PlatformHouseRequest CreateRequest(
        InstalledPlatformHouseAdapter? installed,
        PackagePlatformHouseAdapter package,
        CancellationToken cancellationToken)
    {
        PlatformTargetDiscoveryStage? preferred =
            installed is null
                ? null
                : new(
                    new PlatformTargetDiscoveryScope.AllFrameworks(),
                    [installed.Capabilities.TargetDiscovery]);
        var fallback = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [package.TargetDiscovery]);
        var discoveryCapabilities =
            installed is null
                ? new[] { package.TargetDiscovery }
                : [
                    installed.Capabilities.TargetDiscovery,
                    package.TargetDiscovery,
                ];
        var referenceCapabilities =
            installed is null
                ? new[] { package.ReferenceRealization }
                : [
                    installed.Capabilities.ReferenceRealization,
                    package.ReferenceRealization,
                ];

        return new(
            PlatformHouseRequestIdentity.Create(
                "cli-platform-type-routing"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "cli-versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred,
                    fallback),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 128,
                    maxComparisons: 512)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "cli-platform-type-routing")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "cli-platform-type-routing-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        discoveryCapabilities),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        referenceCapabilities),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 4,
                maxTargetCandidates: 128,
                maxAssemblies: 512,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 2L * 1024 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromMinutes(10)),
            cancellationToken);
    }

    private static CliPlatformTypeCatalogOutcome.NotCompleted HouseFailure(
        PlatformPopulationArtifactMaterializationOutcome.Terminal terminal)
    {
        PlatformHouseOutcome<PlatformPopulationRealizationValue> outcome =
            terminal.TerminalRealization.Outcome;
        CliPlatformTypeCatalogFailureKind kind = outcome switch
        {
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Unavailable =>
                    CliPlatformTypeCatalogFailureKind.HouseUnavailable,
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Ambiguous =>
                    CliPlatformTypeCatalogFailureKind.HouseAmbiguous,
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected =>
                    CliPlatformTypeCatalogFailureKind.HouseRejected,
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete =>
                    CliPlatformTypeCatalogFailureKind.HouseIncomplete,
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Failed =>
                    CliPlatformTypeCatalogFailureKind.HouseFailed,
            _ => throw new InvalidOperationException(
                "Unknown terminal PlatformHouse outcome."),
        };
        return new(
            kind,
            terminal.TerminalRealization.Receipt.HouseReceipt);
    }

    private static async ValueTask<
        ImmutableArray<PlatformHouseFailureKind>> RetireAsync(
            PlatformPopulationArtifactMaterializationOutcome.Completed
                completed)
    {
        var failures =
            ImmutableArray.CreateBuilder<PlatformHouseFailureKind>();
        Task? artifactRetirement = null;
        try
        {
            artifactRetirement =
                completed.Artifacts.DisposeAsync().AsTask();
        }
        catch
        {
            failures.Add(
                PlatformHouseFailureKind.ArtifactRetirement);
        }

        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                failures.Add(
                    PlatformHouseFailureKind.LibraryRetirement);
            }
        }

        if (artifactRetirement is not null)
        {
            try
            {
                await artifactRetirement.ConfigureAwait(false);
            }
            catch
            {
                failures.Add(
                    PlatformHouseFailureKind.ArtifactRetirement);
            }
        }
        if (completed.Artifacts.CleanupFailures.Count != 0)
        {
            failures.Add(
                PlatformHouseFailureKind.ArtifactRetirement);
        }

        return failures.ToImmutable().Distinct().ToImmutableArray();
    }

    private static bool HasPacks(string? dotnetRoot) =>
        !string.IsNullOrWhiteSpace(dotnetRoot)
        && Directory.Exists(Path.Combine(dotnetRoot, "packs"));
}
