using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public abstract record LibraryMetadataRelationInspectionOutcome
{
    private protected LibraryMetadataRelationInspectionOutcome()
    {
    }

    public sealed record Completed(
        MetadataRelationInspectionResult Result)
        : LibraryMetadataRelationInspectionOutcome;

    public sealed record Unavailable(string Detail)
        : LibraryMetadataRelationInspectionOutcome;
}

/// <summary>
/// Inspects Metadata relations while exact Library content remains borrowed.
/// </summary>
public static class LibraryMetadataRelationInspection
{
    public static LibraryMetadataRelationInspectionOutcome Execute(
        LibraryReference library,
        LibraryOperationLease lease,
        MetadataRelationInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(library, lease.Reference))
        {
            return new LibraryMetadataRelationInspectionOutcome.Unavailable(
                "The Library operation lease does not match the requested "
                    + "Library.");
        }

        return lease.Snapshot(
            library.ApiAssembly,
            request,
            static (view, state, token) =>
                view.UseReadStream(
                    content => Inspect(content, state, token)),
            cancellationToken);
    }

    private static LibraryMetadataRelationInspectionOutcome Inspect(
        Stream content,
        MetadataRelationInspectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            return session.Relations(request, cancellationToken) switch
            {
                MetadataRelationInspectionOutcome.Available available =>
                    new LibraryMetadataRelationInspectionOutcome.Completed(
                        available.Result),
                MetadataRelationInspectionOutcome.Rejected rejected =>
                    new LibraryMetadataRelationInspectionOutcome.Unavailable(
                        rejected.Detail),
                _ => throw new InvalidOperationException(
                    "Unknown Metadata relation inspection outcome."),
            };
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new LibraryMetadataRelationInspectionOutcome.Unavailable(
                exception.Message);
        }
    }
}
