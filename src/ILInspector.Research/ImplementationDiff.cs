using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Inspector.Text;

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

public enum ImplementationComplexityChangeKind
{
    Unchanged,
    Changed,
    Added,
    Removed,
    Incomplete,
}

/// <summary>
/// Finding descriptors for Research-owned implementation-complexity context.
/// </summary>
public static class ImplementationComplexityFindings
{
    /// <summary>
    /// Identifies an exact local structural direction cohort.
    /// </summary>
    public static readonly FindingDescriptor StructuralCohortDescriptor =
        new(
            "research.complexity.structural-cohort",
            "Structural complexity cohort");
}

/// <summary>
/// Where one change's absolute normal-flow complexity delta falls within the
/// local comparison population: every change in the same
/// <see cref="ImplementationComplexityComparisonRequest"/> that has a
/// non-null <see cref="ImplementationComplexityChange.Delta"/>, regardless of
/// <see cref="ImplementationComplexityChangeKind"/> (including
/// <see cref="ImplementationComplexityChangeKind.Incomplete"/> rows - callers
/// wanting a stricter population can filter by <c>Kind</c> themselves before
/// interpreting <see cref="PercentileRank"/>). This is deliberately the local,
/// per-diff population described in issue #7696 ("diff analysis emphasizes
/// ... local comparison populations"), not a corpus-wide distribution; the
/// latter belongs to the separate library-report initiative.
/// </summary>
/// <param name="PopulationSize">
/// Count of changes contributing to the population. A small population
/// (for example 1-2) makes <see cref="PercentileRank"/> a weak signal.
/// </param>
/// <param name="PercentileRank">
/// Percentage (0-100) of the population whose absolute delta is less than or
/// equal to this change's absolute delta. This is an inclusive positional
/// fact, not an unusualness signal: when all absolute deltas are equal, every
/// change has a value of 100.
/// </param>
public sealed record ImplementationComplexityPopulationContext(
    int PopulationSize,
    double PercentileRank);

public enum ImplementationStructuralChangeDirection
{
    Decreased = -1,
    Unchanged = 0,
    Increased = 1,
}

/// <summary>
/// Direction-only signature for one complete paired structural change.
/// Magnitudes remain available on <see cref="ImplementationStructuralChange"/>.
/// </summary>
public sealed record ImplementationStructuralChangeSignature(
    ImplementationStructuralChangeDirection Instructions,
    ImplementationStructuralChangeDirection Complexity,
    ImplementationStructuralChangeDirection Loops,
    ImplementationStructuralChangeDirection ExceptionRegions,
    ImplementationStructuralChangeDirection DirectCalls,
    ImplementationStructuralChangeDirection Allocations,
    ImplementationStructuralChangeDirection Async);

/// <summary>
/// Signed deltas over the first explainable structural-change vector.
/// </summary>
public sealed record ImplementationStructuralChange(
    int InstructionDelta,
    int ComplexityDelta,
    int LoopDelta,
    int ExceptionRegionDelta,
    int DirectCallDelta,
    int AllocationDelta,
    int AsyncDelta)
{
    public ImplementationStructuralChangeSignature Signature =>
        new(
            Direction(InstructionDelta),
            Direction(ComplexityDelta),
            Direction(LoopDelta),
            Direction(ExceptionRegionDelta),
            Direction(DirectCallDelta),
            Direction(AllocationDelta),
            Direction(AsyncDelta));

    static ImplementationStructuralChangeDirection Direction(int delta)
        => delta switch
        {
            < 0 => ImplementationStructuralChangeDirection.Decreased,
            > 0 => ImplementationStructuralChangeDirection.Increased,
            _ => ImplementationStructuralChangeDirection.Unchanged,
        };
}

/// <summary>
/// Exact local frequency of one direction-only structural change signature.
/// It is not an outlier probability or quality score.
/// </summary>
public sealed record ImplementationStructuralChangeCohortContext(
    int PopulationSize,
    int CohortSize);

