using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using MetadataLibraryEnablement =
    ILInspector.Metadata.LibraryEnablement;
using MetadataLibraryEnablementFacts =
    ILInspector.Metadata.LibraryEnablementFacts;
using MetadataLibraryEnablementId =
    ILInspector.Metadata.LibraryEnablementId;
using MetadataLibraryEnablementUnavailableReason =
    ILInspector.Metadata.LibraryEnablementUnavailableReason;

namespace DotnetInspector.LibraryMetadata;

/// <summary>
/// Stable identifier for one detached Library enablement.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementId>))]
public enum LibraryEnablementId
{
    [JsonStringEnumMemberName("aot-compatible")]
    AotCompatible,

    [JsonStringEnumMemberName("runtime-async")]
    RuntimeAsync,

    [JsonStringEnumMemberName("memory-safety-v2")]
    MemorySafetyV2,
}

/// <summary>
/// Why a detached Library enablement cannot be decided.
/// </summary>
[JsonConverter(
    typeof(JsonStringEnumConverter<LibraryEnablementUnavailableReason>))]
public enum LibraryEnablementUnavailableReason
{
    [JsonStringEnumMemberName("reference-assembly")]
    ReferenceAssembly,

    [JsonStringEnumMemberName("undecodable-metadata")]
    UndecodableMetadata,

    [JsonStringEnumMemberName("unrecognized-value")]
    UnrecognizedValue,

    [JsonStringEnumMemberName("conflicting-values")]
    ConflictingValues,

    [JsonStringEnumMemberName("unsupported-memory-safety-rules")]
    UnsupportedMemorySafetyRules,

    [JsonStringEnumMemberName("malformed-memory-safety-rules")]
    MalformedMemorySafetyRules,

    [JsonStringEnumMemberName("conflicting-memory-safety-rules")]
    ConflictingMemorySafetyRules,

    [JsonStringEnumMemberName("memory-safety-metadata-unavailable")]
    MemorySafetyMetadataUnavailable,
}

