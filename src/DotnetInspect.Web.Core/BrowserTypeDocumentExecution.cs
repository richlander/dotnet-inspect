using System.Runtime.Versioning;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserTypeDocumentExecution
{
    internal static async Task<
        InspectionEnvelope<TypeDocumentInspectionOutcome>>
        ExecuteAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            TypeDocumentInspectionPlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<TypeDocumentInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        materialization,
                        (reference, owner) =>
                            Execute(
                                reference,
                                owner,
                                plan,
                                cancellationToken))
                    .ConfigureAwait(false);
        return Complete(run, "Type document");
    }

    internal static async Task<InspectionEnvelope<
        TypeDocumentAttachmentCompositionResult>>
        ExecuteDocumentedAsync(
            ValueTask<AssemblyContextLibraryAdapterResult> materialization,
            TypeDocumentInspectionPlan plan,
            MemberDocumentationAttachmentRequest documentation,
            MemberDocumentationAttachmentProvider documentationProvider,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(documentation);
        ArgumentNullException.ThrowIfNull(documentationProvider);

        AssemblyContextLibraryInspectionRun<InspectionEnvelope<
            TypeDocumentAttachmentCompositionResult>> run =
                await AssemblyContextLibraryInspection.ExecuteComposedAsync(
                        materialization,
                        (reference, owner) =>
                            ExecuteDocumentedAsync(
                                reference,
                                owner,
                                plan,
                                documentation,
                                documentationProvider,
                                cancellationToken))
                    .ConfigureAwait(false);
        return Complete(run, "documented Type document");
    }

    private static InspectionEnvelope<TContent> Complete<TContent>(
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<TContent>> run,
        string operation)
    {
        if (run.Failure is { } failure)
        {
            throw new InvalidOperationException(
                $"The {operation} Library could not be materialized: "
                    + failure);
        }
        if (run.Result is not { } inspection)
        {
            throw new InvalidOperationException(
                $"The exact Library owner could not issue the {operation} "
                    + "inspection lease.");
        }
        if (run.CleanupFailures.IsEmpty)
            return inspection;

        return new(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Concat(
                run.CleanupFailures.Select(failure =>
                    new InspectionDiagnostic(
                        "type-document.library-retirement",
                        InspectionDiagnosticSeverity.Warning,
                        failure))));
    }

    private static InspectionEnvelope<
        TypeDocumentInspectionOutcome>? Execute(
        LibraryReference reference,
        LibraryContentOwner owner,
        TypeDocumentInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        if (owner.IssueOperationLease(reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return TypeDocumentInspectionOperation.Execute(
            new(reference, plan),
            lease,
            cancellationToken);
    }

    private static async ValueTask<InspectionEnvelope<
        TypeDocumentAttachmentCompositionResult>?> ExecuteDocumentedAsync(
        LibraryReference reference,
        LibraryContentOwner owner,
        TypeDocumentInspectionPlan plan,
        MemberDocumentationAttachmentRequest documentation,
        MemberDocumentationAttachmentProvider documentationProvider,
        CancellationToken cancellationToken)
    {
        if (owner.IssueOperationLease(reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return await TypeDocumentAttachmentCompositionOperation.ExecuteAsync(
                new(reference, plan, documentation),
                lease,
                documentationProvider,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
