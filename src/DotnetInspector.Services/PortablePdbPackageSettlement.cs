using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// Host capability that executes one PackageHouse semantic content query.
/// </summary>
public interface IPortablePdbPackageContentSource
{
    Task<PackageHouseSettlement> AcquireAsync(
        PackageSourceCoordinate coordinate,
        PackageHouseContentQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One PackageHouse-issued package-local Portable PDB candidate.
/// </summary>
public sealed class PortablePdbPackageCandidate
{
    internal PortablePdbPackageCandidate(
        PackageHouseLibraryInventory inventory,
        PackageHouseLibraryInventoryRow row,
        IPortablePdbPackageContentSource source)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(source);
        if (!inventory.Rows.Contains(row))
        {
            throw new ArgumentException(
                "The package-local PDB row must belong to the supplied inventory.",
                nameof(row));
        }

        Inventory = inventory;
        Row = row;
        Source = source;
    }

    public PackageHouseLibraryInventory Inventory { get; }

    public PackageHouseLibraryInventoryRow Row { get; }

    internal IPortablePdbPackageContentSource Source { get; }

    public PackageSourceCoordinate Coordinate =>
        Inventory.Narrowing.Acquisition.Candidate.Coordinate;

    public PackageProducerIdentity Producer =>
        Inventory.Narrowing.Acquisition.Producer;
}

public enum PortablePdbPackageBindingFailureKind
{
    PackageUnavailable,
    PackageIncomplete,
    PackageFailed,
    AssemblyContentLimit,
    AssemblyContentUnavailable,
    InvalidAssemblyContent,
    AssemblyIdentityMismatch,
    SelectedLibraryUnavailable,
}

/// <summary>
/// The exact implementation assembly binding prepared for package-local
/// Portable PDB settlement.
/// </summary>
public sealed class PortablePdbPackageBinding
{
    internal PortablePdbPackageBinding(
        ResolvedAssemblyReference assembly,
        PackageHouseLibraryAndInventory library,
        PortablePdbPackageCandidate candidate)
    {
        Assembly = assembly;
        Library = library;
        Candidate = candidate;
    }

    public ResolvedAssemblyReference Assembly { get; }

    public PackageHouseLibraryAndInventory Library { get; }

    public PortablePdbPackageCandidate Candidate { get; }
}

public abstract record PortablePdbPackageBindingResult
{
    private protected PortablePdbPackageBindingResult()
    {
    }

    public sealed record Bound(
        PortablePdbPackageBinding Value)
        : PortablePdbPackageBindingResult;

    public sealed record Terminal(
        PortablePdbPackageBindingFailureKind Failure,
        PackageHouseSettlement Settlement)
        : PortablePdbPackageBindingResult;
}

/// <summary>
/// Package-local candidate preparation for an already realized exact
/// implementation assembly.
/// </summary>
public abstract record PortablePdbPackagePreparationResult
{
    private protected PortablePdbPackagePreparationResult()
    {
    }

    public sealed record Prepared(
        PortablePdbPackageCandidate Candidate)
        : PortablePdbPackagePreparationResult;

    public sealed record Terminal(
        PortablePdbPackageBindingFailureKind Failure,
        PackageHouseSettlement Settlement)
        : PortablePdbPackagePreparationResult;
}

/// <summary>
/// Deferred package-local candidate preparation for one exact already
/// realized package assembly.
/// </summary>
public sealed class PortablePdbPackagePreparation
{
    internal PortablePdbPackagePreparation(
        ResolvedAssemblyReference assembly,
        IPortablePdbPackageContentSource source,
        PackageProducerIdentity producer)
    {
        Assembly =
            assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        Source =
            source
            ?? throw new ArgumentNullException(nameof(source));
        Producer = producer;
    }

    internal ResolvedAssemblyReference Assembly { get; }

    internal IPortablePdbPackageContentSource Source { get; }

    public PackageProducerIdentity Producer { get; }

