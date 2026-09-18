using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>One detached package version population outcome for a production host.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Populated), "available")]
[JsonDerivedType(typeof(NotAvailable), "notAvailable")]
public abstract record PackageVersionPopulationOutcome
{
    private protected PackageVersionPopulationOutcome() { }

    public sealed record Populated(
        PackageVersionPopulationDocument Document,
        PackageVersionPopulationCountOutcome? Count)
        : PackageVersionPopulationOutcome;

    public sealed record NotAvailable(PackageVersionPopulationFailure Failure)
        : PackageVersionPopulationOutcome;
}

/// <summary>The normalized semantic input retained by a population document.</summary>
public sealed record PackageVersionPopulationRequest(
    string PackageId,
    string StartVersion,
    string EndVersion,
    bool IncludePrerelease,
    bool IncludeUnlisted);

/// <summary>A complete directed package version population and its source rows.</summary>
public sealed record PackageVersionPopulationDocument(
    PackageVersionPopulationRequest Request,
    ImmutableArray<PackageVersionPopulationVersion> Versions,
    ImmutableArray<PackageVersionSourceInfo> SourceListings);

/// <summary>One stable address in a directed package version population.</summary>
public sealed record PackageVersionPopulationVersion(
    int Ordinal,
    string Selector,
    string Version,
    bool Listed);

[JsonConverter(typeof(JsonStringEnumConverter<PackageVersionPopulationCountCohort>))]
public enum PackageVersionPopulationCountCohort
{
    Versions,
    SourceListings,
}

/// <summary>One semantic Count request over a package population cohort.</summary>
public sealed record PackageVersionPopulationCountRequest(
    PackageVersionPopulationCountCohort Cohort,
    RowSelectionIntent<string>? RowSelection = null);

/// <summary>The completed or rejected result of a requested population Count.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Completed), "completed")]
[JsonDerivedType(typeof(Rejected), "rejected")]
public abstract record PackageVersionPopulationCountOutcome
{
    private protected PackageVersionPopulationCountOutcome() { }

    public sealed record Completed(PackageVersionPopulationCountResult Result)
        : PackageVersionPopulationCountOutcome;

    public sealed record Rejected(PackageVersionPopulationCountFailure Failure)
        : PackageVersionPopulationCountOutcome;
}

/// <summary>One owner-issued Count answer at the requested population grain.</summary>
public sealed record PackageVersionPopulationCountResult(
    PackageVersionPopulationCountCohort Cohort,
    int Value);

/// <summary>A semantic row-selection failure while constructing Count.</summary>
public sealed record PackageVersionPopulationCountFailure(
    PackageVersionPopulationCountCohort Cohort,
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

[JsonConverter(typeof(JsonStringEnumConverter<PackageVersionPopulationFailureKind>))]
public enum PackageVersionPopulationFailureKind
{
    NotFound,
    NoMatch,
    Rejected,
    Unavailable,
    Incomplete,
    Failed,
}

/// <summary>A typed non-success, not an empty package population.</summary>
public sealed record PackageVersionPopulationFailure(
    PackageVersionPopulationRequest Request,
    PackageVersionPopulationFailureKind Kind,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Reason,
    bool OperationTimedOut,
    ImmutableArray<PackageVersionPopulationAuthorityFailure> AuthorityFailures);

/// <summary>Credential-safe source failure evidence without live source authority.</summary>
public sealed record PackageVersionPopulationAuthorityFailure(
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Authority,
    PackageAuthorityFailureKind Kind,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Message,
    PackageSourceTimeoutKind? TimeoutKind);