/// <summary>
/// One paired complexity observation for a logical member. <see cref="OldProfile"/>
/// and <see cref="NewProfile"/> retain the full Analysis-owned structural
/// facts (instructions, branches, switches, loops, exception regions, calls,
/// allocations, async/state-machine) behind the narrow complexity number, so
/// later comparison-population or clustering work can build on the same
/// paired evidence without re-deriving correspondence.
/// </summary>
public sealed record ImplementationComplexityChange(
    ResearchSubjectKey Subject,
    ImplementationComplexityChangeKind Kind,
    int? OldValue,
    int? NewValue,
    int? Delta,
    bool OldIsComplete,
    bool NewIsComplete,
    MethodIdentity? OldEvidenceMethod = null,
    MethodIdentity? NewEvidenceMethod = null,
    MethodImplementationProfile? OldProfile = null,
    MethodImplementationProfile? NewProfile = null,
    ImplementationComplexityPopulationContext? PopulationContext = null,
    ImplementationStructuralChange? StructuralChange = null,
    ImplementationStructuralChangeCohortContext? StructuralCohortContext = null);

public sealed record ImplementationComplexityDiff(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<ImplementationComplexityChange> Changes)
{
    public static ImplementationComplexityDiff Unavailable { get; } =
        new(
            false,
            "Normal-flow cyclomatic complexity was not requested for one or "
                + "both implementation-diff endpoints.",
            []);
}

public sealed record ImplementationAssemblyInput(
    ResolvedAssemblyReference Assembly,
    IAssemblyReferenceResolver Resolver,
    LibraryCallGraphAnalysisResult CallGraph,
    LibraryImplementationProfileAnalysisResult? ProfileAnalysis = null);

