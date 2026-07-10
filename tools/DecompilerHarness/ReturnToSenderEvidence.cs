using ILInspector.Instructions;
using ILInspector.Findings;
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
    IlDiffDisplayResult? IlDiffDiagnostic,
    IlMemberDiffResult? IlDiff)
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
    int MembersWithIlChanges,
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
            result.IlDiffDiagnostic,
            result.IlDiff))];
    }

    public static ResearchComparison ToResearchComparison(IEnumerable<ReturnToSenderEvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new ResearchComparison(
        [
            .. rows.SelectMany(row => Changes(row, SubjectKey(row)))
        ]);
    }

    public static ReturnToSenderResearchSummary Summarize(ResearchComparison research, int maxSubjects)
    {
        ArgumentNullException.ThrowIfNull(research);
        var subjects = research.BySubject();
        var changes = research.Changes;
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
            changes.Count(item => item.Mechanism == ResearchChangeMechanism.ReturnToSender),
            changes.Count(item => item.Mechanism == ResearchChangeMechanism.IlBody),
            subjects.Count(subject => HasRtsStatus(subject, "rts.status.fail")),
            subjects.Count(subject => HasCompileBackStatus(subject, "OpcodeDiff")),
            subjects.Count(subject => HasCompileBackStatus(subject, "RecompileFail")),
            subjects.Count(subject => HasCompileBackStatus(subject, "ContextFail")),
            subjects.Count(subject => subject.Changes.Any(item => item.Mechanism == ResearchChangeMechanism.IlBody)),
            actionable);
    }

    static ReturnToSenderActionableSubject? ActionableSubject(ResearchSubjectChanges subject)
    {
        var rts = subject.Changes.FirstOrDefault(item => item.Mechanism == ResearchChangeMechanism.ReturnToSender);
        if (rts is null || rts.Descriptor.Id == "rts.status.pass")
            return null;

        var changeCounts = subject.Changes
            .Where(item => item.Mechanism != ResearchChangeMechanism.ReturnToSender)
            .GroupBy(item => item.Descriptor.Id, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReturnToSenderChangeCount(group.Key, group.Count()))
            .ToArray();
        var ilEvidence = subject.Changes
            .Where(item => item.Mechanism == ResearchChangeMechanism.IlBody
                && (item.IlDisplayFailureRow is not null || !item.IlDisplayRows.IsDefaultOrEmpty))
            .Select(item => new ReturnToSenderIlEvidence(
                item.Descriptor.Id,
                item.IlDisplayFailureRow,
                item.IlDisplayRows.IsDefault ? [] : item.IlDisplayRows.ToArray()))
            .ToArray();
        return new ReturnToSenderActionableSubject(
            subject.Subject.Id,
            subject.Subject.Display,
            rts.Descriptor.Id,
            rts.NewValue,
            rts.Detail,
            changeCounts,
            ilEvidence);
    }

    static bool HasRtsStatus(ResearchSubjectChanges subject, string descriptorId)
        => subject.Changes.Any(item =>
            item.Mechanism == ResearchChangeMechanism.ReturnToSender
            && item.Descriptor.Id == descriptorId);

    static bool HasCompileBackStatus(ResearchSubjectChanges subject, string status)
        => subject.Changes.Any(item =>
            item.Mechanism == ResearchChangeMechanism.ReturnToSender
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
            return ResearchMemberIdentity.SubjectFromAnchor(anchor, $"{anchor.TypeFullName}.{anchor.MemberName}");

        return new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            $"rts:{row.DisplayMember}",
            $"{row.Type}.{row.Method}",
            row.Type,
            row.Method);
    }

    static IEnumerable<ResearchChange> Changes(
        ReturnToSenderEvidenceRow row,
        ResearchSubjectKey subject)
    {
        string descriptorId = $"rts.status.{StatusId(row.Status)}";
        yield return new ResearchChange(
            subject,
            ResearchChangeMechanism.ReturnToSender,
            new FindingDescriptor(descriptorId, "Return to sender status"),
            ResearchChangeKind.Changed,
            newValue: row.CompileBackStatus?.ToString(),
            detail: row.Detail ?? row.Reason,
            category: ResearchChangeCategory.RoundTrip);

        foreach (var change in ImplementationDiff.ToIlChanges(row.IlDiff, subject, row.IlDiffDiagnostic))
            yield return change;
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
