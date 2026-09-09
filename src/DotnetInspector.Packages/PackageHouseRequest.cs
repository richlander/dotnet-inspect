using DotnetInspector.Platforms;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The maximum work one PackageHouse request authorizes.</summary>
public enum PackageHouseOperationProfile
{
    Settle,
    Acquire,
    Realize,
}

/// <summary>The existing package-owner selection result one realization requests.</summary>
public enum PackageHouseAssetSelectionKind
{
    Compile,
    Runtime,
}

/// <summary>Whether a realization remains package-shaped or also unwraps libraries.</summary>
public enum PackageHouseLibraryHandoffMode
{
    PackageOnly,
    SelectedLibraries,
}

/// <summary>How the package target framework is selected.</summary>
public enum PackageHouseTargetSelectionMode
{
    Exact,
    OwnerDefault,
}

/// <summary>Opaque identity for one PackageHouse operation declaration.</summary>
public sealed class PackageHouseOperationIdentity
{
    internal PackageHouseOperationIdentity()
    {
    }

    public override string ToString() => nameof(PackageHouseOperationIdentity);
}

/// <summary>Opaque caller-owned association with the request's originating fact.</summary>
public sealed class PackageHouseRequestAssociation
{
    private PackageHouseRequestAssociation()
    {
    }

    public static PackageHouseRequestAssociation Create() => new();

    public override string ToString() => nameof(PackageHouseRequestAssociation);
}

/// <summary>One resource-free PackageHouse operation declaration.</summary>
public sealed class PackageHouseOperation
{
    private PackageHouseOperation(
        PackageHouseOperationProfile profile,
        TimeSpan requestTimeout,
        TimeSpan operationTimeout)
    {
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile));

        ValidateTimeout(requestTimeout, nameof(requestTimeout));
        ValidateTimeout(operationTimeout, nameof(operationTimeout));

        Identity = new PackageHouseOperationIdentity();
        Profile = profile;
        RequestTimeout = requestTimeout;
        OperationTimeout = operationTimeout;
    }

    public PackageHouseOperationIdentity Identity { get; }

    public PackageHouseOperationProfile Profile { get; }

    public TimeSpan RequestTimeout { get; }

    public TimeSpan OperationTimeout { get; }

    public static PackageHouseOperation Create(
        PackageHouseOperationProfile profile,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null) =>
        new(
            profile,
            requestTimeout ?? NuGetFetchOptions.DefaultRequestTimeout,
            operationTimeout ?? NuGetFetchOptions.DefaultOperationTimeout);

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                "A PackageHouse timeout must be finite and positive.");
        }
    }
}

/// <summary>One PackageHouse demand form supported by this contract floor.</summary>
public abstract class PackageHouseDemand
{
    private PackageHouseDemand()
    {
    }

    /// <summary>One already normalized exact package coordinate.</summary>
    public sealed class Exact : PackageHouseDemand
    {
        public Exact(PackageSourceCoordinate coordinate)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            Coordinate = coordinate;
        }

        public PackageSourceCoordinate Coordinate { get; }
    }
}

/// <summary>One package-owned target-selection context.</summary>
public sealed class PackageHouseTargetContext
{
    private PackageHouseTargetContext(
        PackageHouseTargetSelectionMode mode,
        string? requestedFramework,
        string? runtimeIdentifier,
        PlatformFamilyTarget? platformTarget)
    {
        Mode = mode;
        RequestedFramework = requestedFramework;
        RuntimeIdentifier = runtimeIdentifier;
        PlatformTarget = platformTarget;
    }

    public PackageHouseTargetSelectionMode Mode { get; }

    public string? RequestedFramework { get; }

    public string? RuntimeIdentifier { get; }

    public PlatformFamilyTarget? PlatformTarget { get; }

    public static PackageHouseTargetContext Exact(
        string requestedFramework,
        string? runtimeIdentifier = null,
        PlatformFamilyTarget? platformTarget = null)
    {
        string framework = NormalizeFramework(
            requestedFramework,
            nameof(requestedFramework));
        string? runtime = ValidateRuntimeIdentifier(
            runtimeIdentifier,
            nameof(runtimeIdentifier));

        if (platformTarget is not null
            && !framework.Equals(
                platformTarget.TargetFramework.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The platform correspondence must describe the requested package framework.",
                nameof(platformTarget));
        }

        return new PackageHouseTargetContext(
            PackageHouseTargetSelectionMode.Exact,
            framework,
            runtime,
            platformTarget);
    }

    public static PackageHouseTargetContext OwnerDefault(
        string? runtimeIdentifier = null) =>
        new(
            PackageHouseTargetSelectionMode.OwnerDefault,
            requestedFramework: null,
            ValidateRuntimeIdentifier(
                runtimeIdentifier,
                nameof(runtimeIdentifier)),
            platformTarget: null);

    internal static string NormalizeFramework(
        string value,
        string parameterName)
    {
        if (!PackageCoordinateResolver.IsAcquisitionTargetText(value))
        {
            throw new ArgumentException(
                "A package target framework must be a bounded ASCII target moniker.",
                parameterName);
        }

        return value.ToLowerInvariant();
    }

    internal static string? ValidateRuntimeIdentifier(
        string? value,
        string parameterName)
    {
        if (value is null)
            return null;

        if (!PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(value))
        {
            throw new ArgumentException(
                "A package runtime identifier must use its canonical lowercase spelling.",
                parameterName);
        }

        return value;
    }
}

/// <summary>The immutable resource-free input to one PackageHouse operation.</summary>
public sealed class PackageHouseRequest
{
    public PackageHouseRequest(
        PackageHouseDemand demand,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext = null,
        PackageHouseAssetSelectionKind? assetSelection = null,
        PackageHouseLibraryHandoffMode libraryHandoff =
            PackageHouseLibraryHandoffMode.PackageOnly,
        PackageHouseRequestAssociation? association = null)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(libraryHandoff))
            throw new ArgumentOutOfRangeException(nameof(libraryHandoff));
        if (assetSelection is { } selection && !Enum.IsDefined(selection))
            throw new ArgumentOutOfRangeException(nameof(assetSelection));

        bool realizes =
            operation.Profile == PackageHouseOperationProfile.Realize;
        if (realizes != assetSelection.HasValue)
        {
            throw new ArgumentException(
                "Only a Realize operation declares an asset-selection kind.",
                nameof(assetSelection));
        }

        if (!realizes
            && libraryHandoff != PackageHouseLibraryHandoffMode.PackageOnly)
        {
            throw new ArgumentException(
                "Only a Realize operation can request library handoffs.",
                nameof(libraryHandoff));
        }

        Demand = demand;
        Operation = operation;
        TargetContext = targetContext;
        AssetSelection = assetSelection;
        LibraryHandoff = libraryHandoff;
        Association = association;
    }

    public PackageHouseDemand Demand { get; }

    public PackageHouseOperation Operation { get; }

    public PackageHouseTargetContext? TargetContext { get; }

    public PackageHouseAssetSelectionKind? AssetSelection { get; }

    public PackageHouseLibraryHandoffMode LibraryHandoff { get; }

    public PackageHouseRequestAssociation? Association { get; }
}
