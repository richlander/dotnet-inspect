using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

// docs/design/annotated-type-map-generation.md owns correspondence and settlement.

public sealed class AnnotatedTypeMapContractException : InvalidOperationException
{
    public AnnotatedTypeMapContractException(string message)
        : base(message)
    {
    }
}

public enum AnnotatedTypeMapSide
{
    /// <summary>The one version of a one-version request.</summary>
    Single,
    Before,
    After,
}

/// <summary>
/// A requested Member key present on no requested side: one side-independent
/// entry per requested family, beside the per-side contributions.
/// </summary>
public sealed record AnnotatedTypeMapAbsentKeyEntry(
    AnnotatedTypeMapFamily Family,
    AnnotatedTypeMapMemberKey Key,
    AnnotatedTypeMapOutcome Outcome);

/// <summary>
/// The result of a family contribution request: the validated contributions
/// keyed by family and side, and the absent keys' entries. It carries no
/// listing, terminal, or map diff.
/// </summary>
public sealed class AnnotatedTypeMapContributionResult
{
    private readonly ImmutableDictionary<(object Family, AnnotatedTypeMapSide Side), AnnotatedTypeMapFamilyContent> _contents;

    internal AnnotatedTypeMapContributionResult(
        AnnotatedTypeMapPlan plan,
        ImmutableDictionary<(object Family, AnnotatedTypeMapSide Side), AnnotatedTypeMapFamilyContent> contents,
        ImmutableDictionary<AnnotatedTypeMapSide, AnnotatedTypeMapTypePresence> presence,
        ImmutableArray<AnnotatedTypeMapAbsentKeyEntry> absentKeys)
    {
        Plan = plan;
        _contents = contents;
        Presence = presence;
        AbsentKeys = absentKeys;
    }

    public AnnotatedTypeMapPlan Plan { get; }

    public ImmutableArray<AnnotatedTypeMapSide> Sides =>
        Plan.Request.VersionPair
            ? [AnnotatedTypeMapSide.Before, AnnotatedTypeMapSide.After]
            : [AnnotatedTypeMapSide.Single];

    /// <summary>The Type's presence on each side.</summary>
    public ImmutableDictionary<AnnotatedTypeMapSide, AnnotatedTypeMapTypePresence> Presence { get; }

    /// <summary>One <c>Unavailable</c> entry per requested family for each requested key present on no side.</summary>
    public ImmutableArray<AnnotatedTypeMapAbsentKeyEntry> AbsentKeys { get; }

    /// <summary>The family's content on one side, read with the family's exact instance.</summary>
    public AnnotatedTypeMapFamilyContent Content(AnnotatedTypeMapFamily family, AnnotatedTypeMapSide side)
    {
        ArgumentNullException.ThrowIfNull(family);
        return _contents.TryGetValue((family, side), out AnnotatedTypeMapFamilyContent? content)
            ? content
            : throw new KeyNotFoundException($"No content for family '{family.Id}' on side {side}.");
    }
}

public static class AnnotatedTypeMapGeneration
{
    /// <summary>Settles a one-version family contribution request.</summary>
    public static AnnotatedTypeMapContributionResult SettleContributions(
        AnnotatedTypeMapPlan plan,
        InspectionQueryResults results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        RequireContributionForm(plan, versionPair: false);
        return Settle(plan, [(AnnotatedTypeMapSide.Single, results)]);
    }

