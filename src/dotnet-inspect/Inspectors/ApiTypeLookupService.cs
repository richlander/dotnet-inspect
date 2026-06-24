using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record ApiTypeLookupResult(string Query, LookupResult Lookup, ApiType? Type)
{
    public bool Found => Type != null;
    public string? Match => Lookup.Match;
    public IReadOnlyList<string> Suggestions => Lookup.Suggestions;

    public void WriteNotFoundError(TextWriter error)
    {
        error.WriteLine($"Error: Type '{Query}' not found.");
        if (Suggestions.Count == 0)
            return;

        error.WriteLine();
        error.WriteLine("Did you mean:");
        foreach (var suggestion in Suggestions)
            error.WriteLine($"  {suggestion}");
    }
}

internal sealed record MemberFilterValidationResult(
    IReadOnlyList<string> MissedFilters,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> NonPublicMatches)
{
    public MemberFilterValidationResult(IReadOnlyList<string> missedFilters, IReadOnlyList<string> suggestions)
        : this(missedFilters, suggestions, [])
    {
    }

    public bool IsValid => MissedFilters.Count == 0;

    public void WriteError(TextWriter error)
    {
        if (IsValid)
            return;

        error.WriteLine($"Error: No members matched filter '{string.Join(", ", MissedFilters)}'");

        // The ranking/graph surfaces (Top Leverage, Call Graph, Caller Graph) walk the full
        // IL index, so they surface non-public members that member selection hides without
        // --all. Point the user at the flag instead of a dead end.
        if (NonPublicMatches.Count > 0)
        {
            error.WriteLine();
            foreach (var name in NonPublicMatches)
                error.WriteLine($"Member '{name}' is non-public; pass --all to include it.");
        }

        if (Suggestions.Count == 0)
            return;

        error.WriteLine();
        error.WriteLine("Did you mean:");
        foreach (var suggestion in Suggestions)
            error.WriteLine($"  {suggestion}");
    }
}

internal static class ApiTypeLookupService
{
    public static ApiTypeLookupResult LookupType(ApiSurface api, string typeName)
    {
        var lookup = TypeMatcher.Lookup(api.Types.Select(t => t.FullName), typeName);
        var type = lookup.Match == null
            ? null
            : api.Types.First(t => t.FullName == lookup.Match);
        return new ApiTypeLookupResult(typeName, lookup, type);
    }

    public static MemberFilterValidationResult ValidateMemberFilters(
        ApiType type,
        IReadOnlyCollection<string> filters)
    {
        if (filters.Count == 0)
            return new MemberFilterValidationResult([], []);

        var memberNames = type.Members
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string> missedFilters = [];

        foreach (var filter in filters)
        {
            bool isGlob = filter.Contains('*') || filter.Contains('?');
            bool anyMatch = isGlob
                ? memberNames.Any(n => TypeMatcher.MatchesGlob(n, filter))
                : memberNames.Any(n => TypeMatcher.MatchesMemberName(n, filter));

            if (!anyMatch)
                missedFilters.Add(filter);
        }

        if (missedFilters.Count == 0)
            return new MemberFilterValidationResult([], []);

        var memberLookup = TypeMatcher.LookupMembers(memberNames, missedFilters);
        return new MemberFilterValidationResult(missedFilters, memberLookup.Suggestions);
    }

    /// <summary>
    /// Of the <paramref name="missedFilters"/> (which matched no visible member), returns
    /// those that would match against <paramref name="allMemberNames"/> — i.e. members that
    /// exist but are non-public, so the user needs <c>--all</c> to select them.
    /// </summary>
    public static IReadOnlyList<string> FindNonPublicMatches(
        IReadOnlyCollection<string> missedFilters,
        IReadOnlyCollection<string> allMemberNames)
    {
        List<string> matches = [];
        foreach (var filter in missedFilters)
        {
            bool isGlob = filter.Contains('*') || filter.Contains('?');
            bool anyMatch = isGlob
                ? allMemberNames.Any(n => TypeMatcher.MatchesGlob(n, filter))
                : allMemberNames.Any(n => TypeMatcher.MatchesMemberName(n, filter));

            if (anyMatch)
                matches.Add(filter);
        }
        return matches;
    }
}
