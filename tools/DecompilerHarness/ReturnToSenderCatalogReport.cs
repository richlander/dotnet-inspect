using System.Text;
using ILInspector.Instructions;
using ILInspector.Research;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

internal sealed record ReturnToSenderCatalogReportCounts(int Passed, int Skipped, int Failed);

internal sealed record ReturnToSenderCatalogReportBucket(string Key, int Count);

internal sealed record ReturnToSenderCatalogFixtureGroup(
    string FixtureId,
    GeneratedFixtureReturnToSenderStatus Status,
    IReadOnlyList<GeneratedFixtureReturnToSenderResult> Rows);

internal sealed record ReturnToSenderCatalogResearchSection(
    ResearchComparison Comparison,
    ReturnToSenderResearchSummary Summary,
    IReadOnlyList<ReturnToSenderCatalogReportBucket> ChangeBuckets);

internal sealed record ReturnToSenderCatalogReportView(
    int FixtureCount,
    int TargetCount,
    ReturnToSenderCatalogReportCounts Fixtures,
    ReturnToSenderCatalogReportCounts Targets,
    ReturnToSenderCatalogResearchSection? Research,
    IReadOnlyList<ReturnToSenderCatalogReportBucket> RoslynFallbacks,
    IReadOnlyList<ReturnToSenderCatalogReportBucket> SkippedTargetReasons,
    IReadOnlyList<ReturnToSenderCatalogReportBucket> FailedTargetBuckets,
    IReadOnlyList<ReturnToSenderCatalogFixtureGroup> FailedFixtures,
    IReadOnlyList<ReturnToSenderCatalogFixtureGroup> PassedFixtures);

internal static class ReturnToSenderCatalogReport
{
    public static ReturnToSenderCatalogReportView Build(GeneratedFixtureReturnToSenderRunResult run, int maxExamples)
    {
        ArgumentNullException.ThrowIfNull(run);

        var fixtureRows = run.Results
            .GroupBy(result => result.FixtureId, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToArray();
                GeneratedFixtureReturnToSenderStatus status =
                    rows.Any(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)
                        ? GeneratedFixtureReturnToSenderStatus.Fail
                        : rows.Any(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass)
                            ? GeneratedFixtureReturnToSenderStatus.Pass
                            : GeneratedFixtureReturnToSenderStatus.Skip;
                return new ReturnToSenderCatalogFixtureGroup(group.Key, status, rows);
            })
            .OrderBy(row => row.FixtureId, StringComparer.Ordinal)
            .ToArray();

        var research = ReturnToSenderEvidence.ToResearchComparison(ReturnToSenderEvidence.FromCatalog(run));
        var researchChanges = research.Changes;
        var researchSection = researchChanges.Length == 0
            ? null
            : new ReturnToSenderCatalogResearchSection(
                research,
                ReturnToSenderEvidence.Summarize(research, maxExamples),
                [.. researchChanges
                    .GroupBy(change => change.Descriptor.Id, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ReturnToSenderCatalogReportBucket(group.Key, group.Count()))]);

