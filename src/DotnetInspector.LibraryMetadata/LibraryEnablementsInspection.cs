using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

/// <summary>Which Library content role the enablements were judged on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryEnablementsContent>))]
public enum LibraryEnablementsContent
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
[JsonDerivedType(typeof(Judged), "judged")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record LibraryEnablementsOutcome
{
    private LibraryEnablementsOutcome()
    {
    }

    public sealed record Judged(
        LibraryEnablementsContent Content,
        LibraryEnablements Enablements)
        : LibraryEnablementsOutcome;

    public sealed record Failed(LibraryEnablementsFailure Reason)
        : LibraryEnablementsOutcome;
}

/// <summary>
/// Judges Library enablements over the implementation content when the
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

        (LibraryContentReference content, LibraryEnablementsContent role) =
            library.ImplementationAssembly is { } implementation
                ? (implementation, LibraryEnablementsContent.ImplementationAssembly)
                : (library.ApiAssembly, LibraryEnablementsContent.ApiAssembly);
        return lease.Snapshot(
            content,
            role,
            static (view, role, token) => Inspect(view, role, token),
            cancellationToken);
    }

    private static LibraryEnablementsOutcome Inspect(
        scoped LibraryContentView view,
        LibraryEnablementsContent role,
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
        LibraryEnablementsContent role)
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

            return new LibraryEnablementsOutcome.Judged(role, session.Enablements());
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
}