public sealed record ImplementationDiffResult(
    IReadOnlyList<ImplementationDiffMember> Members,
    ResearchComparison Research)
{
    public ImplementationComplexityDiff Complexity { get; init; } =
        ImplementationComplexityDiff.Unavailable;

    public bool IsEmpty => Members.Count == 0
        && (!Complexity.IsAvailable
            || !Complexity.Changes.Any(
                change => change.Kind != ImplementationComplexityChangeKind.Unchanged));
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

public sealed record PdbSourceComparisonInput(
    ResearchSubjectKey Subject,
    FindingInspection<string> OldInspection,
    FindingInspection<string> NewInspection);

/// <summary>
/// Product-owned implementation diff projection that joins C# source-shape and
/// IL/body changes by Research member identity.
/// </summary>
public static partial class ImplementationDiff
{
    public static readonly FindingDescriptor PdbSourceFailureDescriptor =
        new("source.pdb.failed", "PDB source acquisition failed");
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

    public static ImplementationDiffResult Compare(
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldInput);
        ArgumentNullException.ThrowIfNull(newInput);

        options ??= new ImplementationDiffOptions();
        var research = ResearchDiff.Compare(
            oldInput,
            newInput,
            new ResearchDiffOptions(
                ToResearchMechanisms(options.Mechanisms),
                TypeFilters: options.TypeFilters,
                MemberTargetIdentities: options.MemberTargetIdentities)
            {
                RetainedComparisonDescriptorIds =
                    RetainedComparisonDescriptorIds(options.Mechanisms),
            });
        return FromResearchComparison(research, options);
    }

    public static ImplementationDiffResult Compare(
        IReadOnlyList<ImplementationAssemblyInput> oldAssemblies,
        IReadOnlyList<ImplementationAssemblyInput> newAssemblies,
        ImplementationDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldAssemblies);
        ArgumentNullException.ThrowIfNull(newAssemblies);

        var oldContents = OpenAssemblyContents(oldAssemblies);
        try
        {
            var newContents = OpenAssemblyContents(newAssemblies);
            try
            {
                var result = Compare(
                    new ResearchDiffInput([])
                    {
                        AssemblyContents = oldContents,
                    },
                    new ResearchDiffInput([])
                    {
                        AssemblyContents = newContents,
                    },
                    options);
                return result with
                {
                    Complexity = ImplementationComplexityService.Execute(
                        new ImplementationComplexityComparisonRequest(
                            [.. oldAssemblies.Select(
                                static assembly => assembly.ProfileAnalysis)],
                            [.. newAssemblies.Select(
                                static assembly => assembly.ProfileAnalysis)],
                            options?.TypeFilters,
                            options?.MemberTargetIdentities)),
                };
            }
            finally
            {
                DisposeAssemblyContents(newContents);
            }
        }
        finally
        {
            DisposeAssemblyContents(oldContents);
        }
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

    static IReadOnlyList<ResearchAssemblyContent> OpenAssemblyContents(
        IReadOnlyList<ImplementationAssemblyInput> assemblies)
    {
        var contents = new List<ResearchAssemblyContent>(assemblies.Count);
        try
        {
            foreach (var assembly in assemblies)
            {
                ArgumentNullException.ThrowIfNull(assembly);
                ArgumentNullException.ThrowIfNull(assembly.Assembly);
                ArgumentNullException.ThrowIfNull(assembly.Resolver);
                ArgumentNullException.ThrowIfNull(assembly.CallGraph);
                var source = MetadataSource.OpenWithoutSymbols(
                    assembly.Assembly,
                    assembly.Resolver);
                try
                {
                    ValidateCallGraph(source, assembly.CallGraph);
                    if (assembly.ProfileAnalysis is not null)
                        ValidateProfileAnalysis(source, assembly.ProfileAnalysis);
                    contents.Add(new ResearchAssemblyContent(
                        source,
                        assembly.CallGraph));
                }
                catch
                {
                    source.Dispose();
                    throw;
                }
            }

            return contents;
        }
        catch
        {
            DisposeAssemblyContents(contents);
            throw;
        }
    }

    static void DisposeAssemblyContents(
        IReadOnlyList<ResearchAssemblyContent> contents)
    {
        foreach (var content in contents)
            content.Source.Dispose();
    }

    static void ValidateCallGraph(
        MetadataSource source,
        LibraryCallGraphAnalysisResult callGraph)
    {
        LibraryBodyModuleIdentity indexedModule = callGraph.ModuleIdentity;
        AssemblyReferenceIdentity? sourceIdentity = source.Reader.IsAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(source.Reader)
            : null;
        Guid sourceMvid = source.Reader.GetGuid(
            source.Reader.GetModuleDefinition().Mvid);
        if (AssemblyReferenceIdentity.EquivalentComparer.Equals(
                sourceIdentity,
                indexedModule.AssemblyIdentity)
            && sourceMvid == indexedModule.ModuleVersionId)
        {
            return;
        }

        throw new ArgumentException(
            $"The call-graph Analysis result for '{indexedModule.AssemblyIdentity?.Name ?? "standalone module"}' does not match "
            + $"assembly content '{source.AssemblyName}'.",
            nameof(callGraph));
    }

    static void ValidateProfileAnalysis(
        MetadataSource source,
        LibraryImplementationProfileAnalysisResult profileAnalysis)
    {
        LibraryBodyModuleIdentity indexedModule = profileAnalysis.Receipt.ModuleIdentity;
        AssemblyReferenceIdentity? sourceIdentity = source.Reader.IsAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(source.Reader)
            : null;
        Guid sourceMvid = source.Reader.GetGuid(
            source.Reader.GetModuleDefinition().Mvid);
        if (AssemblyReferenceIdentity.EquivalentComparer.Equals(
                sourceIdentity,
                indexedModule.AssemblyIdentity)
            && sourceMvid == indexedModule.ModuleVersionId)
        {
            return;
        }

        throw new ArgumentException(
            $"The implementation profile analysis for '{indexedModule.AssemblyIdentity?.Name ?? "standalone module"}' "
            + $"does not match assembly content '{source.AssemblyName}'.",
            nameof(profileAnalysis));
    }

    static ImmutableHashSet<string> RetainedComparisonDescriptorIds(
        ImplementationDiffMechanism mechanisms)
    {
        var descriptors = ImmutableHashSet.CreateBuilder<string>(
            StringComparer.Ordinal);
        if (mechanisms.HasFlag(ImplementationDiffMechanism.CSharp))
            descriptors.Add(CSharpFindings.LineDescriptor.Id);
        if (mechanisms.HasFlag(ImplementationDiffMechanism.IlBody))
            descriptors.Add(IlFindings.OperationDescriptor.Id);
        return descriptors.ToImmutable();
    }

    public static ImplementationDiffResult WithPdbSourceComparisons(
        ImplementationDiffResult result,
        IEnumerable<PdbSourceComparisonInput> inputs,
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
        return FromResearchComparison(research, options) with
        {
            Complexity = result.Complexity,
        };
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
                    PdbSourceFailureDescriptor,
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
            ? ToIlChanges(fallbackDisplay, subject, typed)
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

}
