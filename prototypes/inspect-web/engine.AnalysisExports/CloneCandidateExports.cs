using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InspectWeb.Engine;
using InspectWeb.Engine.AnalysisFacade;

[SupportedOSPlatform("browser")]
public static partial class AnalysisExports
{
    const int MaximumBrowserCloneSeedMethods = 1_000;

    /// <summary>
    /// Runs one Library, Type, or Member Clone Candidates search over the exact
    /// package participants supplied for the browser Workspace.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryCloneCandidates(
        string requestJson)
    {
        BrowserCloneCandidateResult result =
            await CloneCandidatesAsync(requestJson);
        return JsonSerializer.Serialize(
            result,
            BrowserAnalysisJsonContext.Default
                .BrowserCloneCandidateResult);
    }

    static async Task<BrowserCloneCandidateResult> CloneCandidatesAsync(
        string requestJson)
    {
        BrowserCloneCandidateRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserAnalysisJsonContext.Default
                    .BrowserCloneCandidateRequest)
            ?? throw new ArgumentException(
                "A Clone Candidates request is required.");
        ValidateCloneCandidateRequest(request);

        BrowserPackageRequest[] packages =
        [
            .. request.Packages.Select(package =>
                new BrowserPackageRequest(
                    package.PackageId,
                    package.Version,
                    package.TargetFramework)),
        ];
        await using BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(packages);
        BrowserInspectionScope scope = resolution.Lease.Scope;
        if (resolution.RequestedCoordinates.Length != packages.Length)
        {
            throw new InvalidOperationException(
                "The Clone Candidates Workspace did not preserve its "
                    + "distinct package coordinates.");
        }
        BrowserPackageCoordinate coordinate =
            scope.Coordinate(
                resolution.RequestedCoordinates[
                    request.SelectedPackageIndex]);
        BrowserWorkspaceParticipant containingLibrary =
            scope.LibraryParticipant(coordinate, request.Assembly);
        StructuralCloneSearchSeed seed =
            CreateCloneSeed(scope, containingLibrary, request.Seed);
        WorkspaceStructuralCloneSearchResult queryResult =
            await scope.QueryCloneCandidatesAsync(
                containingLibrary,
                seed,
                Parse(request.Breadth),
                Parse(request.Discovery),
                new WorkspaceStructuralCloneSearchLimits(
                    MaximumSeedMethods: MaximumBrowserCloneSeedMethods,
                    MaximumParticipants:
                        BrowserInspectionScope.MaxAssembliesPerRole));
        return BrowserCloneCandidateWireProjection.Project(
            request,
            CloneCandidatePresentation.Create(queryResult));
    }

    static StructuralCloneSearchSeed CreateCloneSeed(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant containingLibrary,
        BrowserCloneCandidateSeedRequest request)
    {
        MetadataTypeDefinitionName? type = request.TypeDefinitionId is null
            ? null
            : ParseTypeDefinition(request.TypeDefinitionId);
        return request.Kind switch
        {
            BrowserCloneCandidateSeedKind.Library =>
                new StructuralCloneSearchSeed.Library(),
            BrowserCloneCandidateSeedKind.Type =>
                new StructuralCloneSearchSeed.Type(type!),
            BrowserCloneCandidateSeedKind.Member =>
                new StructuralCloneSearchSeed.Member(
                    type!,
                    CreateMemberAnchor(
                        scope,
                        containingLibrary,
                        type!,
                        request.Member!,
                        request.Body)),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    static MemberAnchor CreateMemberAnchor(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant containingLibrary,
        MetadataTypeDefinitionName type,
        BrowserCloneMemberAnchor requested,
        BrowserCloneCandidateBodySelection? body)
    {
        var logical = new MemberAnchor(
            requested.StableSelector,
            requested.CanonicalSignature,
            requested.Fingerprint,
            requested.TypeFullName,
            requested.MemberName);
        BrowserWorkspaceParticipant? surfaceParticipant =
            scope.TryGetSurfaceParticipant(containingLibrary);
        LogicalMember? source = surfaceParticipant is null
            ? null
            : scope.UseSurfaceParticipant(
                surfaceParticipant,
                (group, participant) => TryResolveLogicalMember(
                    BrowserMemberResolution.ImplementationSurface(
                        group,
                        participant),
                    type,
                    logical));

        return scope.UseMetadataParticipant(
            containingLibrary,
            (group, participant) =>
            {
                ApiSurface implementation =
                    BrowserMemberResolution.ImplementationSurface(
                        group,
                        participant);
                CallGraphMemberResolution? selected = body is null
                    ? null
                    : BrowserMemberResolution
                        .ResolveImplementationMember(
                            implementation,
                            type.ToEscapedFullName(),
                            body.MemberName,
                            body.SelectorKey,
                            body.MetadataToken);
                LogicalMember mapped = ResolveRequestedMember(
                    implementation,
                    type,
                    logical,
                    source,
                    body);
                if (body is null)
                {
                    if (mapped.Member.MetadataToken is { } token
                        && MetadataTokens.EntityHandle(token).Kind
                            == HandleKind.MethodDefinition)
                    {
                        return BrowserSurfaceProjection.Require(
                            AssemblyContextMethodAnchorQuery
                                .ExecuteParticipant(
                                    group,
                                    participant,
                                    type,
                                    token,
                                    mapped.Member.IsExtension),
                            $"Clone seed '{type.ToEscapedFullName()}."
                                + $"{mapped.Member.Name}'");
                    }

                    return ApiMemberIdentity.GetMemberAnchor(
                        mapped.Type,
                        mapped.Member);
                }

                if (!CallGraphMemberResolver
                    .CreateBodySelectors(mapped.Type, mapped.Member)
                    .Any(candidate =>
                        candidate.BodyToken == selected!.BodyToken))
                {
                    throw new ArgumentException(
                        "The selected body does not belong to the "
                            + "requested logical member.",
                        nameof(body));
                }

                return BrowserSurfaceProjection.Require(
                    AssemblyContextMethodAnchorQuery.ExecuteParticipant(
                        group,
                        participant,
                        type,
                        selected!.BodyToken,
                        mapped.Member.IsExtension),
                    $"Clone seed '{type.ToEscapedFullName()}."
                        + $"{body.MemberName}'");
            });
    }

    static LogicalMember ResolveRequestedMember(
        ApiSurface implementation,
        MetadataTypeDefinitionName type,
        MemberAnchor logical,
        LogicalMember? source,
        BrowserCloneCandidateBodySelection? body)
    {
        LogicalMember? implementationSource =
            TryResolveLogicalMember(
                implementation,
                type,
                logical);
        if (implementationSource is not null
            && (body is null
                || OwnsBody(
                    implementationSource,
                    body.MemberName,
                    body.SelectorKey,
                    body.MetadataToken)))
        {
            return implementationSource;
        }

        if (source is not null
            && (body is null
                || OwnsBody(
                    source,
                    body.MemberName,
                    body.SelectorKey,
                    body.MetadataToken)))
        {
            return ResolveCorrespondingMember(
                implementation,
                type,
                source);
        }

        if (body is null)
        {
            throw new InvalidOperationException(
                "The selected implementation and browser API surfaces do "
                    + "not contain the requested Clone Candidates member.");
        }

        throw new ArgumentException(
            "The selected body does not belong to the requested logical "
                + "member.",
            nameof(body));
    }

    static LogicalMember? TryResolveLogicalMember(
        ApiSurface surface,
        MetadataTypeDefinitionName type,
        MemberAnchor member)
    {
        ApiType[] types =
        [
            .. surface.Types
                .Where(candidate =>
                    candidate.DefinitionName == type)
                .Take(2),
        ];
        if (types.Length == 0)
            return null;
        if (types.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected browser API surface contains multiple "
                    + $"TypeDefs '{type.ToEscapedFullName()}'.");
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
                "The Clone Candidates member resolves more than once in "
                    + "the selected browser API surface.");
        }

        return new LogicalMember(resolvedType, matches[0]);
    }

    static bool OwnsBody(
        LogicalMember member,
        string memberName,
        string selectorKey,
        int bodyToken) =>
        CallGraphMemberResolver
            .CreateBodySelectors(member.Type, member.Member)
            .Any(candidate =>
                candidate.BodyToken == bodyToken
                && candidate.MemberName == memberName
                && candidate.SelectorKey == selectorKey);

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
            ? new LogicalMember(resolvedType, matches[0])
            : throw new ArgumentException(
                "The Clone Candidates member does not map uniquely to the "
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
                .Where(candidate =>
                    candidate.DefinitionName == type)
                .Take(2),
        ];
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"The selected browser API surface does not contain exactly "
                    + $"one TypeDef '{type.ToEscapedFullName()}'.");
    }

    sealed record LogicalMember(ApiType Type, ApiMember Member);

    static MetadataTypeDefinitionName ParseTypeDefinition(
        string identity) =>
        MetadataTypeDefinitionName.ParseSerialized(identity)
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : throw new ArgumentException(
                    $"'{identity}' is not an exact metadata type-definition "
                        + "identity.",
                    nameof(identity));

    static StructuralCloneCandidateBreadth Parse(
        BrowserCloneCandidateBreadth breadth) =>
        breadth switch
        {
            BrowserCloneCandidateBreadth.Self =>
                StructuralCloneCandidateBreadth.Self,
            BrowserCloneCandidateBreadth
                .SelfAndRegisteredEcosystems =>
                    StructuralCloneCandidateBreadth
                        .SelfAndRegisteredEcosystems,
            BrowserCloneCandidateBreadth.Everything =>
                StructuralCloneCandidateBreadth.Everything,
            _ => throw new ArgumentOutOfRangeException(nameof(breadth)),
        };

    static StructuralCloneCandidateDiscovery Parse(
        BrowserCloneCandidateDiscovery discovery) =>
        discovery switch
        {
            BrowserCloneCandidateDiscovery.SimilarNames =>
                StructuralCloneCandidateDiscovery.SimilarNames,
            BrowserCloneCandidateDiscovery.All =>
                StructuralCloneCandidateDiscovery.All,
            _ => throw new ArgumentOutOfRangeException(nameof(discovery)),
        };

    static void ValidateCloneCandidateRequest(
        BrowserCloneCandidateRequest request)
    {
        if (request.SchemaVersion != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Clone Candidates request schema version is unsupported.");
        }
        if (request.Packages is not { Length: > 0 })
        {
            throw new ArgumentException(
                "At least one exact package coordinate is required.",
                nameof(request));
        }
        if (request.SelectedPackageIndex < 0
            || request.SelectedPackageIndex >= request.Packages.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The selected package index is outside the request.");
        }
        if (!Enum.IsDefined(request.Breadth)
            || !Enum.IsDefined(request.Discovery))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Clone Candidates breadth or discovery value is invalid.");
        }
        if (request.Packages.Any(package =>
                package is null
                || string.IsNullOrWhiteSpace(package.PackageId)
                || string.IsNullOrWhiteSpace(package.Version)
                || string.IsNullOrWhiteSpace(package.TargetFramework)))
        {
            throw new ArgumentException(
                "Every Clone Candidates package coordinate must be exact.",
                nameof(request));
        }
        if (request.Packages
            .GroupBy(
                package => (
                    package.PackageId,
                    package.Version,
                    package.TargetFramework),
                EqualityComparer<(
                    string PackageId,
                    string Version,
                    string TargetFramework)>.Default)
            .Any(group => group.Skip(1).Any()))
        {
            throw new ArgumentException(
                "A Clone Candidates request cannot repeat a package "
                    + "coordinate.",
                nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
        ArgumentNullException.ThrowIfNull(request.Seed);
        if (!Enum.IsDefined(request.Seed.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The Clone Candidates seed kind is invalid.");
        }

        bool hasType =
            !string.IsNullOrWhiteSpace(request.Seed.TypeDefinitionId);
        bool hasMember = request.Seed.Member is not null;
        bool hasBody = request.Seed.Body is not null;
        bool validShape = request.Seed.Kind switch
        {
            BrowserCloneCandidateSeedKind.Library =>
                !hasType && !hasMember && !hasBody,
            BrowserCloneCandidateSeedKind.Type =>
                hasType && !hasMember && !hasBody,
            BrowserCloneCandidateSeedKind.Member =>
                hasType && hasMember,
            _ => false,
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "The Clone Candidates seed fields do not match its kind.",
                nameof(request));
        }

        if (request.Seed.Member is { } member)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                member.StableSelector);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                member.CanonicalSignature);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.Fingerprint);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.TypeFullName);
            ArgumentException.ThrowIfNullOrWhiteSpace(member.MemberName);
            if (!MemberAnchor.ComputeFingerprint(
                    member.CanonicalSignature)
                .Equals(
                    member.Fingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The Clone Candidates member fingerprint does not match "
                        + "its canonical signature.",
                    nameof(request));
            }
        }
        if (request.Seed.Body is { } body)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(body.MemberName);
            ArgumentException.ThrowIfNullOrWhiteSpace(body.SelectorKey);
            if ((body.MetadataToken & unchecked((int)0xff000000))
                    != 0x06000000
                || (body.MetadataToken & 0x00ffffff) == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "The Clone Candidates body token is not a MethodDef.");
            }
        }
    }
}
