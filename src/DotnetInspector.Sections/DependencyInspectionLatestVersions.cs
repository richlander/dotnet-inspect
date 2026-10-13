using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using InertText;
using NuGet.Versioning;

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

/// <summary>The first version component at which Latest exceeds Current.</summary>
public enum DependencyInspectionVersionLag
{
    None,
    Major,
    Minor,
    Patch,
    Revision,
    Prerelease,
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
    DependencyInspectionVersionLag? Behind,
    int? MajorsBehind,
    DependencyInspectionLatestVersionState State,
    string? FailureKind,
    [property: JsonConverter(typeof(ProseInertStringJsonConverter))]
    InertString? FailureMessage)
{
    /// <summary>
    /// Returns the inclusive minimum of a canonical NuGet version constraint,
    /// or null when the constraint has no inclusive lower bound.
    /// </summary>
    public static NuGetVersion? InclusiveMinimum(string versionConstraint) =>
        VersionRange.TryParse(versionConstraint, out VersionRange? range)
        && range.HasLowerBound
        && range.IsMinInclusive
            ? range.MinVersion
            : null;
}

/// <summary>Latest, Latest In Major, Behind, and Majors Behind over one candidate population.</summary>
public sealed record DependencyInspectionVersionComparison(
    string? LatestInMajor,
    DependencyInspectionVersionLag Behind,
    int MajorsBehind)
{
    private static readonly IVersionComparer Comparer =
        VersionComparer.VersionReleaseMetadata;

    /// <summary>
    /// Compares Current with Latest and the admitted listed population that
    /// produced Latest. Current need not be a member of that population.
    /// </summary>
    public static DependencyInspectionVersionComparison Create(
        NuGetVersion current,
        NuGetVersion latest,
        IEnumerable<NuGetVersion> listed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(listed);
        ImmutableArray<NuGetVersion> population = [.. listed];
        NuGetVersion? latestInMajor = population
            .Where(version => version.Major == current.Major)
            .OrderByDescending(static version => version, Comparer)
            .FirstOrDefault();
        if (Comparer.Compare(latest, current) <= 0)
        {
            return new(
                latestInMajor?.ToNormalizedString(),
                DependencyInspectionVersionLag.None,
                MajorsBehind: 0);
        }

        int majorsBehind = population
            .Where(version =>
                version.Major > current.Major
                && Comparer.Compare(version, latest) <= 0)
            .Select(static version => version.Major)
            .Distinct()
            .Count();
        return new(
            latestInMajor?.ToNormalizedString(),
            Lag(current, latest),
            majorsBehind);
    }

    private static DependencyInspectionVersionLag Lag(
        NuGetVersion current,
        NuGetVersion latest) =>
        latest.Major != current.Major
            ? DependencyInspectionVersionLag.Major
            : latest.Minor != current.Minor
                ? DependencyInspectionVersionLag.Minor
                : latest.Patch != current.Patch
                    ? DependencyInspectionVersionLag.Patch
                    : latest.Revision != current.Revision
                        ? DependencyInspectionVersionLag.Revision
                        : DependencyInspectionVersionLag.Prerelease;
}

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
