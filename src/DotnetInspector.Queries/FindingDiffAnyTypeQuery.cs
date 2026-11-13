using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>One paired declaration with the anchors issued by each endpoint.</summary>
public sealed record FindingDiffAnyTypeMember(
    string? BeforeFingerprint,
    string? AfterFingerprint,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body);

public sealed record FindingDiffAnyTypeResult(
    string FullName,
    ImmutableArray<FindingDiffAnyTypeMember> Members,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    FindingDiffAnyState ResidualApi,
    FindingDiffAnyState ResidualBody);

/// <summary>
/// One selected Type (a nil handle denotes its absent endpoint), using
/// authenticated generated owners for paired Types and exact declaration
/// anchors. No Library API surface
/// is built. Endpoint images remain owned by the caller until disposal.
/// </summary>
public sealed class FindingDiffAnyTypeQuery : IDisposable
{
    readonly FindingDiffAnyTypePairing _pairing;
    readonly FindingDiffAnyTypePairing.MemberComparison? _comparison;
    readonly FindingDiffAnyTypeMembers? _oneSided;
    readonly Dictionary<int, string> _beforeAnchors;
    readonly Dictionary<int, string> _afterAnchors;
    readonly PEReader _before;
    readonly PEReader _after;
    readonly TypeDefinitionHandle _beforeType;
    readonly TypeDefinitionHandle _afterType;
    bool _disposed;

    public FindingDiffAnyTypeQuery(PEReader before, TypeDefinitionHandle beforeType,
        PEReader after, TypeDefinitionHandle afterType, CancellationToken cancellationToken = default)
        : this(before, beforeType, after, afterType, FindingDiffAnyAxes.ApiAndBody, cancellationToken) { }

    /// <summary>API-only scope is available for the one-sided form.</summary>
    public FindingDiffAnyTypeQuery(PEReader before, TypeDefinitionHandle beforeType,
        PEReader after, TypeDefinitionHandle afterType, FindingDiffAnyAxes axes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (axes is not (FindingDiffAnyAxes.ApiAndBody or FindingDiffAnyAxes.Api))
            throw new ArgumentOutOfRangeException(nameof(axes));
        if (!beforeType.IsNil && !afterType.IsNil && axes != FindingDiffAnyAxes.ApiAndBody)
            throw new ArgumentException("API-only scope requires a one-sided Type.", nameof(axes));
        cancellationToken.ThrowIfCancellationRequested();
        _before = before;
        _after = after;
        _beforeType = beforeType;
        _afterType = afterType;
        if (beforeType.IsNil && afterType.IsNil)
            throw new ArgumentException("At least one endpoint must select a Type.");
        ApiType? typeA = beforeType.IsNil ? null : SelectedType(before, beforeType);
        ApiType? typeB = afterType.IsNil ? null : SelectedType(after, afterType);
        if (typeA is not null && typeB is not null && !Equals(typeA.DefinitionName, typeB.DefinitionName))
            throw new ArgumentException("The selected Types must have the same metadata identity.");
        _beforeAnchors = typeA is null ? [] : Anchors(typeA);
        _afterAnchors = typeB is null ? [] : Anchors(typeB);
        _pairing = new(before, after);
        if (typeA is null || typeB is null)
        {
            // Ownership authentication walks IL. One-sided presence needs no
            // body reads; unproven generated owners therefore stay residual.
            _oneSided = _pairing.OneSidedMembers((typeA ?? typeB)!.FullName, axes);
            return;
        }
        TypeGeneratedMethodOwnership ownersA = TypeGeneratedMethodOwnership.Resolve(before, beforeType, cancellationToken);
        TypeGeneratedMethodOwnership ownersB = TypeGeneratedMethodOwnership.Resolve(after, afterType, cancellationToken);
        _comparison = _pairing.BeginMembers(typeA.FullName, ownersA.Owners, ownersB.Owners);
    }

    public FindingDiffAnyTypeResult? Result { get; private set; }

