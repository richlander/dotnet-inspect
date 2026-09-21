using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.Packages;

public sealed record PackageHouseLibraryMaterializationLimits
{
    public const long DefaultMaxContentBytes = 64L * 1024 * 1024;
    public const long DefaultMaxRetainedBytes = 128L * 1024 * 1024;

    public long MaxContentBytes { get; init; } =
        DefaultMaxContentBytes;

    public long MaxRetainedBytes { get; init; } =
        DefaultMaxRetainedBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxContentBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxContentBytes,
            int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxRetainedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxRetainedBytes,
            int.MaxValue);
    }
}

[Flags]
public enum PackageHouseLibraryArtifactRole
{
    ApiAssembly = 1,
    ImplementationAssembly = 2,
    ApiCompiledXmlDocumentation = 4,
    ImplementationPortablePdb = 8,
}

/// <summary>
/// Resource-free PackageHouse evidence attached to one materialized Artifact.
/// </summary>
public sealed class PackageHouseLibraryArtifactProvenance
    : IArtifactProvenance
{
    internal PackageHouseLibraryArtifactProvenance(
        PackageHouseLibraryHandoff.Compile handoff,
        PackageCompileAsset associatedAsset,
        string packagePath,
        PackageHouseLibraryArtifactRole roles)
    {
        Handoff = handoff;
        AssociatedAsset = associatedAsset;
        PackagePath = packagePath;
        Roles = roles;
    }

    public PackageHouseLibraryHandoff.Compile Handoff { get; }
    public PackageCompileAsset AssociatedAsset { get; }
    public string PackagePath { get; }
    public PackageHouseLibraryArtifactRole Roles { get; }
}

/// <summary>
/// Resource-free receipt for one exact PackageHouse-to-Library realization.
/// </summary>
public sealed class PackageHouseLibraryMaterializationReceipt
{
    internal PackageHouseLibraryMaterializationReceipt(
        PackageHouseResult packageResult,
        PackageHouseLibraryHandoff.Compile handoff,
        LibraryReference library)
    {
        PackageResult = packageResult;
        Handoff = handoff;
        Library = library;
    }

    public PackageHouseResult PackageResult { get; }
    public PackageHouseLibraryHandoff.Compile Handoff { get; }
    public LibraryReference Library { get; }
}

public enum PackageHouseLibraryMaterializationFailureKind
{
    InvalidSettlement,
    InvalidHandoff,
    MissingPackageEntry,
    ContentByteLimit,
    RetainedByteLimit,
    ArtifactPublication,
    MetadataProjection,
    AssemblyIdentityMismatch,
    ArtifactRetirement,
}

/// <summary>
/// Resource-free evidence for a terminal materialization attempt.
/// </summary>
public sealed class PackageHouseLibraryMaterializationFailure
{
    internal PackageHouseLibraryMaterializationFailure(
        PackageHouseResult packageResult,
        PackageHouseLibraryHandoff.Compile handoff,
        IEnumerable<
            PackageHouseLibraryMaterializationFailureKind> failures)
    {
        PackageHouseLibraryMaterializationFailureKind[] snapshot =
            [.. failures];
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A terminal materialization requires at least one failure.",
                nameof(failures));
        }

        PackageResult = packageResult;
        Handoff = handoff;
        Failures = Array.AsReadOnly(snapshot);
    }

    public PackageHouseResult PackageResult { get; }
    public PackageHouseLibraryHandoff.Compile Handoff { get; }
    public IReadOnlyList<
        PackageHouseLibraryMaterializationFailureKind> Failures
    { get; }
}

public abstract class PackageHouseLibraryMaterializationOutcome
{
    private protected PackageHouseLibraryMaterializationOutcome()
    {
    }

    /// <summary>
    /// Transfers distinct Library and Artifact authorities to the caller.
    /// Retire the Library owner before retiring the Artifact session.
    /// </summary>
    [ResourceOwnership]
    public sealed class Completed : PackageHouseLibraryMaterializationOutcome
    {
        internal Completed(
            PackageHouseLibraryMaterializationReceipt receipt,
            LibraryContentOwner owner,
            ArtifactSetSession artifacts)
        {
            Receipt = receipt;
            Owner = owner;
            Artifacts = artifacts;
        }

        public PackageHouseLibraryMaterializationReceipt Receipt { get; }
        public LibraryContentOwner Owner { get; }
        public ArtifactSetSession Artifacts { get; }
    }

    public sealed class Terminal : PackageHouseLibraryMaterializationOutcome
    {
        internal Terminal(
            PackageHouseLibraryMaterializationFailure evidence)
        {
            Evidence = evidence;
        }

        public PackageHouseLibraryMaterializationFailure Evidence { get; }
    }
}
