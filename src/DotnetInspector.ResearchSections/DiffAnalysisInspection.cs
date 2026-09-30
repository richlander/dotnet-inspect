using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.ResearchSections;

[Flags]
public enum DiffAnalysisDocumentViews
{
    None = 0,
    Changes = 1,
    Summary = 2,
    Transitions = 4,
}

public enum DiffAnalysisDocumentOutcomeKind
{
    Compared,
    Unavailable,
    Failed,
}

public sealed record DiffAnalysisInspectionRequest(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    InspectionCapabilityCatalog Catalog,
    AnalysisSetValidationResult.Accepted Selection,
    DiffAnalysisInput Input,
    DiffAnalysisDocumentViews Views,
    LibraryApiDiffOutcome? LibraryApi = null);

public sealed record DiffAnalysisComparisonContext(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    AnalysisReportSurfaceKind Surface,
    DiffAnalysisDocumentViews Views,
    ImmutableArray<string> Analyses);

public sealed record DiffAnalysisDocumentOutcome(
    string Analysis,
    DiffAnalysisDocumentOutcomeKind Kind,
    ImmutableArray<string> Findings,
    string? Detail);

public sealed record DiffAnalysisSummaryRow(
    string Analysis,
    DiffAnalysisDocumentOutcomeKind Outcome,
    int Added,
    int Removed,
    int Changed,
    int Present,
    string? Detail);

public sealed record DiffAnalysisTransitionRow(
    string Transition,
    string Finding,
    string Target,
    string From,
    string To,
    string Old,
    string New,
    string? Detail,
    string? OldInspection = null,
    string? NewInspection = null);

public sealed record DiffAnalysisDocument
{
    private readonly ApiDiff? _apiChanges;

    internal DiffAnalysisDocument(
        DiffAnalysisComparisonContext comparison,
        ImmutableArray<DiffAnalysisDocumentOutcome> outcomes,
        ApiDiff? changes,
        ImmutableArray<DiffAnalysisSummaryRow>? summary,
        ImmutableArray<DiffAnalysisTransitionRow>? transitions,
        ApiDiff? apiResult = null,
        ImmutableArray<DiffAnalysisUnclassifiedApiChange>
            unclassifiedChanges = default,
        LibraryApiDiffOutcome? libraryApi = null)
    {
        Comparison = comparison;
        Outcomes = outcomes;
        _apiChanges = changes;
        ApiInspectionFailures = ProjectApiInspectionFailures(
            apiResult ?? changes);
        Changes = changes is null
            ? null
            : DiffAnalysisChangesDocument.Create(
                changes,
                unclassifiedChanges.IsDefault ? [] : unclassifiedChanges);
        Summary = summary;
        Transitions = transitions;
        LibraryApi = libraryApi;
    }

    public DiffAnalysisComparisonContext Comparison { get; }
    public ImmutableArray<DiffAnalysisDocumentOutcome> Outcomes { get; }
    public ImmutableArray<DiffAnalysisApiInspectionFailure>
        ApiInspectionFailures { get; }
    public DiffAnalysisChangesDocument? Changes { get; }
    public ImmutableArray<DiffAnalysisSummaryRow>? Summary { get; }
    public ImmutableArray<DiffAnalysisTransitionRow>? Transitions { get; }
    public LibraryApiDiffOutcome? LibraryApi { get; }

    public ApiDiff? GetApiChanges() => _apiChanges;

    private static ImmutableArray<DiffAnalysisApiInspectionFailure>
        ProjectApiInspectionFailures(ApiDiff? result)
        => result is null
            ? []
            : [
                .. result.InspectionFailures.Select(failure =>
                    new DiffAnalysisApiInspectionFailure(
                        failure.Side,
                        failure.Operation,
                        failure.SubjectToken,
                        failure.Mechanism,
                        failure.Kind,
                        failure.Detail,
                        failure.SubjectAssembly?.ToString(),
                        failure.DependencyAssembly?.ToString())),
            ];
}

/// <summary>Portable API Changes payload retained by a Diff analysis document.</summary>
public sealed record DiffAnalysisChangesDocument(
    ImmutableArray<DiffAnalysisChangedType> Types,
    int TotalBreaking,
    int TotalAdditive,
    int TotalPotentiallyBreaking)
{
    internal static DiffAnalysisChangesDocument Create(
        ApiDiff diff,
        ImmutableArray<DiffAnalysisUnclassifiedApiChange> unclassified)
        => new(
            [
                .. diff.TypeDiffs.Select(type => type.TypeFullName)
                    .Concat(unclassified.Select(change => change.Type))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(typeName => new DiffAnalysisChangedType(
                        typeName,
                        [
                            .. diff.TypeDiffs
                                .Where(type => type.TypeFullName == typeName)
                                .SelectMany(type => type.Changes)
                                .Select(change =>
                                    new DiffAnalysisApiChange(
                                        change.Kind,
                                        change.Classification,
                                        change.Message,
                                        change.OldValue,
                                        change.NewValue,
                                        change.Category,
                                        change.Subject is null
                                            ? null
                                            : new DiffAnalysisApiChangeSubject(
                                                change.Subject.Kind,
                                                change.Subject.OldType?.TypeFullName,
                                                change.Subject.NewType?.TypeFullName,
                                                change.Subject.OldMember?.Identity,
                                                change.Subject.NewMember?.Identity))),
                        ],
                        [
                            .. unclassified.Where(change =>
                                change.Type == typeName),
                        ])),
            ],
            diff.TotalBreaking,
            diff.TotalAdditive,
            diff.TotalPotentiallyBreaking);
}

