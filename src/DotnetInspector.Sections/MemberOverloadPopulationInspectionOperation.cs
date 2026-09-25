using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public static class MemberOverloadPopulationInspectionOperation
{
    private const string SharePath =
        "member-overload-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Execute(
            MemberOverloadPopulationInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            MemberOverloadRowsRequest? rows =
                request.Plan.Overloads.Rows;
            bool compatibleContinuation =
                rows?.Continuation is not { } continuation
                || IsCompatible(
                    continuation.Binding,
                    request.Plan.Subject,
                    rows.Ordering);
            int startOrdinal =
                compatibleContinuation
                    ? rows?.Continuation?.NextOrdinal ?? 0
                    : 0;
            LibraryMethodGroupInspectionOutcome source =
                LibraryMethodGroupInspection.Execute(
                    new(
                        request.Library,
                        request.Plan.Subject.DeclaringType,
                        request.Plan.Subject.Name,
                        startOrdinal,
                        rows?.MaximumRows ?? 1,
                        materializeRows:
                            rows is not null
                            && compatibleContinuation,
                        request.Plan.Bounds,
                        expectedModuleVersionId:
                            compatibleContinuation
                                ? rows?.Continuation?.Binding
                                    .ModuleVersionId
                                : null),
                    lease,
                    cancellationToken);
            return Project(
                source,
                request,
                startOrdinal,
                compatibleContinuation);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Project(
            LibraryMethodGroupInspectionOutcome outcome,
            MemberOverloadPopulationInspectionRequest request,
            int startOrdinal,
            bool compatibleContinuation) =>
        outcome switch
        {
            LibraryMethodGroupInspectionOutcome.Completed completed =>
                Project(
                    completed.Correspondence,
                    request,
                    startOrdinal,
                    compatibleContinuation),
            LibraryMethodGroupInspectionOutcome.Rejected rejected =>
                Rejected(
                    rejected.Reason switch
                    {
                        LibraryMethodGroupInspectionRejection
                                .LeaseReferenceMismatch =>
                            MemberOverloadPopulationInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryMethodGroupInspectionRejection
                                .AssemblyIdentityMismatch =>
                            MemberOverloadPopulationInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Member-overload inspection rejection."),
                    }),
            LibraryMethodGroupInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    MemberOverloadPopulationBound.MetadataRows,
                    request.Plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryMethodGroupInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryMethodGroupInspectionFailure.NotManagedAssembly =>
                            MemberOverloadPopulationInspectionFailure
                                .NotManagedAssembly,
                        LibraryMethodGroupInspectionFailure.ManagedModule =>
                            MemberOverloadPopulationInspectionFailure
                                .ManagedModule,
                        LibraryMethodGroupInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            MemberOverloadPopulationInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryMethodGroupInspectionFailure
                                .MalformedMetadata =>
                            MemberOverloadPopulationInspectionFailure
                                .MalformedMetadata,
                        LibraryMethodGroupInspectionFailure
                                .EmptyModuleVersionId =>
                            MemberOverloadPopulationInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Member-overload inspection failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Member-group inspection outcome."),
        };

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Project(
            LibraryMethodGroupCorrespondence correspondence,
            MemberOverloadPopulationInspectionRequest request,
            int startOrdinal,
            bool compatibleContinuation) =>
        correspondence.Group switch
        {
            MetadataMethodGroupInspectionOutcome.Read read =>
                Available(
                    correspondence,
                    request,
                    read,
                    startOrdinal,
                    compatibleContinuation),
            MetadataMethodGroupInspectionOutcome.TypeNotFound =>
                Rejected(
                    MemberOverloadPopulationInspectionRejection
                        .TypeNotFound),
            MetadataMethodGroupInspectionOutcome.TypeAmbiguous =>
                Rejected(
                    MemberOverloadPopulationInspectionRejection
                        .TypeAmbiguous),
            MetadataMethodGroupInspectionOutcome.MemberGroupNotFound =>
                Rejected(
                    MemberOverloadPopulationInspectionRejection
                        .MemberGroupNotFound),
            MetadataMethodGroupInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    incomplete.Bound switch
                    {
                        MetadataMethodGroupInspectionBound.Members =>
                            MemberOverloadPopulationBound.Members,
                        MetadataMethodGroupInspectionBound
                                .MethodSemanticsAssociations =>
                            MemberOverloadPopulationBound
                                .MethodSemanticsAssociations,
                        _ => throw new InvalidOperationException(
                            "Unknown Member-group inspection bound."),
                    },
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataMethodGroupInspectionOutcome.Failed =>
                Failed(
                    MemberOverloadPopulationInspectionFailure
                        .MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Method-group inspection outcome."),
        };

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Available(
            LibraryMethodGroupCorrespondence correspondence,
            MemberOverloadPopulationInspectionRequest request,
            MetadataMethodGroupInspectionOutcome.Read read,
            int startOrdinal,
            bool compatibleContinuation)
    {
        MemberGroupSubject subject = request.Plan.Subject;
        MemberOverloadOrdering ordering =
            request.Plan.Overloads.Rows?.Ordering
                ?? MemberOverloadOrdering.Metadata;
        var binding = new MemberOverloadPopulationBinding(
            correspondence.ModuleVersionId,
            read.DeclaringType,
            read.TypeDefinitionToken,
            subject.Name,
            subject.Category,
            subject.Role,
            ordering);
        MemberOverloadCountOutcome? count =
            request.Plan.Overloads.Count is null
                ? null
                : new MemberOverloadCountOutcome.Counted(
                    read.Count);
        MemberOverloadRowsOutcome? rows = null;
        if (request.Plan.Overloads.Rows is { } rowRequest)
        {
            bool exactContinuation =
                compatibleContinuation
                && (rowRequest.Continuation is null
                    || rowRequest.Continuation.Binding
                        .TypeDefinitionToken
                        == read.TypeDefinitionToken);
            if (!exactContinuation
                || correspondence.StaleContinuation
                || read.ContinuationOutOfRange)
            {
                rows = new MemberOverloadRowsOutcome.Rejected(
                    !exactContinuation
                        ? MemberOverloadRowsRejection
                            .IncompatibleContinuation
                        : correspondence.StaleContinuation
                            ? MemberOverloadRowsRejection
                                .StaleContinuation
                            : MemberOverloadRowsRejection
                                .ContinuationOutOfRange);
            }
            else if (read.IncompleteRetainedTextCharacters
                is { } measured)
            {
                rows = new MemberOverloadRowsOutcome.Incomplete(
                    MemberOverloadPopulationBound
                        .RetainedTextCharacters,
                    request.Plan.Bounds
                        .MaxRetainedTextCharacters,
                    measured);
            }
            else if (read.RowsFailed)
            {
                rows = new MemberOverloadRowsOutcome.Failed(
                    MemberOverloadRowsFailure.MalformedMetadata);
            }
            else
            {
                ImmutableArray<MemberOverloadShape> items =
                    [
                        .. read.Rows.Select(
                            (row, index) =>
                                new MemberOverloadShape(
                                    row.MetadataToken,
                                    checked(
                                        startOrdinal + index + 1),
                                    Field(row.DisplaySignature),
                                    Field(row.CanonicalSignature),
                                    Field(row.Fingerprint),
                                    Field(row.Accessibility),
                                    subject.Role,
                                    row.Receiver switch
                                    {
                                        MetadataMethodReceiver.Static =>
                                            MemberReceiver.Static,
                                        MetadataMethodReceiver.This =>
                                            MemberReceiver.This,
                                        MetadataMethodReceiver.Extension =>
                                            MemberReceiver.Extension,
                                        _ => throw new InvalidOperationException(
                                            "Unknown exact-Member receiver."),
                                    },
                                    binding)),
                    ];
                rows = new MemberOverloadRowsOutcome.Read(
                    rowRequest.Ordering,
                    items,
                    read.NextOrdinal is { } next
                        ? new MemberOverloadContinuation(
                            binding,
                            next)
                        : null);
            }
        }

        return Envelope(
            new MemberOverloadPopulationInspectionOutcome.Available(
                new(
                    PortableIdentity(correspondence.AssemblyIdentity),
                    subject,
                    new(binding, count, rows),
                    correspondence.AssemblyBytes)));
    }

    private static bool IsCompatible(
        MemberOverloadPopulationBinding binding,
        MemberGroupSubject subject,
        MemberOverloadOrdering ordering) =>
        binding.DeclaringType == subject.DeclaringType
        && string.Equals(
            binding.Name,
            subject.Name,
            StringComparison.Ordinal)
        && binding.Category == subject.Category
        && binding.Role == subject.Role
        && binding.Ordering == ordering;

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Rejected(
            MemberOverloadPopulationInspectionRejection reason) =>
        Envelope(
            new MemberOverloadPopulationInspectionOutcome.Rejected(
                reason));

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Incomplete(
            MemberOverloadPopulationBound bound,
            long limit,
            long measured) =>
        Envelope(
            new MemberOverloadPopulationInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Failed(
            MemberOverloadPopulationInspectionFailure reason) =>
        Envelope(
            new MemberOverloadPopulationInspectionOutcome.Failed(
                reason));

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Envelope(
            MemberOverloadPopulationInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));

    private static LibraryAssemblyIdentity PortableIdentity(
        AssemblyReferenceIdentity identity) =>
        new(
            Field(identity.Name),
            identity.Version
                ?? throw new InvalidOperationException(
                    "The assembly identity has no version."),
            identity.Culture is null
                ? null
                : Field(identity.Culture),
            identity.PublicKeyToken is null
                ? null
                : Field(identity.PublicKeyToken));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
