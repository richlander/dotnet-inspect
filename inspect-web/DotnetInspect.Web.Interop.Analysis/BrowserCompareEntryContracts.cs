using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Browser projection of one managed Compare entry result.
/// </summary>
public sealed record BrowserCompareEntryResult(
    int SchemaVersion,
    BrowserCompareEntryResultKind Kind,
    BrowserCompareEntry Entry,
    BrowserCompareEntryUnavailable? Unavailable,
    BrowserCompareEntryFailure? Failure);

public sealed record BrowserCompareEntry(
    string Facet,
    string Title,
    BrowserCompareSubject Subject);

public sealed record BrowserCompareSubject(
    BrowserCompareSubjectKind Kind,
    BrowserComparePackageSubject Package,
    BrowserCompareLibrarySubject Library,
    BrowserCompareTypeSubject? Type,
    BrowserCompareMemberSubject? Member);

public sealed record BrowserComparePackageSubject(
    string PackageId,
    string Version,
    string? TargetFramework,
    string? RuntimeIdentifier);

public sealed record BrowserCompareLibrarySubject(
    bool IsAggregate,
    BrowserCompareAssemblyIdentity? Assembly);

public sealed record BrowserCompareAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserCompareTypeSubject(
    string Namespace,
    string[] Segments);

public sealed record BrowserCompareMemberSubject(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);

public sealed record BrowserCompareEntryUnavailable(
    BrowserCompareEntryUnavailabilityKind Kind,
    string Message);

public sealed record BrowserCompareEntryFailure(string Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCompareEntryResultKind>))]
public enum BrowserCompareEntryResultKind
{
    Available,
    Unavailable,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCompareSubjectKind>))]
public enum BrowserCompareSubjectKind
{
    Library,
    Type,
    Member,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserCompareEntryUnavailabilityKind>))]
public enum BrowserCompareEntryUnavailabilityKind
{
    CapabilityAbsent,
    Retired,
}