/// <summary>
/// One detached Library enablement with the Metadata owner's exact state.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Enabled), "enabled")]
[JsonDerivedType(typeof(NotEnabled), "not-enabled")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record LibraryEnablement
{
    private LibraryEnablement(LibraryEnablementId id) => Id = id;

    [JsonPropertyOrder(-1)]
    public LibraryEnablementId Id { get; }

    public sealed record Enabled(LibraryEnablementId Id)
        : LibraryEnablement(Id);

    public sealed record NotEnabled(LibraryEnablementId Id)
        : LibraryEnablement(Id);

    public sealed record Unavailable(
        LibraryEnablementId Id,
        LibraryEnablementUnavailableReason Reason)
        : LibraryEnablement(Id);
}

/// <summary>
/// Every detached enablement fact for one image, in owner vocabulary order.
/// </summary>
public sealed record LibraryEnablementFacts(
    ImmutableArray<LibraryEnablement> Items)
{
    public bool Equals(LibraryEnablementFacts? other)
        => other is not null
            && Items.AsSpan().SequenceEqual(other.Items.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (LibraryEnablement item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerable<LibraryEnablementId> Enabled()
        => Items
            .OfType<LibraryEnablement.Enabled>()
            .Select(static item => item.Id);

    public static string Label(LibraryEnablementId id) =>
        MetadataLibraryEnablementFacts.Label(MetadataId(id));

    private static MetadataLibraryEnablementId MetadataId(
        LibraryEnablementId id) =>
        id switch
        {
            LibraryEnablementId.AotCompatible =>
                MetadataLibraryEnablementId.AotCompatible,
            LibraryEnablementId.RuntimeAsync =>
                MetadataLibraryEnablementId.RuntimeAsync,
            LibraryEnablementId.MemorySafetyV2 =>
                MetadataLibraryEnablementId.MemorySafetyV2,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
}

/// <summary>Which Library content role the enablement facts were decided on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementsRole>))]
public enum LibraryEnablementsRole
{
    [JsonStringEnumMemberName("implementation-assembly")]
    ImplementationAssembly,

    [JsonStringEnumMemberName("api-assembly")]
    ApiAssembly,
}

[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementsFailure>))]
public enum LibraryEnablementsFailure
{
    [JsonStringEnumMemberName("not-managed-assembly")]
    NotManagedAssembly,

    [JsonStringEnumMemberName("managed-module")]
    ManagedModule,

    [JsonStringEnumMemberName("assembly-identity-mismatch")]
    AssemblyIdentityMismatch,

    [JsonStringEnumMemberName("unsupported-windows-metadata")]
    UnsupportedWindowsMetadata,

    [JsonStringEnumMemberName("malformed-metadata")]
    MalformedMetadata,
}

/// <summary>
/// The Enablements fact group of one Library document
/// (<c>docs/design/library-inspection-document.md#library-facts</c>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Available), "available")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record LibraryEnablementsOutcome
{
    private LibraryEnablementsOutcome()
    {
    }

    public sealed record Available(
        LibraryEnablementsRole Role,
        LibraryEnablementFacts Facts)
        : LibraryEnablementsOutcome;

    public sealed record Failed(LibraryEnablementsFailure Reason)
        : LibraryEnablementsOutcome;
}

/// <summary>
/// Decides Library enablement facts over the implementation content when the
/// Library carries one, and over the API content otherwise.
/// </summary>
public static class LibraryEnablementsInspection
{
    public static LibraryEnablementsOutcome Execute(
        LibraryReference library,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(library, lease.Reference))
        {
            throw new ArgumentException(
                "The lease does not govern the requested Library.",
                nameof(lease));
        }

        (LibraryContentReference content, LibraryEnablementsRole role) =
            library.ImplementationAssembly is { } implementation
                ? (implementation, LibraryEnablementsRole.ImplementationAssembly)
                : (library.ApiAssembly, LibraryEnablementsRole.ApiAssembly);
        return lease.Snapshot(
            content,
            role,
            static (view, role, token) => Inspect(view, role, token),
            cancellationToken);
    }

    private static LibraryEnablementsOutcome Inspect(
        scoped LibraryContentView view,
        LibraryEnablementsRole role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
            return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.MalformedMetadata);

        LibraryContentReference reference = view.Reference;
        return view.UseReadStream(content => Inspect(content, reference, role));
    }

    private static LibraryEnablementsOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryEnablementsRole role)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            if (!session.HasMetadata)
                return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.NotManagedAssembly);
            if (!session.IsAssembly)
                return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.ManagedModule);
            if (reference.AssemblyIdentity is not { } expected
                || !session.AssemblyIdentity().IsEquivalentTo(expected.Identity))
            {
                return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.AssemblyIdentityMismatch);
            }

            return new LibraryEnablementsOutcome.Available(
                role,
                Detach(session.Enablements()));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new LibraryEnablementsOutcome.Failed(LibraryEnablementsFailure.MalformedMetadata);
        }
    }

    private static LibraryEnablementFacts Detach(
        MetadataLibraryEnablementFacts facts) =>
        new([.. facts.Items.Select(Detach)]);

    private static LibraryEnablement Detach(
        MetadataLibraryEnablement enablement) =>
        enablement switch
        {
            MetadataLibraryEnablement.Enabled enabled =>
                new LibraryEnablement.Enabled(Detach(enabled.Id)),
            MetadataLibraryEnablement.NotEnabled notEnabled =>
                new LibraryEnablement.NotEnabled(Detach(notEnabled.Id)),
            MetadataLibraryEnablement.Unavailable unavailable =>
                new LibraryEnablement.Unavailable(
                    Detach(unavailable.Id),
                    Detach(unavailable.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown Library enablement state."),
        };

    private static LibraryEnablementId Detach(
        MetadataLibraryEnablementId id) =>
        id switch
        {
            MetadataLibraryEnablementId.AotCompatible =>
                LibraryEnablementId.AotCompatible,
            MetadataLibraryEnablementId.RuntimeAsync =>
                LibraryEnablementId.RuntimeAsync,
            MetadataLibraryEnablementId.MemorySafetyV2 =>
                LibraryEnablementId.MemorySafetyV2,
            _ => throw new InvalidOperationException(
                "Unknown Library enablement identifier."),
        };

    private static LibraryEnablementUnavailableReason Detach(
        MetadataLibraryEnablementUnavailableReason reason) =>
        reason switch
        {
            MetadataLibraryEnablementUnavailableReason.ReferenceAssembly =>
                LibraryEnablementUnavailableReason.ReferenceAssembly,
            MetadataLibraryEnablementUnavailableReason.UndecodableMetadata =>
                LibraryEnablementUnavailableReason.UndecodableMetadata,
            MetadataLibraryEnablementUnavailableReason.UnrecognizedValue =>
                LibraryEnablementUnavailableReason.UnrecognizedValue,
            MetadataLibraryEnablementUnavailableReason.ConflictingValues =>
                LibraryEnablementUnavailableReason.ConflictingValues,
            MetadataLibraryEnablementUnavailableReason
                    .UnsupportedMemorySafetyRules =>
                LibraryEnablementUnavailableReason
                    .UnsupportedMemorySafetyRules,
            MetadataLibraryEnablementUnavailableReason
                    .MalformedMemorySafetyRules =>
                LibraryEnablementUnavailableReason.MalformedMemorySafetyRules,
            MetadataLibraryEnablementUnavailableReason
                    .ConflictingMemorySafetyRules =>
                LibraryEnablementUnavailableReason
                    .ConflictingMemorySafetyRules,
            MetadataLibraryEnablementUnavailableReason
                    .MemorySafetyMetadataUnavailable =>
                LibraryEnablementUnavailableReason
                    .MemorySafetyMetadataUnavailable,
            _ => throw new InvalidOperationException(
                "Unknown Library enablement unavailable reason."),
        };
}
