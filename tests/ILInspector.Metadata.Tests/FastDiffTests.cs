using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.ILDiff;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

public sealed class FastDiffTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Theory]
    [InlineData("FastDiffFixture.Unchanged", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.BodyOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.LambdaOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.AsyncOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.LocalFunctionOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.StringLiteralOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.CatchTypeOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PrivateMemberAdded", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PublicMemberAdded", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.MethodConstraint", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ConstraintType", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ConstantValue", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.DefaultParameter", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ParameterName", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PublicAttribute", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.InterfaceAdded", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.BecomesPublic", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Outer.Inner", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.NullableOuter", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NullableOuter.Inner", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Outer", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Generic`1", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.InternalBodyOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.Added", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.Removed", FastDiffState.Changed, FastDiffState.Changed)]
    public void TypeStates_SeparateApiAndBodyChanges(
        string fullName,
        FastDiffState api,
        FastDiffState body)
    {
        FastDiffTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFastDiffPair).Types,
            type => type.FullName == fullName);

        Assert.Equal((api, body), (state.Api, state.Body));
    }

    [Theory]
    [InlineData("FastDiffFixture.Outer.Inner", "FastDiffFixture.Outer+Inner")]
    [InlineData("FastDiffFixture.Generic`1", "FastDiffFixture.Generic`1")]
    [InlineData("FastDiffFixture.Added", "FastDiffFixture.Added")]
    public void TypeStates_CarryTheEscapedDefinitionIdentifier(
        string fullName,
        string identifier)
    {
        FastDiffTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFastDiffPair).Types,
            type => type.FullName == fullName);

        Assert.Equal(identifier, state.Identifier);
    }

    [Fact]
    public void GeneratedTypes_FoldIntoTheirDeclaredOwner()
    {
        FastDiffResult result = Compare(FixtureCatalog.MetadataFastDiffPair);

        Assert.DoesNotContain(result.Types, type => type.FullName.Contains('<'));
    }

    [Theory]
    [InlineData("fast-diff")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void ApiUnchanged_IsSoundAgainstTheCompletePublicApiComparison(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        ApiSurface before = Surface(pair.OldAssemblyPath());
        ApiSurface after = Surface(pair.NewAssemblyPath());
        ApiFindingComparison comparison =
            MetadataFindings.CompareApi(
                before, after, Subject, new ApiDiffOptions(ApiDiffScope.All));
        HashSet<string> changed = ChangedTypes(comparison);

        FastDiffResult result = Compare(pair);

        Assert.NotEmpty(changed);
        Assert.All(changed, typeName =>
        {
            FastDiffTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == typeName);
            Assert.True(
                state is { Api: not FastDiffState.Unchanged },
                $"{typeName} changes in the complete API comparison but Fast Diff reports {state?.Api}.");
        });
    }

    [Theory]
    [InlineData("fast-diff")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void BodyUnchanged_IsSoundAgainstCanonicalIlComparison(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        IlAssemblyDiffResult il = IlAssemblyDiff.CompareFiles(
            pair.OldAssemblyPath(),
            pair.NewAssemblyPath(),
            maxExamples: int.MaxValue,
    normalization: IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope).Diff;
        FastDiffResult result = Compare(pair);

        Assert.NotEmpty(il.Examples);
        Assert.All(il.Examples, example =>
        {
            string owner = DeclaredOwner(example.Method);
            FastDiffTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == owner);
            Assert.True(
                state is { Body: not FastDiffState.Unchanged },
                $"{example.Method} (owner {owner}) changes in canonical IL but Fast Diff reports {state?.Body}.");
        });
    }

    /// <summary>
    /// The declared Type that owns a canonical IL method display, folding
    /// compiler-generated nested Types into their declaring Type.
    /// </summary>
    static string DeclaredOwner(string method)
    {
        string type = method[..method.IndexOf("::", StringComparison.Ordinal)];
        string[] segments = type.Split('/', '+');
        int declared = segments.Length;
        while (declared > 1 && segments[declared - 1].StartsWith('<'))
            declared--;
        return string.Join('.', segments[..declared]);
    }

    static HashSet<string> ChangedTypes(ApiFindingComparison comparison)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeDiff type in comparison.ApiDiff.TypeDiffs)
            changed.Add(type.TypeFullName);
        if (comparison.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
        {
            foreach (PairFinding<ApiTypeHandle> pair in types.Pairs)
            {
                if (pair.Kind != PairKind.Present)
                    changed.Add(TypeName(((IPairFinding)pair).Old, ((IPairFinding)pair).New, (ApiTypeHandle handle) => handle.TypeFullName));
            }
        }
        if (comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
        {
            foreach (PairFinding<ApiMemberHandle> pair in members.Pairs)
            {
                if (pair.Kind != PairKind.Present)
                    changed.Add(TypeName(((IPairFinding)pair).Old, ((IPairFinding)pair).New, (ApiMemberHandle handle) => handle.TypeFullName));
            }
        }
        return changed;
    }

    static string TypeName<T>(IFinding? old, IFinding? @new, Func<T, string> name)
        where T : notnull
        => name(((Finding<T>?)@new ?? (Finding<T>)old!).Payload);

    static FixturePair PairOf(string name) => name switch
    {
        "fast-diff" => FixtureCatalog.MetadataFastDiffPair,
        "diff" => FixtureCatalog.DiffPair,
        _ => FixtureCatalog.LibraryApiDiffPair,
    };

    static ApiSurface Surface(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader, ApiSurfaceExtractionScope.Public);
    }

    static FastDiffResult Compare(FixturePair pair)
    {
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FastDiff.Compare(before, after);
    }
}