    /// <summary>Advances through complete Member body groups; preparation is paid once at construction.</summary>
    public bool Step(TimeSpan budget, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        if (_comparison is not null && !_comparison.Step(budget, cancellationToken))
            return false;
        if (Result is not null)
            return true;
        FindingDiffAnyTypeMembers result = _oneSided ?? _comparison!.Result!;
        HashSet<int> changedBefore = [], changedAfter = [];
        FindingDiffAnyState residualApi = result.ResidualApi;
        if (_oneSided is null && result.Api == FindingDiffAnyState.Changed)
        {
            ApiSurface apiA = ApiSurfaceExtractor.ExtractDeclarations(_before, ApiSurfaceExtractionScope.ApiAxis,
                includeType: type => type == _beforeType);
            ApiSurface apiB = ApiSurfaceExtractor.ExtractDeclarations(_after, ApiSurfaceExtractionScope.ApiAxis,
                includeType: type => type == _afterType);
            ApiFindingComparison comparison = MetadataFindings.CompareApi(apiA, apiB,
                new FindingSubject(result.FullName, "Type API"), new ApiDiffOptions(ApiDiffScope.All));
            ApiDiff diff = comparison.ApiDiff;
            if (comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete typePairs
                || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete memberPairs
                || diff.InspectionFailures.Count != 0)
                throw new BadImageFormatException("The selected Type's API comparison could not be completed.");
            // Raw uncertain facts are candidates, not declaration cues. Settle
            // them against the complete Type comparison even when a separate
            // Member change already made the aggregate Changed.
            residualApi = FindingDiffAnyState.Unchanged;
            foreach (PairFinding<ApiTypeHandle> pair in typePairs.Pairs)
            {
                if (pair.Kind != PairKind.Present)
                    residualApi = FindingDiffAnyState.Changed;
                if (pair.Kind is PairKind.Added or PairKind.Removed)
                {
                    foreach (ApiMember member in apiA.Types.SelectMany(type => type.Members)) Add(changedBefore, member);
                    foreach (ApiMember member in apiB.Types.SelectMany(type => type.Members)) Add(changedAfter, member);
                }
            }
            foreach (PairFinding<ApiMemberHandle> pair in memberPairs.Pairs)
            {
                switch (pair.Value)
                {
                    case PairFinding<ApiMemberHandle>.Added added:
                        Add(changedAfter, added.New.Payload.Member);
                        break;
                    case PairFinding<ApiMemberHandle>.Removed removed:
                        Add(changedBefore, removed.Old.Payload.Member);
                        break;
                    case PairFinding<ApiMemberHandle>.Changed changed:
                        Add(changedBefore, changed.Old.Payload.Member);
                        Add(changedAfter, changed.New.Payload.Member);
                        break;
                }
            }

        }
        FindingDiffAnyState Api(FindingDiffAnyMemberState member)
            => _oneSided is not null || result.Api != FindingDiffAnyState.Changed ? member.Api
                : changedBefore.Contains(MetadataTokens.GetToken(member.Before))
                    || changedAfter.Contains(MetadataTokens.GetToken(member.After))
                    ? FindingDiffAnyState.Changed : FindingDiffAnyState.Unchanged;
        Result = new(result.FullName,
            [.. result.Members.Select(member => new FindingDiffAnyTypeMember(
                Anchor(_beforeAnchors, member.Before), Anchor(_afterAnchors, member.After), Api(member), member.Body))],
            result.Api, result.Body, residualApi, result.ResidualBody);
        return true;
    }

    static void Add(HashSet<int> tokens, ApiMember member)
    {
        if ((member.DeclarationMetadataToken ?? member.MetadataToken) is int token)
            tokens.Add(token);
    }

    static ApiType SelectedType(PEReader image, TypeDefinitionHandle handle)
        => ApiSurfaceExtractor.ExtractDeclarations(image, ApiSurfaceExtractionScope.IncludeAll,
            includeType: candidate => candidate == handle).Types.Single();

    static Dictionary<int, string> Anchors(ApiType type)
        => MemberTargetResolver.GetDeclaredCandidates(type)
            .Where(candidate => (candidate.Member.DeclarationMetadataToken ?? candidate.Member.MetadataToken).HasValue)
            .ToDictionary(candidate => (candidate.Member.DeclarationMetadataToken ?? candidate.Member.MetadataToken)!.Value,
                candidate => candidate.Anchor.Fingerprint);

    static string? Anchor(Dictionary<int, string> anchors, EntityHandle handle)
        => handle.IsNil ? null : anchors.TryGetValue(MetadataTokens.GetToken(handle), out string? fingerprint)
            ? fingerprint : throw new BadImageFormatException($"A census Member {MetadataTokens.GetToken(handle):x8} has no declaration anchor.");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pairing.Release();
    }
}
