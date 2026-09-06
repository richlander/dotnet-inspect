using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace DotnetInspector.RoundTripCompilation;

internal sealed record RoundTripBodyEvidence(
    RoundTripEvidenceStatus CSharpStatus,
    IlBodyDiffOutcome IlStatus,
    RoundTripCSharpEvidence? CSharpDiff,
    RoundTripIlEvidence? IlDiff,
    LocalComparisonQueryResult Evidence,
    string? CSharpFailure,
    string? IlFailure);

internal sealed class RoundTripComparisonQuery
{
    readonly AssemblyContextGroup _group;
    readonly AssemblyContextParticipant _before;
    readonly AssemblyContextParticipant _after;

    internal RoundTripComparisonQuery(
        InspectionWorkspace workspace,
        DecompilerMetadataSource before,
        DecompilerMetadataSource after)
    {
        var beforeAssembly = Assembly(before);
        var afterAssembly = Assembly(after);
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
        [
            (beforeAssembly, new AssemblyReferenceBindingPolicy(
                DecompilerMetadataSource.DefaultAssemblyReferenceResolver(before.Path))),
            (afterAssembly, new AssemblyReferenceBindingPolicy(
                DecompilerMetadataSource.DefaultAssemblyReferenceResolver(after.Path))),
        ]);
        _before = new(beforeAssembly, policy);
        _after = new(afterAssembly, policy);
        _group = workspace.CreateAssemblyContextGroup([_before, _after]);
    }

    internal RoundTripBodyEvidence Compare(
        MetadataMethodAddress before,
        MetadataMethodAddress after)
    {
        LocalComparisonQueryResult evidence = DirectMemberComparisonQuery.Execute(
            _group,
            new(new(_before, before), new(_after, after), ResearchProducerCatalog.Kinds));
        if (evidence is not LocalComparisonQueryResult.Published
            { Outcome: ResearchProducerSessionOutcome.Completed completed })
        {
            string failure = QueryFailure(evidence);
            return new(
                RoundTripEvidenceStatus.Unavailable, IlBodyDiffOutcome.Unavailable,
                null, null, evidence, failure, failure);
        }

        ResearchProducerWorkOutcome csharp = completed.Completion.Results
            .Single(result => result.Item.Producer == ResearchProducerKind.CSharp).Outcome;
        ResearchProducerWorkOutcome il = completed.Completion.Results
            .Single(result => result.Item.Producer == ResearchProducerKind.IlBody).Outcome;
        CSharpMemberEndpointComparison? csharpResult =
            (csharp as ResearchProducerWorkOutcome.ProducedCSharp)?.Result;
        IlBodyDiffResult? ilDiff =
            (il as ResearchProducerWorkOutcome.ProducedIlBody)?.Result.MemberDiff?.Diff;
        (RoundTripEvidenceStatus status, string? failureReason) = csharpResult is { }
            ? ClassifyCSharp(csharpResult)
            : (RoundTripEvidenceStatus.Unavailable, ProducerFailure(csharp));
        return new(
            status,
            ilDiff?.Outcome ?? IlBodyDiffOutcome.Unavailable,
            ToEvidence(csharpResult?.BodyDiff),
            ToEvidence(ilDiff),
            evidence,
            failureReason,
            ilDiff?.Failure ?? (ilDiff is null ? ProducerFailure(il) : null));
    }

    internal static (RoundTripEvidenceStatus Status, string? Failure) ClassifyCSharp(
        CSharpMemberEndpointComparison result)
    {
        CSharpBodyDiffResult? diff = result.BodyDiff;
        if (diff is null)
        {
            return (RoundTripEvidenceStatus.Unavailable,
                InspectionFailure(result.Findings.OldInspection, result.Findings.NewInspection));
        }
        if (!diff.FailureRows.IsDefaultOrEmpty || !diff.IdentityFailures.IsDefaultOrEmpty)
        {
            return (RoundTripEvidenceStatus.Unavailable,
                string.Join("; ",
                    Normalize(diff.FailureRows).Select(row => row.Message)
                        .Concat(Normalize(diff.IdentityFailures).Select(row => row.Detail))));
        }
        return (diff.IsExact ? RoundTripEvidenceStatus.Exact : RoundTripEvidenceStatus.Changed, null);
    }

    static ResolvedAssemblyReference Assembly(DecompilerMetadataSource source)
        => ResolvedAssemblyReference.CreateFromPath(
            source.Path,
            AssemblyResolutionProvenance.Local("round-trip comparison"));

    static string QueryFailure(LocalComparisonQueryResult result)
        => result switch
        {
            LocalComparisonQueryResult.NonSuccess failure =>
                $"{failure.Side?.ToString() ?? "Comparison"} query: {failure.Failure}",
            LocalComparisonQueryResult.Published published => published.Outcome switch
            {
                ResearchProducerSessionOutcome.Rejected rejected => rejected.Rejection.Summary,
                ResearchProducerSessionOutcome.Failed failed => failed.Diagnostic.Summary,
                ResearchProducerSessionOutcome.Cancelled => "comparison query cancelled",
                _ => throw new InvalidOperationException("Expected a non-completed query outcome."),
            },
            _ => throw new InvalidOperationException("Unknown comparison query result."),
        };

    static string ProducerFailure(ResearchProducerWorkOutcome outcome)
        => outcome switch
        {
            ResearchProducerWorkOutcome.Unavailable unavailable => unavailable.Reason.Summary,
            ResearchProducerWorkOutcome.Failed failed => failed.Diagnostic.Summary,
            ResearchProducerWorkOutcome.ProducedIlBody produced =>
                InspectionFailure(
                    produced.Result.Findings.OldInspection,
                    produced.Result.Findings.NewInspection),
            _ => throw new InvalidOperationException("Expected unavailable producer evidence."),
        };

    static string InspectionFailure<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection) where T : notnull
    {
        List<string> failures = [];
        if (oldInspection.Value is FindingInspection<T>.Absent oldAbsent)
            failures.Add($"old absent: {oldAbsent.Detail}");
        else if (oldInspection.Value is FindingInspection<T>.Failed oldFailed)
            failures.Add($"old failed: {oldFailed.Error.Reason}");
        if (newInspection.Value is FindingInspection<T>.Absent newAbsent)
            failures.Add($"new absent: {newAbsent.Detail}");
        else if (newInspection.Value is FindingInspection<T>.Failed newFailed)
            failures.Add($"new failed: {newFailed.Error.Reason}");
        return string.Join("; ", failures);
    }

    static RoundTripCSharpEvidence? ToEvidence(CSharpBodyDiffResult? diff)
        => diff is null ? null : new(
            Normalize(diff.Rows), Normalize(diff.FailureRows), Normalize(diff.IdentityFailures));

    static RoundTripIlEvidence? ToEvidence(IlBodyDiffResult? diff)
        => diff is null ? null : new(
            diff.Outcome, diff.Failure, Normalize(diff.Rows), Normalize(diff.FailureRows));

    static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values)
        => values.IsDefault ? [] : values;
}
