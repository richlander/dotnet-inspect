using System.Collections.Immutable;
using CSharpText;
using DotnetInspector.Views;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Research;
using InertText;
using Markout;

namespace DotnetInspector.Output;

public static class MethodBodyDiffFormatter
{
    public static MethodBodyDiffDocument Build(
        string beforeDisplay,
        string afterDisplay,
        ResearchProducerSessionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        MethodBodyDiffDocument document = outcome switch
        {
            ResearchProducerSessionOutcome.Completed completed => new(
                beforeDisplay,
                afterDisplay,
                MethodBodyDiffStage.Research,
                "Completed",
                null,
                [.. completed.Completion.Results.Select(BuildProducer)],
                BuildCleanup(completed.Completion.Cleanup))
            {
                WorkItemCount = completed.Completion.WorkItems.Length,
            },
            ResearchProducerSessionOutcome.Rejected rejected => new(
                beforeDisplay,
                afterDisplay,
                MethodBodyDiffStage.Research,
                "Rejected",
                new(rejected.Rejection.Kind.ToString(), null, rejected.Rejection.Summary),
                [],
                []),
            ResearchProducerSessionOutcome.Failed failed => new(
                beforeDisplay,
                afterDisplay,
                MethodBodyDiffStage.Research,
                "Failed",
                Diagnostic(failed.Diagnostic),
                [],
                BuildCleanup(failed.Cleanup)),
            ResearchProducerSessionOutcome.Cancelled cancelled => new(
                beforeDisplay,
                afterDisplay,
                MethodBodyDiffStage.Research,
                "Cancelled",
                null,
                [],
                BuildCleanup(cancelled.Cleanup)),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        return document with { NativeOutcome = outcome, HasFailures = HasFailures(outcome) };
    }

    public static MethodBodyDiffDocument QueryFailure(
        string beforeDisplay,
        string afterDisplay,
        string kind,
        string? side,
        string detail)
        => new(
            beforeDisplay,
            afterDisplay,
            MethodBodyDiffStage.Query,
            kind,
            new(kind, side, detail),
            [],
            [])
        {
            HasFailures = true,
        };

    public static string Render(
        MethodBodyDiffDocument document,
        MarkoutWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var producers = new List<MethodBodyProducerRow>();
        var endpoints = new List<MethodBodyEndpointRow>();
        var evidence = new List<MethodBodyEvidenceRow>();
        var diagnostics = new List<MethodBodyDiagnosticRow>();
        AddDiagnostic(diagnostics, document.Diagnostic);

        foreach (var producer in document.Producers)
        {
            string name = ProducerName(producer.Producer);
            producers.Add(new(
                Field(name),
                Field(producer.Outcome),
                Field(producer.NativeVerdict),
                Field(producer.Before.State.ToString()),
                Field(producer.After.State.ToString()),
                Field(FindingsSummary(producer.Findings))));
            endpoints.Add(EndpointRow(name, producer.Before, document.Before));
            endpoints.Add(EndpointRow(name, producer.After, document.After));
            AddDiagnostic(diagnostics, producer.Diagnostic);
            foreach (var endpoint in new[] { producer.Before, producer.After })
                if (endpoint.Error is { } error)
                    diagnostics.Add(DiagnosticRow(
                        name, endpoint.Side.ToString(), error.Descriptor.Id, error.Reason));

            if (producer.NativeCSharp is { } csharp)
            {
                evidence.AddRange(CSharpDiffPrinter.ToUnifiedLines(csharp)
                    .Select(line => new MethodBodyEvidenceRow(Field(name), Field(line))));
                if (!csharp.FailureRows.IsDefaultOrEmpty)
                    foreach (var failure in csharp.FailureRows)
                        diagnostics.Add(DiagnosticRow(name, failure.Side, failure.Kind.ToString(),
                            Detail(failure.Message, failure.Detail)));
                if (!csharp.IdentityFailures.IsDefaultOrEmpty)
                    foreach (var failure in csharp.IdentityFailures)
                        diagnostics.Add(DiagnosticRow(name, failure.Side, failure.Kind,
                            $"{failure.Mechanism}; token 0x{failure.SubjectToken:X8}; {failure.Path}; {failure.Detail}"));
            }
            if (producer.NativeIl is { } il)
            {
                evidence.AddRange(IlDiffPrinter.ToUnifiedLines(il.Diff)
                    .Select(line => new MethodBodyEvidenceRow(Field(name), Field(line))));
                if (!il.Diff.FailureRows.IsDefaultOrEmpty)
                    foreach (var failure in il.Diff.FailureRows)
                        diagnostics.Add(DiagnosticRow(name, failure.Side, failure.Kind.ToString(),
                            Detail(failure.Message, failure.Detail)));
                if (!il.IdentityFailures.IsDefaultOrEmpty)
                    foreach (var failure in il.IdentityFailures)
                        diagnostics.Add(DiagnosticRow(name, failure.Side, failure.Kind,
                            $"{failure.Mechanism}; token 0x{failure.SubjectToken:X8}; {failure.Detail}"));
            }
        }

        foreach (var cleanup in document.Cleanup)
            AddDiagnostic(diagnostics, cleanup.Diagnostic);

        var view = new MethodBodyDiffView(
            Field($"Method Body Diff: {document.Before} vs {document.After}"),
            Field(document.Stage.ToString()),
            Field(document.Outcome),
            Field(document.Stage == MethodBodyDiffStage.Query
                ? "The query did not publish a Research comparison."
                : document.Outcome == "Completed"
                    ? "Completed accounts for producer work; native body verdicts and Findings are independent."
                    : "Research did not publish a completed producer session."))
        {
            Producers = producers.Count == 0 ? null : producers,
            Endpoints = endpoints.Count == 0 ? null : endpoints,
            Evidence = evidence.Count == 0 ? null : evidence,
            Diagnostics = diagnostics.Count == 0 ? null : diagnostics,
            Cleanup = document.Cleanup.IsEmpty ? null : [.. document.Cleanup.Select(
                cleanup => new MethodBodyCleanupRow(Field(cleanup.Side.ToString()), Field(cleanup.Outcome)))],
        };
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        MethodBodyDiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    static MethodBodyProducerDocument BuildProducer(ResearchProducerWorkResult work)
    {
        var pair = (work.Item.Basis as ResearchProducerWorkBasis.DesignatedPair)?.Pair;
        var before = Endpoint(ResearchComparisonSide.Before, pair?.Before);
        var after = Endpoint(ResearchComparisonSide.After, pair?.After);
        string basis = work.Item.Basis switch
        {
            ResearchProducerWorkBasis.DesignatedPair => "DesignatedPair",
            ResearchProducerWorkBasis.Correspondence => "Correspondence",
            _ => throw new ArgumentOutOfRangeException(nameof(work)),
        };
        MethodBodyProducerDocument document;
        switch (work.Outcome)
        {
            case ResearchProducerWorkOutcome.ProducedCSharp produced:
                var csharp = produced.Result;
                before = Inspection(before, csharp.Findings.OldInspection) with { CSharpSubject = csharp.Old };
                after = Inspection(after, csharp.Findings.NewInspection) with { CSharpSubject = csharp.New };
                document = new(work.Item.Producer, basis, "ProducedCSharp",
                    csharp.BodyDiff is { } body ? body.IsExact ? "Exact" : "NotExact" : MissingBodyVerdict(before, after),
                    before, after)
                {
                    Findings = Findings(csharp.Findings),
                    NativeCSharp = csharp.BodyDiff,
                    CSharp = csharp.BodyDiff is { } diff ? new(
                        diff.IsExact, Array(diff.Rows), Array(diff.FailureRows), Array(diff.IdentityFailures)) : null,
                };
                break;
            case ResearchProducerWorkOutcome.ProducedIlBody produced:
                var il = produced.Result;
                before = Inspection(before, il.Findings.OldInspection) with { IlSubject = il.Old };
                after = Inspection(after, il.Findings.NewInspection) with { IlSubject = il.New };
                document = new(work.Item.Producer, basis, "ProducedIlBody",
                    il.MemberDiff?.Diff.Outcome.ToString() ?? MissingBodyVerdict(before, after),
                    before, after)
                {
                    Findings = Findings(il.Findings),
                    NativeIl = il.MemberDiff,
                    Il = il.MemberDiff is { } member ? new(
                        member.Old, member.New, member.Diff.Outcome, member.Diff.IsExact, member.Diff.IsAvailable,
                        member.Diff.Failure, Array(member.Diff.Rows), Array(member.Diff.FailureRows),
                        Array(member.IdentityFailures)) : null,
                };
                break;
            case ResearchProducerWorkOutcome.Unavailable unavailable:
                document = new(work.Item.Producer, basis, "Unavailable", "NotRun", before, after)
                {
                    Diagnostic = new(
                        unavailable.Reason.Kind.ToString(),
                        unavailable.Reason.Input?.Side.ToString(),
                        unavailable.Reason.Summary,
                        work.Item.Producer)
                    {
                        Input = unavailable.Reason.Input,
                    },
                };
                break;
            case ResearchProducerWorkOutcome.Failed failed:
                document = new(work.Item.Producer, basis, "Failed", "NotRun", before, after)
                {
                    Diagnostic = Diagnostic(failed.Diagnostic),
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(work));
        }
        return document with { NativeWork = work };
    }

    static MethodBodyEndpointDocument Endpoint(
        ResearchComparisonSide side,
        ResearchTargetAttempt? attempt)
    {
        var resolved = attempt?.Outcome as ResearchTargetOutcome.Resolved;
        return new(side, MethodBodyEndpointState.NotInspected)
        {
            TargetState = attempt?.Outcome.Kind,
            Address = resolved?.Address,
            Anchor = resolved?.Anchor,
            Attempt = attempt,
        };
    }

    static MethodBodyEndpointDocument Inspection<T>(
        MethodBodyEndpointDocument endpoint,
        FindingInspection<T> inspection) where T : notnull
        => inspection.Value switch
        {
            FindingInspection<T>.Complete complete => endpoint with
            {
                State = MethodBodyEndpointState.Complete,
                FindingCount = complete.Findings.Length,
            },
            FindingInspection<T>.Absent absent => endpoint with
            {
                State = absent.Kind switch
                {
                    FindingInspectionAbsenceKind.SubjectAbsent => MethodBodyEndpointState.SubjectAbsent,
                    FindingInspectionAbsenceKind.NoApplicableInput => MethodBodyEndpointState.NoApplicableInput,
                    _ => throw new ArgumentOutOfRangeException(nameof(inspection)),
                },
                Detail = absent.Detail,
            },
            FindingInspection<T>.Failed failed => endpoint with
            {
                State = MethodBodyEndpointState.Failed,
                Error = failed.Error,
                Detail = failed.Error.Reason,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(inspection)),
        };

    static MethodBodyFindingsDocument Findings<T>(FindingComparison<T> comparison) where T : notnull
        => comparison.Value switch
        {
            FindingComparison<T>.Complete complete => new(
                "Complete", complete.IsExact, complete.Transition, complete.Match, complete.Pairs.Length, null),
            FindingComparison<T>.Failed failed => new(
                "Failed", null, null, null, null, failed.Failure),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };

    static string MissingBodyVerdict(MethodBodyEndpointDocument before, MethodBodyEndpointDocument after)
        => before.State == MethodBodyEndpointState.Failed || after.State == MethodBodyEndpointState.Failed
            ? "Unavailable"
            : "NotApplicable";

    static ImmutableArray<MethodBodyCleanupDocument> BuildCleanup(
        ImmutableArray<ResearchProducerCleanupOutcome> cleanup)
        => [.. cleanup.Select(item => item switch
        {
            ResearchProducerCleanupOutcome.Succeeded => new MethodBodyCleanupDocument(
                item.Input.Side, "Succeeded", null) { NativeCleanup = item },
            ResearchProducerCleanupOutcome.Failed failed => new MethodBodyCleanupDocument(
                item.Input.Side, "Failed",
                Diagnostic(failed.Diagnostic) with { Side = item.Input.Side.ToString(), Input = item.Input })
            {
                NativeCleanup = item,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(cleanup)),
        })];

    static MethodBodyDiffDiagnostic Diagnostic(ResearchProducerDiagnostic diagnostic)
        => new(diagnostic.Kind.ToString(), null, diagnostic.Summary, diagnostic.Producer);

    static bool HasFailures(ResearchProducerSessionOutcome outcome)
        => outcome switch
        {
            ResearchProducerSessionOutcome.Completed completed =>
                completed.Completion.Cleanup.Any(cleanup => cleanup is ResearchProducerCleanupOutcome.Failed)
                || completed.Completion.Results.Any(work => work.Outcome switch
                {
                    ResearchProducerWorkOutcome.Unavailable or ResearchProducerWorkOutcome.Failed => true,
                    ResearchProducerWorkOutcome.ProducedCSharp csharp =>
                        csharp.Result.Findings.Value is FindingComparison<CSharpCanonicalLine>.Failed
                        || csharp.Result.BodyDiff is { } body
                            && (!body.FailureRows.IsDefaultOrEmpty || !body.IdentityFailures.IsDefaultOrEmpty),
                    ResearchProducerWorkOutcome.ProducedIlBody il =>
                        il.Result.Findings.Value is FindingComparison<CanonicalIlOperation>.Failed
                        || il.Result.MemberDiff is { } member
                            && (!member.Diff.IsAvailable
                                || member.Diff.Failure is not null
                                || !member.Diff.FailureRows.IsDefaultOrEmpty
                                || !member.IdentityFailures.IsDefaultOrEmpty),
                    _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
                }),
            ResearchProducerSessionOutcome.Rejected
                or ResearchProducerSessionOutcome.Failed
                or ResearchProducerSessionOutcome.Cancelled => true,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    static ImmutableArray<T> Array<T>(ImmutableArray<T> values)
        => values.IsDefault ? [] : values;

    static string ProducerName(ResearchProducerKind producer)
        => producer == ResearchProducerKind.CSharp ? "C#" : "IL";

    static string FindingsSummary(MethodBodyFindingsDocument? findings)
        => findings is null ? "NotRun"
            : findings.IsExact is { } exact ? $"{findings.Outcome}; exact: {exact.ToString().ToLowerInvariant()}"
            : findings.Outcome;

    static MethodBodyEndpointRow EndpointRow(
        string producer,
        MethodBodyEndpointDocument endpoint,
        string fallbackDisplay)
        => new(
            Field(producer),
            Field(endpoint.Side.ToString()),
            Field(endpoint.CSharpSubject?.Display ?? endpoint.IlSubject?.Label ?? fallbackDisplay),
            Field(endpoint.State.ToString()),
            Field(endpoint.Address is { } address ? $"{address.ModuleVersionId:D}/0x{address.Token:X8}" : ""),
            Field(endpoint.Detail ?? ""));

    static void AddDiagnostic(
        List<MethodBodyDiagnosticRow> rows,
        MethodBodyDiffDiagnostic? diagnostic)
    {
        if (diagnostic is not null)
            rows.Add(DiagnosticRow(
                diagnostic.Producer is { } producer ? ProducerName(producer) : "",
                diagnostic.Side, diagnostic.Kind, diagnostic.Detail));
    }

    static MethodBodyDiagnosticRow DiagnosticRow(string producer, string? side, string kind, string detail)
        => new(Field(producer), Field(side ?? ""), Field(kind), Field(detail));

    static string Detail(string message, string? detail)
        => string.IsNullOrEmpty(detail) ? message : $"{message}; {detail}";

    static InertString Field(string text)
        => new(TextPolicy.Field, CSharpIdentifier.ContainRenderedText(text));
}