    internal Task<PortablePdbPackagePreparationResult> PrepareAsync(
        CancellationToken cancellationToken) =>
        PortablePdbPackageComposition.PrepareForAssemblyAsync(
            Assembly,
            Source,
            cancellationToken);
}

/// <summary>
/// Composes PackageHouse target inventory with the exact symbol-bearing
/// implementation assembly required by Portable PDB settlement.
/// </summary>
public static class PortablePdbPackageComposition
{
    private const long DefaultMaxAssemblyBytes =
        512L * 1024 * 1024;

    public static PortablePdbPackagePreparation DeferForAssembly(
        ResolvedAssemblyReference assembly,
        IPortablePdbPackageContentSource source,
        PackageProducerIdentity producer)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(source);
        if (assembly.Provenance
            is not AssemblyResolutionProvenance.PackageAsset
            {
                Tfm: not null,
                AssetPath: not null,
            })
        {
            throw new ArgumentException(
                "Deferred package-local Portable PDB preparation requires exact package target and asset provenance.",
                nameof(assembly));
        }

        return new(assembly, source, producer);
    }

    /// <summary>
    /// Issues a package-local candidate for an already owner-bound package
    /// implementation assembly without reacquiring that assembly.
    /// </summary>
    public static async Task<PortablePdbPackagePreparationResult>
        PrepareForAssemblyAsync(
        ResolvedAssemblyReference assembly,
        IPortablePdbPackageContentSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(source);
        if (assembly.Provenance
            is not AssemblyResolutionProvenance.PackageAsset
            {
                Tfm: { } framework,
                AssetPath: { } assetPath,
            } package)
        {
            throw new ArgumentException(
                "An existing package-local Portable PDB binding requires exact package target and asset provenance.",
                nameof(assembly));
        }

        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                package.PackageId,
                package.PackageVersion);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact(
                framework,
                package.Rid);
        PackageHouseSettlement inventorySettlement =
            await AcquireInventoryOnlyAsync(
                    coordinate,
                    target,
                    source,
                    cancellationToken)
                .ConfigureAwait(false);
        if (inventorySettlement
                is not PackageHouseSettlement.Acquired acquired
            || acquired.Result
                is not PackageHouseResult.Settled)
        {
            return new PortablePdbPackagePreparationResult.Terminal(
                Classify(inventorySettlement.Result),
                inventorySettlement);
        }

        PackageHouseLibraryInventory? inventory =
            acquired.Result.Evidence.LibraryInventory;
        if (inventory is null)
        {
            return new PortablePdbPackagePreparationResult.Terminal(
                PortablePdbPackageBindingFailureKind.PackageFailed,
                inventorySettlement);
        }

        PackageHouseLibraryInventoryRow[] matchingRows =
        [
            .. inventory.Rows.Where(
                candidate => string.Equals(
                    (candidate.ImplementationEntry
                        ?? candidate.CompileEntry).Path,
                    assetPath,
                    StringComparison.Ordinal)),
        ];
        if (matchingRows.Length != 1)
        {
            return new PortablePdbPackagePreparationResult.Terminal(
                PortablePdbPackageBindingFailureKind
                    .SelectedLibraryUnavailable,
                inventorySettlement);
        }

