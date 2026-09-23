using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>The completed outcome of one pairwise Library call-use inspection.</summary>
public abstract record AssemblyPairCallUseInspectionOutcome
{
    private AssemblyPairCallUseInspectionOutcome()
    {
    }

    public sealed record Available(
        AssemblyPairCallUseProjection Projection)
        : AssemblyPairCallUseInspectionOutcome;

    public sealed record Rejected(
        AssemblyPairCallUseRequestFailureKind Kind,
        string Detail)
        : AssemblyPairCallUseInspectionOutcome;
}

/// <summary>
/// Executes exact pairwise Library call-use and returns its host-neutral
/// completed-inspection envelope.
/// </summary>
public static class AssemblyPairCallUseInspection
{
    public static InspectionEnvelope<AssemblyPairCallUseInspectionOutcome>
        Execute(
            AssemblyContextGroup group,
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        AssemblyPairCallUseInspectionOutcome content;
        try
        {
            AssemblyPairCallUseResult pair =
                AssemblyPairCallUseQuery.Execute(
                    group,
                    first,
                    second);
            content =
                new AssemblyPairCallUseInspectionOutcome.Available(
                    AssemblyPairCallUseProjection.Create(pair));
        }
        catch (AssemblyPairCallUseRequestException exception)
        {
            content =
                new AssemblyPairCallUseInspectionOutcome.Rejected(
                    exception.Kind,
                    exception.Message);
        }

        return new(
            content,
            new InspectionShare.NonProjectable(
                "assembly-pair/call-use",
                "Pairwise Library call-use does not yet have a canonical "
                    + "Workspace Share projection."),
            Diagnostics(content));
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        AssemblyPairCallUseInspectionOutcome content)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        switch (content)
        {
            case AssemblyPairCallUseInspectionOutcome.Rejected rejected:
                diagnostics.Add(
                    new(
                        "assembly-pair-call-use.request-rejected",
                        InspectionDiagnosticSeverity.Error,
                        rejected.Detail,
                        rejected.Kind.ToString()));
                break;
            case AssemblyPairCallUseInspectionOutcome.Available available:
                AddPairDiagnostics(
                    diagnostics,
                    available.Projection.Pair);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown pairwise Library call-use inspection outcome.");
        }
        return diagnostics.ToImmutable();
    }

    static void AddPairDiagnostics(
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
        AssemblyPairCallUseResult pair)
    {
        foreach (AssemblyPairCallUseFailure failure in pair.Failures)
        {
            diagnostics.Add(
                failure switch
                {
                    AssemblyPairCallUseFailure.Rejected rejected =>
                        new(
                            "assembly-pair-call-use.participant-rejected",
                            InspectionDiagnosticSeverity.Error,
                            $"{FormatAssembly(rejected.Subject)}: "
                                + rejected.Failure.Detail,
                            rejected.Subject.Identity.Name),
                    AssemblyPairCallUseFailure.InvalidImage invalid =>
                        new(
                            "assembly-pair-call-use.invalid-image",
                            InspectionDiagnosticSeverity.Error,
                            $"{FormatAssembly(invalid.Subject)}: "
                                + invalid.Error.Message,
                            invalid.Subject.Identity.Name),
                    _ => throw new InvalidOperationException(
                        "Unknown pairwise Library call-use failure."),
                });
        }

        foreach (AssemblyPairCallUseParticipant participant
            in pair.Participants)
        {
            foreach (AnalysisDiagnostic diagnostic
                in participant.Diagnostics)
            {
                diagnostics.Add(
                    new(
                        "assembly-pair-call-use.analysis-incomplete",
                        InspectionDiagnosticSeverity.Warning,
                        $"{FormatAssembly(participant.Subject)} "
                            + $"method 0x{diagnostic.MethodToken:X8}: "
                            + diagnostic.Message,
                        $"0x{diagnostic.MethodToken:X8}"));
            }
        }

        if (pair.Diagnostics.IsIncomplete)
        {
            diagnostics.Add(
                new(
                    "assembly-pair-call-use.correspondence-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    "Pair correspondence: "
                        + $"{pair.Diagnostics.UnresolvedCandidateCallCount} "
                        + "call sites name the other library but could not "
                        + "be matched."));
        }
    }

    static string FormatAssembly(AssemblyContextSubject subject) =>
        subject.Identity.Version is { } version
            ? $"{subject.Identity.Name}@{version}"
            : subject.Identity.Name;
}
