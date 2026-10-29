using DotnetInspector.ResearchSections;
using ILInspector.Metadata;

namespace DotnetInspector.Diff;

/// <summary>
/// Resolves a host's member and Type target patterns, such as the CLI's
/// <c>--member</c> and <c>-t</c>, against both endpoints' API surfaces. The
/// operation owns these rules
/// (diff-system.md#single-library-api-diff-terminal); a host's other routes
/// that read the same patterns call them here. Every failure throws
/// <see cref="InvalidOperationException"/> with the user-facing message.
/// </summary>
public static class DiffMemberTargetResolver
{
    /// <summary>
    /// Resolves every member target to the member identities it selects at
    /// either endpoint, and the Types that own them.
    /// </summary>
    public static DiffResolvedMemberTargets Resolve(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> memberTargets,
        IReadOnlyCollection<string> typeFilters)
    {
        ArgumentNullException.ThrowIfNull(fromSurface);
        ArgumentNullException.ThrowIfNull(toSurface);
        ArgumentNullException.ThrowIfNull(memberTargets);
        ArgumentNullException.ThrowIfNull(typeFilters);
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> typeNames = new(StringComparer.Ordinal);
        foreach (var rawTarget in memberTargets)
        {
            var parsed = Parse(rawTarget, fromSurface, toSurface, typeFilters);
            foreach (string typeName in ResolveTypeNames(
                fromSurface,
                toSurface,
                [parsed.TypeName]))
            {
                typeNames.Add(typeName);
            }

            var found = false;
            MemberTargetDiagnostic? diagnostic = null;
            MemberTargetDiagnostic? nonFatalDiagnostic = null;
            ApiType? oldType = FindSelectedType(
                fromSurface,
                parsed.TypeName,
                out string? oldTypeError);
            ApiType? newType = FindSelectedType(
                toSurface,
                parsed.TypeName,
                out string? newTypeError);
            if (oldTypeError is not null || newTypeError is not null)
            {
                throw new InvalidOperationException(
                    oldTypeError ?? newTypeError);
            }

            if (oldType is not null)
            {
                var oldResult = AddResolvedIdentities(
                    oldType,
                    parsed.Selector,
                    identities);
                found |= oldResult.Found;
                if (oldResult.Diagnostic is { } oldDiagnostic)
                {
                    if (IsFatal(oldDiagnostic.Kind))
                        diagnostic ??= oldDiagnostic;
                    else
                        nonFatalDiagnostic ??= oldDiagnostic;
                }
                if (oldResult.Found)
                    typeNames.Add(oldType.FullName);
            }
            if (newType is not null)
            {
                var newResult = AddResolvedIdentities(
                    newType,
                    parsed.Selector,
                    identities);
                found |= newResult.Found;
                if (newResult.Diagnostic is { } newDiagnostic)
                {
                    if (IsFatal(newDiagnostic.Kind))
                        diagnostic ??= newDiagnostic;
                    else
                        nonFatalDiagnostic ??= newDiagnostic;
                }
                if (newResult.Found)
                    typeNames.Add(newType.FullName);
            }

            if (diagnostic is not null)
                throw new InvalidOperationException(diagnostic.Message);
            if (!found)
                throw new InvalidOperationException(nonFatalDiagnostic?.Message ?? $"Member target '{rawTarget}' did not resolve in either diff input.");
        }

        return new DiffResolvedMemberTargets(
            identities,
            typeNames);
    }

    /// <summary>
    /// Splits one member target into its Type and member selector: a
    /// Type-qualified selector, or a bare selector in the context of exactly
    /// one Type filter.
    /// </summary>
    public static DiffParsedMemberTarget Parse(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters)
    {
        if (TrySplitTypeQualifiedMemberTarget(rawTarget, fromSurface, toSurface, out var typeName, out var memberSelector))
            return new DiffParsedMemberTarget(typeName, MemberTargetSelector.Parse(memberSelector));

        var typeContext = ResolveTypeContext(fromSurface, toSurface, typeFilters, out var contextError);
        if (contextError is { Length: > 0 })
            throw new InvalidOperationException(contextError);
        if (typeContext is null)
            throw new InvalidOperationException($"--member '{rawTarget}' requires exactly one --type filter or a type-qualified selector.");

        return new DiffParsedMemberTarget(typeContext, MemberTargetSelector.Parse(rawTarget));
    }

    /// <summary>
    /// The one Type a Type query selects on <paramref name="surface"/>, or
    /// null; an ambiguous query sets <paramref name="error"/>.
    /// </summary>
    public static ApiType? FindSelectedType(
        ApiSurface surface,
        string query,
        out string? error)
    {
        string? selectedName = SelectTypeName(
            surface,
            query,
            out error);
        if (selectedName is null)
            return null;

        return surface.Types.FirstOrDefault(type =>
            type.FullName.Equals(
                selectedName,
                StringComparison.Ordinal));
    }

