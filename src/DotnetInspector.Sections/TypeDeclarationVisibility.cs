using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum TypeDeclarationVisibilityFacet
{
    PublicSurface,
    EditorBrowsableNever,
    Obsolete,
}

public enum TypeDeclarationVisibilityValue
{
    True,
    False,
    Unknown,
}

public enum TypeDeclarationVisibilityPreset
{
    Default,
    All,
}

public sealed record TypeDeclarationVisibilityFacetDescriptor(
    TypeDeclarationVisibilityFacet Facet,
    ImmutableArray<RowQueryOperator> Operators,
    ImmutableArray<TypeDeclarationVisibilityValue> Values);

public sealed record TypeDeclarationVisibilityPredicate
{
    public TypeDeclarationVisibilityPredicate(
        TypeDeclarationVisibilityFacet facet,
        RowQueryOperator @operator,
        TypeDeclarationVisibilityValue value)
    {
        if (!Enum.IsDefined(facet))
            throw new ArgumentOutOfRangeException(nameof(facet));
        if (@operator is not (RowQueryOperator.Equals or RowQueryOperator.NotEquals))
            throw new ArgumentOutOfRangeException(nameof(@operator));
        if (!Enum.IsDefined(value)
            || (facet is TypeDeclarationVisibilityFacet.PublicSurface
                && value is TypeDeclarationVisibilityValue.Unknown))
            throw new ArgumentOutOfRangeException(nameof(value));

        Facet = facet;
        Operator = @operator;
        Value = value;
    }

    public TypeDeclarationVisibilityFacet Facet { get; }
    public RowQueryOperator Operator { get; }
    public TypeDeclarationVisibilityValue Value { get; }

    internal bool? Evaluate(TypeDeclarationLocatorSectionCandidate candidate)
    {
        TypeDeclarationVisibilityValue actual = Facet switch
        {
            TypeDeclarationVisibilityFacet.PublicSurface => Known(candidate.IsPublicSurface),
            TypeDeclarationVisibilityFacet.EditorBrowsableNever =>
                candidate.DiscoveryAttributes is { } attributes
                    ? Known(attributes.IsEditorBrowsableNever)
                    : TypeDeclarationVisibilityValue.Unknown,
            TypeDeclarationVisibilityFacet.Obsolete =>
                candidate.DiscoveryAttributes is { } attributes
                    ? Known(attributes.IsObsolete)
                    : TypeDeclarationVisibilityValue.Unknown,
            _ => throw new InvalidOperationException("Unknown visibility facet."),
        };

        if (actual is TypeDeclarationVisibilityValue.Unknown
            && Value is not TypeDeclarationVisibilityValue.Unknown)
            return null;

        bool equal = actual == Value;
        return Operator is RowQueryOperator.Equals ? equal : !equal;
    }

    private static TypeDeclarationVisibilityValue Known(bool value) =>
        value ? TypeDeclarationVisibilityValue.True : TypeDeclarationVisibilityValue.False;
}

public sealed record TypeDeclarationVisibilityBindingFailure(
    int PredicatePosition,
    TypeDeclarationVisibilityFacet? Facet,
    RowQueryFailureReason Reason);

public sealed class TypeDeclarationVisibilityBindingResult
{
    internal TypeDeclarationVisibilityBindingResult(
        TypeDeclarationVisibilityPlan? plan,
        TypeDeclarationVisibilityBindingFailure? failure)
    {
        Plan = plan;
        Failure = failure;
    }

    public TypeDeclarationVisibilityPlan? Plan { get; }
    public TypeDeclarationVisibilityBindingFailure? Failure { get; }
    public bool IsSuccess => Plan is not null;
}

/// <summary>Shared visibility policy over an all-declaration locator result.</summary>
public sealed class TypeDeclarationVisibilityPlan
{
    private static readonly ImmutableArray<TypeDeclarationVisibilityPredicate> DefaultPredicates =
    [
        new(TypeDeclarationVisibilityFacet.PublicSurface,
            RowQueryOperator.Equals, TypeDeclarationVisibilityValue.True),
        new(TypeDeclarationVisibilityFacet.EditorBrowsableNever,
            RowQueryOperator.Equals, TypeDeclarationVisibilityValue.False),
        new(TypeDeclarationVisibilityFacet.Obsolete,
            RowQueryOperator.Equals, TypeDeclarationVisibilityValue.False),
    ];

    public TypeDeclarationVisibilityPlan(
        TypeDeclarationVisibilityPreset preset,
        IEnumerable<TypeDeclarationVisibilityPredicate>? predicates = null)
    {
        if (!Enum.IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset));

        Preset = preset;
        Predicates = [.. predicates ?? []];
        foreach (TypeDeclarationVisibilityPredicate predicate in Predicates)
            ArgumentNullException.ThrowIfNull(predicate);

