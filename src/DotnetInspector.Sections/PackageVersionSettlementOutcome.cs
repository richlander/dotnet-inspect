using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>One detached exact/latest package settlement for a production host.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Settled), "settled")]
[JsonDerivedType(typeof(NotSettled), "notSettled")]
public abstract record PackageVersionSettlementOutcome
{
    private protected PackageVersionSettlementOutcome() { }

    public sealed record Settled(PackageVersionSettlementResult Result)
        : PackageVersionSettlementOutcome;

    public sealed record NotSettled(PackageVersionSettlementFailure Failure)
        : PackageVersionSettlementOutcome;
}

/// <summary>The selected coordinate and the evidence needed to interpret it.</summary>
public sealed record PackageVersionSettlementResult(
    PackageCoordinate Request,
    PackageSourceCoordinate Coordinate,
    bool IncludePrerelease,
    PackageVersionDiscoveryFreshness? Freshness,
    ImmutableArray<PackageVersionInfo> Listings,
    ImmutableArray<PackageVersionSourceInfo> SourceListings);

[JsonConverter(typeof(JsonStringEnumConverter<PackageVersionSettlementFailureKind>))]
public enum PackageVersionSettlementFailureKind
{
    NotFound,
    NoMatch,
    Ambiguous,
    Rejected,
    Unavailable,
    Incomplete,
    Failed,
}

/// <summary>A typed non-success, not an empty selected package.</summary>
public sealed record PackageVersionSettlementFailure(
    PackageCoordinate Request,
    PackageVersionSettlementFailureKind Kind,
    InertString Reason,
    bool OperationTimedOut,
    ImmutableArray<PackageVersionSettlementAuthorityFailure> AuthorityFailures);

/// <summary>Credential-safe source failure evidence without live source authority.</summary>
public sealed record PackageVersionSettlementAuthorityFailure(
    InertString Authority,
    PackageAuthorityFailureKind Kind,
    InertString Message,
    PackageSourceTimeoutKind? TimeoutKind);
