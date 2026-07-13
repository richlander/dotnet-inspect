using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
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

public sealed record ImplementationDiffOptions(
    ImplementationDiffMechanism Mechanisms = ImplementationDiffMechanism.All,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

public sealed record ImplementationDiffResult(
    IReadOnlyList<ImplementationDiffMember> Members,
    ResearchComparison Research)
{
    public bool IsEmpty => Members.Count == 0;
}

public sealed record ImplementationDiffMember(
    ResearchSubjectKey Subject,
    IReadOnlyList<ResearchChange> Changes)
{
    public bool HasCSharpChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.CSharp);

    public bool HasIlChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.IlBody);
}

public sealed record ImplementationMemberDiffResult(
    ResearchSubjectKey Subject,
    CSharpBodyDiffResult? CSharpDiff,
    IlMemberDiffResult? IlDiff,
    IReadOnlyList<ResearchChange> Changes,
    RetainedFindingComparisonSet RetainedComparisons)
{
    public bool HasCSharpChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.CSharp);

    public bool HasIlChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.IlBody);

    public bool IsExact
        => Changes.Count == 0
           && (CSharpDiff is null || CSharpDiff.IsExact)
           && (IlDiff is null || IlDiff.Diff.IsExact)
           && RetainedComparisons.Items.All(comparison => comparison.IsExact);
}

