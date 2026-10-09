using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// One exact member selection issued by a product projection.
/// </summary>
public sealed record AssemblyContextMemberSelection
{
    public AssemblyContextMemberSelection(
        string typeIdentity,
        string memberName,
        string selectorKey,
        int? metadataToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        TypeIdentity = typeIdentity;
        MemberName = memberName;
        SelectorKey = selectorKey;
        MetadataToken = metadataToken;
    }

    public string TypeIdentity { get; }
    public string MemberName { get; }
    public string SelectorKey { get; }
    public int? MetadataToken { get; }
}

/// <summary>One exact API declaration selected from a participant.</summary>
public sealed record AssemblyContextMemberDeclaration(
    ApiType Type,
    ApiMember Member);

/// <summary>One exact physical member body selected from a participant.</summary>
public sealed record AssemblyContextMemberBody(
    ApiType Type,
    ApiMember Member,
    int BodyToken);

/// <summary>
/// Resolves exact type, declaration, and body selections over one complete API
/// surface.
/// </summary>
public static class ApiSurfaceMemberSelection
{
    public static ApiType SelectType(
        ApiSurface surface,
        string typeIdentity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        ApiType[] matches =
        [
            .. surface.Types
                .Where(candidate =>
                    candidate.DefinitionName?.ToEscapedFullName()
                        .Equals(
                            typeIdentity,
                            StringComparison.Ordinal) == true)
                .Take(2),
        ];
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"The API surface does not contain one exact type identity "
                    + $"for '{typeIdentity}'.");
    }

    public static AssemblyContextMemberDeclaration SelectDeclaration(
        ApiSurface surface,
        AssemblyContextMemberSelection selection)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(selection);
        ApiType type = SelectType(surface, selection.TypeIdentity);
        ApiMember[] named =
        [
            .. type.Members.Where(candidate =>
                candidate.Name.Equals(
                    selection.MemberName,
                    StringComparison.Ordinal)),
        ];
        if (selection.MetadataToken is int metadataToken)
        {
            ApiMember[] tokenMatches =
            [
                .. named.Where(candidate =>
                    (candidate.DeclarationMetadataToken
                        ?? candidate.MetadataToken) == metadataToken),
            ];
            if (tokenMatches.Length == 1
                && CallGraphMemberResolver.CreateSelector(
                        type,
                        tokenMatches[0])
                    .Key.Equals(
                        selection.SelectorKey,
                        StringComparison.Ordinal))
            {
                return new(type, tokenMatches[0]);
            }
            if (tokenMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"The declaration token for "
                        + $"'{selection.TypeIdentity}."
                        + $"{selection.MemberName}' is ambiguous.");
            }
        }

        ApiMember[] selectorMatches =
        [
            .. named.Where(candidate =>
                CallGraphMemberResolver.CreateSelector(type, candidate)
                    .Key.Equals(
                        selection.SelectorKey,
                        StringComparison.Ordinal)),
        ];
        return selectorMatches.Length == 1
            ? new(type, selectorMatches[0])
            : throw new InvalidOperationException(
                $"The API surface does not contain one exact declaration for "
                    + $"'{selection.TypeIdentity}.{selection.MemberName}'.");
    }

    public static AssemblyContextMemberBody SelectBody(
        ApiSurface surface,
        AssemblyContextMemberSelection selection)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(selection);
        CallGraphMemberResolution resolution =
            CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface,
                selection.TypeIdentity,
                selection.MemberName,
                selection.SelectorKey,
                selection.MetadataToken)
            ?? throw new InvalidOperationException(
                $"The API surface does not contain one exact body for "
                    + $"'{selection.TypeIdentity}.{selection.MemberName}'.");
        return new(
            resolution.Type,
            resolution.Member,
            resolution.BodyToken);
    }
}