        EffectivePredicates =
        [
            .. (preset is TypeDeclarationVisibilityPreset.Default
                ? DefaultPredicates.Where(defaultPredicate =>
                    !Predicates.Any(predicate => predicate.Facet == defaultPredicate.Facet))
                : []),
            .. Predicates,
        ];
    }

    public static TypeDeclarationVisibilityPlan Default { get; } =
        new(TypeDeclarationVisibilityPreset.Default);

    public static TypeDeclarationVisibilityPlan All { get; } =
        new(TypeDeclarationVisibilityPreset.All);

    public static ImmutableArray<TypeDeclarationVisibilityFacetDescriptor> Facets { get; } =
    [
        new(TypeDeclarationVisibilityFacet.PublicSurface,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            [TypeDeclarationVisibilityValue.True, TypeDeclarationVisibilityValue.False]),
        new(TypeDeclarationVisibilityFacet.EditorBrowsableNever,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            [TypeDeclarationVisibilityValue.True, TypeDeclarationVisibilityValue.False,
                TypeDeclarationVisibilityValue.Unknown]),
        new(TypeDeclarationVisibilityFacet.Obsolete,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            [TypeDeclarationVisibilityValue.True, TypeDeclarationVisibilityValue.False,
                TypeDeclarationVisibilityValue.Unknown]),
    ];

    public TypeDeclarationVisibilityPreset Preset { get; }
    public ImmutableArray<TypeDeclarationVisibilityPredicate> Predicates { get; }
    public ImmutableArray<TypeDeclarationVisibilityPredicate> EffectivePredicates { get; }
    public bool ExcludeCompilerGeneratedNames => true;

    public static TypeDeclarationVisibilityBindingResult Bind(
        TypeDeclarationVisibilityPreset preset,
        IReadOnlyList<RowQueryPredicateIntent> predicates)
    {
        if (!Enum.IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset));
        ArgumentNullException.ThrowIfNull(predicates);

        var bound = ImmutableArray.CreateBuilder<TypeDeclarationVisibilityPredicate>(predicates.Count);
        for (int index = 0; index < predicates.Count; index++)
        {
            RowQueryPredicateIntent predicate = predicates[index];
            ArgumentNullException.ThrowIfNull(predicate);
            TypeDeclarationVisibilityFacetDescriptor? descriptor = Facets.FirstOrDefault(
                facet => string.Equals(facet.Facet.ToString(), predicate.FieldKey,
                    StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                return Failed(index, null, RowQueryFailureReason.UnknownField);
            if (!descriptor.Operators.Contains(predicate.Operator))
                return Failed(index, descriptor.Facet, RowQueryFailureReason.UnsupportedPredicateOperator);

            string text = predicate.Value.Text.Trim();
            TypeDeclarationVisibilityValue? value =
                bool.TryParse(text, out bool boolean)
                    ? boolean ? TypeDeclarationVisibilityValue.True : TypeDeclarationVisibilityValue.False
                    : string.Equals(text, "unknown", StringComparison.OrdinalIgnoreCase)
                        ? TypeDeclarationVisibilityValue.Unknown
                        : null;
            if (value is null || !descriptor.Values.Contains(value.Value))
                return Failed(index, descriptor.Facet, RowQueryFailureReason.InvalidValue);

            bound.Add(new(descriptor.Facet, predicate.Operator, value.Value));
        }

        return new(new(preset, bound.MoveToImmutable()), null);
    }

    internal TypeDeclarationVisibilitySelection Select(
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> candidates)
    {
        var matches = ImmutableArray.CreateBuilder<TypeDeclarationLocatorSectionCandidate>();
        var unknown = ImmutableArray.CreateBuilder<TypeDeclarationVisibilityUnknownCandidate>();
        int excluded = 0;
        foreach (TypeDeclarationLocatorSectionCandidate candidate in candidates)
        {
            if (candidate.Name.Segments.Any(TypeFilters.IsCompilerGenerated))
            {
                excluded++;
                continue;
            }

            var unknownFacets = ImmutableArray.CreateBuilder<TypeDeclarationVisibilityFacet>();
            bool rejected = false;
            foreach (TypeDeclarationVisibilityPredicate predicate in EffectivePredicates)
            {
                bool? outcome = predicate.Evaluate(candidate);
                if (outcome is false)
                {
                    rejected = true;
                    break;
                }
                if (outcome is null && !unknownFacets.Contains(predicate.Facet))
                    unknownFacets.Add(predicate.Facet);
            }

            if (rejected)
                excluded++;
            else if (unknownFacets.Count > 0)
                unknown.Add(new(candidate, unknownFacets.ToImmutable()));
            else
                matches.Add(candidate);
        }

        return new(matches.ToImmutable(),
            new(candidates.Length, excluded, unknown.ToImmutable(), IsEvaluated: true));
    }

    private static TypeDeclarationVisibilityBindingResult Failed(
        int index, TypeDeclarationVisibilityFacet? facet, RowQueryFailureReason reason) =>
        new(null, new(index + 1, facet, reason));
}

public enum TypeDeclarationVisibilityInputFailure
{
    AllDeclarationsRequired,
}

public sealed record TypeDeclarationVisibilityUnknownCandidate(
    TypeDeclarationLocatorSectionCandidate Candidate,
    ImmutableArray<TypeDeclarationVisibilityFacet> Facets);

public sealed record TypeDeclarationVisibilityCoverage(
    int InputCandidateCount,
    int ExcludedCandidateCount,
    ImmutableArray<TypeDeclarationVisibilityUnknownCandidate> UnknownCandidates,
    bool IsEvaluated)
{
    public bool IsComplete => IsEvaluated && UnknownCandidates.IsEmpty;
}

internal sealed record TypeDeclarationVisibilitySelection(
    ImmutableArray<TypeDeclarationLocatorSectionCandidate> Candidates,
    TypeDeclarationVisibilityCoverage Coverage);
