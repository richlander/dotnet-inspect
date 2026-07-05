using ILInspector.Instructions;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace ILInspector.DecompilerHarness;

internal sealed record ReturnToSenderEvidenceRow(
    MemberAnchor? Anchor,
    string Type,
    string Method,
    int Overload,
    GeneratedFixtureReturnToSenderStatus Status,
    FidelityCheck.CompileBackStatus? CompileBackStatus,
    string Reason,
    string? Detail,
    IlDiffDisplayResult? IlDiffDiagnostic)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record ReturnToSenderResearchSummary(
    int Subjects,
    int RtsRows,
    int IlRows,
    int FailingMembers,
    int OpcodeDiffMembers,
    int RecompileFailMembers,
    int ContextFailMembers,
    int MembersWithIlEvidence,
    IReadOnlyList<ReturnToSenderActionableSubject> ActionableSubjects);

internal sealed record ReturnToSenderActionableSubject(
    string SubjectId,
    string Display,
    string? RtsStatus,
    string? CompileBackStatus,
    string? Detail,
    IReadOnlyList<ReturnToSenderChangeCount> ChangeCounts,
    IReadOnlyList<ReturnToSenderIlEvidence> IlEvidence);

internal sealed record ReturnToSenderChangeCount(string ChangeId, int Count);

internal sealed record ReturnToSenderIlEvidence(
    string ChangeId,
    IlDiffDisplayFailureRow? Failure,
    IReadOnlyList<IlDiffDisplayRow> Rows);

internal static class ReturnToSenderEvidence
{
    public static IReadOnlyList<ReturnToSenderEvidenceRow> FromCatalog(GeneratedFixtureReturnToSenderRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return [.. run.Results.Select(result => new ReturnToSenderEvidenceRow(
            result.MemberAnchor,
            result.Type,
            result.Method,
            result.Overload,
            result.Status,
            result.ActualStatus,
            result.Reason,
            result.Detail,
            result.IlDiffDiagnostic))];
    }

    public static ResearchDiffResult ToResearchDiff(IEnumerable<ReturnToSenderEvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var subjects = rows
            .GroupBy(SubjectKey)
            .Select(group => new ResearchSubjectDiff(group.Key, [.. group.SelectMany(Evidence)]))
            .Where(subject => subject.Evidence.Count > 0)
            .ToArray();
        return new ResearchDiffResult(subjects, Rows: []);
    }

    public static ReturnToSenderResearchSummary Summarize(ResearchDiffResult research, int maxSubjects)
    {
        ArgumentNullException.ThrowIfNull(research);
        var subjects = research.Subjects;
        var evidence = subjects.SelectMany(subject => subject.Evidence).ToArray();
        var actionable = subjects
            .Select(ActionableSubject)
            .Where(subject => subject is not null)
            .Select(subject => subject!)
            .OrderBy(subject => Rank(subject))
            .ThenBy(subject => subject.SubjectId, StringComparer.Ordinal)
            .Take(Math.Max(0, maxSubjects))
            .ToArray();

        return new ReturnToSenderResearchSummary(
            subjects.Count,
            evidence.Count(item => item.Mechanism == ResearchDiffMechanism.ReturnToSender),
            evidence.Count(item => item.Mechanism == ResearchDiffMechanism.IlBody),
            subjects.Count(subject => HasRtsStatus(subject, "rts.status.fail")),
            subjects.Count(subject => HasCompileBackStatus(subject, "OpcodeDiff")),
            subjects.Count(subject => HasCompileBackStatus(subject, "RecompileFail")),
            subjects.Count(subject => HasCompileBackStatus(subject, "ContextFail")),
            subjects.Count(subject => subject.Evidence.Any(item => item.Mechanism == ResearchDiffMechanism.IlBody)),
            actionable);
    }

    static ReturnToSenderActionableSubject? ActionableSubject(ResearchSubjectDiff subject)
    {
        var rts = subject.Evidence.FirstOrDefault(item => item.Mechanism == ResearchDiffMechanism.ReturnToSender);
        if (rts is null || rts.ChangeId == "rts.status.pass")
            return null;

        var changeCounts = subject.Evidence
            .Where(item => item.Mechanism != ResearchDiffMechanism.ReturnToSender)
            .GroupBy(item => item.ChangeId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReturnToSenderChangeCount(group.Key, group.Count()))
            .ToArray();
        var ilEvidence = subject.Evidence
            .Where(item => item.Mechanism == ResearchDiffMechanism.IlBody
                && (item.IlDisplayFailureRow is not null || !item.IlDisplayRows.IsDefaultOrEmpty))
            .Select(item => new ReturnToSenderIlEvidence(
                item.ChangeId,
                item.IlDisplayFailureRow,
                item.IlDisplayRows.IsDefault ? [] : item.IlDisplayRows.ToArray()))
            .ToArray();
        return new ReturnToSenderActionableSubject(
            subject.Subject.Id,
            subject.Subject.Display,
            rts.ChangeId,
            rts.NewValue,
            rts.Detail,
            changeCounts,
            ilEvidence);
    }