/// <summary>
/// Resolves exact type, declaration, and body selections over one bounded
/// assembly-context participant.
/// </summary>
public static class AssemblyContextMemberSelectionQuery
{
    public static InspectionQuery<AssemblyContextEntry<ApiType>>
        TypeDefinition { get; } =
            new("Assembly context type selection", InspectionCost.NetworkFree);

    public static InspectionQuery<
        AssemblyContextEntry<AssemblyContextMemberDeclaration>>
        DeclarationDefinition { get; } =
            new(
                "Assembly context member declaration selection",
                InspectionCost.NetworkFree);

    public static InspectionQuery<AssemblyContextEntry<AssemblyContextMemberBody>>
        BodyDefinition { get; } =
            new(
                "Assembly context member body selection",
                InspectionCost.NetworkFree);

    public static AssemblyContextEntry<ApiType> ExecuteType(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeIdentity,
        ApiSurfaceProjectionLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);
        return AssemblyContextApiSurfaceSelection.Execute<ApiType>(
            group,
            participant,
            limits,
            surface => ApiSurfaceMemberSelection.SelectType(
                surface,
                typeIdentity));
    }

    public static AssemblyContextEntry<AssemblyContextMemberDeclaration>
        ExecuteDeclaration(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextMemberSelection selection,
        ApiSurfaceProjectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return AssemblyContextApiSurfaceSelection.Execute<
            AssemblyContextMemberDeclaration>(
            group,
            participant,
            limits,
            surface => ApiSurfaceMemberSelection.SelectDeclaration(
                surface,
                selection));
    }

    public static AssemblyContextEntry<AssemblyContextMemberBody> ExecuteBody(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextMemberSelection selection,
        ApiSurfaceProjectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return AssemblyContextApiSurfaceSelection.Execute<
            AssemblyContextMemberBody>(
            group,
            participant,
            limits,
            surface => ApiSurfaceMemberSelection.SelectBody(
                surface,
                selection));
    }
}

internal static class AssemblyContextApiSurfaceSelection
{
    internal static AssemblyContextEntry<TValue> Execute<TValue>(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        ApiSurfaceProjectionLimits limits,
        Func<ApiSurface, TValue> select)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(select);

        AssemblyContextApiSurfaceResult projection =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.IncludeAll,
                limits,
                [participant]);
        if (projection.Truncation is { } truncation)
        {
            return new AssemblyContextEntry<TValue>.Failed(
                new AssemblyContextSubject(participant.Assembly),
                new InvalidOperationException(
                    $"API-surface selection exceeded the "
                        + $"{truncation.Limit} bound "
                        + $"({truncation.Bound})."));
        }

        AssemblyContextEntry<AssemblyApiSurface>? entry =
            projection.Assemblies.Assemblies.SingleOrDefault();
        if (entry is null)
        {
            return new AssemblyContextEntry<TValue>.Failed(
                new AssemblyContextSubject(participant.Assembly),
                new InvalidOperationException(
                    "API-surface selection produced no participant outcome."));
        }

        return entry switch
        {
            AssemblyContextEntry<AssemblyApiSurface>.Available available =>
                Select(available, select),
            AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected =>
                new AssemblyContextEntry<TValue>.Rejected(
                    rejected.Subject,
                    rejected.Failure),
            AssemblyContextEntry<AssemblyApiSurface>.Failed failed =>
                new AssemblyContextEntry<TValue>.Failed(
                    failed.Subject,
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown API-surface participant outcome."),
        };
    }

    static AssemblyContextEntry<TValue> Select<TValue>(
        AssemblyContextEntry<AssemblyApiSurface>.Available available,
        Func<ApiSurface, TValue> select)
    {
        try
        {
            return new AssemblyContextEntry<TValue>.Available(
                available.Subject,
                select(available.Value.Surface));
        }
        catch (InvalidOperationException ex)
        {
            return new AssemblyContextEntry<TValue>.Failed(
                available.Subject,
                ex);
        }
    }
}
