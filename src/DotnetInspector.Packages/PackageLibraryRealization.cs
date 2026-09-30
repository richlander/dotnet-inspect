using System.Collections.Immutable;

namespace DotnetInspector.Packages;

/// <summary>How one exact package Library is identified after compile selection.</summary>
public enum PackageLibrarySelectionKind
{
    Query,
    AssetId,
    AssetPath,
}

/// <summary>How much package content one exact Library consumer requires.</summary>
public enum PackageLibraryRealizationDepth
{
    Selection,
    Implementation,
}

/// <summary>One exact Library selector interpreted by package-owned asset selection.</summary>
public sealed class PackageLibrarySelector
{
    public PackageLibrarySelector(
        string value,
        PackageLibrarySelectionKind kind =
            PackageLibrarySelectionKind.Query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        string normalized = value.Replace('\\', '/');
        if (kind == PackageLibrarySelectionKind.AssetPath
            && !PackageCoordinateResolver.IsPackageRelativeAssetPath(
                normalized))
        {
            throw new ArgumentException(
                "An exact Library asset path must be a safe package-relative asset path.",
                nameof(value));
        }

        Value = normalized;
        Kind = kind;
    }

    public string Value { get; }

    public PackageLibrarySelectionKind Kind { get; }

    internal string ImplementationName
    {
        get
        {
            string value = Value.StartsWith(
                    "compile:",
                    StringComparison.Ordinal)
                ? Value["compile:".Length..]
                : Value;
            string name = Path.GetFileName(value);
            return name.EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".dll";
        }
    }
}

/// <summary>Stable host capability and policy for realizing one exact Library.</summary>
public sealed class PackageLibraryRealizationPlan
{
    internal PackagePayloadAcquisitionPlan PayloadAcquisition { get; }

    internal PackageVersionServicePlan? VersionSettlement { get; }

    public PackageLibraryRealizationPlan(
        PackageStoreProvider getStore,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        Action<string>? log = null,
        long rangedSizeCut = PackageRangedRead.DefaultSizeCut,
        PackageVersionServicePlan? versionSettlement = null)
    {
        PayloadAcquisition = new(
            getStore,
            limits,
            transferPolicy,
            log,
            PackagePayloadAccess.Ranged,
            rangedSizeCut);
        VersionSettlement = versionSettlement;
    }
}

/// <summary>
/// One package demand, target, and exact Library selector to realize.
/// </summary>
public sealed class PackageLibraryRealizationRequest
{
    public PackageLibraryRealizationRequest(
        PackageHouseDemand package,
        string targetFramework,
        PackageLibrarySelector selector,
        PackageLibraryRealizationDepth depth,
        PackageHouseOperation operation,
        PackageHouseLibraryCompanionDemand companionDemand =
            PackageHouseLibraryCompanionDemand.None)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(depth))
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (!Enum.IsDefined(companionDemand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(companionDemand));
        }
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Exact Library realization requires a PackageHouse Realize operation.",
                nameof(operation));
        }
        if (depth == PackageLibraryRealizationDepth.Selection
            && companionDemand
                != PackageHouseLibraryCompanionDemand.None)
        {
            throw new ArgumentException(
                "An exact Library companion requires named implementation realization.",
                nameof(companionDemand));
        }

        Package = package;
        TargetFramework = targetFramework;
        Selector = selector;
        Depth = depth;
        Operation = operation;
        CompanionDemand = companionDemand;
    }

    public PackageHouseDemand Package { get; }

    public string TargetFramework { get; }

    public PackageLibrarySelector Selector { get; }

    public PackageLibraryRealizationDepth Depth { get; }

    public PackageHouseOperation Operation { get; }

    public PackageHouseLibraryCompanionDemand CompanionDemand { get; }
}

public enum PackageLibraryRealizationStatus
{
    Realized,
    NotSettled,
    NotRealized,
    Missing,
    Ambiguous,
}

/// <summary>The typed outcome of one exact package Library realization.</summary>
public abstract class PackageLibraryRealizationResult
{
    private PackageLibraryRealizationResult(
        PackageLibraryRealizationStatus status,
        PackageHouseSettlement settlement)
    {
        Status = status;
        Settlement = settlement
            ?? throw new ArgumentNullException(nameof(settlement));
    }

    public PackageLibraryRealizationStatus Status { get; }

    public PackageHouseSettlement Settlement { get; }

    public sealed class Realized : PackageLibraryRealizationResult
    {
        internal Realized(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseRealizationReceipt.Compile realization,
            PackageHouseLibraryHandoff.Compile handoff)
            : base(PackageLibraryRealizationStatus.Realized, settlement)
        {
            Acquired = settlement;
            Realization = realization;
            Handoff = handoff;
        }

        public PackageHouseSettlement.Acquired Acquired { get; }

        public PackageHouseRealizationReceipt.Compile Realization { get; }

        public PackageHouseLibraryHandoff.Compile Handoff { get; }
    }

    public sealed class Unavailable : PackageLibraryRealizationResult
    {
        internal Unavailable(
            PackageLibraryRealizationStatus status,
            PackageHouseSettlement settlement)
            : base(status, settlement)
        {
            if (status == PackageLibraryRealizationStatus.Realized)
            {
                throw new ArgumentException(
                    "An unavailable exact Library cannot have realized status.",
                    nameof(status));
            }
        }
    }
}

