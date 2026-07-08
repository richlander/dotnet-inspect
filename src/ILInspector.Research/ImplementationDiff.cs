using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Instructions;

namespace ILInspector.Research;

[Flags]
public enum ImplementationDiffMechanism
{
    None = 0,
    CSharp = 1,
    IlBody = 2,
    All = CSharp | IlBody,
}

public enum ImplementationDiffEvidenceKind
{
    CSharp,
    IlBody,
}

public sealed record ImplementationDiffOptions(
    ImplementationDiffMechanism Mechanisms = ImplementationDiffMechanism.All,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

public sealed record ImplementationDiffResult(
    IReadOnlyList<ImplementationDiffMember> Members,
    ResearchDiffResult ResearchDiff)
{
    public bool IsEmpty => Members.Count == 0;
}

public sealed record ImplementationDiffMember(
    ResearchSubjectKey Subject,
    IReadOnlyList<ImplementationDiffEvidence> Evidence)
{
    public bool HasCSharpEvidence => Evidence.Any(evidence => evidence.Kind == ImplementationDiffEvidenceKind.CSharp);
    public bool HasIlEvidence => Evidence.Any(evidence => evidence.Kind == ImplementationDiffEvidenceKind.IlBody);
}

public sealed record ImplementationDiffEvidence(
    ImplementationDiffEvidenceKind Kind,
    string ChangeId,
    ResearchDiffDirection Direction,
    string? OldValue = null,
    string? NewValue = null,
    string? Detail = null,
    int? OldIlOffset = null,
    int? NewIlOffset = null,
    ImmutableArray<CSharpDiffDisplayRow> CSharpDisplayRows = default,
    CSharpDiffDisplayFailureRow? CSharpDisplayFailureRow = null,
    ImmutableArray<IlDiffDisplayRow> IlDisplayRows = default,
    IlDiffDisplayFailureRow? IlDisplayFailureRow = null,
    IlMemberDiffResult? IlMemberDiff = null)
{
    public ImmutableArray<string> UnifiedLines
    {
        get
        {
            var lines = ImmutableArray.CreateBuilder<string>();
            if (CSharpDisplayFailureRow is not null)
                lines.Add(CSharpDisplayFailureRow.UnifiedLine);
            if (!CSharpDisplayRows.IsDefaultOrEmpty)
                lines.AddRange(CSharpDisplayRows.Select(row => row.UnifiedLine));
            if (IlDisplayFailureRow is not null)
                lines.Add(IlDisplayFailureRow.UnifiedLine);
            if (!IlDisplayRows.IsDefaultOrEmpty)
                lines.AddRange(IlDisplayRows.Select(row => row.UnifiedLine));
            return lines.ToImmutable();
        }
    }
}

/// <summary>
/// Product-owned implementation diff projection that joins C# source-shape and
/// IL/body evidence by Research member identity.
/// </summary>
public static class ImplementationDiff
{
    public static ImplementationDiffResult CompareAssemblies(
        string oldAssemblyPath,
        string newAssemblyPath,
        ImplementationDiffOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newAssemblyPath);
        return Compare(
            ResearchDiffInput.FromAssembly(oldAssemblyPath),
            ResearchDiffInput.FromAssembly(newAssemblyPath),
            options);
    }

    public static ImplementationDiffResult Compare(
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldInput);
        ArgumentNullException.ThrowIfNull(newInput);

        options ??= new ImplementationDiffOptions();
        var research = ResearchDiff.Compare(oldInput, newInput, new ResearchDiffOptions(
            ToResearchMechanisms(options.Mechanisms),
            TypeFilters: options.TypeFilters,
            MemberTargetIdentities: options.MemberTargetIdentities));
        return FromResearchDiff(research, options);
    }

    public static ImplementationDiffResult FromResearchDiff(
        ResearchDiffResult research,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        options ??= new ImplementationDiffOptions();

        var members = research.MembersWhere(member => member.ImplementationChanged)
            .Select(member => new ImplementationDiffMember(
                member.Subject,
                [.. member.Evidence.Select(ToImplementationEvidence).Where(evidence => evidence is not null).Select(evidence => evidence!)]))
            .Where(member => member.Evidence.Count > 0)
            .Where(member => ResearchDiff.MatchesTypeFilters(member.Subject.TypeName ?? "", options.TypeFilters))
            .Where(member => MatchesMemberTargets(member.Subject, options.MemberTargetIdentities))
            .ToArray();

        return new ImplementationDiffResult(members, research);
    }

    public static ImmutableArray<ResearchDiffEvidence> ToIlEvidence(
        IlMemberDiffResult? diff,
        IlDiffDisplayResult? fallbackDisplay = null)
    {
        var display = diff is { } typed
            ? IlDiffPrinter.ToDisplayResult(typed.Diff)
            : fallbackDisplay;
        return display is null
            ? []
            : ToIlEvidence(display, diff);
    }

    public static ImmutableArray<ResearchDiffEvidence> ToIlEvidence(
        IlDiffDisplayResult display,
        IlMemberDiffResult? diff = null)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (display.IsEmpty)
            return [];

        var evidence = ImmutableArray.CreateBuilder<ResearchDiffEvidence>();
        var failureRows = display.FailureRows.IsDefault ? [] : display.FailureRows;
        foreach (var failureRow in failureRows)
        {
            evidence.Add(new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                $"il.diff.{ResearchDiff.ToChangeIdPart(failureRow.Kind.ToString())}",
                ResearchDiffDirection.Changed,
                Detail: failureRow.Detail ?? failureRow.Message,
                Category: ResearchDiffChangeCategory.IlBody,
                IlDisplayFailureRow: failureRow,
                IlMemberDiff: diff));
        }

        if (failureRows.IsDefaultOrEmpty && display.Failure is { Length: > 0 } failure)
        {
            evidence.Add(new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                "il.diff.failed",
                ResearchDiffDirection.Changed,
                Detail: failure,
                Category: ResearchDiffChangeCategory.IlBody,
                IlMemberDiff: diff));
        }

        var displayRows = display.Rows.IsDefault ? [] : display.Rows;
        foreach (var displayRow in displayRows)
        {
            if (displayRow.Kind == IlDiffKind.Context)
                continue;

            var direction = displayRow.Kind == IlDiffKind.Add
                ? ResearchDiffDirection.Added
                : ResearchDiffDirection.Removed;
            evidence.Add(new ResearchDiffEvidence(
                ResearchDiffMechanism.IlBody,
                displayRow.Kind == IlDiffKind.Add ? "il.operation.added" : "il.operation.removed",
                direction,
                OldValue: displayRow.Kind == IlDiffKind.Remove ? displayRow.Operation : null,
                NewValue: displayRow.Kind == IlDiffKind.Add ? displayRow.Operation : null,
                OldIlOffset: displayRow.Kind == IlDiffKind.Remove ? displayRow.RawOffset : null,
                NewIlOffset: displayRow.Kind == IlDiffKind.Add ? displayRow.RawOffset : null,
                Detail: displayRow.Message,
                Category: ResearchDiffChangeCategory.IlBody,
                IlDisplayRows: [displayRow],
                IlMemberDiff: diff));
        }

        return evidence.ToImmutable();
    }

    static ResearchDiffMechanism ToResearchMechanisms(ImplementationDiffMechanism mechanisms)
    {
        var research = ResearchDiffMechanism.None;
        if (mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
            research |= ResearchDiffMechanism.CSharp;
        if (mechanisms.HasFlag(ImplementationDiffMechanism.IlBody))
            research |= ResearchDiffMechanism.IlBody;
        return research;
    }

    static ImplementationDiffEvidence? ToImplementationEvidence(ResearchDiffEvidence evidence)
        => evidence.Mechanism switch
        {
            ResearchDiffMechanism.CSharp => new ImplementationDiffEvidence(
                ImplementationDiffEvidenceKind.CSharp,
                evidence.ChangeId,
                evidence.Direction,
                evidence.OldValue,
                evidence.NewValue,
                evidence.Detail,
                CSharpDisplayRows: evidence.CSharpDisplayRows.IsDefault ? [] : evidence.CSharpDisplayRows,
                CSharpDisplayFailureRow: evidence.CSharpDisplayFailureRow),
            ResearchDiffMechanism.IlBody => new ImplementationDiffEvidence(
                ImplementationDiffEvidenceKind.IlBody,
                evidence.ChangeId,
                evidence.Direction,
                evidence.OldValue,
                evidence.NewValue,
                evidence.Detail,
                evidence.OldIlOffset,
                evidence.NewIlOffset,
                IlDisplayRows: evidence.IlDisplayRows.IsDefault ? [] : evidence.IlDisplayRows,
                IlDisplayFailureRow: evidence.IlDisplayFailureRow,
                IlMemberDiff: evidence.IlMemberDiff),
            _ => null,
        };

    static bool MatchesMemberTargets(ResearchSubjectKey subject, IReadOnlySet<string>? memberTargetIdentities)
        => memberTargetIdentities is null
           || memberTargetIdentities.Count == 0
           || memberTargetIdentities.Contains(subject.Id);
}