    /// <summary>Settles a version-pair family contribution request.</summary>
    public static AnnotatedTypeMapContributionResult SettleContributions(
        AnnotatedTypeMapPlan plan,
        InspectionQueryResults before,
        InspectionQueryResults after)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        RequireContributionForm(plan, versionPair: true);
        return Settle(plan, [(AnnotatedTypeMapSide.Before, before), (AnnotatedTypeMapSide.After, after)]);
    }

    private static AnnotatedTypeMapContributionResult Settle(
        AnnotatedTypeMapPlan plan,
        ImmutableArray<(AnnotatedTypeMapSide Side, InspectionQueryResults Results)> sides)
    {
        var contents = ImmutableDictionary.CreateBuilder<(object Family, AnnotatedTypeMapSide Side), AnnotatedTypeMapFamilyContent>(
            new FamilySideComparer());
        var presence = ImmutableDictionary.CreateBuilder<AnnotatedTypeMapSide, AnnotatedTypeMapTypePresence>();
        var present = new HashSet<AnnotatedTypeMapMemberKey>();

        foreach ((AnnotatedTypeMapSide side, InspectionQueryResults results) in sides)
        {
            AnnotatedTypeMapTypePresence? sidePresence = null;
            foreach (InspectionQuery<AnnotatedTypeMapContribution> query in plan.Queries)
            {
                AnnotatedTypeMapContribution contribution = results.Get(query);
                Correspond(plan, query, side, contribution);
                if (sidePresence is { } known && known != contribution.Presence)
                {
                    throw Contract(
                        $"The contributions of side {side} disagree on the Type's presence.");
                }

                sidePresence = contribution.Presence;
                foreach (AnnotatedTypeMapFamilyContent content in contribution.Families)
                {
                    contents.Add((content.Family, side), content);
                    present.UnionWith(content.Keys);
                }
            }

            presence.Add(side, sidePresence ?? AnnotatedTypeMapTypePresence.Absent);
        }

        var absent = ImmutableArray.CreateBuilder<AnnotatedTypeMapAbsentKeyEntry>();
        if (plan.Request.MemberKeys is { } requested)
        {
            AnnotatedTypeMapOutcome unavailable = new(
                AnnotatedTypeMapOutcomeKind.Unavailable,
                "The key is present on no requested side.");
            foreach (AnnotatedTypeMapMemberKey key in requested)
            {
                if (present.Contains(key))
                    continue;
                foreach (AnnotatedTypeMapFamily family in plan.Request.Families)
                    absent.Add(new(family, key, unavailable));
            }
        }

        return new(plan, contents.ToImmutable(), presence.ToImmutable(), absent.ToImmutable());
    }

    private static void Correspond(
        AnnotatedTypeMapPlan plan,
        InspectionQuery<AnnotatedTypeMapContribution> query,
        AnnotatedTypeMapSide side,
        AnnotatedTypeMapContribution contribution)
    {
        if (!ReferenceEquals(contribution.Focus, plan.Request.Focus))
        {
            throw Contract(
                $"Adapter query '{query.Name}' returned a contribution for a foreign focus on side {side}.");
        }

        var bound = plan.Trail
            .Where(entry => ReferenceEquals(entry.Query, query))
            .Select(static entry => entry.Family)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var held = contribution.Families
            .Select(static content => content.Family)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        if (!bound.SetEquals(held))
        {
            throw Contract(
                $"Adapter query '{query.Name}' must hold exactly the families the plan binds to it on side {side}.");
        }

        if (plan.Request.MemberKeys is { } requested)
        {
            var allowed = requested.ToHashSet();
            foreach (AnnotatedTypeMapFamilyContent content in contribution.Families)
            {
                if (content.Keys.Any(key => !allowed.Contains(key)))
                {
                    throw Contract(
                        $"Family '{content.Family.Id}' on side {side} holds a key outside the plan's Member keys.");
                }
            }
        }
    }

    private static void RequireContributionForm(AnnotatedTypeMapPlan plan, bool versionPair)
    {
        if (plan.Request.Form != AnnotatedTypeMapRequestForm.Contribution)
            throw Contract("A family contribution result settles only a family contribution request.");
        if (plan.Request.VersionPair != versionPair)
        {
            throw Contract(
                versionPair
                    ? "A one-version plan cannot settle two sides."
                    : "A version-pair plan settles two sides.");
        }
    }

    private static AnnotatedTypeMapContractException Contract(string message) => new(message);

    private sealed class FamilySideComparer : IEqualityComparer<(object Family, AnnotatedTypeMapSide Side)>
    {
        public bool Equals((object Family, AnnotatedTypeMapSide Side) x, (object Family, AnnotatedTypeMapSide Side) y) =>
            ReferenceEquals(x.Family, y.Family) && x.Side == y.Side;

        public int GetHashCode((object Family, AnnotatedTypeMapSide Side) obj) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Family), obj.Side);
    }
}
