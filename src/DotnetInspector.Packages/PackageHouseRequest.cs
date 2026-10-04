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

/// <summary>Optional package-local content needed by selected Library consumers.</summary>
public enum PackageHouseLibraryCompanionDemand
{
    None,
    ImplementationPortablePdb,
}

/// <summary>Additional package-authored evidence one realization reads.</summary>
public enum PackageHouseEvidenceDemand
{
    None,
    FrameworkReferences,
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
        if (timeout > NuGetOperationContext.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                $"A PackageHouse timeout cannot exceed {NuGetOperationContext.MaximumTimeout}.");
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

    /// <summary>
    /// One exact candidate issued by the supplied operation's root generation.
    /// </summary>
    public sealed class Candidate : PackageHouseDemand
    {
        public Candidate(PackageAcquisitionCandidate value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public PackageAcquisitionCandidate Value { get; }
    }

    /// <summary>One unresolved package version-selection request.</summary>
    public sealed class Selecting : PackageHouseDemand
    {
        public Selecting(PackageVersionSelectionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Request = request;
        }

        public PackageVersionSelectionRequest Request { get; }
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
        PackageHouseRequestAssociation? association = null,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation,
        IEnumerable<string>? implementationNames = null,
        PackageFileDemand? fileDemand = null,
        PackageHouseEvidenceDemand evidenceDemand =
            PackageHouseEvidenceDemand.None,
        PackageHouseLibraryCompanionDemand libraryCompanionDemand =
            PackageHouseLibraryCompanionDemand.None,
        PackageHouseContentQuery? contentQuery = null)
        : this(
            demand,
            operation,
            targetContext,
            assetSelection,
            libraryHandoff,
            association,
            assetDemand,
            implementationNames,
            fileDemand,
            evidenceDemand,
            libraryCompanionDemand,
            contentQuery,
            allowReferenceOnlyImplementationNames: false)
    {
    }

    internal PackageHouseRequest(
        PackageHouseDemand demand,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        PackageHouseAssetSelectionKind? assetSelection,
        PackageHouseLibraryHandoffMode libraryHandoff,
        PackageHouseRequestAssociation? association,
        PackageAssetDemand assetDemand,
        IEnumerable<string>? implementationNames,
        PackageFileDemand? fileDemand,
        PackageHouseEvidenceDemand evidenceDemand,
        PackageHouseLibraryCompanionDemand libraryCompanionDemand,
        PackageHouseContentQuery? contentQuery,
        bool allowReferenceOnlyImplementationNames)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(libraryHandoff))
            throw new ArgumentOutOfRangeException(nameof(libraryHandoff));
        if (assetSelection is { } selection && !Enum.IsDefined(selection))
            throw new ArgumentOutOfRangeException(nameof(assetSelection));
        if (!Enum.IsDefined(assetDemand))
            throw new ArgumentOutOfRangeException(nameof(assetDemand));
        if (!Enum.IsDefined(evidenceDemand))
            throw new ArgumentOutOfRangeException(nameof(evidenceDemand));
        if (!Enum.IsDefined(libraryCompanionDemand))
        {
            throw new ArgumentOutOfRangeException(
                nameof(libraryCompanionDemand));
        }

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

        if (implementationNames is not null)
        {
            if (!realizes)
            {
                throw new ArgumentException(
                    "Only a Realize operation can name implementation assemblies.",
                    nameof(implementationNames));
            }
            if (assetDemand != PackageAssetDemand.SurfaceAndImplementation)
            {
                throw new ArgumentException(
                    "Named implementation assemblies require the SurfaceAndImplementation demand.",
                    nameof(implementationNames));
            }
        }
        if (allowReferenceOnlyImplementationNames
            && (implementationNames is null
                || assetSelection
                    != PackageHouseAssetSelectionKind.Compile
                || libraryHandoff
                    != PackageHouseLibraryHandoffMode.SelectedLibraries))
        {
            throw new ArgumentException(
                "Reference-only implementation names require named compile implementation demand with selected Library handoffs.",
                nameof(allowReferenceOnlyImplementationNames));
        }

