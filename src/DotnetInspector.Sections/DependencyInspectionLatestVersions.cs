using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

public enum DependencyInspectionLatestVersionCompletion
{
    NotRequested,
    Complete,
    Partial,
}

/// <summary>Where a Latest Versions row's Current value comes from.</summary>
public enum DependencyInspectionCurrentVersionBasis
{
    /// <summary>Owner-issued restored evidence associates the declaration with a resolved edge.</summary>
    Resolved,

    /// <summary>The declared constraint's inclusive minimum; not a restore claim.</summary>
    LowerBound,
}

public enum DependencyInspectionLatestVersionState
{
    Compared,
    NoCurrentVersion,
    NoListedVersion,
    DiscoveryFailed,
}

/// <summary>Whether PackageHouse can select a compile slice of Latest for the Compatibility Target.</summary>
public enum DependencyInspectionLatestCompatibility
{
    /// <summary>The House settled the target, including through an explicit empty reference group.</summary>
    Yes,

    /// <summary>The House found compile assets but none for the target framework.</summary>
    No,

    /// <summary>The House found no compile assets, as in a dependency-only or tools package.</summary>
    NoAssets,

    /// <summary>The House could not answer; a typed failure accompanies the row.</summary>
    Unavailable,
}

/// <summary>One direct declaration's current version and latest listed versions.</summary>
public sealed record DependencyInspectionLatestVersion(
    int RootOccurrence,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    string PackageId,
    string VersionConstraint,
    string? CurrentVersion,
    DependencyInspectionCurrentVersionBasis? CurrentBasis,
    string? LatestInMajor,
    string? LatestVersion,
    PackageVersionLag? Behind,
    int? MajorsBehind,
    string CompatibilityTarget,
    DependencyInspectionLatestCompatibility? LatestCompatible,
    DependencyInspectionLatestVersionState State,
    string? FailureKind,
    [property: JsonConverter(typeof(ProseInertStringJsonConverter))]
    InertString? FailureMessage);

public sealed record DependencyInspectionLatestVersionSummary(
    DependencyInspectionLatestVersionCompletion Completion,
    int Declarations,
    int Packages,
    int Compared,
    int Behind,
    int NoCurrentVersion,
    int NoListedVersion,
    int DiscoveryFailed,
    int Incompatible,
    int CompatibilityUnavailable)
{
    public static DependencyInspectionLatestVersionSummary NotRequested
    { get; } =
        new(
            DependencyInspectionLatestVersionCompletion.NotRequested,
            Declarations: 0,
            Packages: 0,
            Compared: 0,
            Behind: 0,
            NoCurrentVersion: 0,
            NoListedVersion: 0,
            DiscoveryFailed: 0,
            Incompatible: 0,
            CompatibilityUnavailable: 0);
}
