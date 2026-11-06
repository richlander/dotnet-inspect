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
/// One selected paired Type, using Analysis's authenticated generated owners
/// and the exact lookup resolver's declaration anchors. No Library API surface
/// is built. Endpoint images remain owned by the caller until disposal.
/// </summary>
public sealed class FindingDiffAnyTypeQuery : IDisposable
{
    readonly FindingDiffAnyTypePairing _pairing;
    readonly FindingDiffAnyTypePairing.MemberComparison _comparison;
    readonly Dictionary<int, string> _beforeAnchors;
    readonly Dictionary<int, string> _afterAnchors;
    readonly PEReader _before;
    readonly PEReader _after;
    readonly TypeDefinitionHandle _beforeType;
    readonly TypeDefinitionHandle _afterType;
    bool _disposed;

    public FindingDiffAnyTypeQuery(PEReader before, TypeDefinitionHandle beforeType,
        PEReader after, TypeDefinitionHandle afterType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _before = before;
        _after = after;
        _beforeType = beforeType;
        _afterType = afterType;
        ApiType typeA = SelectedType(before, beforeType);
        ApiType typeB = SelectedType(after, afterType);
        if (!Equals(typeA.DefinitionName, typeB.DefinitionName))
            throw new ArgumentException("The selected Types must have the same metadata identity.");
        _beforeAnchors = Anchors(typeA);
        _afterAnchors = Anchors(typeB);
        TypeGeneratedMethodOwnership ownersA = TypeGeneratedMethodOwnership.Resolve(before, beforeType, cancellationToken);
        TypeGeneratedMethodOwnership ownersB = TypeGeneratedMethodOwnership.Resolve(after, afterType, cancellationToken);
        _pairing = new(before, after);
        _comparison = _pairing.BeginMembers(typeA.FullName, ownersA.Owners, ownersB.Owners);
    }

    public FindingDiffAnyTypeResult? Result { get; private set; }

    /// <summary>Advances through complete Member body groups; preparation is paid once at construction.</summary>
    public bool Step(TimeSpan budget, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_comparison.Step(budget, cancellationToken))
            return false;
        if (Result is not null)
            return true;
        FindingDiffAnyTypeMembers result = _comparison.Result!;
        HashSet<int> changedBefore = [], changedAfter = [];
        FindingDiffAnyState residualApi = result.ResidualApi;
        if (result.Api == FindingDiffAnyState.Changed)
        {
            ApiSurface apiA = ApiSurfaceExtractor.ExtractDeclarations(_before, ApiSurfaceExtractionScope.ApiAxis,
                includeType: type => type == _beforeType);
            ApiSurface apiB = ApiSurfaceExtractor.ExtractDeclarations(_after, ApiSurfaceExtractionScope.ApiAxis,
                includeType: type => type == _afterType);
            ApiDiff diff = MetadataFindings.CompareApi(apiA, apiB, new FindingSubject(result.FullName, "Type API"),
                new ApiDiffOptions(ApiDiffScope.Signature)).ApiDiff;
            if (diff.InspectionFailures.Count != 0)
                throw new BadImageFormatException("The selected Type's API comparison could not be completed.");
            residualApi = FindingDiffAnyState.Unchanged;
            foreach (TypeDiff type in diff.TypeDiffs)
            {
                if (type.IsAdded || type.IsRemoved)
                {
                    residualApi = FindingDiffAnyState.Changed;
                    foreach (ApiMember member in apiA.Types.SelectMany(type => type.Members)) Add(changedBefore, member);
                    foreach (ApiMember member in apiB.Types.SelectMany(type => type.Members)) Add(changedAfter, member);
                }
                foreach (ApiChange change in type.Changes)
                {
                    if (change.Subject?.Kind == ApiChangeSubjectKind.Type)
                        residualApi = FindingDiffAnyState.Changed;
                    if (change.Subject?.OldMember is { } oldMember) Add(changedBefore, oldMember.Member);
                    if (change.Subject?.NewMember is { } newMember) Add(changedAfter, newMember.Member);
                }
            }
        }
        FindingDiffAnyState Api(FindingDiffAnyMemberState member)
            => result.Api != FindingDiffAnyState.Changed ? member.Api
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