public sealed record DiffAnalysisChangedType(
    string Type,
    ImmutableArray<DiffAnalysisApiChange> Changes,
    ImmutableArray<DiffAnalysisUnclassifiedApiChange> UnclassifiedChanges);

public enum DiffAnalysisUnclassifiedApiChangeKind
{
    TypeDefinitionChanged,
    MemberAdded,
    MemberRemoved,
    MemberChanged,
}

public sealed record DiffAnalysisUnclassifiedApiChange(
    string Type,
    string? Member,
    DiffAnalysisUnclassifiedApiChangeKind Kind,
    string Detail);

public sealed record DiffAnalysisApiChange(
    ChangeKind Kind,
    ChangeClassification Classification,
    string Message,
    string? OldValue,
    string? NewValue,
    ApiChangeCategory Category,
    DiffAnalysisApiChangeSubject? Subject);

public sealed record DiffAnalysisApiChangeSubject(
    ApiChangeSubjectKind Kind,
    string? OldType,
    string? NewType,
    string? OldMember,
    string? NewMember);

public sealed record DiffAnalysisApiInspectionFailure(
    string Side,
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail,
    string? SubjectAssembly,
    string? DependencyAssembly);

public static class DiffAnalysisInspection
{
    public static InspectionEnvelope<DiffAnalysisDocument> Execute(
        DiffAnalysisInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BeforeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AfterVersion);
        ArgumentNullException.ThrowIfNull(request.Catalog);
        ArgumentNullException.ThrowIfNull(request.Selection);
        ArgumentNullException.ThrowIfNull(request.Input);
        AnalysisReportSurfaceKind inputSurface = SurfaceOf(request.Input);
        if (request.Selection.Surface != inputSurface)
        {
            throw new ArgumentException(
                "The validated analysis surface does not match the resolved input.",
                nameof(request));
        }

        DiffAnalysisResult result = DiffAnalysisOperation.Execute(
            request.Catalog,
            request.Selection,
            request.Input);
        bool requiresTransitions = request.Views.HasFlag(
                DiffAnalysisDocumentViews.Summary)
            || request.Views.HasFlag(DiffAnalysisDocumentViews.Transitions);
        ImmutableArray<OutcomeProjection> projections = requiresTransitions
            ? [
                .. result.Outcomes.Select(outcome => new OutcomeProjection(
                    outcome,
                    ProjectTransitions(
                        outcome,
                        request.Input,
                        request.BeforeVersion,
                        request.AfterVersion))),
            ]
            : [
                .. result.Outcomes.Select(outcome =>
                    new OutcomeProjection(outcome, [])),
            ];
        ApiDiff? apiDiff = result.Outcomes
            .OfType<DiffAnalysisOutcome.Compared>()
            .Select(outcome => outcome.Comparison)
            .OfType<KeyedFindingComparison.Api>()
            .Select(comparison => comparison.Comparison.ApiDiff)
            .FirstOrDefault();
        ApiDiff? selectedApiDiff =
            apiDiff is not null
                && request.Views.HasFlag(DiffAnalysisDocumentViews.Changes)
            ? FilterApiChanges(apiDiff, request.Input)
            : null;
        ImmutableArray<DiffAnalysisUnclassifiedApiChange>
            unclassifiedApiChanges =
            selectedApiDiff is not null
                && result.Outcomes
                    .OfType<DiffAnalysisOutcome.Compared>()
                    .Select(outcome => outcome.Comparison)
                    .OfType<KeyedFindingComparison.Api>()
                    .Select(comparison => comparison.Comparison)
                    .FirstOrDefault() is { } apiComparison
            ? ProjectUnclassifiedApiChanges(
                apiComparison,
                selectedApiDiff,
                request.Input)
            : [];

