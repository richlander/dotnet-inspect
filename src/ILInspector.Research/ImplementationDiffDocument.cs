using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research;

public enum ImplementationDiffDocumentScope
{
    ExactLibraryPair,
}

public enum ImplementationDiffDocumentMechanism
{
    CSharp,
    IlBody,
    Complexity,
}

public sealed record ImplementationDiffDocumentRequest(
    ImplementationDiffDocumentScope Scope,
    IReadOnlyList<ImplementationDiffDocumentMechanism> Mechanisms,
    IReadOnlyList<string> TypeFilters,
    IReadOnlyList<string> MemberTargetIdentities);

public sealed record ImplementationDiffEndpoint(
    AssemblyReferenceIdentity AssemblyIdentity,
    Guid ModuleVersionId,
    AssemblyResolutionProvenance Provenance);

public sealed record ImplementationDiffEvidence(
    ResearchChangeMechanism Mechanism,
    string DescriptorId,
    string DescriptorTitle,
    ResearchChangeKind Kind,
    ResearchChangeCategory Category,
    string? OldValue,
    string? NewValue,
    string? Delta,
    int? OldIlOffset,
    int? NewIlOffset,
    string? Detail,
    string? Signal,
    string? Shape,
    int? Magnitude,
    int DirectionScore,
    bool SubjectInBoth,
    bool InLoop,
    CSharpDiffRow? CSharpRow,
    CSharpDiffFailureRow? CSharpFailure,
    IReadOnlyList<IlDiffRow> IlRows,
    IReadOnlyList<IlDiffFailureRow> IlFailureRows,
    IlBodyDiffOutcome? IlBodyOutcome,
    string? IlBodyFailure);

public sealed record ImplementationDiffDocumentMember(
    ResearchSubjectKey Subject,
    IReadOnlyList<ImplementationDiffEvidence> Evidence);

public sealed record ImplementationDiffMethodEvidence(
    string AssemblyName,
    Guid ModuleVersionId,
    int MetadataToken,
    string Name);

public sealed record ImplementationDiffDocumentComplexityChange(
    ResearchSubjectKey Subject,
    ImplementationComplexityChangeKind Kind,
    int? OldValue,
    int? NewValue,
    int? Delta,
    bool OldIsComplete,
    bool NewIsComplete,
    ImplementationDiffMethodEvidence? OldEvidence,
    ImplementationDiffMethodEvidence? NewEvidence,
    ImplementationComplexityPopulationContext? PopulationContext);

public sealed record ImplementationDiffDocumentComplexity(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<ImplementationDiffDocumentComplexityChange> Changes);

public sealed record ImplementationDiffMechanismCoverage(
    ImplementationDiffDocumentMechanism Mechanism,
    bool Requested,
    bool IsAvailable,
    int EvaluatedSubjectCount,
    int ExactSubjectCount,
    int ChangedSubjectCount,
    int UnavailableSubjectCount,
    int IncompleteSubjectCount,
    int FailedSubjectCount);

public sealed record ImplementationDiffCoverage(
    IReadOnlyList<ImplementationDiffMechanismCoverage> Mechanisms)
{
    public bool IsComplete => Mechanisms
        .Where(mechanism => mechanism.Requested)
        .All(mechanism =>
            mechanism.IsAvailable
            && mechanism.UnavailableSubjectCount == 0
            && mechanism.IncompleteSubjectCount == 0
            && mechanism.FailedSubjectCount == 0);
}

public sealed record ImplementationDiffDocument(
    ImplementationDiffDocumentRequest Request,
    ImplementationDiffEndpoint Before,
    ImplementationDiffEndpoint After,
    IReadOnlyList<ImplementationDiffDocumentMember> Members,
    ImplementationDiffDocumentComplexity Complexity,
    ImplementationDiffCoverage Coverage);

public static partial class ImplementationDiff
{
    public static ImplementationDiffDocument CompareExactPair(
        ImplementationAssemblyInput oldAssembly,
        ImplementationAssemblyInput newAssembly,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldAssembly);
        ArgumentNullException.ThrowIfNull(newAssembly);
        options ??= new ImplementationDiffOptions();

        ImplementationDiffResult result = Compare(
            [oldAssembly],
            [newAssembly],
            options);

