using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// One owner-issued logical Member and optional exact physical body selected
/// for structural-clone seed binding.
/// </summary>
public sealed record AssemblyContextStructuralCloneMemberSeedRequest
{
    public AssemblyContextStructuralCloneMemberSeedRequest(
        MetadataTypeDefinitionName type,
        MemberAnchor member,
        AssemblyContextMemberSelection? body = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        if (body is not null)
        {
            if (body.TypeIdentity != type.ToEscapedFullName())
            {
                throw new ArgumentException(
                    "The exact body selection must identify the requested "
                        + "declaring Type.",
                    nameof(body));
            }

            if (body.MetadataToken is not int token)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(body),
                    "The exact body selection must identify a MethodDef "
                        + "token.");
            }

            EntityHandle handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.MethodDefinition
                || MetadataTokens.GetRowNumber(handle) == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(body),
                    "The exact body selection must identify a non-nil "
                        + "MethodDef token.");
            }
        }

        Type = type;
        Member = member;
        Body = body;
    }

    public MetadataTypeDefinitionName Type { get; }

    public MemberAnchor Member { get; }

    public AssemblyContextMemberSelection? Body { get; }
}

/// <summary>
/// Binds one owner-issued logical Member and optional exact body from an
/// implementation or reference-preferred surface to an implementation-backed
/// structural-clone Member seed.
/// </summary>
public static class AssemblyContextStructuralCloneSeedQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<StructuralCloneSearchSeed.Member>>
        MemberDefinition { get; } =
            new(
                "Assembly context structural clone member seed",
                InspectionCost.NetworkFree);

    public static AssemblyContextEntry<StructuralCloneSearchSeed.Member>
        ExecuteMember(
            AssemblyContextGroup implementationGroup,
            AssemblyContextParticipant implementationParticipant,
            AssemblyContextGroup? sourceGroup,
            AssemblyContextParticipant? sourceParticipant,
            AssemblyContextStructuralCloneMemberSeedRequest request,
            ApiSurfaceProjectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(implementationGroup);
        ArgumentNullException.ThrowIfNull(implementationParticipant);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        if ((sourceGroup is null) != (sourceParticipant is null))
        {
            throw new ArgumentException(
                "Source group and participant must either both be supplied "
                    + "or both be absent.",
                nameof(sourceGroup));
        }

        bool sharedParticipant =
            ReferenceEquals(sourceGroup, implementationGroup)
            && sourceParticipant is not null
            && ReferenceEquals(
                sourceParticipant.Assembly.Registration,
                implementationParticipant.Assembly.Registration);
        ApiSurface? sourceSurface = null;
        if (sourceGroup is not null && !sharedParticipant)
        {
            AssemblyContextEntry<ApiSurface> source =
                AssemblyContextApiSurfaceSelection.Execute(
                    sourceGroup,
                    sourceParticipant!,
                    limits,
                    static surface => surface);
            if (source is not AssemblyContextEntry<ApiSurface>.Available
                sourceAvailable)
            {
                return Propagate<
                    ApiSurface,
                    StructuralCloneSearchSeed.Member>(source);
            }

            sourceSurface = sourceAvailable.Value;
        }

        AssemblyContextEntry<MemberBinding> binding =
            AssemblyContextApiSurfaceSelection.Execute(
                implementationGroup,
                implementationParticipant,
                limits,
                implementationSurface => Bind(
                    implementationSurface,
                    sharedParticipant
                        ? implementationSurface
                        : sourceSurface,
                    request));
        if (binding is not AssemblyContextEntry<MemberBinding>.Available
            available)
        {
            return Propagate<
                MemberBinding,
                StructuralCloneSearchSeed.Member>(binding);
        }

        if (available.Value is MemberBinding.Logical logical)
        {
            return new AssemblyContextEntry<
                StructuralCloneSearchSeed.Member>.Available(
                    available.Subject,
                    new(request.Type, logical.Anchor));
        }

        if (available.Value is not MemberBinding.Method method)
        {
            throw new InvalidOperationException(
                "Unknown structural-clone Member binding.");
        }

        AssemblyContextEntry<MemberAnchor> anchor =
            AssemblyContextMethodAnchorQuery.ExecuteParticipant(
                implementationGroup,
                implementationParticipant,
                request.Type,
                method.MetadataToken,
                method.IsExtension);
        return anchor switch
        {
            AssemblyContextEntry<MemberAnchor>.Available result =>
                new AssemblyContextEntry<
                    StructuralCloneSearchSeed.Member>.Available(
                        result.Subject,
                        new(request.Type, result.Value)),
            AssemblyContextEntry<MemberAnchor>.Rejected rejected =>
                new AssemblyContextEntry<
                    StructuralCloneSearchSeed.Member>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<MemberAnchor>.Failed failed =>
                new AssemblyContextEntry<
                    StructuralCloneSearchSeed.Member>.Failed(
                        failed.Subject,
                        failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown method-anchor outcome."),
        };
    }

    static MemberBinding Bind(
        ApiSurface implementation,
        ApiSurface? source,
        AssemblyContextStructuralCloneMemberSeedRequest request)
    {
        AssemblyContextMemberBody? selectedBody =
            request.Body is null
                ? null
                : ApiSurfaceMemberSelection.SelectBody(
                    implementation,
                    request.Body);
        LogicalMember requested = ResolveRequestedMember(
            implementation,
            source,
            request);
        if (selectedBody is null)
        {
            if (requested.Member.MetadataToken is not int methodToken
                || MetadataTokens.EntityHandle(methodToken).Kind
                    != HandleKind.MethodDefinition)
            {
                return new MemberBinding.Logical(
                    ApiMemberIdentity.GetMemberAnchor(
                        requested.Type,
                        requested.Member));
            }

            return new MemberBinding.Method(
                methodToken,
                requested.Member.IsExtension);
        }

        if (!CallGraphMemberResolver
            .CreateBodySelectors(requested.Type, requested.Member)
            .Any(candidate =>
                candidate.BodyToken == selectedBody.BodyToken))
        {
            throw new ArgumentException(
                "The selected body does not belong to the requested "
                    + "logical member.",
                nameof(request));
        }

        return new MemberBinding.Method(
            selectedBody.BodyToken,
            requested.Member.IsExtension);
    }

    static LogicalMember ResolveRequestedMember(
        ApiSurface implementation,
        ApiSurface? source,
        AssemblyContextStructuralCloneMemberSeedRequest request)
    {
        LogicalMember? implementationSource =
            TryResolveLogicalMember(
                implementation,
                request.Type,
                request.Member);
        if (implementationSource is not null
            && (request.Body is null
                || OwnsBody(implementationSource, request.Body)))
        {
            return implementationSource;
        }

        LogicalMember? sourceMember = source is null
            ? null
            : TryResolveLogicalMember(
                source,
                request.Type,
                request.Member);
        if (sourceMember is not null
            && (request.Body is null
                || OwnsBody(sourceMember, request.Body)))
        {
            return ResolveCorrespondingMember(
                implementation,
                request.Type,
                sourceMember);
        }

        if (request.Body is null)
        {
            throw new InvalidOperationException(
                "The selected implementation and source API surfaces do "
                    + "not contain the requested structural-clone Member.");
        }

        throw new ArgumentException(
            "The selected body does not belong to the requested logical "
                + "member.",
            nameof(request));
    }

    static LogicalMember? TryResolveLogicalMember(
        ApiSurface surface,
        MetadataTypeDefinitionName type,
        MemberAnchor member)
    {
        ApiType[] types =
        [
            .. surface.Types
                .Where(candidate => candidate.DefinitionName == type)
                .Take(2),
        ];
        if (types.Length == 0)
            return null;
        if (types.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected API surface contains multiple TypeDefs "
                    + $"'{type.ToEscapedFullName()}'.");
        }

        ApiType resolvedType = types[0];
        ApiMember[] matches =
        [
            .. resolvedType.Members
                .Where(candidate =>
                    ApiMemberIdentity.GetMemberAnchor(
                        resolvedType,
                        candidate)
                    == member)
                .Take(2),
        ];
        if (matches.Length == 0)
            return null;
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "The structural-clone Member resolves more than once in "
                    + "the selected API surface.");
        }

        return new(resolvedType, matches[0]);
    }

    static bool OwnsBody(
        LogicalMember member,
        AssemblyContextMemberSelection body) =>
        CallGraphMemberResolver
            .CreateBodySelectors(member.Type, member.Member)
            .Any(candidate =>
                candidate.BodyToken == body.MetadataToken
                && candidate.MemberName == body.MemberName
                && candidate.SelectorKey == body.SelectorKey);

    static LogicalMember ResolveCorrespondingMember(
        ApiSurface implementation,
        MetadataTypeDefinitionName type,
        LogicalMember source)
    {
        ApiType resolvedType = ResolveType(implementation, type);
        string selector =
            CallGraphMemberResolver.CreateSelector(
                source.Type,
                source.Member).Key;
        ApiMember[] matches =
        [
            .. resolvedType.Members
                .Where(candidate =>
                    candidate.Kind == source.Member.Kind
                    && candidate.Name == source.Member.Name
                    && CallGraphMemberResolver.CreateSelector(
                            resolvedType,
                            candidate).Key
                        == selector)
                .Take(2),
        ];
        return matches.Length == 1
            ? new(resolvedType, matches[0])
            : throw new ArgumentException(
                "The structural-clone Member does not map uniquely to the "
                    + "selected implementation.",
                nameof(source));
    }

    static ApiType ResolveType(
        ApiSurface surface,
        MetadataTypeDefinitionName type)
    {
        ApiType[] matches =
        [
            .. surface.Types
                .Where(candidate => candidate.DefinitionName == type)
                .Take(2),
        ];
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"The selected implementation API surface does not contain "
                    + $"exactly one TypeDef '{type.ToEscapedFullName()}'.");
    }

    static AssemblyContextEntry<TDestination> Propagate<
        TSource,
        TDestination>(
        AssemblyContextEntry<TSource> entry)
        where TSource : notnull
        where TDestination : notnull =>
        entry switch
        {
            AssemblyContextEntry<TSource>.Rejected rejected =>
                new AssemblyContextEntry<TDestination>.Rejected(
                    rejected.Subject,
                    rejected.Failure),
            AssemblyContextEntry<TSource>.Failed failed =>
                new AssemblyContextEntry<TDestination>.Failed(
                    failed.Subject,
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Only unavailable assembly-context outcomes can be "
                    + "propagated."),
        };

    sealed record LogicalMember(ApiType Type, ApiMember Member);

    abstract record MemberBinding
    {
        private protected MemberBinding()
        {
        }

        public sealed record Logical(MemberAnchor Anchor) : MemberBinding;

        public sealed record Method(
            int MetadataToken,
            bool IsExtension) : MemberBinding;
    }
}
