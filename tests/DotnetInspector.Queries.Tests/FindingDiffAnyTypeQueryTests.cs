using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class FindingDiffAnyTypeQueryTests
{
    [Theory]
    [InlineData("LambdaOnly")]
    [InlineData("LocalFunctionOnly")]
    [InlineData("AsyncOnly")]
    [InlineData("IteratorOnly")]
    public void GeneratedChange_IsReportedOnItsAnchoredMember(string name)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        using var query = new FindingDiffAnyTypeQuery(before, Type(before, name), after, Type(after, name), TestContext.Current.CancellationToken);
        while (!query.Step(TimeSpan.Zero, TestContext.Current.CancellationToken)) { }
        FindingDiffAnyTypeResult result = query.Result!;
        Assert.Equal(FindingDiffAnyState.Changed, result.Body);
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualBody);
        Assert.Contains(result.Members, member => member.Body == FindingDiffAnyState.Changed
            && member.BeforeFingerprint is not null && member.AfterFingerprint is not null);
    }

    [Fact]
    public void NullableChange_PreservesBothDifferentFingerprintsOnOneMember()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        using var query = new FindingDiffAnyTypeQuery(before, Type(before, "AnchorNullability"), after, Type(after, "AnchorNullability"), TestContext.Current.CancellationToken);
        Assert.True(query.Step(TimeSpan.MaxValue, TestContext.Current.CancellationToken));
        FindingDiffAnyTypeMember changed = Assert.Single(query.Result!.Members, member => member.Api == FindingDiffAnyState.Changed);
        Assert.NotNull(changed.BeforeFingerprint);
        Assert.NotNull(changed.AfterFingerprint);
        Assert.NotEqual(changed.BeforeFingerprint, changed.AfterFingerprint);
    }

    [Theory]
    [InlineData("MemberFoldUncertainGetterStatus")]
    [InlineData("PublicAttribute")]
    [InlineData("AbstractToVirtual")]
    [InlineData("TypeAttributeChanged")]
    [InlineData("ITypeAttributeOnly")]
    [InlineData("MixedUncertainApi")]
    [InlineData("PublicTypeToInternal")]
    [InlineData("AnchorNullability")]
    public void MemberApiCues_AgreeWithCompleteSelectedTypeComparison(string name)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        TypeDefinitionHandle a = Type(before, name), b = Type(after, name);
        using var query = new FindingDiffAnyTypeQuery(before, a, after, b, TestContext.Current.CancellationToken);
        Assert.True(query.Step(TimeSpan.MaxValue, TestContext.Current.CancellationToken));
        ApiSurface apiA = ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.ApiAxis, type => type == a);
        ApiSurface apiB = ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.ApiAxis, type => type == b);
        ApiFindingComparison comparison = MetadataFindings.CompareApi(apiA, apiB, new FindingSubject(name, "oracle"),
            new ApiDiffOptions(ApiDiffScope.All));
        ApiDiff diff = comparison.ApiDiff;
        HashSet<string> changed = [];
        foreach (TypeDiff type in diff.TypeDiffs)
        {
            if (type.IsAdded || type.IsRemoved)
            {
                foreach (ApiType owner in apiA.Types.Concat(apiB.Types))
                    foreach (MemberTargetCandidate member in MemberTargetResolver.GetDeclaredCandidates(owner))
                        changed.Add(member.Anchor.Fingerprint);
            }
            foreach (ApiChange change in type.Changes)
            {
                if (change.Subject?.OldMember is { } oldMember) changed.Add(oldMember.Anchor!.Fingerprint);
                if (change.Subject?.NewMember is { } newMember) changed.Add(newMember.Anchor!.Fingerprint);
            }
        }
        foreach (PairFinding<ApiMemberHandle> pairFinding in Assert.IsType<FindingComparison<ApiMemberHandle>.Complete>(comparison.Members.Value).Pairs)
        {
            if (pairFinding.Kind == PairKind.Present) continue;
            var pairValue = (IPairFinding)pairFinding;
            if (pairValue.Old is Finding<ApiMemberHandle> oldMember) changed.Add(oldMember.Payload.Anchor!.Fingerprint);
            if (pairValue.New is Finding<ApiMemberHandle> newMember) changed.Add(newMember.Payload.Anchor!.Fingerprint);
        }
        Assert.All(query.Result!.Members, member => Assert.Equal(
            member.BeforeFingerprint is not null && changed.Contains(member.BeforeFingerprint)
                || member.AfterFingerprint is not null && changed.Contains(member.AfterFingerprint)
                ? FindingDiffAnyState.Changed : FindingDiffAnyState.Unchanged, member.Api));
    }

    [Theory]
    [InlineData("TypeAttributeChanged")]
    [InlineData("ITypeAttributeOnly")]
    public void TypeAttributeOnlyChange_IsAnApiChangeInTheResidual(string name)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        using var query = new FindingDiffAnyTypeQuery(before, Type(before, name), after, Type(after, name), TestContext.Current.CancellationToken);
        Assert.True(query.Step(TimeSpan.MaxValue, TestContext.Current.CancellationToken));
        Assert.Equal(FindingDiffAnyState.Changed, query.Result!.Api);
        Assert.Equal(FindingDiffAnyState.Changed, query.Result.ResidualApi);
        Assert.All(query.Result.Members, member => Assert.Equal(FindingDiffAnyState.Unchanged, member.Api));
    }

    [Fact]
    public void UncertainLayoutWithMemberChange_DoesNotProduceADeclarationCue()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        using var query = new FindingDiffAnyTypeQuery(before, Type(before, "MixedUncertainTypeLayout"),
            after, Type(after, "MixedUncertainTypeLayout"), TestContext.Current.CancellationToken);
        Assert.True(query.Step(TimeSpan.MaxValue, TestContext.Current.CancellationToken));
        Assert.Equal(FindingDiffAnyState.Changed, query.Result!.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, query.Result.ResidualApi);
        Assert.Contains(query.Result.Members, member => member.Api == FindingDiffAnyState.Changed);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void EveryPairedFixtureType_FoldsLikeLibraryAndSteppingPreservesRows()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        var pairing = new FindingDiffAnyTypePairing(before, after);
        foreach (string name in pairing.Types)
        {
            TypeDefinitionHandle a = TypeFull(before, name), b = TypeFull(after, name);
            using var stepped = new FindingDiffAnyTypeQuery(before, a, after, b, TestContext.Current.CancellationToken);
            using var whole = new FindingDiffAnyTypeQuery(before, a, after, b, TestContext.Current.CancellationToken);
            int steps = 0;
            while (!stepped.Step(TimeSpan.Zero, TestContext.Current.CancellationToken)) Assert.True(++steps < 1000);
            Assert.True(whole.Step(TimeSpan.MaxValue, TestContext.Current.CancellationToken));
            Assert.Equal(whole.Result!.Members, stepped.Result!.Members);
            Assert.Equal(whole.Result.Api, stepped.Result.Api);
            Assert.Equal(whole.Result.Body, stepped.Result.Body);
            Assert.True((stepped.Result.Api == FindingDiffAnyState.Unchanged)
                == (stepped.Result.ResidualApi == FindingDiffAnyState.Unchanged
                    && stepped.Result.Members.All(member => member.Api == FindingDiffAnyState.Unchanged)), $"API owner fold disagrees for {name}");
            Assert.True((stepped.Result.Body == FindingDiffAnyState.Unchanged)
                == (stepped.Result.ResidualBody == FindingDiffAnyState.Unchanged
                    && stepped.Result.Members.All(member => member.Body == FindingDiffAnyState.Unchanged)), $"Body owner fold disagrees for {name}");
            FindingDiffAnyTypeInventory inventory = pairing.Inventory(name);
            Assert.Equal(inventory.Api, stepped.Result.Api);
            Assert.Equal(inventory.Body, stepped.Result.Body);
        }
        pairing.Release();
    }

    static TypeDefinitionHandle Type(PEReader image, string name) => TypeFull(image, "FindingDiffAnyFixture." + name);
    static TypeDefinitionHandle TypeFull(PEReader image, string name)
    {
        MetadataReader reader = image.GetMetadataReader();
        return Assert.Single(reader.TypeDefinitions, handle =>
            MetadataDeclarationQuery.GetTypeSurface(reader, handle).FullName == name);
    }
    static PEReader Open(string path) => new(new MemoryStream(File.ReadAllBytes(path), writable: false));
}
