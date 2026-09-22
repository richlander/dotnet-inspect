using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// A bounded request for one exact realized Library overview.
/// </summary>
public sealed record LibraryOverviewRequest
{
    public LibraryOverviewRequest(
        LibraryReference library,
        ApiSurfaceExtractionBounds bounds)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public LibraryReference Library { get; }

    public ApiSurfaceExtractionBounds Bounds { get; }
}

/// <summary>
/// Portable managed identity for the inspected Library API assembly.
/// </summary>
public sealed record LibraryOverviewAssemblyIdentity
{
    public LibraryOverviewAssemblyIdentity(
        InertString name,
        Version version,
        InertString? culture,
        InertString? publicKeyToken)
    {
        Name = name;
        Version = version
            ?? throw new ArgumentNullException(nameof(version));
        Culture = culture;
        PublicKeyToken = publicKeyToken;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Name { get; }

    public Version Version { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Culture { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? PublicKeyToken { get; }
}

/// <summary>
/// Resource-free portable content for one complete Library overview.
/// </summary>
public sealed record LibraryOverviewDocument(
    LibraryOverviewAssemblyIdentity Assembly,
    Guid ModuleVersionId,
    int PublicTypeCount,
    int PublicMethodCount,
    int PublicPropertyCount,
    int PublicEventCount,
    int PublicFieldCount,
    long TotalPublicMemberCount,
    int MetadataRows,
    int RetainedTextCharacters,
    ApiSurfaceExtractionBounds Bounds);

/// <summary>
/// The terminal result of applying Count to one Library overview operation.
/// </summary>
public abstract record LibraryOverviewCountResult
{
    private LibraryOverviewCountResult()
    {
    }

    public sealed record Completed(InspectionEnvelope<int> Inspection)
        : LibraryOverviewCountResult;

    public sealed record NotAvailable(
        InspectionEnvelope<LibraryOverviewOutcome> Inspection)
        : LibraryOverviewCountResult;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryOverviewIncompleteReason.ExtractionBound),
    "extractionBound")]
[JsonDerivedType(
    typeof(LibraryOverviewIncompleteReason.MetadataInspectionFailures),
    "metadataInspectionFailures")]
public abstract record LibraryOverviewIncompleteReason
{
    private LibraryOverviewIncompleteReason()
    {
    }

    public sealed record ExtractionBound(ApiSurfaceExtractionBound Bound)
        : LibraryOverviewIncompleteReason;

    public sealed record MetadataInspectionFailures
        : LibraryOverviewIncompleteReason
    {
        public MetadataInspectionFailures(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            Count = count;
        }

        public int Count { get; }
    }
}

public enum LibraryOverviewRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryOverviewFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

/// <summary>
/// The closed terminal content outcome for one Library overview.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryOverviewOutcome.Available), "available")]
[JsonDerivedType(typeof(LibraryOverviewOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(LibraryOverviewOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(LibraryOverviewOutcome.Failed), "failed")]
public abstract record LibraryOverviewOutcome
{
    private LibraryOverviewOutcome()
    {
    }

    public sealed record Available(LibraryOverviewDocument Document)
        : LibraryOverviewOutcome;

    public sealed record Incomplete(
        LibraryOverviewIncompleteReason Reason)
        : LibraryOverviewOutcome;

    public sealed record Rejected(LibraryOverviewRejection Reason)
        : LibraryOverviewOutcome;

    public sealed record Failed(LibraryOverviewFailure Reason)
        : LibraryOverviewOutcome;
}
