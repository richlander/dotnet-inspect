using System.Collections.Immutable;

using Inspector.Findings;
using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// The exact allocation comparison, or the focused reason that requires the
/// caller to retain its established fallback.
/// </summary>
public abstract class WorkspaceMethodAllocationComparisonResult
{
    private WorkspaceMethodAllocationComparisonResult()
    {
    }

    public sealed class Compared : WorkspaceMethodAllocationComparisonResult
    {
        internal Compared(ResearchComparison comparison)
            => Comparison = comparison;

        public ResearchComparison Comparison { get; }
    }

    public sealed class Fallback : WorkspaceMethodAllocationComparisonResult
    {
        public Fallback(string reason)
            => Reason = reason;

        public string Reason { get; }
    }
}

/// <summary>
/// Compares one caller-selected MethodDef pair using only allocation findings
/// from the owner-issued exact allocation producer.
/// </summary>
public static class WorkspaceMethodAllocationComparisonQuery
{
    public static InspectionQuery<WorkspaceMethodAllocationComparisonResult>
        Definition { get; } =
        new(
            "Workspace method allocation comparison",
            InspectionCost.Unbounded);

    public static WorkspaceMethodAllocationComparisonResult Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant beforeParticipant,
        int beforeMethodToken,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant afterParticipant,
        int afterMethodToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);
        cancellationToken.ThrowIfCancellationRequested();

        if ((beforeMethodToken & unchecked((int)0xff000000)) != 0x06000000
            || (afterMethodToken & unchecked((int)0xff000000)) != 0x06000000)
        {
            return new WorkspaceMethodAllocationComparisonResult.Fallback(
                "The exact allocation target is not a MethodDef pair.");
        }

        AssemblyContextMethodCorrespondenceResult beforeCorrespondence =
            AssemblyContextMethodCorrespondenceQuery.Execute(
                beforeGroup,
                beforeParticipant,
                beforeMethodToken,
                beforeGroup,
                beforeParticipant,
                cancellationToken);
        AssemblyContextMethodCorrespondenceResult afterCorrespondence =
            AssemblyContextMethodCorrespondenceQuery.Execute(
                afterGroup,
                afterParticipant,
                afterMethodToken,
                afterGroup,
                afterParticipant,
                cancellationToken);
        if (beforeCorrespondence is not
                AssemblyContextMethodCorrespondenceResult.Exact beforeExact
            || afterCorrespondence is not
                AssemblyContextMethodCorrespondenceResult.Exact afterExact)
        {
            return new WorkspaceMethodAllocationComparisonResult.Fallback(
                "The exact allocation target does not identify one managed "
                    + "implementation body at each endpoint.");
        }

        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> beforeEntry =
            AssemblyContextMethodAllocationQuery.ExecuteParticipant(
                beforeGroup,
                beforeParticipant,
                beforeExact.MethodToken,
                cancellationToken);
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> afterEntry =
            AssemblyContextMethodAllocationQuery.ExecuteParticipant(
                afterGroup,
                afterParticipant,
                afterExact.MethodToken,
                cancellationToken);
        return Compare(
            beforeEntry,
            afterEntry,
            afterParticipant.Assembly.Identity.Name,
            afterExact.MethodToken,
            cancellationToken);
    }

    public static WorkspaceMethodAllocationComparisonResult Compare(
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> beforeEntry,
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> afterEntry,
        string afterAssemblyName,
        int afterMethodToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeEntry);
        ArgumentNullException.ThrowIfNull(afterEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterAssemblyName);
        cancellationToken.ThrowIfCancellationRequested();

        MethodIdentity? method =
            (afterEntry as
                AssemblyContextEntry<AssemblyMethodAllocationAnalysis>
                    .Available)?.Value.Method
            ?? (beforeEntry as
                AssemblyContextEntry<AssemblyMethodAllocationAnalysis>
                    .Available)?.Value.Method;
        ResearchSubjectKey subject = method is not null
            ? ResearchMemberIdentity.SubjectFromMethod(method)
            : new(
                ResearchSubjectKind.Member,
                $"member:{afterAssemblyName}"
                    + $"|0x{afterMethodToken:X8}",
                $"{afterAssemblyName}"
                    + $"!0x{afterMethodToken:X8}");
        var findingSubject = new FindingSubject(
            subject.Id,
            subject.Display);
        FindingInspection<AllocationOccurrence> beforeInspection =
            Inspect(beforeEntry, findingSubject);
        FindingInspection<AllocationOccurrence> afterInspection =
            Inspect(afterEntry, findingSubject);
        FindingComparison<AllocationOccurrence> comparison =
            AnalysisFindings.CompareAllocations(
                beforeInspection,
                afterInspection);
        return new WorkspaceMethodAllocationComparisonResult.Compared(
            new ResearchComparison(
                [],
                apiDiff: null,
                apiComparison: null,
                new RetainedFindingComparisonSet(
                [
                    new RetainedFindingComparison<AllocationOccurrence>(
                        subject,
                        AnalysisFindings.AllocationDescriptor,
                        comparison),
                ])));
    }

    static FindingInspection<AllocationOccurrence> Inspect(
        AssemblyContextEntry<AssemblyMethodAllocationAnalysis> entry,
        FindingSubject subject) =>
        entry switch
        {
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Available
                available =>
                available.Value.Diagnostics.IsEmpty
                    ? new FindingInspection<AllocationOccurrence>.Complete(
                        AnalysisFindings.InspectAllocations(
                            available.Value.Allocations,
                            subject))
                    : Failed(
                        subject,
                        IncompleteReason(
                            available.Value.Diagnostics)),
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Rejected
                rejected =>
                Failed(
                    subject,
                    $"The exact allocation image was rejected: "
                        + $"{rejected.Failure}."),
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Failed
                failed =>
                Failed(
                    subject,
                    $"Exact allocation analysis failed: "
                        + $"{failed.Error.GetType().Name}: "
                        + failed.Error.Message),
            _ => throw new InvalidOperationException(
                "Unknown exact allocation analysis outcome."),
        };

    static FindingInspection<AllocationOccurrence> Failed(
        FindingSubject subject,
        string reason) =>
        new FindingInspection<AllocationOccurrence>.Failed(
            new InspectionError(
                subject,
                AnalysisFindings.AllocationDescriptor,
                reason));

    static string IncompleteReason(
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        AnalysisDiagnostic first = diagnostics[0];
        string prefix = diagnostics.Length == 1
            ? "Method Analysis is incomplete"
            : $"Method Analysis is incomplete with "
                + $"{diagnostics.Length} diagnostics; first";
        return $"{prefix}: {first.Method}: {first.Message}";
    }
}
