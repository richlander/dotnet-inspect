using DotnetInspector.Libraries;
using ILInspector.Decompiler;

namespace DotnetInspector.SourceHouse;

public static partial class SourceHouse
{
    public static async ValueTask<SourceHouseBestAvailableOutcome>
        ExecuteBestAvailableAsync(
            SourceHouseBestAvailableRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationLease);

        var authoredRequest = new SourceHouseAuthoredRequest(
            request.Identity,
            request.Library,
            request.SelectedAssembly,
            request.Target,
            request.AuthoredPlan);
        ProvisionalOutcome authored;
        SourceHouseDecompilationOutcome? decompiled = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            authored = CanUsePortablePdbUnavailablePrecondition(
                request,
                operationLease)
                ? PortablePdbUnavailable()
                : await ExecuteCoreAsync(
                        authoredRequest,
                        operationLease,
                        session: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (authored is not AvailableOutcome)
            {
                var decompilationRequest =
                    new SourceHouseDecompilationRequest(
                        request.Identity,
                        request.Library,
                        request.SelectedAssembly,
                        request.Target,
                        request.DecompilationPlan);
                decompiled = ExecuteDecompilationCore(
                    decompilationRequest,
                    operationLease,
                    cancellationToken);
            }
        }
        finally
        {
            operationLease.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var settlement = new SourceHouseLibraryLeaseSettlement(
            SourceHouseLibraryLeaseConsumer.SourceHouse);
        SourceHouseOutcome authoredOutcome = authored.Complete(
            new SourceHouseRequestEvidence(
                authoredRequest.Identity,
                authoredRequest.Library,
                authoredRequest.SelectedAssembly,
                authoredRequest.Target,
                authoredRequest.Plan.Identity,
                authoredRequest.Plan.PolicyGeneration),
            settlement);
        return CompleteBestAvailable(
            new(request),
            authoredOutcome,
            decompiled,
            settlement);
    }

    private static bool
        CanUsePortablePdbUnavailablePrecondition(
            SourceHouseBestAvailableRequest request,
            LibraryOperationLease operationLease)
    {
        if (request.AuthoredPrecondition
                != SourceHouseBestAvailableAuthoredPrecondition
                    .PortablePdbUnavailable
            || !ReferenceEquals(
                request.SelectedAssembly.Library,
                request.Library)
            || (!request.SelectedAssembly.HasRole(
                    LibraryContentRole.ApiAssembly)
                && !request.SelectedAssembly.HasRole(
                    LibraryContentRole.ImplementationAssembly))
            || !ReferenceEquals(
                operationLease.Reference,
                request.Library)
            || DeadlineExpired(request.AuthoredPlan))
        {
            return false;
        }

        return !request.Library.Contents.Any(
            content =>
                content.HasRole(
                    LibraryContentRole.PortablePdb)
                && ReferenceEquals(
                    content.AssociatedAssembly,
                    request.SelectedAssembly));
    }

    private static ProvisionalOutcome
        PortablePdbUnavailable() =>
        new UnavailableOutcome(
            new SourceHousePdbContribution(
                SourceHousePdbContributionKind.Unavailable,
                content: null,
                bytesObserved: 0,
                sourceLinkMap: null,
                observations: []),
            new SourceHouseAuthoredAttempt.Unavailable(
                mapping: null,
                sourceAttempts: []),
            new(
                AssemblyBytesObserved: 0,
                PortablePdbBytesObserved: 0,
                DocumentsObserved: 0,
                TargetMappingsObserved: 0,
                CandidateAttempts: 0,
                SourceBytesObserved: 0,
                SourceTextCharactersObserved: 0));

    private static SourceHouseBestAvailableOutcome CompleteBestAvailable(
        SourceHouseBestAvailableRequestEvidence request,
        SourceHouseOutcome authored,
        SourceHouseDecompilationOutcome? decompiled,
        SourceHouseLibraryLeaseSettlement settlement)
    {
        if (authored is SourceHouseOutcome.Available available)
        {
            return new SourceHouseBestAvailableOutcome.Available(
                request,
                authored,
                decompilationOutcome: null,
                SourceHouseSelectedSource.Authored,
                available.Source.Text,
                settlement);
        }

        if (decompiled is null)
        {
            throw new InvalidOperationException(
                "Best-available settlement did not attempt decompilation after authored non-success.");
        }

        if (decompiled
                is SourceHouseDecompilationOutcome.Completed completed)
        {
            CSharpDecompilationAttempt attempt = completed.Attempt;
            if (attempt.IsAvailable
                && attempt.Text is { } text)
            {
                return new SourceHouseBestAvailableOutcome.Available(
                    request,
                    authored,
                    decompiled,
                    SourceHouseSelectedSource.Decompiled,
                    text,
                    settlement);
            }

            return attempt.Status switch
            {
                CSharpDecompilationStatus.Failed =>
                    new SourceHouseBestAvailableOutcome.Failed(
                        request,
                        authored,
                        decompiled,
                        new(
                            SourceHouseFailureStage.Decompilation,
                            "DecompilerFailed",
                            attempt.DiagnosticSummary),
                        settlement),
                CSharpDecompilationStatus.Incomplete =>
                    new SourceHouseBestAvailableOutcome.Incomplete(
                        request,
                        authored,
                        decompiled,
                        SourceHouseIncompleteBoundary.BodyProjections,
                        settlement),
                CSharpDecompilationStatus.Absent =>
                    CompleteWithoutAvailableProducer(
                        request,
                        authored,
                        decompiled,
                        settlement),
                _ => throw new InvalidOperationException(
                    "Available decompilation did not contain source text."),
            };
        }

        return decompiled switch
        {
            SourceHouseDecompilationOutcome.Rejected rejected =>
                new SourceHouseBestAvailableOutcome.Rejected(
                    request,
                    authored,
                    decompiled,
                    rejected.Rejection,
                    settlement),
            SourceHouseDecompilationOutcome.Failed failed =>
                new SourceHouseBestAvailableOutcome.Failed(
                    request,
                    authored,
                    decompiled,
                    failed.Failure,
                    settlement),
            SourceHouseDecompilationOutcome.Incomplete incomplete =>
                new SourceHouseBestAvailableOutcome.Incomplete(
                    request,
                    authored,
                    decompiled,
                    incomplete.Boundary,
                    settlement),
            _ => throw new InvalidOperationException(
                "Unknown SourceHouse decompilation outcome."),
        };
    }

    private static SourceHouseBestAvailableOutcome
        CompleteWithoutAvailableProducer(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authored,
            SourceHouseDecompilationOutcome decompiled,
            SourceHouseLibraryLeaseSettlement settlement) =>
        authored switch
        {
            SourceHouseOutcome.Rejected rejected =>
                new SourceHouseBestAvailableOutcome.Rejected(
                    request,
                    authored,
                    decompiled,
                    rejected.Rejection,
                    settlement),
            SourceHouseOutcome.Failed failed =>
                new SourceHouseBestAvailableOutcome.Failed(
                    request,
                    authored,
                    decompiled,
                    failed.Failure,
                    settlement),
            SourceHouseOutcome.Incomplete incomplete =>
                new SourceHouseBestAvailableOutcome.Incomplete(
                    request,
                    authored,
                    decompiled,
                    incomplete.Boundary,
                    settlement),
            SourceHouseOutcome.Unavailable =>
                new SourceHouseBestAvailableOutcome.Unavailable(
                    request,
                    authored,
                    decompiled,
                    settlement),
            _ => throw new InvalidOperationException(
                "Unknown SourceHouse authored outcome."),
        };
}
