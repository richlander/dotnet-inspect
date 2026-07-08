using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

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

public sealed record ImplementationMemberDiffResult(
    ResearchSubjectKey Subject,
    CSharpBodyDiffResult? CSharpDiff,
    IlMemberDiffResult? IlDiff,
    IReadOnlyList<ImplementationDiffEvidence> Evidence)
{
    public bool HasCSharpEvidence => Evidence.Any(evidence => evidence.Kind == ImplementationDiffEvidenceKind.CSharp);
    public bool HasIlEvidence => Evidence.Any(evidence => evidence.Kind == ImplementationDiffEvidenceKind.IlBody);
    public bool IsExact
        => Evidence.Count == 0
           && (CSharpDiff is null || CSharpDiff.IsExact)
           && (IlDiff is null || IlDiff.Diff.IsExact);
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

    public static ImplementationMemberDiffResult CompareMembers(
        MetadataSource oldSource,
        MethodDefinitionHandle oldMethod,
        MetadataSource newSource,
        MethodDefinitionHandle newMethod,
        ImplementationDiffMechanism mechanisms = ImplementationDiffMechanism.All,
        ResearchSubjectKey? subject = null)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        if (oldMethod.IsNil)
            throw new ArgumentException("Old method handle must not be nil.", nameof(oldMethod));
        if (newMethod.IsNil)
            throw new ArgumentException("New method handle must not be nil.", nameof(newMethod));

        subject ??= SubjectFromMethod(oldSource, oldMethod);
        CSharpBodyDiffResult? csharpDiff = null;
        IlMemberDiffResult? ilDiff = null;
        var evidence = ImmutableArray.CreateBuilder<ImplementationDiffEvidence>();

        if (mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
        {
            csharpDiff = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
            evidence.AddRange(ToCSharpImplementationEvidence(csharpDiff));
        }

        if (mechanisms.HasFlag(ImplementationDiffMechanism.IlBody))
        {
            string label = subject.TypeName is { Length: > 0 } typeName && subject.MemberName is { Length: > 0 } memberName
                ? $"{typeName}::{memberName}"
                : subject.Display;
            ilDiff = IlAssemblyDiff.CompareMembers(
                oldSource.Pe,
                oldSource.Reader,
                oldMethod,
                newSource.Pe,
                newSource.Reader,
                newMethod,
                oldLabel: label,
                newLabel: label);
            evidence.AddRange(ToIlEvidence(ilDiff).Select(item => ToImplementationEvidence(item)!));
        }

        return new ImplementationMemberDiffResult(subject, csharpDiff, ilDiff, evidence.ToImmutable());
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
        if (diff is not { } typed)
            return fallbackDisplay is null ? [] : ToIlEvidence(fallbackDisplay);

        var typedEvidence = ToIlEvidence(IlDiffPrinter.ToDisplayResult(typed.Diff), typed);
        return typedEvidence.IsEmpty && fallbackDisplay is not null
            ? ToIlEvidence(fallbackDisplay)
            : typedEvidence;
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

    static ImmutableArray<ImplementationDiffEvidence> ToCSharpImplementationEvidence(CSharpBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.IsExact)
            return [];

        var evidence = ImmutableArray.CreateBuilder<ImplementationDiffEvidence>();
        foreach (var failure in diff.FailureRows.IsDefault ? [] : diff.FailureRows)
        {
            var direction = failure.Kind switch
            {
                CSharpDiffFailureKind.OldBodyMissing => ResearchDiffDirection.Added,
                CSharpDiffFailureKind.NewBodyMissing => ResearchDiffDirection.Removed,
                _ => ResearchDiffDirection.Changed,
            };
            evidence.Add(new ImplementationDiffEvidence(
                ImplementationDiffEvidenceKind.CSharp,
                $"csharp.diff.{ResearchDiff.ToChangeIdPart(failure.Kind.ToString())}",
                direction,
                OldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
                NewValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
                Detail: failure.Detail ?? failure.Message,
                CSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(failure)));
        }

        foreach (var row in diff.Rows.IsDefault ? [] : diff.Rows)
        {
            var direction = row.Kind switch
            {
                CSharpDiffKind.Add => ResearchDiffDirection.Added,
                CSharpDiffKind.Remove => ResearchDiffDirection.Removed,
                _ => ResearchDiffDirection.Changed,
            };
            evidence.Add(new ImplementationDiffEvidence(
                ImplementationDiffEvidenceKind.CSharp,
                row.ChangeId,
                direction,
                OldValue: row.OldOperation?.Value ?? row.OldValue ?? (direction == ResearchDiffDirection.Removed ? row.Text : null),
                NewValue: row.NewOperation?.Value ?? row.NewValue ?? (direction == ResearchDiffDirection.Added ? row.Text : null),
                Detail: row.Message,
                CSharpDisplayRows: [CSharpDiffPrinter.ToDisplayRow(row)]));
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

    static ResearchSubjectKey SubjectFromMethod(MetadataSource source, MethodDefinitionHandle methodHandle)
    {
        var reader = source.Reader;
        var method = reader.GetMethodDefinition(methodHandle);
        var typeHandle = method.GetDeclaringType();
        var type = reader.GetTypeDefinition(typeHandle);
        var anchor = ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method, IsExtensionMethod(reader, type, method));
        string typeFullName = reader.GetFullTypeName(type);
        string memberName = reader.GetString(method.Name);
        return ResearchMemberIdentity.SubjectFromAnchor(anchor, $"{typeFullName}.{memberName}");
    }

    static bool IsExtensionMethod(MetadataReader reader, TypeDefinition type, MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
           && type.Attributes.HasFlag(TypeAttributes.Sealed)
           && method.Attributes.HasFlag(MethodAttributes.Static)
           && AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
}
