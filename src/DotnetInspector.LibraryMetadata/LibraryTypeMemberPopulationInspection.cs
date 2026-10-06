using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibraryTypeMemberPopulationInspectionRequest(
    LibraryReference Library,
    MetadataTypeMemberPopulationRequest Population,
    ApiSurfaceExtractionBounds Bounds);

public enum LibraryTypeMemberPopulationInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryTypeMemberPopulationInspectionFailure
{
    MalformedMetadata,
}

public abstract record LibraryTypeMemberPopulationInspectionOutcome
{
    private protected LibraryTypeMemberPopulationInspectionOutcome()
    {
    }

    public sealed record Completed(
        MetadataTypeMemberPopulationOutcome Population)
        : LibraryTypeMemberPopulationInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeMemberPopulationInspectionRejection Reason)
        : LibraryTypeMemberPopulationInspectionOutcome;

    public sealed record Failed(
        LibraryTypeMemberPopulationInspectionFailure Reason)
        : LibraryTypeMemberPopulationInspectionOutcome;
}

/// <summary>
/// Inspects one exact realized Library's Type Member population while its
/// owner-backed content remains borrowed.
/// </summary>
public static class LibraryTypeMemberPopulationInspection
{
    public static LibraryTypeMemberPopulationInspectionOutcome Execute(
        LibraryTypeMemberPopulationInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeMemberPopulationInspectionOutcome.Rejected(
                LibraryTypeMemberPopulationInspectionRejection
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    static LibraryTypeMemberPopulationInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryTypeMemberPopulationInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
            return Failed();

        ManagedMetadataIdentity.Assembly? expected =
            view.Reference.AssemblyIdentity;
        try
        {
            return view.UseReadStream<
                LibraryTypeMemberPopulationInspectionOutcome>(content =>
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.OpenPrefetched(content);
                if (expected is null
                    || !session.AssemblyIdentity().IsEquivalentTo(
                        expected.Identity))
                {
                    return new LibraryTypeMemberPopulationInspectionOutcome
                        .Rejected(
                            LibraryTypeMemberPopulationInspectionRejection
                                .AssemblyIdentityMismatch);
                }

                return new LibraryTypeMemberPopulationInspectionOutcome
                    .Completed(
                        MetadataTypeMemberPopulationInspection.Inspect(
                            session,
                            request.Population,
                            request.Bounds));
            });
        }
        catch (Exception error) when (
            error is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed();
        }
    }

    static LibraryTypeMemberPopulationInspectionOutcome Failed() =>
        new LibraryTypeMemberPopulationInspectionOutcome.Failed(
            LibraryTypeMemberPopulationInspectionFailure.MalformedMetadata);
}
