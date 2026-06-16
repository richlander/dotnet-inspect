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

internal sealed record MemberFilterValidationResult(IReadOnlyList<string> MissedFilters, IReadOnlyList<string> Suggestions)
{
    public bool IsValid => MissedFilters.Count == 0;

    public void WriteError(TextWriter error)
    {
        if (IsValid)
            return;

        error.WriteLine($"Error: No members matched filter '{string.Join(", ", MissedFilters)}'");
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
}
