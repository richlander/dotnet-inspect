using System.Collections.Immutable;
using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace InspectWeb.Engine.SourceFacade;

internal static class BrowserMethodBodyProjection
{
    internal static BrowserMethodBodyComparison Project(
        BrowserMethodBodyComparisonRequest request, LocalComparisonQueryResult result) =>
        result switch
        {
            LocalComparisonQueryResult.NonSuccess failure => QueryFailure(request, failure),
            LocalComparisonQueryResult.Published published => Research(request, published.Outcome),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    static BrowserMethodBodyComparison QueryFailure(
        BrowserMethodBodyComparisonRequest request, LocalComparisonQueryResult.NonSuccess result)
    {
        string? side = result.Side?.ToString();
        (string Outcome, BrowserMethodBodyDiagnostic[] Diagnostics) failure = result.Failure switch
        {
            LocalComparisonQueryFailure.InvalidDesignation invalid =>
                ("InvalidDesignation",
                [
                    new(invalid.Kind.ToString(), side, "The physical designation is unavailable.", null),
                    .. invalid.MetadataFailures.Select(failure => new BrowserMethodBodyDiagnostic(
                        failure.Kind, side, failure.Operation, failure.Detail,
                        SubjectToken: failure.SubjectToken, Mechanism: failure.Mechanism.ToString())),
                ]),
            LocalComparisonQueryFailure.AccessRejected rejected =>
                ("AccessRejected", [new(rejected.Cause.Kind.ToString(), side, rejected.Cause.Detail, null)]),
            LocalComparisonQueryFailure.PopulationRejected rejected =>
                ("PopulationRejected", [new(rejected.Cause.Kind.ToString(), side,
                    "Query population admission was rejected.", rejected.Cause.ToString())]),
            LocalComparisonQueryFailure.AdmissionRejected rejected =>
                ("AdmissionRejected", [new(rejected.Cause.Kind.ToString(), side,
                    rejected.Cause.Summary, rejected.Cause.Location.ToString())]),
            LocalComparisonQueryFailure.PlanningRejected rejected =>
                ("PlanningRejected", [new(rejected.Cause.Kind.ToString(), side,
                    rejected.Cause.Summary, rejected.Cause.Location.ToString())]),
            LocalComparisonQueryFailure.DesignationRejected rejected =>
                ("DesignationRejected", [new(rejected.Cause.Kind.ToString(), side,
                    "Research rejected the designated pair.", null)]),
            LocalComparisonQueryFailure.DesignationUnavailable unavailable =>
                ("DesignationUnavailable",
                [.. unavailable.Cause.Endpoints.SelectMany(endpoint => TargetDiagnostics(endpoint))]),
            LocalComparisonQueryFailure.Failed failed =>
                ("Failed", [new("Failed", side, failed.Cause.Message, failed.Cause.ToString())]),
            LocalComparisonQueryFailure.Cancelled canceled =>
                ("Cancelled", [new("Cancelled", side, canceled.Cause.Message, canceled.Cause.ToString())]),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return new(request, "Query", failure.Outcome, [], failure.Diagnostics);
    }

    static IEnumerable<BrowserMethodBodyDiagnostic> TargetDiagnostics(ResearchDesignatedPairUnavailable endpoint)
    {
        yield return new(endpoint.Kind.ToString(), endpoint.Side.ToString(),
            $"Target {endpoint.Attempt.Outcome.Kind}; domain {endpoint.Census.Health}.", null);
        ResearchTargetDiagnostic? diagnostic = endpoint.Attempt.Outcome switch
        {
            ResearchTargetOutcome.NotFound value => value.ResearchDiagnostic,
            ResearchTargetOutcome.Unavailable value => value.Diagnostic,
            ResearchTargetOutcome.Failed value => value.Diagnostic,
            _ => null,
        };
        if (diagnostic is not null)
            yield return new(diagnostic.Kind.ToString(), endpoint.Side.ToString(), diagnostic.Summary, null);
        MemberTargetDiagnostic? metadata = endpoint.Attempt.Outcome switch
        {
            ResearchTargetOutcome.NotFound value => value.MetadataDiagnostic,
            ResearchTargetOutcome.Ambiguous value => value.Diagnostic,
            ResearchTargetOutcome.Rejected value => value.Diagnostic,
            _ => null,
        };
        if (metadata is not null)
            yield return new(metadata.Kind.ToString(), endpoint.Side.ToString(), metadata.Message,
                string.Join("; ", metadata.CandidateDetails()));
    }

    static BrowserMethodBodyComparison Research(
        BrowserMethodBodyComparisonRequest request, ResearchProducerSessionOutcome outcome) =>
        outcome switch
        {
            ResearchProducerSessionOutcome.Completed completed =>
                new(request, "Research", "Completed",
                    [.. completed.Completion.Results.Select(Producer)],
                    Cleanup(completed.Completion.Cleanup)),
            ResearchProducerSessionOutcome.Rejected rejected =>
                new(request, "Research", "Rejected", [],
                    [new(rejected.Rejection.Kind.ToString(), null, rejected.Rejection.Summary, null)]),
            ResearchProducerSessionOutcome.Failed failed =>
                new(request, "Research", "Failed", [],
                    [Diagnostic(failed.Diagnostic), .. Cleanup(failed.Cleanup)]),
            ResearchProducerSessionOutcome.Cancelled canceled =>
                new(request, "Research", "Cancelled", [], Cleanup(canceled.Cleanup)),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    static BrowserMethodBodyProducer Producer(ResearchProducerWorkResult work)
    {
        ResearchDesignatedPair pair = (work.Item.Basis as ResearchProducerWorkBasis.DesignatedPair)?.Pair
            ?? throw new InvalidOperationException("A direct-member result must retain its designated pair.");
        BrowserMethodBodyEndpoint before = Endpoint(pair.Before);
        BrowserMethodBodyEndpoint after = Endpoint(pair.After);
        var diagnostics = new List<BrowserMethodBodyDiagnostic>();
        string name = work.Item.Producer.ToString();
        switch (work.Outcome)
        {
            case ResearchProducerWorkOutcome.ProducedCSharp produced:
                var csharp = produced.Result;
                before = Inspection(before, csharp.Findings.OldInspection, "Before", diagnostics);
                after = Inspection(after, csharp.Findings.NewInspection, "After", diagnostics);
                BrowserCSharpBodyEvidence? csharpEvidence = null;
                if (csharp.BodyDiff is { } body)
                {
                    csharpEvidence = new(body.IsExact,
                        [.. Values(body.Rows).Select(row => new BrowserCSharpBodyRow(
                            row.AssemblyIdentity, row.StableMemberKey, row.Member,
                            row.ChangeId, row.Message, row.HunkId, row.Kind.ToString(),
                            row.Line, row.SourceCoordinate, row.Fidelity, row.Text,
                            row.OldValue, row.NewValue, Operation(row.OldOperation), Operation(row.NewOperation)))]);
                    diagnostics.AddRange(Values(body.FailureRows).Select(failure =>
                        new BrowserMethodBodyDiagnostic(failure.Kind.ToString(), failure.Side,
                            failure.Message, failure.Detail, failure.HunkId)));
                    diagnostics.AddRange(Values(body.IdentityFailures).Select(failure =>
                        new BrowserMethodBodyDiagnostic(failure.Kind, failure.Side,
                            "C# identity resolution failed.", failure.Detail,
                            SubjectToken: failure.SubjectToken,
                            Mechanism: failure.Mechanism.ToString(), Path: failure.Path)));
                }
                return new(name, "ProducedCSharp",
                    csharpEvidence is null ? MissingBodyVerdict(before, after)
                        : csharpEvidence.IsExact ? "Exact" : "NotExact",
                    before, after, csharpEvidence, null, [.. diagnostics]);
            case ResearchProducerWorkOutcome.ProducedIlBody produced:
                var il = produced.Result;
                before = Inspection(before, il.Findings.OldInspection, "Before", diagnostics);
                after = Inspection(after, il.Findings.NewInspection, "After", diagnostics);
                BrowserIlBodyEvidence? ilEvidence = null;
                if (il.MemberDiff is { } member)
                {
                    ilEvidence = new(member.Diff.Outcome.ToString(), member.Diff.IsExact,
                        member.Diff.IsAvailable, member.Diff.Failure,
                        [.. Values(member.Diff.Rows).Select(row => new BrowserIlBodyRow(
                            row.HunkId, row.Kind.ToString(),
                            new(row.Operation.Offset, row.Operation.OpcodeFamily,
                                row.Operation.Operand is { } operand
                                    ? new(operand.Kind.ToString(), operand.Value) : null),
                            row.Message))]);
                    diagnostics.AddRange(Values(member.Diff.FailureRows).Select(failure =>
                        new BrowserMethodBodyDiagnostic(failure.Kind.ToString(), failure.Side,
                            failure.Message, failure.Detail)));
                    diagnostics.AddRange(Values(member.IdentityFailures).Select(failure =>
                        new BrowserMethodBodyDiagnostic(failure.Kind, failure.Side,
                            "IL identity resolution failed.", failure.Detail,
                            SubjectToken: failure.SubjectToken, Mechanism: failure.Mechanism.ToString())));
                }
                return new(name, "ProducedIlBody",
                    ilEvidence?.Outcome ?? MissingBodyVerdict(before, after),
                    before, after, null, ilEvidence, [.. diagnostics]);
            case ResearchProducerWorkOutcome.Unavailable unavailable:
                return new(name, "Unavailable", "NotRun", before, after, null, null,
                    [new(unavailable.Reason.Kind.ToString(), unavailable.Reason.Input?.Side.ToString(),
                        unavailable.Reason.Summary, null)]);
            case ResearchProducerWorkOutcome.Failed failed:
                return new(name, "Failed", "NotRun", before, after, null, null,
                    [Diagnostic(failed.Diagnostic)]);
            default:
                throw new ArgumentOutOfRangeException(nameof(work));
        }
    }

    static BrowserMethodBodyEndpoint Endpoint(ResearchTargetAttempt attempt)
    {
        var resolved = attempt.Outcome as ResearchTargetOutcome.Resolved;
        return new("NotInspected", resolved?.Address?.ModuleVersionId.ToString("D"),
            resolved?.Address?.Token, attempt.Outcome.Kind.ToString(), null);
    }

    static BrowserMethodBodyEndpoint Inspection<T>(
        BrowserMethodBodyEndpoint endpoint, FindingInspection<T> inspection,
        string side, List<BrowserMethodBodyDiagnostic> diagnostics) where T : notnull
    {
        switch (inspection.Value)
        {
            case FindingInspection<T>.Complete:
                return endpoint with { State = "Complete" };
            case FindingInspection<T>.Absent absent:
                return endpoint with { State = absent.Kind.ToString(), Detail = absent.Detail };
            case FindingInspection<T>.Failed failed:
                diagnostics.Add(new(failed.Error.Descriptor.Id.ToString(), side, failed.Error.Reason, null));
                return endpoint with { State = "Failed", Detail = failed.Error.Reason };
            default:
                throw new ArgumentOutOfRangeException(nameof(inspection));
        }
    }

    static string MissingBodyVerdict(BrowserMethodBodyEndpoint before, BrowserMethodBodyEndpoint after) =>
        before.State == "Failed" || after.State == "Failed" ? "Unavailable" : "NotApplicable";

    static BrowserMethodBodyDiagnostic Diagnostic(ResearchProducerDiagnostic diagnostic) =>
        new(diagnostic.Kind.ToString(), null, diagnostic.Summary, diagnostic.Producer?.ToString());

    static BrowserMethodBodyDiagnostic[] Cleanup(ImmutableArray<ResearchProducerCleanupOutcome> outcomes) =>
        [.. Values(outcomes).OfType<ResearchProducerCleanupOutcome.Failed>().Select(failure =>
            Diagnostic(failure.Diagnostic) with { Side = failure.Input.Side.ToString() })];

    static BrowserCSharpBodyOperation? Operation(CSharpDiffOperation? operation) =>
        operation is null ? null : new(operation.Kind.ToString(), operation.Value);

    static ImmutableArray<T> Values<T>(ImmutableArray<T> values) => values.IsDefault ? [] : values;
}