/// <summary>
/// Product-owned implementation diff projection that joins C# source-shape and
/// IL/body changes by Research member identity.
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
        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        var retainedComparisons = ImmutableArray.CreateBuilder<RetainedFindingComparison>();

        if (mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
        {
            csharpDiff = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
            changes.AddRange(ToCSharpChanges(csharpDiff, subject));
            var comparison = CSharpFindings.Compare(
                oldSource,
                oldMethod,
                newSource,
                newMethod,
                new FindingSubject(subject.Id, subject.Display));
            retainedComparisons.Add(new RetainedFindingComparison<CSharpCanonicalLine>(
                subject,
                CSharpFindings.LineDescriptor,
                comparison));
            if (comparison is FindingComparison<CSharpCanonicalLine>.Failed failed)
            {
                changes.Add(FindingFailureChange(
                    subject,
                    ResearchChangeMechanism.CSharp,
                    ResearchChangeCategory.CSharp,
                    CSharpFindings.InspectionDescriptor,
                    failed.Failure));
            }
            else if (comparison.IsExact != csharpDiff.IsExact)
            {
                throw new InvalidOperationException(
                    $"C# Finding comparison diverged from the semantic C# diff for '{subject.Display}'.");
            }
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
            changes.AddRange(ToIlChanges(ilDiff, subject));
            var comparison = IlFindings.Compare(
                oldSource.Pe,
                oldSource.Reader,
                oldMethod,
                newSource.Pe,
                newSource.Reader,
                newMethod,
                new FindingSubject(subject.Id, subject.Display));
            retainedComparisons.Add(new RetainedFindingComparison<CanonicalIlOperation>(
                subject,
                IlFindings.OperationDescriptor,
                comparison));
            if (comparison is FindingComparison<CanonicalIlOperation>.Failed failed)
            {
                changes.Add(FindingFailureChange(
                    subject,
                    ResearchChangeMechanism.IlBody,
                    ResearchChangeCategory.IlBody,
                    IlFindings.InspectionDescriptor,
                    failed.Failure));
            }
            else if (MethodHasBody(oldSource, oldMethod)
                && MethodHasBody(newSource, newMethod)
                && comparison.IsExact != ilDiff.Diff.IsExact)
            {
                throw new InvalidOperationException(
                    $"IL Finding comparison diverged from the semantic IL diff for '{subject.Display}'.");
            }
        }

        return new ImplementationMemberDiffResult(
            subject,
            csharpDiff,
            ilDiff,
            changes.ToImmutable(),
            new RetainedFindingComparisonSet(retainedComparisons));
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
        return FromResearchComparison(research, options);
    }

    public static ImplementationDiffResult FromResearchComparison(
        ResearchComparison research,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        options ??= new ImplementationDiffOptions();

        var members = research.MembersWhere(member => member.ImplementationChanged)
            .Select(member => new ImplementationDiffMember(
                member.Subject,
                [.. member.Changes.Where(change =>
                    change.Mechanism is ResearchChangeMechanism.CSharp
                        or ResearchChangeMechanism.IlBody)]))
            .Where(member => member.Changes.Count > 0)
            .Where(member => ResearchDiff.MatchesTypeFilters(member.Subject.TypeName ?? "", options.TypeFilters))
            .Where(member => MatchesMemberTargets(member.Subject, options.MemberTargetIdentities))
            .ToArray();

        return new ImplementationDiffResult(members, research);
    }

    public static ImmutableArray<ResearchChange> ToIlChanges(
        IlMemberDiffResult? diff,
        ResearchSubjectKey? subject = null,
        IlDiffDisplayResult? fallbackDisplay = null)
    {
        subject ??= diff is { } typedDiff
            ? new ResearchSubjectKey(
                ResearchSubjectKind.Member,
                typedDiff.New.Identity,
                typedDiff.New.Label)
            : new ResearchSubjectKey(ResearchSubjectKind.Member, "member:il-body", "il-body");
        if (diff is not { } typed)
            return fallbackDisplay is null ? [] : ToIlChanges(fallbackDisplay, subject);

        var typedChanges = ToIlChanges(IlDiffPrinter.ToDisplayResult(typed.Diff), subject, typed);
        return typedChanges.IsEmpty && fallbackDisplay is not null
            ? ToIlChanges(fallbackDisplay, subject)
            : typedChanges;
    }

    public static ImmutableArray<ResearchChange> ToIlChanges(
        IlDiffDisplayResult display,
        ResearchSubjectKey subject,
        IlMemberDiffResult? diff = null)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (display.IsEmpty)
            return [];

        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        var failureRows = display.FailureRows.IsDefault ? [] : display.FailureRows;
        foreach (var failureRow in failureRows)
        {
            string descriptorId = $"il.diff.{ResearchDiff.ToChangeIdPart(failureRow.Kind.ToString())}";
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.IlBody,
                new FindingDescriptor(descriptorId, failureRow.Kind.ToString()),
                ResearchChangeKind.Changed,
                detail: failureRow.Detail ?? failureRow.Message,
                category: ResearchChangeCategory.IlBody,
                ilDisplayFailureRow: failureRow,
                ilMemberDiff: diff));
        }

        if (failureRows.IsDefaultOrEmpty && display.Failure is { Length: > 0 } failure)
        {
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.IlBody,
                new FindingDescriptor("il.diff.failed", "IL diff failed"),
                ResearchChangeKind.Changed,
                detail: failure,
                category: ResearchChangeCategory.IlBody,
                ilMemberDiff: diff));
        }

        var displayRows = display.Rows.IsDefault ? [] : display.Rows;
        foreach (var displayRow in displayRows)
        {
            if (displayRow.Kind == IlDiffKind.Context)
                continue;

            var kind = displayRow.Kind == IlDiffKind.Add
                ? ResearchChangeKind.Added
                : ResearchChangeKind.Removed;
            string descriptorId = displayRow.Kind == IlDiffKind.Add
                ? "il.operation.added"
                : "il.operation.removed";
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.IlBody,
                new FindingDescriptor(descriptorId, "IL operation"),
                kind,
                oldValue: displayRow.Kind == IlDiffKind.Remove ? displayRow.Operation : null,
                newValue: displayRow.Kind == IlDiffKind.Add ? displayRow.Operation : null,
                oldIlOffset: displayRow.Kind == IlDiffKind.Remove ? displayRow.RawOffset : null,
                newIlOffset: displayRow.Kind == IlDiffKind.Add ? displayRow.RawOffset : null,
                detail: displayRow.Message,
                category: ResearchChangeCategory.IlBody,
                ilDisplayRows: [displayRow],
                ilMemberDiff: diff));
        }

        return changes.ToImmutable();
    }

    public static ImmutableArray<string> UnifiedLines(ResearchChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var lines = ImmutableArray.CreateBuilder<string>();
        if (change.CSharpDisplayFailureRow is not null)
            lines.Add(change.CSharpDisplayFailureRow.UnifiedLine);
        if (!change.CSharpDisplayRows.IsDefaultOrEmpty)
            lines.AddRange(change.CSharpDisplayRows.Select(row => row.UnifiedLine));
        if (change.IlDisplayFailureRow is not null)
            lines.Add(change.IlDisplayFailureRow.UnifiedLine);
        if (!change.IlDisplayRows.IsDefaultOrEmpty)
            lines.AddRange(change.IlDisplayRows.Select(row => row.UnifiedLine));
        return lines.ToImmutable();
    }

    static ImmutableArray<ResearchChange> ToCSharpChanges(
        CSharpBodyDiffResult diff,
        ResearchSubjectKey subject)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.IsExact)
            return [];

        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        foreach (var failure in diff.FailureRows.IsDefault ? [] : diff.FailureRows)
        {
            var kind = failure.Kind switch
            {
                CSharpDiffFailureKind.OldBodyMissing => ResearchChangeKind.Added,
                CSharpDiffFailureKind.NewBodyMissing => ResearchChangeKind.Removed,
                _ => ResearchChangeKind.Changed,
            };
            string descriptorId = $"csharp.diff.{ResearchDiff.ToChangeIdPart(failure.Kind.ToString())}";
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.CSharp,
                new FindingDescriptor(descriptorId, failure.Kind.ToString()),
                kind,
                oldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
                newValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
                detail: failure.Detail ?? failure.Message,
                category: ResearchChangeCategory.CSharp,
                cSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(failure)));
        }

        foreach (var row in diff.Rows.IsDefault ? [] : diff.Rows)
        {
            var kind = row.Kind switch
            {
                CSharpDiffKind.Add => ResearchChangeKind.Added,
                CSharpDiffKind.Remove => ResearchChangeKind.Removed,
                _ => ResearchChangeKind.Changed,
            };
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.CSharp,
                new FindingDescriptor(row.ChangeId, row.ChangeId),
                kind,
                oldValue: row.OldOperation?.Value
                    ?? row.OldValue
                    ?? (kind == ResearchChangeKind.Removed ? row.Text : null),
                newValue: row.NewOperation?.Value
                    ?? row.NewValue
                    ?? (kind == ResearchChangeKind.Added ? row.Text : null),
                detail: row.Message,
                category: ResearchChangeCategory.CSharp,
                cSharpDisplayRows: [CSharpDiffPrinter.ToDisplayRow(row)]));
        }

        return changes.ToImmutable();
    }

    static ResearchChangeMechanism ToResearchMechanisms(ImplementationDiffMechanism mechanisms)
    {
        var research = ResearchChangeMechanism.None;
        if (mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
            research |= ResearchChangeMechanism.CSharp;
        if (mechanisms.HasFlag(ImplementationDiffMechanism.IlBody))
            research |= ResearchChangeMechanism.IlBody;
        return research;
    }

    static bool MatchesMemberTargets(ResearchSubjectKey subject, IReadOnlySet<string>? memberTargetIdentities)
        => memberTargetIdentities is null
           || memberTargetIdentities.Count == 0
           || memberTargetIdentities.Contains(subject.Id);

    static ResearchChange FindingFailureChange(
        ResearchSubjectKey subject,
        ResearchChangeMechanism mechanism,
        ResearchChangeCategory category,
        FindingDescriptor descriptor,
        string failure)
        => new(
            subject,
            mechanism,
            descriptor,
            ResearchChangeKind.Changed,
            detail: failure,
            category: category);

    static bool MethodHasBody(MetadataSource source, MethodDefinitionHandle method)
        => source.Reader.GetMethodDefinition(method).RelativeVirtualAddress != 0;

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
