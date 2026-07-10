using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class MetadataFindingsTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Fact]
    public void SameSurface_IsExactAndAllPresent()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Get", "int Get()"),
            Property("Name", "string Name { get; }"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Get", "int Get()"),
            Property("Name", "string Name { get; }"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.True(result.IsExact);
        Assert.All(Pairs(result.Types), pair => Assert.Equal(PairKind.Present, pair.Kind));
        Assert.All(Pairs(result.Members), pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void AddedAndRemovedMembers_AgreeWithApiDiffAnalyzer()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Removed", "void Removed()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Added", "void Added()"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);
        var expected = GetExpectedMembers(result.ApiDiff);

        Assert.Equal(expected.Added, PairKeys(result.Members, PairKind.Added));
        Assert.Equal(expected.Removed, PairKeys(result.Members, PairKind.Removed));
        Assert.Empty(PairKeys(result.Members, PairKind.Changed));
    }

    [Fact]
    public void MatchedMemberFacetChanges_AreChangedPairs()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method(
                "Run",
                "void Run()",
                accessibility: "protected",
                isObsolete: true,
                attributes: ["System.ObsoleteAttribute"]),
        ]));

        var result = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            Subject,
            new ApiDiffOptions(ApiDiffScope.All));

        var pair = Assert.Single(Pairs(result.Members));
        Assert.Equal(PairKind.Changed, pair.Kind);
        Assert.Contains("accessibility: public -> protected", pair.Detail, StringComparison.Ordinal);
        Assert.Contains("obsolete: false -> true", pair.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchedTypeFacetChanges_AreChangedPairs()
    {
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", isByRefLike: true));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        var pair = Assert.Single(Pairs(result.Types));
        Assert.Equal(PairKind.Changed, pair.Kind);
        Assert.Contains("byref-like: false -> true", pair.Detail, StringComparison.Ordinal);
        Assert.False(result.IsExact);
        Assert.True(result.ApiDiff.IsEmpty);
    }

    [Fact]
    public void AddedAndRemovedTypes_AgreeWithApiDiffAnalyzer()
    {
        var oldSurface = Surface(Type("Keep"), Type("Removed"));
        var newSurface = Surface(Type("Keep"), Type("Added"));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);
        var pairs = Pairs(result.Types);

        Assert.Contains(pairs, pair =>
            pair is PairFinding<ApiTypeHandle>.Added added
            && added.New.Payload.TypeFullName == "TestNamespace.Added");
        Assert.Contains(pairs, pair =>
            pair is PairFinding<ApiTypeHandle>.Removed removed
            && removed.Old.Payload.TypeFullName == "TestNamespace.Removed");
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes),
            change => change.Kind == ChangeKind.TypeAdded);
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes),
            change => change.Kind == ChangeKind.TypeRemoved);
    }

    [Fact]
    public void ReorderedMembers_AreAllPresentUnderIdentitySetMode()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("A", "void A()"),
            Method("B", "void B()"),
            Method("C", "void C()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("C", "void C()"),
            Method("A", "void A()"),
            Method("B", "void B()"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.True(result.IsExact);
        Assert.Equal(3, Pairs(result.Members).Length);
        Assert.All(Pairs(result.Members), pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void SignatureChangesRemainConservativeAddRemovePairs()
    {
        var oldSurface = Surface(Type("Widget", members: [Method("Run", "void Run(int value)")]));
        var newSurface = Surface(Type("Widget", members: [Method("Run", "void Run(string value)")]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.Collection(
            Pairs(result.Members).OrderBy(pair => pair.Kind),
            pair => Assert.Equal(PairKind.Added, pair.Kind),
            pair => Assert.Equal(PairKind.Removed, pair.Kind));
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(type => type.Changes),
            change => change.Kind == ChangeKind.MemberSignatureChanged);
    }

    [Fact]
    public void ApiSurfaceMembersAreNotFilteredByNameHeuristics()
    {
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", members: [Method("s_value", "void s_value()")]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        var added = Assert.Single(Pairs(result.Members));
        Assert.Equal(PairKind.Added, added.Kind);
    }

    [Fact]
    public void RealAssemblySelfCompare_IsExact()
    {
        using var stream = File.OpenRead(typeof(ApiDiffAnalyzer).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var result = MetadataFindings.CompareApi(surface, surface, Subject);

        Assert.True(result.IsExact);
        Assert.True(Pairs(result.Members).Length > 50);
    }

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected a complete comparison; failure: {comparison.Failure}");

    static string[] PairKeys(
        FindingComparison<ApiMemberHandle> comparison,
        PairKind kind)
        => Pairs(comparison)
            .Where(pair => pair.Kind == kind)
            .Select(pair => pair switch
            {
                PairFinding<ApiMemberHandle>.Added added => added.New.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Removed removed => removed.Old.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Changed changed => changed.New.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Present present => present.New.Key.IdentityKey,
                _ => throw new InvalidOperationException(),
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

    static ExpectedMembers GetExpectedMembers(ApiDiff diff)
    {
        var added = new List<string>();
        var removed = new List<string>();
        foreach (var change in diff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject?.Kind != ApiChangeSubjectKind.Member)
                continue;

            switch (change.Kind)
            {
                case ChangeKind.MemberAdded when change.Subject.NewMember?.CanonicalSignature is { } newKey:
                    added.Add(newKey);
                    break;
                case ChangeKind.MemberRemoved when change.Subject.OldMember?.CanonicalSignature is { } oldKey:
                    removed.Add(oldKey);
                    break;
            }
        }

        return new ExpectedMembers(
            [.. added.Order(StringComparer.Ordinal)],
            [.. removed.Order(StringComparer.Ordinal)]);
    }

    static ApiSurface Surface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiType Type(
        string name,
        string kind = "class",
        string? ns = "TestNamespace",
        bool isByRefLike = false,
        List<ApiMember>? members = null)
        => new()
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            IsByRefLike = isByRefLike,
            Members = members ?? [],
        };

    static ApiMember Method(
        string name,
        string signature,
        bool isObsolete = false,
        string? accessibility = null,
        IReadOnlyList<string>? attributes = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            IsObsolete = isObsolete,
            Accessibility = accessibility,
            Attributes = attributes?.ToList() ?? [],
        };

    static ApiMember Property(string name, string signature)
        => new() { Name = name, Kind = "property", Signature = signature };

    sealed record ExpectedMembers(string[] Added, string[] Removed);
}
