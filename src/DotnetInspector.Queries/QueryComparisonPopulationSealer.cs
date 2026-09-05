using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Seals borrowed input occurrences and selection values without inspecting content.
/// Public comparison execution adopts this boundary in a later migration.
/// </summary>
public static class QueryComparisonPopulationSealer
{
    public static QueryPopulationSealingOutcome Execute(
        ImplementationComparisonPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Seal(request.Before, request.After, request.TypeFilters,
            request.MemberTargetIdentities,
            QueryComparisonProfile.ImplementationComparison,
            static binding => binding.Assembly is null
                ? QueryPopulationRejectionKind.MissingAssembly
                : binding.Resolver is null
                    ? QueryPopulationRejectionKind.MissingResolver
                    : binding.BodyIndex is null
                        ? QueryPopulationRejectionKind.MissingBodyIndex
                        : null);
    }

    public static QueryPopulationSealingOutcome Execute(
        BodySignalComparisonPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Seal(request.Before, request.After, request.TypeFilters,
            request.MemberTargetIdentities, QueryComparisonProfile.BodySignal,
            static binding => binding.BodyIndex is null
                ? QueryPopulationRejectionKind.MissingBodyIndex
                : null);
    }

    static QueryPopulationSealingOutcome Seal<TBinding>(
        IReadOnlyList<TBinding?>? before,
        IReadOnlyList<TBinding?>? after,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities,
        QueryComparisonProfile profile,
        Func<TBinding, QueryPopulationRejectionKind?> validate)
        where TBinding : class
    {
        QueryPopulationRejection? rejection = SnapshotSide(
            before, QueryComparisonSide.Before, profile, validate, out var oldBindings);
        if (rejection is not null)
            return new QueryPopulationSealingOutcome.Rejected(rejection);
        rejection = SnapshotSide(
            after, QueryComparisonSide.After, profile, validate, out var newBindings);
        if (rejection is not null)
            return new QueryPopulationSealingOutcome.Rejected(rejection);

        if (!SnapshotSelection(typeFilters, out var types))
        {
            return new QueryPopulationSealingOutcome.Rejected(
                new(QueryPopulationRejectionKind.MissingTypeFilter, profile));
        }
        if (!SnapshotSelection(memberTargetIdentities, out var members))
        {
            return new QueryPopulationSealingOutcome.Rejected(
                new(QueryPopulationRejectionKind.MissingMemberTarget, profile));
        }

        QueryComparisonQuestionId question = new(new QueryComparisonOperationId());
        return new QueryPopulationSealingOutcome.Sealed(
            new QueryComparisonPopulation<TBinding>(
                profile, question,
                Mint(oldBindings, question, QueryComparisonSide.Before),
                Mint(newBindings, question, QueryComparisonSide.After),
                types, members));
    }

    static QueryPopulationRejection? SnapshotSide<TBinding>(
        IReadOnlyList<TBinding?>? source,
        QueryComparisonSide side,
        QueryComparisonProfile profile,
        Func<TBinding, QueryPopulationRejectionKind?> validate,
        out ImmutableArray<TBinding> snapshot)
        where TBinding : class
    {
        snapshot = [];
        if (source is null)
            return new(QueryPopulationRejectionKind.MissingSide, profile, side);

        var bindings = ImmutableArray.CreateBuilder<TBinding>(source.Count);
        for (int index = 0; index < source.Count; index++)
        {
            TBinding? binding = source[index];
            QueryPopulationRejectionKind? failure = binding is null
                ? QueryPopulationRejectionKind.MissingBinding
                : validate(binding);
            if (failure is not null)
                return new(failure.Value, profile, side, index);
            bindings.Add(binding!);
        }
        snapshot = bindings.ToImmutable();
        return null;
    }

    static bool SnapshotSelection(
        IReadOnlySet<string>? source,
        out ImmutableHashSet<string>? snapshot)
    {
        snapshot = null;
        if (source is null)
            return true;
        var values = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (string value in source)
        {
            if (value is null)
                return false;
            values.Add(value);
        }
        snapshot = values.ToImmutable();
        return true;
    }

    static ImmutableArray<QueryComparisonInput<TBinding>> Mint<TBinding>(
        ImmutableArray<TBinding> bindings,
        QueryComparisonQuestionId question,
        QueryComparisonSide side)
        where TBinding : class
        => [.. bindings.Select(binding =>
            new QueryComparisonInput<TBinding>(
                new QueryComparisonInputId(question, side), binding))];
}
