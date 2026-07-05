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
        return new ResearchDiffResult(subjects);
    }

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

        if (diagnostic.Failure is { Length: > 0 } failure)
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
                Category: ResearchDiffChangeCategory.IlBody);
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