    /// <summary>Whether a member-target diagnostic fails the target outright.</summary>
    public static bool IsFatal(MemberTargetDiagnosticKind kind)
        => kind is MemberTargetDiagnosticKind.AmbiguousMember
            or MemberTargetDiagnosticKind.DigestAmbiguous
            or MemberTargetDiagnosticKind.ConflictingSelectors;

    /// <summary>
    /// The positions of <paramref name="value"/>'s dots outside generic
    /// argument lists, from the right.
    /// </summary>
    public static IEnumerable<int> TopLevelDotPositionsFromRight(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var ch = value[i];
            if (ch == '>')
                depth++;
            else if (ch == '<')
                depth--;
            else if (ch == '.' && depth == 0)
                yield return i;
        }
    }

    static (
        bool Found,
        MemberTargetDiagnostic? Diagnostic)
        AddResolvedIdentities(
            ApiType type,
            MemberTargetSelector selector,
            HashSet<string> identities)
    {
        var resolution = MemberTargetResolver.Resolve(type, selector);
        if (!resolution.Found)
            return (false, resolution.Diagnostic);

        identities.Add(resolution.Target!.Anchor.StableSelector);
        identities.Add(resolution.Target.Anchor.CanonicalSignature);
        return (true, null);
    }

    static bool TrySplitTypeQualifiedMemberTarget(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        out string typeName,
        out string memberSelector)
    {
        foreach (var marker in (ReadOnlySpan<string>)[".operator:", ".explicit:", ".extension:"])
        {
            var markerIndex = rawTarget.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                var candidate = rawTarget[..markerIndex];
                if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
                {
                    memberSelector = rawTarget[(markerIndex + 1)..];
                    return true;
                }
            }
        }

        foreach (var dot in TopLevelDotPositionsFromRight(rawTarget))
        {
            var candidate = rawTarget[..dot];
            if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
            {
                memberSelector = rawTarget[(dot + 1)..];
                return true;
            }
        }

        typeName = "";
        memberSelector = rawTarget;
        return false;
    }

    static string? ResolveTypeContext(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters,
        out string? error)
    {
        error = null;
        if (typeFilters.Count != 1)
            return null;

        var query = typeFilters.First();
        if (TryFindSingleType(fromSurface, toSurface, query, out var typeName, out error))
            return typeName;

        return null;
    }

    static bool TryFindSingleType(ApiSurface fromSurface, ApiSurface toSurface, string query, out string typeName, out string? error)
    {
        string? oldTypeName = SelectTypeName(
            fromSurface,
            query,
            out string? oldError);
        string? newTypeName = SelectTypeName(
            toSurface,
            query,
            out string? newError);
        error = oldError ?? newError;
        if (error is not null
            || oldTypeName is null && newTypeName is null)
        {
            typeName = "";
            return false;
        }

        typeName = query;
        return true;
    }

    static IReadOnlyList<string> ResolveTypeNames(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters)
    {
        var names = typeFilters
            .SelectMany(filter => ResolveTypeNames(fromSurface, filter)
                .Concat(ResolveTypeNames(toSurface, filter))
                .DefaultIfEmpty(filter))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return names;
    }

    static IEnumerable<string> ResolveTypeNames(
        ApiSurface surface,
        string filter)
    {
        string[] matches = DiffAnalysisTypeFilter.EnumerateResolvable(surface)
            .Where(typeName =>
                TypeMatcher.MatchesTypeFilter(typeName, filter))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string? exact = matches.FirstOrDefault(typeName =>
            typeName.Equals(filter, StringComparison.Ordinal));
        return exact is null ? matches : [exact];
    }

    static string? SelectTypeName(
        ApiSurface surface,
        string query,
        out string? error)
    {
        string[] matches = ResolveTypeNames(surface, query)
            .ToArray();
        if (matches.Length > 1)
        {
            error = $"Type target '{query}' is ambiguous. Use one of: "
                + $"{string.Join(", ", matches)}.";
            return null;
        }

        error = null;
        return matches.SingleOrDefault();
    }
}

/// <summary>One member target split into its Type and member selector.</summary>
public sealed record DiffParsedMemberTarget(
    string TypeName,
    MemberTargetSelector Selector);

/// <summary>
/// The member identities a set of member targets selects at either endpoint,
/// and the Types that own them.
/// </summary>
public sealed record DiffResolvedMemberTargets(
    IReadOnlySet<string> MemberIdentities,
    IReadOnlySet<string> TypeNames);