        var document = new DiffAnalysisDocument(
            new DiffAnalysisComparisonContext(
                request.Name,
                request.BeforeVersion,
                request.AfterVersion,
                request.Selection.Surface,
                request.Views,
                [.. request.Selection.Analyses.Select(analysis => analysis.Id.Value)]),
            [.. result.Outcomes.Select(ProjectOutcome)],
            selectedApiDiff,
            request.Views.HasFlag(DiffAnalysisDocumentViews.Summary)
                ? [.. projections.Select(projection => ProjectSummary(
                    projection.Outcome,
                    projection.Transitions))]
                : null,
            request.Views.HasFlag(DiffAnalysisDocumentViews.Transitions)
                ? [.. projections.SelectMany(projection => projection.Transitions)]
                : null,
            apiResult: apiDiff,
            unclassifiedChanges: unclassifiedApiChanges,
            libraryApi: request.LibraryApi);

        return new(
            document,
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "No portable projection is available for an ordered Diff analysis endpoint pair."),
            [.. Diagnostics(result, apiDiff)]);
    }

    private static AnalysisReportSurfaceKind SurfaceOf(
        DiffAnalysisInput input)
        => input.MemberTargetIdentities switch
        {
            not null => AnalysisReportSurfaceKind.Member,
            null when input.TypeNames.Length > 0 =>
                AnalysisReportSurfaceKind.Type,
            _ => AnalysisReportSurfaceKind.Library,
        };

    private static ApiDiff FilterApiChanges(
        ApiDiff changes,
        DiffAnalysisInput input)
    {
        AnalysisReportSurfaceKind surface = SurfaceOf(input);
        if (surface == AnalysisReportSurfaceKind.Library)
            return changes;

        var typeNames = input.TypeNames.ToHashSet(StringComparer.Ordinal);
        List<TypeDiff> filtered = [];
        foreach (TypeDiff type in changes.TypeDiffs)
        {
            if (!typeNames.Contains(type.TypeFullName))
                continue;

            IReadOnlyList<ApiChange> selected = surface
                == AnalysisReportSurfaceKind.Type
                ? type.Changes
                : [
                    .. type.Changes.Where(change =>
                        MatchesMemberTarget(
                            type.TypeFullName,
                            change,
                            input)),
                ];
            if (selected.Count > 0)
                filtered.Add(new TypeDiff(type.TypeFullName, selected));
        }

        return new ApiDiff
        {
            TypeDiffs = filtered,
            InspectionFailures = changes.InspectionFailures,
            TotalBreaking = filtered.Sum(type => type.BreakingCount),
            TotalAdditive = filtered.Sum(type => type.AdditiveCount),
            TotalPotentiallyBreaking =
                filtered.Sum(type => type.PotentiallyBreakingCount),
        };
    }

    private static ImmutableArray<DiffAnalysisUnclassifiedApiChange>
        ProjectUnclassifiedApiChanges(
            ApiFindingComparison comparison,
            ApiDiff selectedChanges,
            DiffAnalysisInput input)
    {
        AnalysisReportSurfaceKind surface = SurfaceOf(input);
        var selectedTypes = input.TypeNames.ToHashSet(StringComparer.Ordinal);
        var rows = ImmutableArray.CreateBuilder<
            DiffAnalysisUnclassifiedApiChange>();

        if (surface != AnalysisReportSurfaceKind.Member
            && comparison.Types
            is FindingComparison<ApiTypeHandle>.Complete types)
        {
            foreach (PairFinding<ApiTypeHandle> pair in types.Pairs)
            {
                if (pair is not PairFinding<ApiTypeHandle>.Changed changed)
                    continue;

                string typeName = changed.New.Payload.TypeFullName;
                if (!IncludesType(typeName)
                    || HasClassifiedTypeChange(selectedChanges, typeName))
                {
                    continue;
                }

                rows.Add(new(
                    typeName,
                    Member: null,
                    DiffAnalysisUnclassifiedApiChangeKind.TypeDefinitionChanged,
                    "Type definition changed without a compatibility classification."));
            }
        }

        if (comparison.Members
            is FindingComparison<ApiMemberHandle>.Complete members)
        {
            foreach (PairFinding<ApiMemberHandle> pair in members.Pairs)
            {
                (ApiMemberHandle? oldMember, ApiMemberHandle? newMember,
                    DiffAnalysisUnclassifiedApiChangeKind kind) = pair switch
                {
                    PairFinding<ApiMemberHandle>.Added added =>
                        (null, added.New.Payload,
                            DiffAnalysisUnclassifiedApiChangeKind.MemberAdded),
                    PairFinding<ApiMemberHandle>.Removed removed =>
                        (removed.Old.Payload, null,
                            DiffAnalysisUnclassifiedApiChangeKind.MemberRemoved),
                    PairFinding<ApiMemberHandle>.Changed changed =>
                        (changed.Old.Payload, changed.New.Payload,
                            DiffAnalysisUnclassifiedApiChangeKind.MemberChanged),
                    _ => (null, null, default),
                };
                if (oldMember is null && newMember is null)
                    continue;

                string typeName =
                    newMember?.TypeFullName ?? oldMember!.TypeFullName;
                if (!IncludesType(typeName)
                    || !IncludesMember(oldMember, newMember)
                    || HasClassifiedMemberChange(
                        selectedChanges,
                        oldMember,
                        newMember)
                    || HasClassifiedContainingTypeChange(
                        selectedChanges,
                        typeName,
                        kind))
                {
                    continue;
                }

                string member =
                    newMember?.Identity ?? oldMember!.Identity;
                string transition = kind switch
                {
                    DiffAnalysisUnclassifiedApiChangeKind.MemberAdded =>
                        "added",
                    DiffAnalysisUnclassifiedApiChangeKind.MemberRemoved =>
                        "removed",
                    DiffAnalysisUnclassifiedApiChangeKind.MemberChanged =>
                        "changed",
                    _ => throw new InvalidOperationException(
                        "The unclassified member transition is unknown."),
                };
                rows.Add(new(
                    typeName,
                    member,
                    kind,
                    $"Member {transition} without a compatibility classification."));
            }
        }

        return [
            .. rows.OrderBy(row => row.Type, StringComparer.Ordinal)
                .ThenBy(row => row.Member, StringComparer.Ordinal)
                .ThenBy(row => row.Kind),
        ];

        bool IncludesType(string typeName)
            => selectedTypes.Count == 0 || selectedTypes.Contains(typeName);

        bool IncludesMember(
            ApiMemberHandle? oldMember,
            ApiMemberHandle? newMember)
            => input.MemberTargetIdentities is null
                || oldMember is not null
                    && input.MemberTargetIdentities.Contains(oldMember.Identity)
                || newMember is not null
                    && input.MemberTargetIdentities.Contains(newMember.Identity);
    }

    private static bool HasClassifiedTypeChange(
        ApiDiff changes,
        string typeName)
        => changes.TypeDiffs
            .Where(type => type.TypeFullName == typeName)
            .SelectMany(type => type.Changes)
            .Any(change =>
                change.Subject?.Kind == ApiChangeSubjectKind.Type);

    private static bool HasClassifiedMemberChange(
        ApiDiff changes,
        ApiMemberHandle? oldMember,
        ApiMemberHandle? newMember)
        => changes.TypeDiffs
            .SelectMany(type => type.Changes)
            .Any(change =>
                change.Subject?.Kind == ApiChangeSubjectKind.Member
                && (oldMember is not null
                    && change.Subject.OldIdentity == oldMember.Identity
                    || newMember is not null
                    && change.Subject.NewIdentity == newMember.Identity));

    private static bool HasClassifiedContainingTypeChange(
        ApiDiff changes,
        string typeName,
        DiffAnalysisUnclassifiedApiChangeKind memberChangeKind)
    {
        ChangeKind? typeChangeKind = memberChangeKind switch
        {
            DiffAnalysisUnclassifiedApiChangeKind.MemberAdded =>
                ChangeKind.TypeAdded,
            DiffAnalysisUnclassifiedApiChangeKind.MemberRemoved =>
                ChangeKind.TypeRemoved,
            _ => null,
        };
        return typeChangeKind is not null
            && changes.TypeDiffs
                .Where(type => type.TypeFullName == typeName)
                .SelectMany(type => type.Changes)
                .Any(change => change.Kind == typeChangeKind);
    }

    private static bool MatchesMemberTarget(
        string typeFullName,
        ApiChange change,
        DiffAnalysisInput input)
    {
        IReadOnlySet<string> targetIdentities =
            input.MemberTargetIdentities
            ?? throw new InvalidOperationException(
                "Member targets were not resolved.");
        return change.Subject?.Kind == ApiChangeSubjectKind.Member
            ? MatchesHandle(change.Subject?.OldMember, targetIdentities)
                || MatchesHandle(change.Subject?.NewMember, targetIdentities)
            : IsWholeTypeChange(change.Kind)
                && input.TypeNames.Contains(
                    typeFullName,
                    StringComparer.Ordinal);
    }

    private static bool IsWholeTypeChange(ChangeKind kind)
        => kind is ChangeKind.TypeAdded or ChangeKind.TypeRemoved;

    private sealed record OutcomeProjection(
        DiffAnalysisOutcome Outcome,
        ImmutableArray<DiffAnalysisTransitionRow> Transitions);

    private static DiffAnalysisDocumentOutcome ProjectOutcome(
        DiffAnalysisOutcome outcome)
        => new(
            outcome.Identity,
            outcome switch
            {
                DiffAnalysisOutcome.Compared =>
                    DiffAnalysisDocumentOutcomeKind.Compared,
                DiffAnalysisOutcome.Unavailable =>
                    DiffAnalysisDocumentOutcomeKind.Unavailable,
                DiffAnalysisOutcome.Failed =>
                    DiffAnalysisDocumentOutcomeKind.Failed,
                _ => throw new InvalidOperationException(
                    "Unknown Diff analysis outcome."),
            },
            [.. outcome.Participation.Descriptors.Select(
                descriptor => descriptor.Id)],
            outcome switch
            {
                DiffAnalysisOutcome.Unavailable unavailable =>
                    unavailable.Reason,
                DiffAnalysisOutcome.Failed failed =>
                    failed.Diagnostic,
                _ => null,
            });

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        DiffAnalysisResult result,
        ApiDiff? apiDiff)
    {
        foreach (DiffAnalysisOutcome outcome in result.Outcomes)
        {
            switch (outcome)
            {
                case DiffAnalysisOutcome.Unavailable unavailable:
                    yield return new InspectionDiagnostic(
                        "diff-analysis.unavailable",
                        InspectionDiagnosticSeverity.Warning,
                        $"Analysis '{outcome.Identity}' was not compared: "
                            + unavailable.Reason,
                        outcome.Identity);
                    break;
                case DiffAnalysisOutcome.Failed failed:
                    yield return new InspectionDiagnostic(
                        "diff-analysis.failed",
                        InspectionDiagnosticSeverity.Error,
                        $"Analysis '{outcome.Identity}' failed: "
                            + failed.Diagnostic,
                        outcome.Identity);
                    break;
            }
        }

        if (apiDiff is null)
            yield break;

        foreach (ApiDiffInspectionFailure failure in apiDiff.InspectionFailures)
        {
            yield return new InspectionDiagnostic(
                "diff-analysis.api-inspection-failure",
                InspectionDiagnosticSeverity.Error,
                $"{failure.Side} API inspection {failure.Operation} failed: "
                    + failure.Detail,
                $"0x{failure.SubjectToken:x8}");
        }
    }

    private static DiffAnalysisSummaryRow ProjectSummary(
        DiffAnalysisOutcome outcome,
        IReadOnlyList<DiffAnalysisTransitionRow> rows)
    {
        int Count(PairKind kind)
            => rows.Count(row => row.Transition == $"PairFinding.{kind}");
        return outcome switch
        {
            DiffAnalysisOutcome.Compared => new DiffAnalysisSummaryRow(
                outcome.Identity,
                DiffAnalysisDocumentOutcomeKind.Compared,
                Count(PairKind.Added),
                Count(PairKind.Removed),
                Count(PairKind.Changed),
                Count(PairKind.Present),
                rows.FirstOrDefault(row =>
                    row.Transition == "FindingComparison.Failed")?.Detail
                    ?? (outcome is DiffAnalysisOutcome.Compared
                        {
                            Comparison: KeyedFindingComparison.Api
                            {
                                Comparison.ApiDiff.InspectionFailures.Count: > 0
                                    and var failures,
                            },
                        }
                        ? $"incomplete: {failures} metadata inspection failure(s)"
                        : null)),
            DiffAnalysisOutcome.Unavailable unavailable =>
                new DiffAnalysisSummaryRow(
                    outcome.Identity,
                    DiffAnalysisDocumentOutcomeKind.Unavailable,
                    0,
                    0,
                    0,
                    0,
                    unavailable.Reason),
            DiffAnalysisOutcome.Failed failed => new DiffAnalysisSummaryRow(
                outcome.Identity,
                DiffAnalysisDocumentOutcomeKind.Failed,
                0,
                0,
                0,
                0,
                failed.Diagnostic),
            _ => throw new InvalidOperationException(
                "Unknown Diff analysis outcome."),
        };
    }

    private static ImmutableArray<DiffAnalysisTransitionRow> ProjectTransitions(
        DiffAnalysisOutcome outcome,
        DiffAnalysisInput input,
        string beforeVersion,
        string afterVersion)
    {
        string descriptors = string.Join(
            ", ",
            outcome.Participation.Descriptors.Select(descriptor => descriptor.Id));
        switch (outcome)
        {
            case DiffAnalysisOutcome.Unavailable unavailable:
                return [new(
                    "Unavailable",
                    descriptors,
                    outcome.Identity,
                    beforeVersion,
                    afterVersion,
                    "n/a",
                    "n/a",
                    unavailable.Reason)];
            case DiffAnalysisOutcome.Failed failed:
                return [new(
                    "Failed",
                    descriptors,
                    outcome.Identity,
                    beforeVersion,
                    afterVersion,
                    "n/a",
                    "n/a",
                    failed.Diagnostic)];
        }

        var compared = (DiffAnalysisOutcome.Compared)outcome;
        var rows = ImmutableArray.CreateBuilder<DiffAnalysisTransitionRow>();
        foreach (FindingDescriptor descriptor in outcome.Participation.Descriptors)
        {
            IEnumerable<DiffAnalysisTransitionRow> descriptorRows =
                compared.Comparison switch
                {
                    KeyedFindingComparison.Api api =>
                        ApiTransitionRows(
                            api.Comparison,
                            descriptor,
                            input,
                            beforeVersion,
                            afterVersion),
                    KeyedFindingComparison.Retained retained =>
                        RetainedTransitionRows(
                            retained.Comparisons,
                            descriptor,
                            beforeVersion,
                            afterVersion),
                    _ => throw new InvalidOperationException(
                        "Unknown keyed Finding comparison."),
                };
            rows.AddRange(descriptorRows);
        }
        return rows.ToImmutable();
    }

    private static IEnumerable<DiffAnalysisTransitionRow> ApiTransitionRows(
        ApiFindingComparison comparison,
        FindingDescriptor descriptor,
        DiffAnalysisInput input,
        string beforeVersion,
        string afterVersion)
        => ApiComparisonTransitionRows(
                comparison,
                descriptor,
                input,
                beforeVersion,
                afterVersion)
            .Where(row => row.Transition != "FindingComparison.Complete");

    private static IEnumerable<DiffAnalysisTransitionRow>
        ApiComparisonTransitionRows(
            ApiFindingComparison comparison,
            FindingDescriptor descriptor,
            DiffAnalysisInput input,
            string beforeVersion,
            string afterVersion)
    {
        if (descriptor.Id == MetadataFindings.TypeDescriptor.Id)
        {
            return ComparisonRows(
                    comparison.Types,
                    MetadataFindings.TypeDescriptor,
                    "API surface",
                    beforeVersion,
                    afterVersion,
                    emitEmptyComparison: false,
                    pair => ToTypeTransitionRow(
                        pair,
                        beforeVersion,
                        afterVersion),
                    input.SelectionSurface() == AnalysisReportSurfaceKind.Library
                        ? null
                        : pair => input.TypeNames.Contains(TypeTarget(pair)))
                .OrderBy(row => row.Target, StringComparer.Ordinal);
        }
        if (descriptor.Id == MetadataFindings.MemberDescriptor.Id)
        {
            Func<PairFinding<ApiMemberHandle>, bool>? include =
                input.SelectionSurface() switch
                {
                    AnalysisReportSurfaceKind.Library => null,
                    AnalysisReportSurfaceKind.Type =>
                        pair => input.TypeNames.Contains(MemberTypeTarget(pair)),
                    _ => pair => MatchesMemberPair(pair, input),
                };
            return ComparisonRows(
                    comparison.Members,
                    MetadataFindings.MemberDescriptor,
                    "API surface",
                    beforeVersion,
                    afterVersion,
                    emitEmptyComparison: false,
                    pair => ToMemberTransitionRow(
                        pair,
                        beforeVersion,
                        afterVersion),
                    include)
                .OrderBy(row => row.Target, StringComparer.Ordinal);
        }
        throw new InvalidOperationException(
            $"The api comparison carries no '{descriptor.Id}' Findings.");
    }

    private static IEnumerable<DiffAnalysisTransitionRow> RetainedTransitionRows(
        RetainedFindingComparisonSet comparisons,
        FindingDescriptor descriptor,
        string beforeVersion,
        string afterVersion)
    {
        IEnumerable<DiffAnalysisTransitionRow> Rows<T>(
            bool emitEmptyComparison,
            Func<ResearchSubjectKey, PairFinding<T>, string, string,
                DiffAnalysisTransitionRow> toTransitionRow)
            where T : notnull
            => comparisons.Get<T>(descriptor)
                .SelectMany(comparison => RetainedComparisonRows(
                    comparison,
                    beforeVersion,
                    afterVersion,
                    emitEmptyComparison,
                    toTransitionRow))
                .OrderBy(row => row.Target, StringComparer.Ordinal)
                .ThenBy(row => row.Transition, StringComparer.Ordinal);

        return descriptor.Id switch
        {
            var id when id == MetadataFindings.AttributeDescriptor.Id =>
                Rows<ApiAttributeHandle>(
                    emitEmptyComparison: false,
                    (_, pair, before, after) =>
                        ToAttributeTransitionRow(pair, before, after)),
            var id when id == AnalysisFindings.AllocationDescriptor.Id =>
                Rows<AllocationOccurrence>(
                    emitEmptyComparison: false,
                    ToAllocationTransitionRow),
            var id when id == AnalysisFindings.CallSiteDescriptor.Id =>
                Rows<DirectCall>(
                    emitEmptyComparison: false,
                    ToCallSiteTransitionRow),
            var id when id == AnalysisFindings.UnsafetyDescriptor.Id =>
                Rows<UnsafetyOccurrence>(
                    emitEmptyComparison: false,
                    ToUnsafetyTransitionRow),
            var id when id == CSharpFindings.LineDescriptor.Id =>
                Rows<CSharpCanonicalLine>(
                    emitEmptyComparison: true,
                    ToCSharpTransitionRow),
            var id when id == IlFindings.OperationDescriptor.Id =>
                Rows<CanonicalIlOperation>(
                    emitEmptyComparison: true,
                    ToIlTransitionRow),
            _ => throw new InvalidOperationException(
                $"No Transitions projection is registered for '{descriptor.Id}'."),
        };
    }

    internal static IEnumerable<DiffAnalysisTransitionRow>
        RetainedComparisonRows<T>(
            RetainedFindingComparison<T> retained,
            string beforeVersion,
            string afterVersion,
            bool emitEmptyComparison,
            Func<ResearchSubjectKey, PairFinding<T>, string, string,
                DiffAnalysisTransitionRow> toTransitionRow)
        where T : notnull
        => ComparisonRows(
            retained.Comparison,
            retained.Descriptor,
            retained.Subject.Display,
            beforeVersion,
            afterVersion,
            emitEmptyComparison,
            pair => toTransitionRow(
                retained.Subject,
                pair,
                beforeVersion,
                afterVersion));

    private static IEnumerable<DiffAnalysisTransitionRow> ComparisonRows<T>(
        FindingComparison<T> comparison,
        FindingDescriptor descriptor,
        string target,
        string beforeVersion,
        string afterVersion,
        bool emitEmptyComparison,
        Func<PairFinding<T>, DiffAnalysisTransitionRow> toTransitionRow,
        Func<PairFinding<T>, bool>? includePair = null)
        where T : notnull
    {
        if (comparison.Value is FindingComparison<T>.Failed failed)
        {
            string oldInspection = InspectionState(failed.OldInspection);
            string newInspection = InspectionState(failed.NewInspection);
            yield return new(
                "FindingComparison.Failed",
                descriptor.Id,
                target,
                beforeVersion,
                afterVersion,
                oldInspection,
                newInspection,
                failed.Failure,
                oldInspection,
                newInspection);
            yield break;
        }

        var complete = (FindingComparison<T>.Complete)comparison.Value;
        PairFinding<T>[] pairs = includePair is null
            ? [.. complete.Pairs]
            : [.. complete.Pairs.Where(includePair)];
        string completeOldInspection = InspectionState(complete.OldInspection);
        string completeNewInspection = InspectionState(complete.NewInspection);
        if (pairs.Length == 0)
        {
            if (!emitEmptyComparison
                && complete.Transition.IsSameTopology)
            {
                yield break;
            }

            yield return new(
                "FindingComparison.Complete",
                descriptor.Id,
                target,
                beforeVersion,
                afterVersion,
                completeOldInspection,
                completeNewInspection,
                null,
                completeOldInspection,
                completeNewInspection);
            yield break;
        }

        foreach (PairFinding<T> pair in pairs)
        {
            yield return toTransitionRow(pair) with
            {
                OldInspection = completeOldInspection,
                NewInspection = completeNewInspection,
            };
        }
    }

    private static string InspectionState<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection.Value switch
        {
            FindingInspection<T>.Complete => "complete",
            FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.SubjectAbsent,
                } => "subject-absent",
            FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.NoApplicableInput,
                } => "no-applicable-input",
            FindingInspection<T>.Absent absent =>
                throw new InvalidOperationException(
                    "Unsupported Finding inspection absence kind "
                        + $"'{absent.Kind}'."),
            FindingInspection<T>.Failed => "failed",
            _ => throw new InvalidOperationException(
                "Finding inspection returned an unknown outcome."),
        };

    private static bool MatchesMemberPair(
        PairFinding<ApiMemberHandle> pair,
        DiffAnalysisInput input)
    {
        var oldHandle = OldSide(pair)?.Payload;
        var newHandle = NewSide(pair)?.Payload;
        var typeName = newHandle?.TypeFullName ?? oldHandle?.TypeFullName;
        IReadOnlySet<string> targetIdentities =
            input.MemberTargetIdentities
            ?? throw new InvalidOperationException(
                "Member targets were not resolved.");
        return typeName is not null
            && input.TypeNames.Contains(typeName)
            && (MatchesHandle(oldHandle, targetIdentities)
                || MatchesHandle(newHandle, targetIdentities));
    }

    private static bool MatchesHandle(
        ApiMemberHandle? handle,
        IReadOnlySet<string> targetIdentities)
        => handle is not null
            && ((handle.StableSelector is { } stable
                    && targetIdentities.Contains(stable))
                || (handle.CanonicalSignature is { } canonical
                    && targetIdentities.Contains(canonical))
                || targetIdentities.Contains(handle.Identity));

    private static DiffAnalysisTransitionRow ToTypeTransitionRow(
        PairFinding<ApiTypeHandle> pair,
        string beforeVersion,
        string afterVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            TypeTarget(pair),
            beforeVersion,
            afterVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    private static DiffAnalysisTransitionRow ToMemberTransitionRow(
        PairFinding<ApiMemberHandle> pair,
        string beforeVersion,
        string afterVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            MemberTarget(pair),
            beforeVersion,
            afterVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    private static DiffAnalysisTransitionRow ToAttributeTransitionRow(
        PairFinding<ApiAttributeHandle> pair,
        string beforeVersion,
        string afterVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            AttributeTarget(pair),
            beforeVersion,
            afterVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    private static DiffAnalysisTransitionRow ToAllocationTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<AllocationOccurrence> pair,
        string beforeVersion,
        string afterVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(
                subject.Display,
                newFinding ?? oldFinding!),
            beforeVersion,
            afterVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail ?? newFinding?.Detail ?? oldFinding?.Detail);
    }

    private static DiffAnalysisTransitionRow ToCallSiteTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<DirectCall> pair,
        string beforeVersion,
        string afterVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(
                subject.Display,
                newFinding ?? oldFinding!),
            beforeVersion,
            afterVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail);
    }

    private static DiffAnalysisTransitionRow ToUnsafetyTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<UnsafetyOccurrence> pair,
        string beforeVersion,
        string afterVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            FindingTargetFormatter.Format(
                subject.Display,
                newFinding ?? oldFinding!),
            beforeVersion,
            afterVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail ?? newFinding?.Detail ?? oldFinding?.Detail);
    }

    private static DiffAnalysisTransitionRow ToCSharpTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<CSharpCanonicalLine> pair,
        string beforeVersion,
        string afterVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            $"{subject.Display} :: line {(newFinding ?? oldFinding!).Payload.Line}",
            beforeVersion,
            afterVersion,
            oldFinding?.Payload.Text ?? "absent",
            newFinding?.Payload.Text ?? "absent",
            pair.Detail);
    }

    private static DiffAnalysisTransitionRow ToIlTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<CanonicalIlOperation> pair,
        string beforeVersion,
        string afterVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            $"{subject.Display} :: IL_{(newFinding ?? oldFinding!).Payload.Offset:X4}",
            beforeVersion,
            afterVersion,
            FormatIlFinding(oldFinding),
            FormatIlFinding(newFinding),
            pair.Detail);
    }

    private static string FormatIlFinding(
        Finding<CanonicalIlOperation>? finding)
        => finding is null
            ? "absent"
            : $"IL_{finding.Payload.Offset:X4} {finding.Payload.Display}";

    private static string TypeTarget(PairFinding<ApiTypeHandle> pair)
        => (NewSide(pair) ?? OldSide(pair))!.Payload.TypeFullName;

    private static string MemberTarget(PairFinding<ApiMemberHandle> pair)
    {
        var handle = (NewSide(pair) ?? OldSide(pair))!.Payload;
        return $"{handle.TypeFullName}."
            + $"{handle.StableSelector ?? handle.Identity}";
    }

    private static string MemberTypeTarget(PairFinding<ApiMemberHandle> pair)
        => (NewSide(pair) ?? OldSide(pair))!.Payload.TypeFullName;

    private static string AttributeTarget(PairFinding<ApiAttributeHandle> pair)
    {
        var handle = (NewSide(pair) ?? OldSide(pair))!.Payload;
        return $"{handle.TypeFullName} [{handle.Attribute}]";
    }

    private static Finding<T>? OldSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added => null,
            PairFinding<T>.Removed =>
                ((PairFinding<T>.Removed)pair.Value!).Old,
            PairFinding<T>.Present =>
                ((PairFinding<T>.Present)pair.Value!).Old,
            PairFinding<T>.Changed =>
                ((PairFinding<T>.Changed)pair.Value!).Old,
        };

    private static Finding<T>? NewSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added =>
                ((PairFinding<T>.Added)pair.Value!).New,
            PairFinding<T>.Removed => null,
            PairFinding<T>.Present =>
                ((PairFinding<T>.Present)pair.Value!).New,
            PairFinding<T>.Changed =>
                ((PairFinding<T>.Changed)pair.Value!).New,
        };

    private static AnalysisReportSurfaceKind SelectionSurface(
        this DiffAnalysisInput input)
        => input.MemberTargetIdentities is not null
            ? AnalysisReportSurfaceKind.Member
            : input.TypeNames.Length > 0
                ? AnalysisReportSurfaceKind.Type
                : AnalysisReportSurfaceKind.Library;
}

[JsonSourceGenerationOptions(
    Converters = [typeof(LibraryApiDiffOutcomeJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiffAnalysisDocument))]
[JsonSerializable(typeof(InspectionEnvelope<DiffAnalysisDocument>))]
public partial class DiffAnalysisInspectionJsonContext : JsonSerializerContext;
