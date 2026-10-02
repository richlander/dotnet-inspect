using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

internal static class BrowserMemberDocumentExecution
{
    internal static async Task<
        InspectionEnvelope<MemberDocumentInspectionOutcome>>
        ExecuteAsync(
            ValueTask<AssemblyContextLibraryAdapterResult>
                materialization,
            MemberDocumentInspectionPlan plan,
            MemberDocumentationAttachmentProvider?
                documentationProvider = null,
            MemberSourceAttachmentProvider? sourceProvider = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<MemberDocumentInspectionOutcome>> run =
                plan.Documentation is null
                    && plan.Source is null
                    ? await AssemblyContextLibraryInspection
                        .ExecuteAsync(
                            materialization,
                            (reference, owner) =>
                                Execute(
                                    reference,
                                    owner,
                                    plan,
                                    cancellationToken))
                        .ConfigureAwait(false)
                    : await AssemblyContextLibraryInspection
                        .ExecuteComposedAsync(
                            materialization,
                            (reference, owner) =>
                                ExecuteAsync(
                                    reference,
                                    owner,
                                    plan,
                                    documentationProvider,
                                    sourceProvider,
                                    cancellationToken))
                        .ConfigureAwait(false);
        if (run.Failure is { } failure)
        {
            throw new InvalidOperationException(
                $"The Member Library could not be materialized: "
                    + failure);
        }
        if (run.Result is not { } inspection)
        {
            throw new InvalidOperationException(
                "The exact Library owner could not issue the Member "
                    + "inspection lease.");
        }
        if (run.CleanupFailures.IsEmpty)
            return inspection;

        return new(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Concat(
                run.CleanupFailures.Select(static failure =>
                    new InspectionDiagnostic(
                        "member-document.library-retirement",
                        InspectionDiagnosticSeverity.Warning,
                        failure))));
    }

    private static InspectionEnvelope<
        MemberDocumentInspectionOutcome>? Execute(
            LibraryReference reference,
            LibraryContentOwner owner,
            MemberDocumentInspectionPlan plan,
            CancellationToken cancellationToken)
    {
        if (owner.IssueOperationLease(reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return MemberDocumentInspectionOperation.Execute(
            new(reference, plan),
            lease,
            cancellationToken);
    }

    private static async ValueTask<InspectionEnvelope<
        MemberDocumentInspectionOutcome>?> ExecuteAsync(
            LibraryReference reference,
            LibraryContentOwner owner,
            MemberDocumentInspectionPlan plan,
            MemberDocumentationAttachmentProvider?
                documentationProvider,
            MemberSourceAttachmentProvider? sourceProvider,
            CancellationToken cancellationToken)
    {
        if (owner.IssueOperationLease(reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return await MemberDocumentInspectionOperation.ExecuteAsync(
                new(reference, plan),
                lease,
                documentationProvider,
                sourceProvider,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