        if (fileDemand is not null
            && operation.Profile != PackageHouseOperationProfile.Acquire)
        {
            throw new ArgumentException(
                "Only an Acquire operation carries a file demand.",
                nameof(fileDemand));
        }
        if (contentQuery is not null
            && operation.Profile != PackageHouseOperationProfile.Acquire)
        {
            throw new ArgumentException(
                "Only an Acquire operation carries a semantic content query.",
                nameof(contentQuery));
        }
        if (contentQuery is not null && fileDemand is not null)
        {
            throw new ArgumentException(
                "A semantic content query and legacy file demand are mutually exclusive.",
                nameof(contentQuery));
        }
        if (contentQuery?.Narrowing
                is PackageHouseContentNarrowing.PackageWide
            && targetContext is not null)
        {
            throw new ArgumentException(
                "Package-wide content narrowing does not carry a target context.",
                nameof(targetContext));
        }
        if (contentQuery?.Narrowing
                is PackageHouseContentNarrowing.TfmWide tfmWide
            && !ReferenceEquals(tfmWide.Target, targetContext))
        {
            throw new ArgumentException(
                "TFM-wide content narrowing requires its exact target context.",
                nameof(targetContext));
        }
        if (contentQuery?.RetainedFileList is { } retainedFileList)
        {
            PackageSourceCoordinate? requestedCoordinate = demand switch
            {
                PackageHouseDemand.Exact exact => exact.Coordinate,
                PackageHouseDemand.Candidate candidate =>
                    candidate.Value.Coordinate,
                _ => null,
            };
            if (requestedCoordinate is null
                || requestedCoordinate
                    != retainedFileList
                        .Narrowing
                        .Acquisition
                        .Candidate
                        .Coordinate)
            {
                throw new ArgumentException(
                    "Retained File List evidence requires its exact package coordinate.",
                    nameof(contentQuery));
            }
        }

        if (evidenceDemand != PackageHouseEvidenceDemand.None
            && (!realizes
                || assetSelection != PackageHouseAssetSelectionKind.Compile))
        {
            throw new ArgumentException(
                "Framework-reference evidence requires a compile Realize operation.",
                nameof(evidenceDemand));
        }
        if (libraryCompanionDemand
                != PackageHouseLibraryCompanionDemand.None
            && (!realizes
                || assetSelection
                    != PackageHouseAssetSelectionKind.Compile
                || libraryHandoff
                    != PackageHouseLibraryHandoffMode.SelectedLibraries
                || assetDemand
                    != PackageAssetDemand.SurfaceAndImplementation))
        {
            throw new ArgumentException(
                "Library companion demand requires a compile Realize operation with selected Library handoffs and implementation assets.",
                nameof(libraryCompanionDemand));
        }

        Demand = demand;
        Operation = operation;
        TargetContext = targetContext;
        AssetSelection = assetSelection;
        LibraryHandoff = libraryHandoff;
        Association = association;
        AssetDemand = assetDemand;
        FileDemand = fileDemand;
        ContentQuery = contentQuery;
        EvidenceDemand = evidenceDemand;
        LibraryCompanionDemand = libraryCompanionDemand;
        AllowReferenceOnlyImplementationNames =
            allowReferenceOnlyImplementationNames;
        ImplementationNames = implementationNames is null
            ? null
            : PackageImplementationNames.Create(
                implementationNames,
                nameof(implementationNames));
    }

    public PackageHouseDemand Demand { get; }

    public PackageHouseOperation Operation { get; }

    public PackageHouseTargetContext? TargetContext { get; }

    public PackageHouseAssetSelectionKind? AssetSelection { get; }

    public PackageHouseLibraryHandoffMode LibraryHandoff { get; }

    public PackageHouseRequestAssociation? Association { get; }

    /// <summary>
    /// Which assets the consumer reads. A ranged Realize reads only these;
    /// complete access acquires the whole archive regardless.
    /// </summary>
    public PackageAssetDemand AssetDemand { get; }

    /// <summary>
    /// The implementation assemblies the consumer names, by file name, or
    /// <see langword="null"/> for every selected implementation asset. With
    /// names, the realization selects only the named implementation assets,
    /// and a ranged read fetches the aligned blocks that hold them
    /// (docs/design/package-read-demand.md).
    /// </summary>
    public PackageImplementationNames? ImplementationNames { get; }

    /// <summary>
    /// Whether a named implementation demand is satisfied when the same name
    /// selects an API asset whose owner-issued correspondence has no
    /// implementation counterpart. Other unmatched names remain a visible
    /// realization failure.
    /// </summary>
    internal bool AllowReferenceOnlyImplementationNames { get; }

    /// <summary>
    /// The package files an Acquire operation reads, or
    /// <see langword="null"/>. It bounds a ranged read, which an Acquire
    /// operation may take only with one, and every named entry and folder
    /// must be listed by the acquired archive's directory
    /// (docs/design/package-read-demand.md#exact-file-demand).
    /// </summary>
    public PackageFileDemand? FileDemand { get; }

    /// <summary>
    /// The semantic package-content request carried by an Acquire operation,
    /// or <see langword="null"/> for legacy operation-specific demand.
    /// </summary>
    public PackageHouseContentQuery? ContentQuery { get; }

    /// <summary>
    /// Additional package-authored evidence this compile realization reads.
    /// A ranged acquisition adds only the evidence entry to the selected
    /// compile entries.
    /// </summary>
    public PackageHouseEvidenceDemand EvidenceDemand { get; }

    /// <summary>
    /// Optional package-local content retained for selected Library handoffs.
    /// </summary>
    public PackageHouseLibraryCompanionDemand LibraryCompanionDemand { get; }
}