    static bool HasRtsStatus(ResearchSubjectDiff subject, string changeId)
        => subject.Evidence.Any(item => item.Mechanism == ResearchDiffMechanism.ReturnToSender && item.ChangeId == changeId);

    static bool HasCompileBackStatus(ResearchSubjectDiff subject, string status)
        => subject.Evidence.Any(item =>
            item.Mechanism == ResearchDiffMechanism.ReturnToSender
            && string.Equals(item.NewValue, status, StringComparison.Ordinal));

    static int Rank(ReturnToSenderActionableSubject subject)
        => subject.CompileBackStatus switch
        {
            "RecompileFail" => 0,
            "ContextFail" => 1,
            "OpcodeDiff" => 2,
            _ => 3,
        };

    static ResearchSubjectKey SubjectKey(ReturnToSenderEvidenceRow row)
    {
        if (row.Anchor is { } anchor)
        {
            return new ResearchSubjectKey(
                ResearchDiffSubjectKind.Member,
                anchor.StableSelector,
                $"{anchor.TypeFullName}.{anchor.MemberName}",
                anchor.TypeFullName,
                anchor.MemberName);
        }

        return new ResearchSubjectKey(
            ResearchDiffSubjectKind.Member,
            $"rts:{row.DisplayMember}",
            $"{row.Type}.{row.Method}",
            row.Type,
            row.Method);
    }

    static IEnumerable<ResearchDiffEvidence> Evidence(ReturnToSenderEvidenceRow row)
    {
        yield return new ResearchDiffEvidence(
            ResearchDiffMechanism.ReturnToSender,
            $"rts.status.{StatusId(row.Status)}",
            ResearchDiffDirection.Changed,
            OldValue: null,
            NewValue: row.CompileBackStatus?.ToString(),
            Detail: row.Detail ?? row.Reason,
            Category: ResearchDiffChangeCategory.RoundTrip);

        if (row.IlDiffDiagnostic is not { } diagnostic)
            yield break;

        var failureRows = diagnostic.FailureRows.IsDefault
            ? []
            : diagnostic.FailureRows;
        foreach (var failureRow in failureRows)
        {
            yield return new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                $"il.diff.{ResearchDiff.ToChangeIdPart(failureRow.Kind.ToString())}",
                ResearchDiffDirection.Changed,
                Detail: failureRow.Detail ?? failureRow.Message,
                Category: ResearchDiffChangeCategory.IlBody,
                IlDisplayFailureRow: failureRow);
        }

        if (failureRows.IsDefaultOrEmpty && diagnostic.Failure is { Length: > 0 } failure)
        {
            yield return new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                "il.diff.failed",
                ResearchDiffDirection.Changed,
                Detail: failure,
                Category: ResearchDiffChangeCategory.IlBody);
        }

        var displayRows = diagnostic.Rows.IsDefault
            ? []
            : diagnostic.Rows;
        foreach (var displayRow in displayRows)
        {
            if (displayRow.Kind == IlDiffKind.Context)
                continue;

            var direction = displayRow.Kind == IlDiffKind.Add
                ? ResearchDiffDirection.Added
                : ResearchDiffDirection.Removed;
            yield return new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                displayRow.Kind == IlDiffKind.Add ? "il.operation.added" : "il.operation.removed",
                direction,
                OldValue: displayRow.Kind == IlDiffKind.Remove ? displayRow.Operation : null,
                NewValue: displayRow.Kind == IlDiffKind.Add ? displayRow.Operation : null,
                OldIlOffset: displayRow.Kind == IlDiffKind.Remove ? displayRow.RawOffset : null,
                NewIlOffset: displayRow.Kind == IlDiffKind.Add ? displayRow.RawOffset : null,
                Detail: displayRow.Message,
                Category: ResearchDiffChangeCategory.IlBody,
                IlDisplayRows: [displayRow]);
        }
    }

    static string StatusId(GeneratedFixtureReturnToSenderStatus status)
        => status switch
        {
            GeneratedFixtureReturnToSenderStatus.Pass => "pass",
            GeneratedFixtureReturnToSenderStatus.Skip => "skip",
            GeneratedFixtureReturnToSenderStatus.Fail => "fail",
            _ => status.ToString().ToLowerInvariant(),
        };

}
