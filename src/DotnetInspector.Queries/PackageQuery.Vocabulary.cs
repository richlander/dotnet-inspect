using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

public enum PackageQueryTermRole
{
    Population,
    Inspection,
}

public enum PackageQueryTermControlKind
{
    Input,
    Toggle,
    Choice,
}

public sealed record PackageQueryTermOptionDescriptor(
    string Value,
    string Label,
    string Summary);

/// <summary>One product-owned Package Query term.</summary>
public sealed record PackageQueryTermDescriptor(
    string Key,
    string Label,
    string Summary,
    int Weight,
    PackageQueryAcquisitionTier Tier,
    PackageQueryExecutionClass ExecutionClass,
    ImmutableArray<string> Operators,
    string ValueKind,
    string ExampleValue,
    PackageQueryTermRole Role,
    PackageQueryTermControlKind ControlKind)
{
    public ImmutableArray<PackageQueryTermOptionDescriptor> Options { get; init; } = [];
    public string? SelectionGroupId { get; init; }
    public bool CombinesWithinSelectionGroup { get; init; }
    public string? ReplacementGroupId { get; init; }
    public string? DisplayGroupId { get; init; }
    public string? DisplayGroupLabel { get; init; }
}

internal enum PackageQueryPredicateKind
{
    Package,
    Prefix,
    Prerelease,
    NoDependencies,
    CrossPrefixDependencies,
    DependencyTarget,
    Depends,
    DependsPrefix,
    DependsTransitive,
    DependencyDepth,
    DependsEcosystem,
    Downloads,
    License,
    Readme,
    Tool,
    ToolFormat,
    AssemblyReference,
    Skill,
}

internal sealed record PackageQueryPredicate(
    PackageQueryPredicateKind Kind,
    string? Text = null,
    long Number = 0,
    bool Flag = false,
    PackageQueryEcosystemMembershipDeclaration? EcosystemMembership = null,
    PackagePrefixDeclaration? PackagePrefix = null)
{
    internal bool RequiresPackageContent =>
        Kind is PackageQueryPredicateKind.ToolFormat
            or PackageQueryPredicateKind.AssemblyReference
            or PackageQueryPredicateKind.Skill;
}

internal sealed class PackageQueryKeyDeclaration(
    PackageQueryTermDescriptor descriptor,
    Func<PortableQueryOperator, string, PortableQueryBinding<PackageQueryPredicate>>
        bind)
    : PortableQueryKeyDeclaration<PackageQueryPredicate>
{
    public PackageQueryTermDescriptor Descriptor { get; } = descriptor;

    public override string Key => Descriptor.Key;

    public override string? Family => Descriptor.SelectionGroupId;

    public override PortableQueryFamilyKind FamilyKind =>
        Descriptor.CombinesWithinSelectionGroup
            ? PortableQueryFamilyKind.Combining
            : PortableQueryFamilyKind.Exclusive;

    public override bool AdmitsOperator(PortableQueryOperator @operator) =>
        Descriptor.Operators.Contains(
            PortableQueryModel.TextOf(@operator),
            StringComparer.Ordinal);

    public override PortableQueryBinding<PackageQueryPredicate> Bind(
        PortableQueryOperator @operator,
        string value) => bind(@operator, value);
}

internal sealed class PackageQueryDimensionDeclaration(
    string dimension,
    Func<int, IReadOnlyList<PortableQueryResolvedTerm<PackageQueryPredicate>>, bool>
        admits)
    : PortableQueryDimensionDeclaration<PackageQueryPredicate>
{
    public override string Dimension { get; } = dimension;

    public override bool Admits(
        int requestedMaximum,
        IReadOnlyList<PortableQueryResolvedTerm<PackageQueryPredicate>> boundTerms) =>
        admits(requestedMaximum, boundTerms);
}

