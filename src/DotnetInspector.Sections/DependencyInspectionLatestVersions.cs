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
    int DiscoveryFailed)
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
            DiscoveryFailed: 0);
}