        return new ReturnToSenderCatalogReportView(
            fixtureRows.Length,
            run.Results.Count,
            new ReturnToSenderCatalogReportCounts(
                fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass),
                fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip),
                fixtureRows.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)),
            new ReturnToSenderCatalogReportCounts(
                run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass),
                run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip),
                run.Results.Count(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)),
            researchSection,
            RoslynFallbackBuckets(run.Results),
            Buckets(run.Results.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Skip)),
            Buckets(run.Results.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail)),
            [.. fixtureRows.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail).Take(maxExamples)],
            [.. fixtureRows.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Pass).Take(maxExamples)]);
    }

    public static string RenderPlain(ReturnToSenderCatalogReportView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var sb = new StringBuilder();
        sb.AppendLine($"RETURNTOSENDER GENERATED FIXTURE FRONTIER over {view.FixtureCount} fixture(s), {view.TargetCount} target(s)");
        sb.AppendLine();
        sb.AppendLine("Fixtures:");
        sb.AppendLine($"  Passed : {view.Fixtures.Passed}");
        sb.AppendLine($"  Skipped: {view.Fixtures.Skipped}");
        sb.AppendLine($"  Failed : {view.Fixtures.Failed}");
        sb.AppendLine();
        sb.AppendLine("Targets:");
        sb.AppendLine($"  Passed : {view.Targets.Passed}");
        sb.AppendLine($"  Skipped: {view.Targets.Skipped}");
        sb.AppendLine($"  Failed : {view.Targets.Failed}");

        AppendResearchEvidence(sb, view.Research);
        AppendActionableSubjects(sb, view.Research?.Summary);
        AppendBuckets(sb, "Roslyn fallback evidence:", view.RoslynFallbacks);
        AppendBuckets(sb, "Skipped target reasons:", view.SkippedTargetReasons);
        AppendBuckets(sb, "Failed target buckets:", view.FailedTargetBuckets);
        AppendFailedFixtures(sb, view);
        AppendPassedFixtures(sb, view);

        return sb.ToString();
    }

    public static string RenderMarkout(ReturnToSenderCatalogReportView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var output = new StringWriter();
        MarkoutSerializer.Serialize(
            BuildMarkoutView(view),
            output,
            new MarkdownFormatter(),
            ReturnToSenderCatalogReportContext.Default,
            new MarkoutWriterOptions());
        return output.ToString();
    }

    static ReturnToSenderCatalogMarkoutView BuildMarkoutView(ReturnToSenderCatalogReportView view)
        => new()
        {
            Summary =
            [
                new("Fixtures", view.Fixtures.Passed, view.Fixtures.Skipped, view.Fixtures.Failed),
                new("Targets", view.Targets.Passed, view.Targets.Skipped, view.Targets.Failed),
            ],
            ResearchEvidence = view.Research is null
                ? null
                : [
                    new("Subjects", view.Research.Comparison.BySubject().Count),
                    new("RTS rows", view.Research.Comparison.Changes.Count(change => change.Mechanism == ResearchChangeMechanism.ReturnToSender)),
                    new("IL rows", view.Research.Comparison.Changes.Count(change => change.Mechanism == ResearchChangeMechanism.IlBody)),
                    .. view.Research.ChangeBuckets.Select(bucket => new ReturnToSenderResearchEvidenceRow(bucket.Key, bucket.Count)),
                ],
            ActionableSubjects = view.Research?.Summary.ActionableSubjects.Count > 0
                ? [.. view.Research.Summary.ActionableSubjects.Select(ActionableRow)]
                : null,
            RoslynFallbacks = view.RoslynFallbacks.Count == 0 ? null : [.. view.RoslynFallbacks.Select(BucketRow)],
            SkippedTargetReasons = view.SkippedTargetReasons.Count == 0 ? null : [.. view.SkippedTargetReasons.Select(BucketRow)],
            FailedTargetBuckets = view.FailedTargetBuckets.Count == 0 ? null : [.. view.FailedTargetBuckets.Select(BucketRow)],
            FailedFixtures = view.FailedFixtures.Count == 0 ? null : [.. view.FailedFixtures.SelectMany(fixture => fixture.Rows.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail).Select(row => FailedRow(fixture.FixtureId, row)))],
            PassedFixtures = view.PassedFixtures.Count == 0 ? null : [.. view.PassedFixtures.Select(fixture => new ReturnToSenderPassedFixtureRow(fixture.FixtureId))],
        };

    static ReturnToSenderActionableRow ActionableRow(ReturnToSenderActionableSubject subject)
        => new(
            subject.SubjectId,
            subject.CompileBackStatus ?? subject.RtsStatus ?? "unknown",
            subject.Detail ?? "-",
            string.Join(", ", subject.ChangeCounts.Select(count => $"{count.ChangeId}: {count.Count}")),
            [.. subject.IlEvidence.SelectMany(evidence =>
            {
                var lines = new List<string>();
                if (evidence.Failure is { } failure)
                    lines.Add($"{evidence.ChangeId}: {failure.UnifiedLine}");
                lines.AddRange(evidence.Rows.Select(row => $"{evidence.ChangeId}: {row.UnifiedLine}"));
                return lines;
            })]);

    static ReturnToSenderBucketRow BucketRow(ReturnToSenderCatalogReportBucket bucket)
        => new(bucket.Key, bucket.Count);

    static ReturnToSenderFailedFixtureRow FailedRow(string fixtureId, GeneratedFixtureReturnToSenderResult row)
    {
        string actual = row.ActualStatus?.ToString() ?? "Missing";
        string closure = row.ClosureEvidence is null
            ? ""
            : ClosureText(row.ClosureEvidence);
        return new ReturnToSenderFailedFixtureRow(
            fixtureId,
            row.DisplayMember,
            actual,
            row.Reason,
            row.MemberAnchor?.StableSelector,
            row.MemberAnchor?.CanonicalSignature,
            closure,
            row.Detail,
            IlDiffLines(row));
    }

    static List<string> IlDiffLines(GeneratedFixtureReturnToSenderResult row)
    {
        if (row.IlDiff is { } typed)
            return [.. IlDiffPrinter.ToUnifiedLines(typed.Diff)];
        return row.IlDiffDiagnostic is null ? [] : [.. IlDiffPrinter.ToUnifiedLines(row.IlDiffDiagnostic)];
    }

    static IReadOnlyList<ReturnToSenderCatalogReportBucket> Buckets(IEnumerable<GeneratedFixtureReturnToSenderResult> rows)
        => [.. rows
            .GroupBy(row => row.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReturnToSenderCatalogReportBucket(group.Key, group.Count()))];

    internal static IReadOnlyList<ReturnToSenderCatalogReportBucket> RoslynFallbackBuckets(IEnumerable<GeneratedFixtureReturnToSenderResult> rows)
        => [.. rows
            .Where(row => row.ClosureEvidence is not null)
            .SelectMany(row => row.ClosureEvidence!.RoslynFallbacks)
            .GroupBy(RoslynFallbackKey, StringComparer.Ordinal)
            .Select(group => new ReturnToSenderCatalogReportBucket(group.Key, group.Sum(fallback => fallback.Count)))
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Key, StringComparer.Ordinal)];

    static string ClosureText(ReturnToSenderClosureEvidence evidence)
    {
        var text = $"types={evidence.RequiredTypes} members={evidence.RequiredMembers} roslyn-types={evidence.RoslynRecoveredTypes} roslyn-member-surfaces={evidence.RoslynRecoveredMemberSurfaces}";
        if (evidence.RoslynFallbacks.Count == 0)
            return text;
        return $"{text} roslyn-fallbacks={string.Join(",", evidence.RoslynFallbacks.Select(fallback => $"{RoslynFallbackKey(fallback)}:{fallback.Count}"))}";
    }

    static string RoslynFallbackKey(ReturnToSenderRoslynFallback fallback)
        => $"{fallback.Diagnostic}/{fallback.Recovery}";

    static void AppendResearchEvidence(StringBuilder sb, ReturnToSenderCatalogResearchSection? research)
    {
        if (research is null)
            return;

        var changes = research.Comparison.Changes;
        sb.AppendLine();
        sb.AppendLine("Research evidence:");
        sb.AppendLine($"  Subjects : {research.Comparison.BySubject().Count}");
        sb.AppendLine($"  RTS rows : {changes.Count(change => change.Mechanism == ResearchChangeMechanism.ReturnToSender)}");
        sb.AppendLine($"  IL rows  : {changes.Count(change => change.Mechanism == ResearchChangeMechanism.IlBody)}");
        foreach (var bucket in research.ChangeBuckets)
            sb.AppendLine($"  {bucket.Key}: {bucket.Count}");
    }

    static void AppendActionableSubjects(StringBuilder sb, ReturnToSenderResearchSummary? summary)
    {
        if (summary is null || summary.ActionableSubjects.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Actionable subjects (first {summary.ActionableSubjects.Count}):");
        foreach (var subject in summary.ActionableSubjects)
        {
            string status = subject.CompileBackStatus ?? subject.RtsStatus ?? "unknown";
            sb.AppendLine($"  {subject.SubjectId}  rts={status}  detail={subject.Detail ?? "-"}");
            foreach (var count in subject.ChangeCounts)
                sb.AppendLine($"      {count.ChangeId}: {count.Count}");
            if (subject.IlEvidence.Count == 0)
                continue;

            sb.AppendLine("      il-display:");
            foreach (var evidence in subject.IlEvidence)
            {
                if (evidence.Failure is { } failure)
                    sb.AppendLine($"        {evidence.ChangeId}: {failure.UnifiedLine}");
                foreach (var row in evidence.Rows)
                    sb.AppendLine($"        {evidence.ChangeId}: {row.UnifiedLine}");
            }
        }
    }

    static void AppendBuckets(StringBuilder sb, string title, IReadOnlyList<ReturnToSenderCatalogReportBucket> buckets)
    {
        if (buckets.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine(title);
        foreach (var bucket in buckets)
            sb.AppendLine($"  {bucket.Key}: {bucket.Count}");
    }

    static void AppendFailedFixtures(StringBuilder sb, ReturnToSenderCatalogReportView view)
    {
        if (view.FailedFixtures.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Failed fixtures (first {view.FailedFixtures.Count} of {view.Fixtures.Failed}):");
        foreach (var fixture in view.FailedFixtures)
        {
            sb.AppendLine($"  {fixture.FixtureId}");
            foreach (var row in fixture.Rows.Where(row => row.Status == GeneratedFixtureReturnToSenderStatus.Fail))
                AppendFailedRow(sb, row);
        }
    }

    static void AppendFailedRow(StringBuilder sb, GeneratedFixtureReturnToSenderResult row)
    {
        string actual = row.ActualStatus?.ToString() ?? "Missing";
        sb.AppendLine($"      {row.DisplayMember}  rts={actual}  bucket={row.Reason}");
        if (row.MemberAnchor is not null)
            sb.AppendLine($"      member: {row.MemberAnchor.StableSelector}  canonical={row.MemberAnchor.CanonicalSignature}");
        if (row.ClosureEvidence is not null)
        {
            sb.AppendLine(
                $"      closure: {ClosureText(row.ClosureEvidence)}");
        }
        if (!string.IsNullOrWhiteSpace(row.Detail))
            sb.AppendLine($"      detail: {row.Detail}");
        if (row.IlDiffDiagnostic is null)
            return;

        sb.AppendLine("      il-diff:");
        foreach (string line in IlDiffPrinter.ToUnifiedLines(row.IlDiffDiagnostic))
            sb.AppendLine($"        {line}");
    }

    static void AppendPassedFixtures(StringBuilder sb, ReturnToSenderCatalogReportView view)
    {
        if (view.PassedFixtures.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Passed fixtures (first {view.PassedFixtures.Count} of {view.Fixtures.Passed}):");
        foreach (var fixture in view.PassedFixtures)
            sb.AppendLine($"  {fixture.FixtureId}");
    }
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
internal sealed class ReturnToSenderCatalogMarkoutView
{
    [MarkoutIgnore]
    public string Title => "ReturnToSender Catalog";

    [MarkoutSection(Name = "Summary")]
    public List<ReturnToSenderSummaryRow>? Summary { get; init; }

    [MarkoutSection(Name = "Research evidence", EmptyText = "None")]
    public List<ReturnToSenderResearchEvidenceRow>? ResearchEvidence { get; init; }

    [MarkoutSection(Name = "Actionable subjects", EmptyText = "None")]
    public List<ReturnToSenderActionableRow>? ActionableSubjects { get; init; }

    [MarkoutSection(Name = "Roslyn fallback evidence", EmptyText = "None")]
    public List<ReturnToSenderBucketRow>? RoslynFallbacks { get; init; }

    [MarkoutSection(Name = "Skipped target reasons", EmptyText = "None")]
    public List<ReturnToSenderBucketRow>? SkippedTargetReasons { get; init; }

    [MarkoutSection(Name = "Failed target buckets", EmptyText = "None")]
    public List<ReturnToSenderBucketRow>? FailedTargetBuckets { get; init; }

    [MarkoutSection(Name = "Failed fixtures", EmptyText = "None")]
    public List<ReturnToSenderFailedFixtureRow>? FailedFixtures { get; init; }

    [MarkoutSection(Name = "Passed fixtures", EmptyText = "None")]
    public List<ReturnToSenderPassedFixtureRow>? PassedFixtures { get; init; }
}

[MarkoutSerializable]
internal sealed record ReturnToSenderSummaryRow(string Scope, int Passed, int Skipped, int Failed);

[MarkoutSerializable]
internal sealed record ReturnToSenderResearchEvidenceRow(string Metric, int Count);

[MarkoutSerializable]
internal sealed record ReturnToSenderActionableRow(
    string Subject,
    string Status,
    string Detail,
    string Changes,
    [property: MarkoutPropertyName("IL display")] List<string> IlDisplay);

[MarkoutSerializable]
internal sealed record ReturnToSenderBucketRow(string Bucket, int Count);

[MarkoutSerializable]
internal sealed record ReturnToSenderFailedFixtureRow(
    string Fixture,
    string Member,
    string Status,
    string Bucket,
    [property: MarkoutPropertyName("Stable selector")] string? StableSelector,
    [property: MarkoutPropertyName("Canonical signature")] string? CanonicalSignature,
    string? Closure,
    string? Detail,
    [property: MarkoutPropertyName("IL diff")] List<string> IlDiff);

[MarkoutSerializable]
internal sealed record ReturnToSenderPassedFixtureRow(string Fixture);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(ReturnToSenderCatalogMarkoutView))]
[MarkoutContext(typeof(ReturnToSenderSummaryRow))]
[MarkoutContext(typeof(ReturnToSenderResearchEvidenceRow))]
[MarkoutContext(typeof(ReturnToSenderActionableRow))]
[MarkoutContext(typeof(ReturnToSenderBucketRow))]
[MarkoutContext(typeof(ReturnToSenderFailedFixtureRow))]
[MarkoutContext(typeof(ReturnToSenderPassedFixtureRow))]
internal sealed partial class ReturnToSenderCatalogReportContext : MarkoutSerializerContext
{
}
