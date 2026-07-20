using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Text;

namespace ILInspector.Research;

[Flags]
public enum ImplementationDiffMechanism
{
    None = 0,
    CSharp = 1,
    IlBody = 2,
    Source = 4,
    All = CSharp | IlBody,
    AllAvailable = All | Source,
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
    public FindingComparison<string>? SourceComparison { get; init; }

    public bool HasCSharpChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.CSharp);

    public bool HasIlChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.IlBody);

    public bool HasSourceChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.Source);
}

public sealed record AuthoredSourceComparisonInput(
    ResearchSubjectKey Subject,
    FindingInspection<string> OldInspection,
    FindingInspection<string> NewInspection);

public sealed record ImplementationMemberDiffResult(
    ResearchSubjectKey Subject,
    CSharpBodyDiffResult? CSharpDiff,
    IlMemberDiffResult? IlDiff,
    IReadOnlyList<ResearchChange> Changes,
    RetainedFindingComparisonSet RetainedComparisons)
{
    public FindingComparison<string>? SourceComparison { get; init; }

    public bool HasCSharpChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.CSharp);

    public bool HasIlChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.IlBody);

    public bool HasSourceChanges
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.Source);

    public bool IsExact
        => Changes.Count == 0
           && (CSharpDiff is null || CSharpDiff.IsExact)
           && (IlDiff is null || IlDiff.Diff.IsExact)
           && RetainedComparisons.Items.All(comparison => comparison.IsExact)
           && (SourceComparison is null || SourceComparison.IsExact);
}

