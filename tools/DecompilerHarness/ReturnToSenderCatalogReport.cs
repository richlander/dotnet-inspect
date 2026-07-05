using System.Text;
using ILInspector.Instructions;
using ILInspector.Research;

namespace ILInspector.DecompilerHarness;

internal sealed record ReturnToSenderCatalogReportCounts(int Passed, int Skipped, int Failed);

internal sealed record ReturnToSenderCatalogReportBucket(string Key, int Count);

internal sealed record ReturnToSenderCatalogFixtureGroup(
    string FixtureId,
    GeneratedFixtureReturnToSenderStatus Status,
    IReadOnlyList<GeneratedFixtureReturnToSenderResult> Rows);

internal sealed record ReturnToSenderCatalogResearchSection(
    ResearchDiffResult Diff,
    ReturnToSenderResearchSummary Summary,
    IReadOnlyList<ReturnToSenderCatalogReportBucket> ChangeBuckets);

internal sealed record ReturnToSenderCatalogReportView(
    int FixtureCount,
    int TargetCount,
    ReturnToSenderCatalogReportCounts Fixtures,
    ReturnToSenderCatalogReportCounts Targets,
    ReturnToSenderCatalogResearchSection? Research,
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

        var research = ReturnToSenderEvidence.ToResearchDiff(ReturnToSenderEvidence.FromCatalog(run));
        var researchEvidence = research.Subjects.SelectMany(subject => subject.Evidence).ToArray();
        var researchSection = researchEvidence.Length == 0
            ? null
            : new ReturnToSenderCatalogResearchSection(
                research,
                ReturnToSenderEvidence.Summarize(research, maxExamples),
                [.. researchEvidence
                    .GroupBy(row => row.ChangeId, StringComparer.Ordinal)
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
        AppendBuckets(sb, "Skipped target reasons:", view.SkippedTargetReasons);
        AppendBuckets(sb, "Failed target buckets:", view.FailedTargetBuckets);
        AppendFailedFixtures(sb, view);
        AppendPassedFixtures(sb, view);

        return sb.ToString();
    }

    static IReadOnlyList<ReturnToSenderCatalogReportBucket> Buckets(IEnumerable<GeneratedFixtureReturnToSenderResult> rows)
        => [.. rows
            .GroupBy(row => row.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReturnToSenderCatalogReportBucket(group.Key, group.Count()))];

    static void AppendResearchEvidence(StringBuilder sb, ReturnToSenderCatalogResearchSection? research)
    {
        if (research is null)
            return;

        var evidence = research.Diff.Subjects.SelectMany(subject => subject.Evidence).ToArray();
        sb.AppendLine();
        sb.AppendLine("Research evidence:");
        sb.AppendLine($"  Subjects : {research.Diff.Subjects.Count}");
        sb.AppendLine($"  RTS rows : {evidence.Count(row => row.Mechanism == ResearchDiffMechanism.ReturnToSender)}");
        sb.AppendLine($"  IL rows  : {evidence.Count(row => row.Mechanism == ResearchDiffMechanism.IlBody)}");
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
                $"      closure: types={row.ClosureEvidence.RequiredTypes} members={row.ClosureEvidence.RequiredMembers} " +
                $"roslyn-types={row.ClosureEvidence.RoslynRecoveredTypes} roslyn-member-surfaces={row.ClosureEvidence.RoslynRecoveredMemberSurfaces}");
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