        return new ImplementationDiffDocument(
            CreateRequest(options),
            CreateEndpoint(oldAssembly),
            CreateEndpoint(newAssembly),
            [.. result.Members
                .OrderBy(member => member.Subject.Id, StringComparer.Ordinal)
                .Select(CreateMember)],
            new ImplementationDiffDocumentComplexity(
                result.Complexity.IsAvailable,
                result.Complexity.UnavailableReason,
                [.. result.Complexity.Changes
                    .OrderBy(change => change.Subject.Id, StringComparer.Ordinal)
                    .Select(CreateComplexityChange)]),
            CreateCoverage(result, options));
    }

    static ImplementationDiffDocumentRequest CreateRequest(
        ImplementationDiffOptions options)
    {
        var mechanisms =
            new List<ImplementationDiffDocumentMechanism>(3);
        if (options.Mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
            mechanisms.Add(ImplementationDiffDocumentMechanism.CSharp);
        if (options.Mechanisms.HasFlag(ImplementationDiffMechanism.IlBody))
            mechanisms.Add(ImplementationDiffDocumentMechanism.IlBody);
        mechanisms.Add(ImplementationDiffDocumentMechanism.Complexity);

        return new(
            ImplementationDiffDocumentScope.ExactLibraryPair,
            mechanisms,
            Sorted(options.TypeFilters),
            Sorted(options.MemberTargetIdentities));
    }

    static IReadOnlyList<string> Sorted(IReadOnlySet<string>? values)
        => values is null
            ? []
            : [.. values.OrderBy(value => value, StringComparer.Ordinal)];

    static ImplementationDiffEndpoint CreateEndpoint(
        ImplementationAssemblyInput input)
    {
        LibraryBodyModuleIdentity module = input.BodyIndex.ModuleIdentity;
        if (module.AssemblyIdentity is null)
        {
            throw new ArgumentException(
                "Implementation Diff documents require assembly-definition inputs.",
                nameof(input));
        }

        return new(
            module.AssemblyIdentity,
            module.ModuleVersionId,
            input.Assembly.Provenance);
    }

    static ImplementationDiffDocumentMember CreateMember(
        ImplementationDiffMember member)
        => new(
            member.Subject,
            [.. member.Changes.Select(CreateEvidence)]);

    static ImplementationDiffEvidence CreateEvidence(ResearchChange change)
        => new(
            change.Mechanism,
            change.Descriptor.Id,
            change.Descriptor.Title,
            change.Kind,
            change.Category,
            change.OldValue,
            change.NewValue,
            change.Delta,
            change.OldIlOffset,
            change.NewIlOffset,
            change.Detail,
            change.Signal,
            change.Shape,
            change.Magnitude,
            change.DirectionScore,
            change.SubjectInBoth,
            change.InLoop,
            change.CSharpRow,
            change.CSharpFailureRow,
            CreateIlRows(change),
            change.IlFailureRow is not null ? [change.IlFailureRow] : [],
            change.IlBodyDiff?.Outcome,
            change.IlBodyDiff?.Failure);

    static IReadOnlyList<IlDiffRow> CreateIlRows(ResearchChange change)
    {
        if (change.IlRow is not null)
            return [change.IlRow];
        if (change.IlBodyDiff is not { } diff
            || diff.Rows.IsDefaultOrEmpty
            || change.IlDisplayRows.IsDefaultOrEmpty)
        {
            return [];
        }

        HashSet<int> hunkIds = [
            .. change.IlDisplayRows.Select(row => row.HunkId),
        ];
        return [.. diff.Rows.Where(row => hunkIds.Contains(row.HunkId))];
    }

    static ImplementationDiffDocumentComplexityChange CreateComplexityChange(
        ImplementationComplexityChange change)
        => new(
            change.Subject,
            change.Kind,
            change.OldValue,
            change.NewValue,
            change.Delta,
            change.OldIsComplete,
            change.NewIsComplete,
            CreateMethodEvidence(change.OldEvidenceMethod),
            CreateMethodEvidence(change.NewEvidenceMethod),
            change.PopulationContext);

    static ImplementationDiffMethodEvidence? CreateMethodEvidence(
        MethodIdentity? method)
        => method is null
            ? null
            : new(
                method.AssemblyName,
                method.ModuleVersionId,
                method.MetadataToken,
                method.Name);

    static ImplementationDiffCoverage CreateCoverage(
        ImplementationDiffResult result,
        ImplementationDiffOptions options)
        => new(
        [
            CreateResearchCoverage(
                result,
                ImplementationDiffDocumentMechanism.CSharp,
                ResearchChangeMechanism.CSharp,
                CSharpFindings.LineDescriptor.Id,
                options.Mechanisms.HasFlag(ImplementationDiffMechanism.CSharp)),
            CreateResearchCoverage(
                result,
                ImplementationDiffDocumentMechanism.IlBody,
                ResearchChangeMechanism.IlBody,
                IlFindings.OperationDescriptor.Id,
                options.Mechanisms.HasFlag(ImplementationDiffMechanism.IlBody)),
            CreateComplexityCoverage(result.Complexity),
        ]);

    static ImplementationDiffMechanismCoverage CreateResearchCoverage(
        ImplementationDiffResult result,
        ImplementationDiffDocumentMechanism documentMechanism,
        ResearchChangeMechanism researchMechanism,
        string descriptorId,
        bool requested)
    {
        var comparisons = result.Research.RetainedComparisons.Items
            .Where(comparison =>
                comparison.Descriptor.Id == descriptorId)
            .ToArray();
        var changes = result.Members
            .SelectMany(member => member.Changes)
            .Where(change => change.Mechanism == researchMechanism)
            .ToArray();

        HashSet<string> unavailableSubjects = researchMechanism switch
        {
            ResearchChangeMechanism.CSharp => [
                .. changes
                    .Where(change => change.CSharpFailureRow is not null)
                    .Select(change => change.Subject.Id),
            ],
            ResearchChangeMechanism.IlBody => [
                .. changes
                    .Where(change =>
                        change.IlFailureRow is not null
                        || change.IlBodyDiff?.Outcome
                            == IlBodyDiffOutcome.Unavailable)
                    .Select(change => change.Subject.Id),
            ],
            _ => [],
        };
        var failedSubjects = comparisons
            .Where(comparison => comparison.Failure is not null)
            .Select(comparison => comparison.Subject.Id)
            .Concat(changes
                .Where(change => change.Kind == ResearchChangeKind.Failed)
                .Select(change => change.Subject.Id))
            .ToHashSet(StringComparer.Ordinal);

        return new(
            documentMechanism,
            requested,
            IsAvailable: requested,
            comparisons
                .Select(comparison => comparison.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            comparisons
                .Where(comparison => comparison.IsExact)
                .Select(comparison => comparison.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            comparisons
                .Where(comparison =>
                    !comparison.IsExact
                    && comparison.Failure is null
                    && !unavailableSubjects.Contains(comparison.Subject.Id)
                    && !failedSubjects.Contains(comparison.Subject.Id))
                .Select(comparison => comparison.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            unavailableSubjects.Count,
            0,
            failedSubjects.Count);
    }

    internal static ImplementationDiffMechanismCoverage CreateComplexityCoverage(
        ImplementationComplexityDiff complexity)
    {
        return new(
            ImplementationDiffDocumentMechanism.Complexity,
            Requested: true,
            IsAvailable: complexity.IsAvailable,
            EvaluatedSubjectCount: CountComplexitySubjects(
                complexity.Changes),
            ExactSubjectCount: CountComplexitySubjects(
                complexity.Changes.Where(change =>
                    change.Kind == ImplementationComplexityChangeKind.Unchanged
                    && change.OldIsComplete
                    && change.NewIsComplete)),
            ChangedSubjectCount: CountComplexitySubjects(
                complexity.Changes.Where(change =>
                    change.Kind is ImplementationComplexityChangeKind.Changed
                        or ImplementationComplexityChangeKind.Added
                        or ImplementationComplexityChangeKind.Removed)),
            UnavailableSubjectCount: 0,
            IncompleteSubjectCount: CountComplexitySubjects(
                complexity.Changes.Where(change =>
                    change.Kind == ImplementationComplexityChangeKind.Incomplete
                    || !change.OldIsComplete
                    || !change.NewIsComplete)),
            FailedSubjectCount: 0);
    }

    static int CountComplexitySubjects(
        IEnumerable<ImplementationComplexityChange> changes)
        => changes
            .Select(change => change.Subject.Id)
            .Distinct(StringComparer.Ordinal)
            .Count();
}