/// <summary>
/// Product-owned implementation diff projection that joins C# source-shape and
/// IL/body changes by Research member identity.
/// </summary>
public static class ImplementationDiff
{
    public static readonly FindingDescriptor AuthoredSourceFailureDescriptor =
        new("source.authored.failed", "Authored source acquisition failed");
    internal static readonly FindingDescriptor CSharpFindingDivergenceDescriptor =
        new("csharp.finding.diverged", "C# Finding comparison diverged");
    internal static readonly FindingDescriptor IlFindingDivergenceDescriptor =
        new("il.finding.diverged", "IL Finding comparison diverged");

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
            var semanticChanges = ToCSharpChanges(csharpDiff, subject);
            changes.AddRange(semanticChanges);
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
                if (!semanticChanges.Any(change => change.Kind == ResearchChangeKind.Failed))
                {
                    changes.Add(FindingFailureChange(
                        subject,
                        ResearchChangeMechanism.CSharp,
                        ResearchChangeCategory.CSharp,
                        CSharpFindings.InspectionDescriptor,
                        failed.Failure));
                }
            }
            else if (FindingDivergenceChange(
                subject,
                ResearchChangeMechanism.CSharp,
                ResearchChangeCategory.CSharp,
                CSharpFindingDivergenceDescriptor,
                comparison.IsExact,
                csharpDiff.IsExact) is { } divergence)
            {
                changes.Add(divergence);
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
            var semanticChanges = ToIlChanges(ilDiff, subject);
            changes.AddRange(semanticChanges);
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
                if (!semanticChanges.Any(change => change.Kind == ResearchChangeKind.Failed))
                {
                    changes.Add(FindingFailureChange(
                        subject,
                        ResearchChangeMechanism.IlBody,
                        ResearchChangeCategory.IlBody,
                        IlFindings.InspectionDescriptor,
                        failed.Failure));
                }
            }
            else if (MethodHasBody(oldSource, oldMethod)
                && MethodHasBody(newSource, newMethod)
                && FindingDivergenceChange(
                    subject,
                    ResearchChangeMechanism.IlBody,
                    ResearchChangeCategory.IlBody,
                    IlFindingDivergenceDescriptor,
                    comparison.IsExact,
                    ilDiff.Diff.IsExact) is { } divergence)
            {
                changes.Add(divergence);
            }
        }

        return new ImplementationMemberDiffResult(
            subject,
            csharpDiff,
            ilDiff,
            changes.ToImmutable(),
            new RetainedFindingComparisonSet(retainedComparisons));
    }

    public static ImplementationMemberDiffResult CompareMembersWithAuthoredSource(
        MetadataSource oldSource,
        MethodDefinitionHandle oldMethod,
        MetadataSource newSource,
        MethodDefinitionHandle newMethod,
        FindingInspection<string> oldAuthoredSource,
        FindingInspection<string> newAuthoredSource,
        ImplementationDiffMechanism mechanisms = ImplementationDiffMechanism.AllAvailable,
        ResearchSubjectKey? subject = null)
    {
        ArgumentNullException.ThrowIfNull(oldAuthoredSource);
        ArgumentNullException.ThrowIfNull(newAuthoredSource);

        var result = CompareMembers(
            oldSource,
            oldMethod,
            newSource,
            newMethod,
            mechanisms & ~ImplementationDiffMechanism.Source,
            subject);
        if (!mechanisms.HasFlag(ImplementationDiffMechanism.Source))
            return result;

        var comparison = FindingComparison.Compare(
            oldAuthoredSource,
            newAuthoredSource);
        var retained = result.RetainedComparisons.Items.ToBuilder();
        retained.Add(new RetainedFindingComparison<string>(
            result.Subject,
            TextFindings.LineDescriptor,
            comparison));
        return result with
        {
            Changes = [.. result.Changes, .. ToSourceChanges(comparison, result.Subject)],
            RetainedComparisons = new RetainedFindingComparisonSet(retained),
            SourceComparison = comparison,
        };
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

        var sourceComparisons = research.RetainedComparisons.Items
            .Where(comparison => comparison.Descriptor.Id == TextFindings.LineDescriptor.Id)
            .OfType<RetainedFindingComparison<string>>()
            .GroupBy(comparison => comparison.Subject.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var changedMembers = research.BySubject()
            .Where(member => member.Subject.Kind == ResearchSubjectKind.Member)
            .ToDictionary(member => member.Subject.Id, StringComparer.Ordinal);
        var subjects = changedMembers.Values
            .Select(member => member.Subject)
            .Concat(sourceComparisons.Values.Select(comparison => comparison.Subject))
            .DistinctBy(subject => subject.Id, StringComparer.Ordinal);
        var members = subjects
            .Select(subject => (
                Subject: subject,
                Changes: changedMembers.TryGetValue(subject.Id, out var member)
                    ? member.Changes
                    : ImmutableArray<ResearchChange>.Empty))
            .Where(item => item.Changes.Any(change =>
                    change.Mechanism is ResearchChangeMechanism.CSharp
                        or ResearchChangeMechanism.IlBody
                        or ResearchChangeMechanism.Source)
                || sourceComparisons.ContainsKey(item.Subject.Id))
            .Select(member => new ImplementationDiffMember(
                member.Subject,
                [.. member.Changes.Where(change =>
                    change.Mechanism is ResearchChangeMechanism.CSharp
                        or ResearchChangeMechanism.IlBody
                        or ResearchChangeMechanism.Source)])
                {
                    SourceComparison = sourceComparisons.GetValueOrDefault(member.Subject.Id)?.Comparison,
                })
            .Where(member => member.Changes.Count > 0 || member.SourceComparison is not null)
            .Where(member => ResearchDiff.MatchesTypeFilters(member.Subject.TypeName ?? "", options.TypeFilters))
            .Where(member => MatchesMemberTargets(member.Subject, options.MemberTargetIdentities))
            .ToArray();

        return new ImplementationDiffResult(members, research);
    }

    public static ImplementationDiffResult WithAuthoredSourceComparisons(
        ImplementationDiffResult result,
        IEnumerable<AuthoredSourceComparisonInput> inputs,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inputs);
        options ??= new ImplementationDiffOptions();

        var changes = result.Research.Changes.ToBuilder();
        var retained = result.Research.RetainedComparisons.Items.ToBuilder();
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            var comparison = FindingComparison.Compare(
                input.OldInspection,
                input.NewInspection);
            retained.Add(new RetainedFindingComparison<string>(
                input.Subject,
                TextFindings.LineDescriptor,
                comparison));
            changes.AddRange(ToSourceChanges(comparison, input.Subject));
        }

        var research = new ResearchComparison(
            changes.ToImmutable(),
            result.Research.ApiDiff,
            result.Research.ApiComparison,
            new RetainedFindingComparisonSet(retained));
        return FromResearchComparison(research, options);
    }

    public static ImmutableArray<ResearchChange> ToSourceChanges(
        FindingComparison<string> comparison,
        ResearchSubjectKey subject)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(subject);

        if (comparison is FindingComparison<string>.Failed failed)
        {
            return
            [
                new ResearchChange(
                    subject,
                    ResearchChangeMechanism.Source,
                    AuthoredSourceFailureDescriptor,
                    ResearchChangeKind.Failed,
                    detail: failed.Failure,
                    category: ResearchChangeCategory.Source)
            ];
        }

        var complete = (FindingComparison<string>.Complete)comparison.Value;
        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        foreach (var pair in complete.Pairs)
        {
            switch (pair)
            {
                case PairFinding<string>.Added added:
                    changes.Add(SourceChange(
                        subject,
                        ResearchChangeKind.Added,
                        newValue: added.New.Payload,
                        detail: added.Detail));
                    break;
                case PairFinding<string>.Removed removed:
                    changes.Add(SourceChange(
                        subject,
                        ResearchChangeKind.Removed,
                        oldValue: removed.Old.Payload,
                        detail: removed.Detail));
                    break;
                case PairFinding<string>.Changed changed:
                    changes.Add(SourceChange(
                        subject,
                        ResearchChangeKind.Changed,
                        changed.Old.Payload,
                        changed.New.Payload,
                        changed.Detail));
                    break;
                case PairFinding<string>.Present:
                    break;
            }
        }

        return changes.ToImmutable();
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
                ResearchDiff.Direction(failureRow.Kind),
                detail: failureRow.Detail ?? failureRow.Message,
                category: ResearchChangeCategory.IlBody,
                ilDisplayFailureRow: failureRow,
                ilMemberDiff: diff,
                ilBodyDiff: diff?.Diff));
        }

        if (failureRows.IsDefaultOrEmpty && display.Failure is { Length: > 0 } failure)
        {
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.IlBody,
                new FindingDescriptor("il.diff.failed", "IL diff failed"),
                ResearchChangeKind.Failed,
                detail: failure,
                category: ResearchChangeCategory.IlBody,
                ilMemberDiff: diff,
                ilBodyDiff: diff?.Diff));
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
                ilMemberDiff: diff,
                ilBodyDiff: diff?.Diff));
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
        if (change.Mechanism == ResearchChangeMechanism.Source)
        {
            if (change.OldValue is { } oldValue)
                lines.Add($"- {oldValue}");
            if (change.NewValue is { } newValue)
                lines.Add($"+ {newValue}");
        }
        return lines.ToImmutable();
    }

    static ResearchChange SourceChange(
        ResearchSubjectKey subject,
        ResearchChangeKind kind,
        string? oldValue = null,
        string? newValue = null,
        string? detail = null)
        => new(
            subject,
            ResearchChangeMechanism.Source,
            TextFindings.LineDescriptor,
            kind,
            oldValue,
            newValue,
            detail: detail,
            category: ResearchChangeCategory.Source);

    static ImmutableArray<ResearchChange> ToCSharpChanges(
        CSharpBodyDiffResult diff,
        ResearchSubjectKey subject)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.IsExact)
            return [];

        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        foreach (var failure in diff.IdentityFailures.IsDefault
            ? []
            : diff.IdentityFailures)
        {
            string detail = $"{failure.Side} 0x{failure.SubjectToken:X8} "
                + $"{failure.Mechanism}/{failure.Kind}: {failure.Detail}";
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.CSharp,
                new FindingDescriptor(
                    "csharp.diff.identity-resolution-failure",
                    "Identity resolution failure"),
                ResearchChangeKind.Failed,
                oldValue: failure.Side == "old" ? detail : null,
                newValue: failure.Side == "new" ? detail : null,
                detail: detail,
                category: ResearchChangeCategory.CSharp));
        }

        var failureRows = diff.FailureRows.IsDefault ? [] : diff.FailureRows;
        var operationalFailureHunks = ResearchDiff.OperationalCSharpFailureHunks(failureRows);
        foreach (var failure in failureRows)
        {
            var kind = ResearchDiff.Direction(failure.Kind);
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
            if (operationalFailureHunks.Contains(row.HunkId))
                continue;

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

    internal static ResearchChange FindingFailureChange(
        ResearchSubjectKey subject,
        ResearchChangeMechanism mechanism,
        ResearchChangeCategory category,
        FindingDescriptor descriptor,
        string failure)
        => new(
            subject,
            mechanism,
            descriptor,
            ResearchChangeKind.Failed,
            detail: failure,
            category: category);

    internal static ResearchChange? FindingDivergenceChange(
        ResearchSubjectKey subject,
        ResearchChangeMechanism mechanism,
        ResearchChangeCategory category,
        FindingDescriptor descriptor,
        bool findingExact,
        bool semanticExact)
        => findingExact == semanticExact
            ? null
            : FindingFailureChange(
                subject,
                mechanism,
                category,
                descriptor,
                $"{descriptor.Title} from the semantic projection for '{subject.Display}'.");

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