        return new PortablePdbPackagePreparationResult.Prepared(
            new PortablePdbPackageCandidate(
                inventory,
                matchingRows[0],
                source));
    }

    public static async Task<PortablePdbPackageBindingResult>
        PrepareAsync(
        PackageSourceCoordinate coordinate,
        PackageHouseTargetContext target,
        IPortablePdbPackageContentSource source,
        string? selectedLibraryPath = null,
        long maxAssemblyBytes = DefaultMaxAssemblyBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxAssemblyBytes);

        PackageHouseSettlement inventorySettlement =
            await AcquireInventoryAsync(
                    coordinate,
                    target,
                    source,
                    cancellationToken)
                .ConfigureAwait(false);
        if (inventorySettlement
                is not PackageHouseSettlement.Acquired acquired
            || acquired.Result
                is not PackageHouseResult.Settled)
        {
            return new PortablePdbPackageBindingResult.Terminal(
                Classify(inventorySettlement.Result),
                inventorySettlement);
        }

        PackageHouseLibraryAndInventory? library =
            acquired.Result.Evidence.LibraryAndInventory;
        if (library is null)
        {
            return new PortablePdbPackageBindingResult.Terminal(
                PortablePdbPackageBindingFailureKind.PackageFailed,
                inventorySettlement);
        }

        PackageHouseLibraryInventoryRow? row =
            selectedLibraryPath is null
                ? library.Selection.SelectedRow
                : library.Inventory.Rows.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.CompileEntry.Path,
                        selectedLibraryPath,
                        StringComparison.Ordinal));
        if (row is null)
        {
            return new PortablePdbPackageBindingResult.Terminal(
                PortablePdbPackageBindingFailureKind
                    .SelectedLibraryUnavailable,
                inventorySettlement);
        }

        PackageHouseContentNarrowing.TfmWide narrowing =
            (PackageHouseContentNarrowing.TfmWide)
                library.Inventory.Narrowing.Narrowing;
        PackageHouseSettlement.Acquired compileAcquired = acquired;
        PackageHouseSettlement compileSettlement = inventorySettlement;
        if (!row.CompileEntry.Equals(
                library.Selection.SelectedRow.CompileEntry))
        {
            PackageHouseContentQuery compileQuery =
                library.Inventory.CreateFilesQuery(
                    [row.CompileEntry]);
            compileSettlement =
                await source.AcquireAsync(
                        coordinate,
                        compileQuery,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (compileSettlement
                    is not PackageHouseSettlement.Acquired
                        selectedLibraryAcquired
                || selectedLibraryAcquired.Result
                    is not PackageHouseResult.Settled)
            {
                return new PortablePdbPackageBindingResult.Terminal(
                    Classify(compileSettlement.Result),
                    compileSettlement);
            }

            compileAcquired = selectedLibraryAcquired;
        }

        (byte[]? compileImage,
            PortablePdbPackageBindingFailureKind? compileFailure) =
            await ReadEntryAsync(
                    compileAcquired,
                    row.CompileEntry,
                    maxAssemblyBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        if (compileFailure is not null
            || compileImage is null)
        {
            return new PortablePdbPackageBindingResult.Terminal(
                compileFailure
                    ?? PortablePdbPackageBindingFailureKind
                        .AssemblyContentUnavailable,
                compileSettlement);
        }

        ResolvedAssemblyReference? compileAssembly =
            CreateAssemblyReference(
                coordinate,
                narrowing.Target,
                row.CompileEntry,
                compileImage);
        if (compileAssembly is null)
        {
            return new PortablePdbPackageBindingResult.Terminal(
                PortablePdbPackageBindingFailureKind
                    .InvalidAssemblyContent,
                compileSettlement);
        }

        ResolvedAssemblyReference assembly =
            compileAssembly;
        if (row.ImplementationEntry is { } implementationEntry
            && !implementationEntry.Equals(row.CompileEntry))
        {
            PackageHouseContentQuery implementationQuery =
                library.Inventory.CreateFilesQuery(
                    [implementationEntry]);
            PackageHouseSettlement implementationSettlement =
                await source.AcquireAsync(
                        coordinate,
                        implementationQuery,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (implementationSettlement
                    is not PackageHouseSettlement.Acquired
                        implementationAcquired
                || implementationAcquired.Result
                    is not PackageHouseResult.Settled)
            {
                return new PortablePdbPackageBindingResult.Terminal(
                    Classify(implementationSettlement.Result),
                    implementationSettlement);
            }

            (byte[]? implementationImage,
                PortablePdbPackageBindingFailureKind?
                    implementationFailure) =
                await ReadEntryAsync(
                        implementationAcquired,
                        implementationEntry,
                        maxAssemblyBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (implementationFailure is not null
                || implementationImage is null)
            {
                return new PortablePdbPackageBindingResult.Terminal(
                    implementationFailure
                        ?? PortablePdbPackageBindingFailureKind
                            .AssemblyContentUnavailable,
                    implementationSettlement);
            }

            ResolvedAssemblyReference? implementationAssembly =
                CreateAssemblyReference(
                    coordinate,
                    narrowing.Target,
                    implementationEntry,
                    implementationImage);
            if (implementationAssembly is null)
            {
                return new PortablePdbPackageBindingResult.Terminal(
                    PortablePdbPackageBindingFailureKind
                        .InvalidAssemblyContent,
                    implementationSettlement);
            }

            if (implementationAssembly.Identity
                != compileAssembly.Identity)
            {
                return new PortablePdbPackageBindingResult.Terminal(
                    PortablePdbPackageBindingFailureKind
                        .AssemblyIdentityMismatch,
                    implementationSettlement);
            }

            assembly = implementationAssembly;
        }

        var candidate =
            new PortablePdbPackageCandidate(
                library.Inventory,
                row,
                source);
        return new PortablePdbPackageBindingResult.Bound(
            new PortablePdbPackageBinding(
                assembly,
                library,
                candidate));
    }

    private static Task<PackageHouseSettlement>
        AcquireInventoryAsync(
        PackageSourceCoordinate coordinate,
        PackageHouseTargetContext target,
        IPortablePdbPackageContentSource source,
        CancellationToken cancellationToken)
    {
        PackageHouseContentQuery query =
            PackageHouseContentQuery
                .GetLibraryAndInventoryForTarget(target);
        return source.AcquireAsync(
            coordinate,
            query,
            cancellationToken);
    }

    private static Task<PackageHouseSettlement>
        AcquireInventoryOnlyAsync(
        PackageSourceCoordinate coordinate,
        PackageHouseTargetContext target,
        IPortablePdbPackageContentSource source,
        CancellationToken cancellationToken)
    {
        PackageHouseContentQuery query =
            PackageHouseContentQuery
                .GetLibraryInventoryForTarget(target);
        return source.AcquireAsync(
            coordinate,
            query,
            cancellationToken);
    }

    private static ResolvedAssemblyReference?
        CreateAssemblyReference(
        PackageSourceCoordinate coordinate,
        PackageHouseTargetContext target,
        PackageContentEntry entry,
        byte[] image)
    {
        try
        {
            return ResolvedAssemblyReference
                .CreateFromStreamIfManaged(
                    () => new MemoryStream(
                        image,
                        writable: false),
                    AssemblyResolutionProvenance.Package(
                        coordinate.PackageId,
                        coordinate.Version,
                        target.RequestedFramework,
                        target.RuntimeIdentifier,
                        entry.Path),
                    lastWriteTimeUtc: null,
                    assetFileName:
                        Path.GetFileName(entry.Path));
        }
        catch (Exception exception)
            when (exception
                is BadImageFormatException
                    or IOException
                    or InvalidDataException
                    or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<(
        byte[]? Content,
        PortablePdbPackageBindingFailureKind? Failure)>
        ReadEntryAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageContentEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maxBytes)
        {
            return (
                null,
                PortablePdbPackageBindingFailureKind
                    .AssemblyContentLimit);
        }

        try
        {
            using PackageHousePayloadRead read =
                settlement.OpenPayloadRead(
                    entry.Path,
                    maxBytes);
            using var content =
                new MemoryStream(
                    entry.Length <= int.MaxValue
                        ? checked((int)entry.Length)
                        : 0);
            await read.CopyToAsync(
                    content,
                    cancellationToken)
                .ConfigureAwait(false);
            return (content.ToArray(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return (
                null,
                PortablePdbPackageBindingFailureKind
                    .AssemblyContentLimit);
        }
        catch (Exception exception)
            when (exception
                is FileNotFoundException
                    or IOException
                    or NotSupportedException
                    or PackageEntryNotMaterializedException)
        {
            return (
                null,
                PortablePdbPackageBindingFailureKind
                    .AssemblyContentUnavailable);
        }
    }

    private static PortablePdbPackageBindingFailureKind Classify(
        PackageHouseResult result) =>
        result switch
        {
            PackageHouseResult.Incomplete =>
                PortablePdbPackageBindingFailureKind
                    .PackageIncomplete,
            PackageHouseResult.Unavailable
                or PackageHouseResult.NoMatch =>
                PortablePdbPackageBindingFailureKind
                    .PackageUnavailable,
            _ =>
                PortablePdbPackageBindingFailureKind
                    .PackageFailed,
        };
}