/// <summary>
/// Cache-aware realization and exact handoff selection for one package Library.
/// </summary>
public static class PackageLibraryRealization
{
    public static async Task<PackageLibraryRealizationResult> ExecuteAsync(
        PackageLibraryRealizationRequest request,
        IPackageSourceAuthorization sourceAuthorization,
        PackageLibraryRealizationPlan plan,
        PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceOperation);

        var house = new PackageHouse(
            sourceAuthorization,
            plan.PayloadAcquisition,
            plan.PayloadAcquisition.Log,
            plan.VersionSettlement);
        bool implementation =
            request.Depth == PackageLibraryRealizationDepth.Implementation;
        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                    CreateHouseRequest(
                        request,
                        implementation),
                    sourceOperation)
                .ConfigureAwait(false);
        return Project(settlement, request.Selector);
    }

    private static PackageHouseRequest CreateHouseRequest(
        PackageLibraryRealizationRequest request,
        bool implementation) =>
        new(
            request.Package,
            request.Operation,
            PackageHouseTargetContext.Exact(request.TargetFramework),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries,
            association: null,
            assetDemand: PackageAssetDemand.SurfaceAndImplementation,
            implementationNames: implementation
                ? [request.Selector.ImplementationName]
                : null,
            fileDemand: null,
            evidenceDemand: PackageHouseEvidenceDemand.None,
            libraryCompanionDemand: request.CompanionDemand,
            contentQuery: null,
            allowReferenceOnlyImplementationNames: implementation);

    private static PackageLibraryRealizationResult Project(
        PackageHouseSettlement settlement,
        PackageLibrarySelector selector)
    {
        if (settlement is not PackageHouseSettlement.Acquired acquired)
        {
            return new PackageLibraryRealizationResult.Unavailable(
                PackageLibraryRealizationStatus.NotSettled,
                settlement);
        }
        if (settlement.Result is not PackageHouseResult.Settled
            || settlement.Result.Evidence.Realization
                is not PackageHouseRealizationReceipt.Compile realization)
        {
            return new PackageLibraryRealizationResult.Unavailable(
                PackageLibraryRealizationStatus.NotRealized,
                settlement);
        }

        ImmutableArray<PackageHouseLibraryHandoff.Compile> matches =
            Match(realization, selector);
        return matches.Length switch
        {
            0 => new PackageLibraryRealizationResult.Unavailable(
                PackageLibraryRealizationStatus.Missing,
                settlement),
            1 => new PackageLibraryRealizationResult.Realized(
                acquired,
                realization,
                matches[0]),
            _ => new PackageLibraryRealizationResult.Unavailable(
                PackageLibraryRealizationStatus.Ambiguous,
                settlement),
        };
    }

    private static ImmutableArray<PackageHouseLibraryHandoff.Compile> Match(
        PackageHouseRealizationReceipt.Compile realization,
        PackageLibrarySelector selector)
    {
        PackageHouseLibraryHandoff.Compile[] handoffs =
        [
            .. realization.LibraryHandoffs
                .OfType<PackageHouseLibraryHandoff.Compile>(),
        ];
        if (selector.Kind is PackageLibrarySelectionKind.AssetId
            or PackageLibrarySelectionKind.Query)
        {
            PackageHouseLibraryHandoff.Compile[] identityMatches =
            [
                .. handoffs.Where(candidate =>
                    candidate.Asset.Id.Equals(
                        selector.Value,
                        StringComparison.Ordinal)),
            ];
            if (selector.Kind == PackageLibrarySelectionKind.AssetId
                || identityMatches.Length > 0)
            {
                return [.. identityMatches];
            }
        }

        if (selector.Kind == PackageLibrarySelectionKind.AssetPath
            || selector.Value.Contains('/'))
        {
            return
            [
                .. handoffs.Where(candidate =>
                    PathMatches(candidate, selector.Value)),
            ];
        }

        return
        [
            .. handoffs.Where(candidate =>
                NameMatches(candidate.Asset.AssemblyName, selector.Value)
                || candidate.ImplementationAsset is { } implementation
                    && NameMatches(
                        implementation.AssemblyName,
                        selector.Value)),
        ];
    }

    private static bool PathMatches(
        PackageHouseLibraryHandoff.Compile candidate,
        string path) =>
        candidate.Asset.Path.Equals(
            path,
            StringComparison.OrdinalIgnoreCase)
        || candidate.ImplementationAsset?.Path.Equals(
            path,
            StringComparison.OrdinalIgnoreCase)
            is true;

    private static bool NameMatches(string assetName, string query)
    {
        if (assetName.Equals(
            query,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ReadOnlySpan<char> assetAssemblyName =
            WithoutDllExtension(assetName);
        ReadOnlySpan<char> queryAssemblyName =
            WithoutDllExtension(query);
        return assetAssemblyName.Equals(
            queryAssemblyName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<char> WithoutDllExtension(string value) =>
        value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? value.AsSpan(0, value.Length - ".dll".Length)
            : value;
}
