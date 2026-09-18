using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>One detached package version listing for a production host.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Listed), "available")]
[JsonDerivedType(typeof(NotAvailable), "notAvailable")]
public abstract record PackageVersionListingOutcome
{
    private protected PackageVersionListingOutcome() { }

    public sealed record Listed : PackageVersionListingOutcome
    {
        [JsonConstructor]
        public Listed(PackageVersionListingDocument document)
            : this(document, [])
        {
        }

        internal Listed(
            PackageVersionListingDocument document,
            ImmutableArray<PackageVersionListingAuthorityFailure>
                authorityFailures)
        {
            Document =
                document ?? throw new ArgumentNullException(nameof(document));
            AuthorityFailures = authorityFailures;
        }

        public PackageVersionListingDocument Document { get; }

        [JsonIgnore]
        public ImmutableArray<PackageVersionListingAuthorityFailure>
            AuthorityFailures { get; }
    }

    public sealed record NotAvailable(PackageVersionListingFailure Failure)
        : PackageVersionListingOutcome;
}

/// <summary>The normalized semantic input retained by a listing document.</summary>
public sealed record PackageVersionListingRequest(
    string PackageId,
    bool IncludePrerelease,
    bool IncludeUnlisted);

[JsonConverter(typeof(JsonStringEnumConverter<PackageVersionListingCompleteness>))]
public enum PackageVersionListingCompleteness
{
    Authoritative,
    Partial,
}

/// <summary>A complete detached package version listing and source rows.</summary>
public sealed record PackageVersionListingDocument(
    PackageVersionListingRequest Request,
    PackageVersionListingCompleteness Completeness,
    ImmutableArray<PackageVersionInfo> Versions,
    ImmutableArray<PackageVersionSourceInfo> SourceListings);

[JsonConverter(typeof(JsonStringEnumConverter<PackageVersionListingFailureKind>))]
public enum PackageVersionListingFailureKind
{
    NotFound,
    Incomplete,
    Rejected,
    Unavailable,
    Failed,
}

/// <summary>A typed non-success, not an empty package listing.</summary>
public sealed record PackageVersionListingFailure(
    PackageVersionListingRequest Request,
    PackageVersionListingFailureKind Kind,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Reason,
    bool OperationTimedOut,
    ImmutableArray<PackageVersionListingAuthorityFailure> AuthorityFailures);

/// <summary>Credential-safe source failure evidence without live source authority.</summary>
public sealed record PackageVersionListingAuthorityFailure(
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Authority,
    PackageAuthorityFailureKind Kind,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Message,
    PackageSourceTimeoutKind? TimeoutKind);
