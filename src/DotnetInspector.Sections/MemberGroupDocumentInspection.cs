using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;

namespace DotnetInspector.Sections;

/// <summary>
/// A resource-free document for one exact member group and its requested
/// overload population.
/// </summary>
public sealed record MemberGroupDocument
{
    public MemberGroupDocument(
        MemberGroupSubject subject,
        MemberOverloadPopulationResult overloads,
        ImmutableArray<MemberDocumentationAttachment>
            returnedRowDocumentation = default)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Overloads = overloads
            ?? throw new ArgumentNullException(nameof(overloads));
        ReturnedRowDocumentation =
            returnedRowDocumentation.IsDefault
                ? []
                : returnedRowDocumentation;
    }

    public MemberGroupSubject Subject { get; }
    public MemberOverloadPopulationResult Overloads { get; }
    public ImmutableArray<MemberDocumentationAttachment>
        ReturnedRowDocumentation { get; }
}

public sealed record MemberGroupDocumentInspectionRequest
{
    public MemberGroupDocumentInspectionRequest(
        LibraryReference library,
        MemberOverloadPopulationInspectionPlan plan,
        MemberDocumentationAttachmentRequest?
            returnedRowDocumentation = null)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
        if (returnedRowDocumentation is not null
            && plan.Overloads.Rows is null)
        {
            throw new ArgumentException(
                "Returned-row documentation requires realized exact-Member Rows.",
                nameof(returnedRowDocumentation));
        }

        ReturnedRowDocumentation = returnedRowDocumentation;
    }

    public LibraryReference Library { get; }
    public MemberOverloadPopulationInspectionPlan Plan { get; }
    public MemberDocumentationAttachmentRequest?
        ReturnedRowDocumentation { get; }
}

public enum MemberGroupDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    DocumentationResultSetMismatch,
    DocumentationSubjectMismatch,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Available),
    typeDiscriminator: "available")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Rejected),
    typeDiscriminator: "rejected")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Incomplete),
    typeDiscriminator: "incomplete")]
[JsonDerivedType(
    typeof(MemberGroupDocumentInspectionOutcome.Failed),
    typeDiscriminator: "failed")]
public abstract record MemberGroupDocumentInspectionOutcome
{
    private MemberGroupDocumentInspectionOutcome()
    {
    }

    public sealed record Available(MemberGroupDocument Document)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Rejected(
        MemberOverloadPopulationInspectionRejection Reason)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberGroupDocumentInspectionOutcome;

    public sealed record Failed(
        MemberGroupDocumentInspectionFailure Reason)
        : MemberGroupDocumentInspectionOutcome;
}

/// <summary>
/// Composes the exact-overload population into one owner-issued member-group
/// document without reconstructing population semantics.
/// </summary>
public static class MemberGroupDocumentInspectionOperation
{
    public static async ValueTask<
        InspectionEnvelope<MemberGroupDocumentInspectionOutcome>>
        ExecuteAsync(
            MemberGroupDocumentInspectionRequest request,
            LibraryOperationLease lease,
            MemberDocumentationAttachmentProvider documentationProvider,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(documentationProvider);

        InspectionEnvelope<MemberGroupDocumentInspectionOutcome> inspection =
            Execute(
                request,
                lease,
                cancellationToken);
        if (request.ReturnedRowDocumentation is null
            || inspection.Content
                is not MemberGroupDocumentInspectionOutcome.Available
                    available
            || available.Document.Overloads.Rows
                is not MemberOverloadRowsOutcome.Read rows)
        {
            return inspection;
        }

        IReadOnlyList<MemberSubject> subjects =
            [
                .. rows.Items.Select(row =>
                    new MemberSubject(
                        available.Document.Subject,
                        row.Binding,
                        row.MetadataToken,
                        row.Anchor,
                        row.BaselineOrdinal,
                        row.Fingerprint,
                        row.DocumentationId)),
            ];
        MemberDocumentationAttachmentResult attachment =
            await MemberDocumentationAttachmentOperation.ExecuteAsync(
                    subjects,
                    request.ReturnedRowDocumentation,
                    documentationProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        MemberGroupDocumentInspectionOutcome content =
            attachment switch
            {
                MemberDocumentationAttachmentResult.Attached attached =>
                    new MemberGroupDocumentInspectionOutcome.Available(
                        new(
                            available.Document.Subject,
                            available.Document.Overloads,
                            attached.Attachments)),
                MemberDocumentationAttachmentResult.Failed failed =>
                    new MemberGroupDocumentInspectionOutcome.Failed(
                        Map(failed.Reason)),
                _ => throw new InvalidOperationException(
                    "Unknown returned-row documentation attachment result."),
            };
        return new(
            content,
            inspection.Share,
            inspection.Diagnostics);
    }

    public static InspectionEnvelope<MemberGroupDocumentInspectionOutcome> Execute(
        MemberGroupDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(lease);

        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome> population =
            MemberOverloadPopulationInspectionOperation.Execute(
                new(request.Library, request.Plan),
                lease,
                cancellationToken);
        return new(
            Project(population.Content),
            population.Share,
            population.Diagnostics);
    }

    private static MemberGroupDocumentInspectionOutcome Project(
        MemberOverloadPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            MemberOverloadPopulationInspectionOutcome.Available available =>
                new MemberGroupDocumentInspectionOutcome.Available(
                    new MemberGroupDocument(
                        available.Content.Subject,
                        available.Content.Overloads)),
            MemberOverloadPopulationInspectionOutcome.Rejected rejected =>
                new MemberGroupDocumentInspectionOutcome.Rejected(
                    rejected.Reason),
            MemberOverloadPopulationInspectionOutcome.Incomplete incomplete =>
                new MemberGroupDocumentInspectionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            MemberOverloadPopulationInspectionOutcome.Failed failed =>
                new MemberGroupDocumentInspectionOutcome.Failed(
                    Map(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown member-overload population inspection outcome."),
        };

    private static MemberGroupDocumentInspectionFailure Map(
        MemberDocumentationAttachmentFailure failure) =>
        failure switch
        {
            MemberDocumentationAttachmentFailure.ResultSetMismatch =>
                MemberGroupDocumentInspectionFailure
                    .DocumentationResultSetMismatch,
            MemberDocumentationAttachmentFailure.SubjectMismatch =>
                MemberGroupDocumentInspectionFailure
                    .DocumentationSubjectMismatch,
            _ => throw new InvalidOperationException(
                "Unknown returned-row documentation attachment failure."),
        };

    private static MemberGroupDocumentInspectionFailure Map(
        MemberOverloadPopulationInspectionFailure failure) =>
        failure switch
        {
            MemberOverloadPopulationInspectionFailure.NotManagedAssembly =>
                MemberGroupDocumentInspectionFailure.NotManagedAssembly,
            MemberOverloadPopulationInspectionFailure.ManagedModule =>
                MemberGroupDocumentInspectionFailure.ManagedModule,
            MemberOverloadPopulationInspectionFailure
                    .UnsupportedWindowsMetadata =>
                MemberGroupDocumentInspectionFailure
                    .UnsupportedWindowsMetadata,
            MemberOverloadPopulationInspectionFailure.MalformedMetadata =>
                MemberGroupDocumentInspectionFailure.MalformedMetadata,
            MemberOverloadPopulationInspectionFailure.EmptyModuleVersionId =>
                MemberGroupDocumentInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown member-group population failure."),
        };
}