internal sealed class PackageQueryVocabulary
    : PortableQueryVocabulary<PackageQueryPredicate, PackageQueryPlan>
{
    internal const string VocabularyIdentity = "package-query/v1";
    internal const string PopulationFamily = "population";
    internal const string PrereleaseFamily = "prerelease";
    internal const string DependenciesFamily = "dependencies";
    internal const string DependencyTargetFamily = "dependency-target";
    internal const string DependencyDepthFamily = "dependency-depth";
    internal const string DownloadsFamily = "downloads";
    internal const string ToolFormatFamily = "tool-format";
    internal const string CandidatesDimension = "candidates";
    internal const string MatchesDimension = "matches";

    private readonly IReadOnlyDictionary<string, PackageQueryKeyDeclaration> _keys;
    private readonly IReadOnlyDictionary<string, PackageQueryDimensionDeclaration>
        _dimensions;

    internal PackageQueryVocabulary(
        IEnumerable<PackageQueryKeyDeclaration> keys)
    {
        _keys = keys.ToDictionary(key => key.Key, StringComparer.Ordinal);
        _dimensions = new Dictionary<string, PackageQueryDimensionDeclaration>(
            StringComparer.Ordinal)
        {
            [CandidatesDimension] = new(
                CandidatesDimension,
                static (maximum, terms) =>
                {
                    if (maximum is <= 0 or > PackageQuery.MaximumCandidates)
                        return false;
                    if (terms.Any(term =>
                        term.Predicate.Kind == PackageQueryPredicateKind.Package))
                        return maximum == 1;
                    if (terms.Any(term =>
                            term.Predicate.Kind
                                == PackageQueryPredicateKind.DependsTransitive)
                        && maximum
                            > PackageQuery.MaximumNuspecExpensiveCandidates)
                    {
                        return false;
                    }
                    return maximum <= PackageQuery.MaximumPackageContentCandidates
                        || !terms.Any(term =>
                            term.Predicate.RequiresPackageContent);
                }),
            [MatchesDimension] = new(
                MatchesDimension,
                static (maximum, _) =>
                    maximum is > 0 and <= PackageQuery.MaximumCandidates),
        };
    }

    public override string Identity => VocabularyIdentity;

    public override IReadOnlyList<string> RequiredTermFamilies =>
        [PopulationFamily, PrereleaseFamily];

    public override IReadOnlyList<string> RequiredDimensions =>
        [CandidatesDimension];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<PackageQueryPredicate>? declaration)
    {
        bool found = _keys.TryGetValue(key, out PackageQueryKeyDeclaration? value);
        declaration = value;
        return found;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<PackageQueryPredicate>? declaration)
    {
        bool found = _dimensions.TryGetValue(
            dimension,
            out PackageQueryDimensionDeclaration? value);
        declaration = value;
        return found;
    }

    public override bool AdmitsStageKind(RowSelectionStageKind kind) =>
        kind is RowSelectionStageKind.Head
            or RowSelectionStageKind.Tail
            or RowSelectionStageKind.Window;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        purpose = default;
        return false;
    }

    public override bool IsOrderable(string key) => false;

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<PackageQueryPredicate> first,
        PortableQueryResolvedTerm<PackageQueryPredicate> second)
    {
        PackageQueryPredicateKind firstKind = first.Predicate.Kind;
        PackageQueryPredicateKind secondKind = second.Predicate.Kind;
        if (firstKind is PackageQueryPredicateKind.Tool
            && secondKind is PackageQueryPredicateKind.ToolFormat
            || firstKind is PackageQueryPredicateKind.ToolFormat
                && secondKind is PackageQueryPredicateKind.Tool)
        {
            return false;
        }

        return firstKind != secondKind
            || firstKind is not (
                PackageQueryPredicateKind.Downloads
                or PackageQueryPredicateKind.DependencyDepth
                or PackageQueryPredicateKind.Prerelease)
            || first.Predicate == second.Predicate;
    }

    public override PackageQueryPlan CreatePlan(
        PortableQueryResolvedIntent<PackageQueryPredicate> resolved)
    {
        int maximumCandidates = resolved.Bounds.Single(bound =>
            bound.Dimension == CandidatesDimension).RequestedMaximum;
        int? maximumMatches = resolved.Bounds.FirstOrDefault(bound =>
            bound.Dimension == MatchesDimension)?.RequestedMaximum;
        bool includePrerelease = resolved.Terms.Any(term =>
            term.Predicate is
            {
                Kind: PackageQueryPredicateKind.Prerelease,
                Flag: true,
            });
        PortableQueryResolvedTerm<PackageQueryPredicate> population =
            resolved.Terms.Single(term =>
                term.Predicate.Kind is PackageQueryPredicateKind.Package
                    or PackageQueryPredicateKind.Prefix);
        SourceSelector input;
        string scope;
        if (population.Predicate.Kind == PackageQueryPredicateKind.Package)
        {
            scope = population.Predicate.Text!;
            input = new SourceSelector.Package(new PackageCoordinate(scope));
        }
        else
        {
            scope = population.Predicate.Text!;
            input = new SourceSelector.PackagePrefix(
                new PackagePrefixRequest(
                    scope,
                    maximumCandidates,
                    includePrerelease));
        }

        ImmutableArray<BoundPackageQueryTerm> terms =
        [
            .. resolved.Terms
                .Where(term => term.Predicate.Kind is not (
                    PackageQueryPredicateKind.Package
                    or PackageQueryPredicateKind.Prefix
                    or PackageQueryPredicateKind.Prerelease))
                .Select(term => new BoundPackageQueryTerm(
                    PackageQuery.Descriptor(term.Term.Key),
                    term.Term,
                    term.Predicate)),
        ];
        RowSelectionIntent<string> selection = RowSelectionIntent<string>.Create(
        [
            .. resolved.Stages.Select(ToRowSelectionOperation),
        ]);
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [.. resolved.Terms.Select(term => term.Term)],
            [.. resolved.Bounds],
            [.. resolved.Stages],
            []);

        return new PackageQueryPlan(
            intent,
            Evidence(scope),
            terms,
            DependencyTarget(resolved.Terms),
            DependencyDepth(resolved.Terms),
            maximumCandidates,
            maximumMatches,
            includePrerelease,
            selection,
            input);
    }

    private static PackageQueryDependencyTarget DependencyTarget(
        IReadOnlyList<PortableQueryResolvedTerm<PackageQueryPredicate>> terms)
    {
        PortableQueryResolvedTerm<PackageQueryPredicate>? target =
            terms.SingleOrDefault(term =>
                term.Predicate.Kind
                    == PackageQueryPredicateKind.DependencyTarget);
        return target?.Predicate.Text is { } framework
            && !framework.Equals(
                PackageQuery.DependencyTargetAllValue,
                StringComparison.Ordinal)
                ? PackageQueryDependencyTarget.ForTargetFramework(framework)
                : PackageQueryDependencyTarget.All;
    }

    private static int? DependencyDepth(
        IReadOnlyList<PortableQueryResolvedTerm<PackageQueryPredicate>> terms) =>
        terms.SingleOrDefault(term =>
            term.Predicate.Kind == PackageQueryPredicateKind.DependencyDepth)
            ?.Predicate.Number is long depth
                ? checked((int)depth)
                : null;

    private static RowSelectionIntentOperation<string> ToRowSelectionOperation(
        PortableQueryStage stage) =>
        stage.Kind switch
        {
            RowSelectionStageKind.Head =>
                RowSelectionIntentOperation<string>.Head(stage.Count),
            RowSelectionStageKind.Tail =>
                RowSelectionIntentOperation<string>.Tail(stage.Count),
            RowSelectionStageKind.Window =>
                RowSelectionIntentOperation<string>.Window(
                    stage.Start,
                    stage.End),
            _ => throw new InvalidOperationException(
                "Package Query resolved an unsupported selection stage."),
        };

    private static InertString Evidence(string value) =>
        new(TextPolicy.Prose, value);
}
