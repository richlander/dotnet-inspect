using System.Text;
using DotnetInspector.Output;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record ApiTypeLookupResult(string Query, LookupResult Lookup, ApiType? Type, string? ImpliedMember = null)
{
    public bool Found => Type != null;
    public string? Match => Lookup.Match;
    public IReadOnlyList<string> Suggestions => Lookup.Suggestions;

    /// <summary>
    /// Writes the not-found diagnostic, suggestions included, as one message.
    /// </summary>
    /// <remarks>
    /// This used to take a <see cref="TextWriter"/> and write line by line.
    /// Every caller passed <c>Console.Error</c>, so the parameter bought no
    /// flexibility while putting the write outside what
    /// <c>CommandErrorOwnershipTests</c> can see -- the scan looks for
    /// <c>Console.Error</c>, and an aliased writer is exactly the shape that
    /// slips past it. Composing the message and handing it to
    /// <see cref="CommandError"/> puts the prefix, the containment, and the
    /// continuation indent all in one place.
    /// </remarks>
    public void WriteNotFoundError()
    {
        var message = new StringBuilder($"Type '{Query}' not found.");
        AppendSuggestions(message, Suggestions);
        CommandError.Write(message.ToString());
    }

    internal static void AppendSuggestions(StringBuilder message, IReadOnlyList<string> suggestions)
    {
        if (suggestions.Count == 0)
            return;

        message.AppendLine().AppendLine().Append("Did you mean:");
        foreach (var suggestion in suggestions)
            message.AppendLine().Append($"  {suggestion}");
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

    /// <inheritdoc cref="ApiTypeLookupResult.WriteNotFoundError"/>
    public void WriteError()
    {
        if (IsValid)
            return;

        var message = new StringBuilder(
            $"No members matched filter '{string.Join(", ", MissedFilters)}'");

        // The ranking/graph surfaces (Top Leverage, Call Graph) walk the full
        // IL index, so they surface non-public members that member selection hides without
        // --all. Point the user at the flag instead of a dead end.
        if (NonPublicMatches.Count > 0)
        {
            message.AppendLine();
            foreach (var name in NonPublicMatches)
                message.AppendLine().Append($"Member '{name}' is non-public; pass --all to include it.");
        }

        ApiTypeLookupResult.AppendSuggestions(message, Suggestions);
        CommandError.Write(message.ToString());
    }
}

internal static class ApiTypeLookupService
{
    public static ApiTypeLookupResult LookupType(ApiSurface api, string typeName)
    {
        var lookup = TypeMatcher.Lookup(api.Types.Select(t => t.FullName), typeName);
        if (lookup.Match != null)
            return new ApiTypeLookupResult(typeName, lookup, api.Types.First(t => t.FullName == lookup.Match));

        // The query may be a Type.Member where the type portion is itself namespace-qualified
        // (e.g. "System.String.Length" or "System.Text.Json.JsonSerializer.Serialize"). The type
        // and member boundary is not knowable syntactically — "System.String" is a type while
        // "System" is a namespace — so it is resolved here against real metadata: peel the
        // trailing segment and retry the prefix as a type. If the prefix resolves, the peeled
        // suffix is surfaced as an implied member filter.
        var dot = FqnParser.LastTopLevelDot(typeName);
        if (dot > 0)
        {
            var member = typeName[(dot + 1)..];
            var typeCandidate = typeName[..dot];
            var prefixLookup = TypeMatcher.Lookup(api.Types.Select(t => t.FullName), typeCandidate);
            if (prefixLookup.Match != null)
                return new ApiTypeLookupResult(
                    typeName,
                    prefixLookup,
                    api.Types.First(t => t.FullName == prefixLookup.Match),
                    member);
        }

        return new ApiTypeLookupResult(typeName, lookup, null);
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
