using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record AssemblyContextCallGraphAnalysisParticipant(
    AssemblyContextParticipant ContextParticipant,
    AssemblyContextSubject Subject,
    ResolvedAssemblyReference Assembly,
    Analysis.LibraryCallGraphAnalysisResult CallGraph);

internal abstract record AssemblyContextCallGraphAnalysisResult(
    AssemblyContextSubject Subject)
{
    internal sealed record Available(
        AssemblyContextCallGraphAnalysisParticipant Participant)
        : AssemblyContextCallGraphAnalysisResult(Participant.Subject);

    internal sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyContextCallGraphAnalysisResult(Subject);

    internal sealed record InvalidImage(
        AssemblyContextSubject Subject,
        Exception Error)
        : AssemblyContextCallGraphAnalysisResult(Subject);
}

internal static class AssemblyContextCallGraphAnalysis
{
    internal static AssemblyContextCallGraphAnalysisResult Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<
            AssemblyContextCallGraphAnalysisResult> access =
            group.UseSnapshot<AssemblyContextCallGraphAnalysisResult>(
                participant,
                cancellationToken,
                snapshot =>
                {
                    try
                    {
                        Analysis.LibraryCallGraphAnalysisResult callGraph =
                            Analysis.LibraryBodyAnalysisService.ExecuteImage(
                                participant.Assembly.Path
                                    ?? participant.Assembly.Identity.Name,
                                snapshot.Content,
                                Analysis.LibraryBodyAnalysisRequest.Create(
                                    Analysis.LibraryBodyAnalysisFeatures
                                        .MethodEvidence))
                            .CallGraph;
                        ResolvedAssemblyReference assembly =
                            snapshot.RetainAssemblyReference(
                                participant.Assembly);
                        return new AssemblyContextCallGraphAnalysisResult
                            .Available(
                                new(
                                    participant,
                                    subject,
                                    assembly,
                                    callGraph));
                    }
                    catch (Exception exception)
                        when (MemberCallGraphSession
                            .IsInvalidImageException(exception))
                    {
                        return new AssemblyContextCallGraphAnalysisResult
                            .InvalidImage(
                                subject,
                                exception);
                    }
                });

        return access switch
        {
            AssemblyImageAccessResult<
                AssemblyContextCallGraphAnalysisResult>.Available available =>
                available.Value,
            AssemblyImageAccessResult<
                AssemblyContextCallGraphAnalysisResult>.Rejected rejected =>
                new AssemblyContextCallGraphAnalysisResult.Rejected(
                    subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }
}
