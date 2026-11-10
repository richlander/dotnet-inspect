using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Gates generated-code ownership at Finding Diff Any's Type level
/// (docs/design/finding-diff-any.md#type): a lifted body belongs to the one
/// Member the lifted-owner resolution names, and the partition claim holds.
/// </summary>
public sealed class FindingDiffAnyGeneratedOwnerTests
{
    static readonly string BeforePath = FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath();
    static readonly string AfterPath = FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath();

    [Fact]
    public void EveryPairedFixtureType_PartitionsTheLibraryAxes_WithGeneratedOwners()
    {
        using var before = Open(BeforePath);
        using var after = Open(AfterPath);
        var library = FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken)
            .Types.ToDictionary(t => t.FullName);
        var pairing = new FindingDiffAnyTypePairing(before, after);
        foreach (string name in pairing.Types)
        {
            FindingDiffAnyTypeMembers result = Members(pairing, before, after, name);
            Assert.Equal((library[name].Api, library[name].Body), (result.Api, result.Body));
            Assert.Equal(result.Api == FindingDiffAnyState.Unchanged,
                result.ResidualApi == FindingDiffAnyState.Unchanged && result.Members.All(m => m.Api == FindingDiffAnyState.Unchanged));
            Assert.Equal(result.Body == FindingDiffAnyState.Unchanged,
                result.ResidualBody == FindingDiffAnyState.Unchanged && result.Members.All(m => m.Body == FindingDiffAnyState.Unchanged));
        }
    }

    [Theory]
    [InlineData("LambdaOnly", "Value")]
    [InlineData("AsyncOnly", "Value")]
    [InlineData("LocalFunctionOnly", "Value")]
    [InlineData("MemberFoldIterator", "Value")]
    [InlineData("MemberFoldCapturedLambda", "Value")]
    [InlineData("MemberFoldSharedLambdas", "Second")]
    public void GeneratedBodyChange_IsOwnedByItsOneMember(string type, string member)
    {
        using var before = Open(BeforePath);
        using var after = Open(AfterPath);
        FindingDiffAnyTypeMembers result = Members(new FindingDiffAnyTypePairing(before, after), before, after,
            "FindingDiffAnyFixture." + type);
        FindingDiffAnyMemberState row = Assert.Single(result.Members, m => Name(after.GetMetadataReader(), m.After) == member);
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed), (row.Api, row.Body));
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (result.ResidualApi, result.ResidualBody));
        Assert.All(result.Members.Where(m => m != row), m =>
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (m.Api, m.Body)));
    }

    [Fact]
    public void GeneratedCodeWithoutADeclaredOwner_IsResidualAndNotAGuessedMember()
    {
        using var before = Open(BeforePath);
        using var after = Open(AfterPath);
        FindingDiffAnyTypeMembers result = Members(new FindingDiffAnyTypePairing(before, after), before, after,
            "FindingDiffAnyFixture.MemberFoldStaticLambda");
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed), (result.ResidualApi, result.ResidualBody));
        Assert.All(result.Members, m =>
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (m.Api, m.Body)));
    }

    [Fact]
    public void WithoutAResolution_GeneratedCodeStaysResidual()
    {
        using var before = Open(BeforePath);
        using var after = Open(AfterPath);
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after)
            .Members("FindingDiffAnyFixture.MemberFoldSharedLambdas");
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualBody);
        Assert.All(result.Members, m => Assert.Equal(FindingDiffAnyState.Unchanged, m.Body));
    }

    [Fact]
    public void DeclaredMethods_HaveNoGeneratedOwner()
    {
        using var image = Open(AfterPath);
        MetadataReader reader = image.GetMetadataReader();
        var owners = new FindingDiffAnyGeneratedOwners(image, AfterPath);
        TypeDefinition type = reader.GetTypeDefinition(reader.TypeDefinitions.Single(t =>
            reader.GetString(reader.GetTypeDefinition(t).Name) == "MemberFoldSharedLambdas"));
        Assert.All(type.GetMethods(), method =>
            Assert.Equal(FindingDiffAnyGeneratedOwnership.NotGenerated, owners.Owner(method)));
    }

    static FindingDiffAnyTypeMembers Members(FindingDiffAnyTypePairing pairing, PEReader before, PEReader after, string name)
    {
        // Resolution work is bounded per instance, so each Type gets its own.
        var ownersBefore = new FindingDiffAnyGeneratedOwners(before, BeforePath);
        var ownersAfter = new FindingDiffAnyGeneratedOwners(after, AfterPath);
        return pairing.Members(name, ownersBefore.Owner, ownersAfter.Owner);
    }

    static string Name(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
        HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
        HandleKind.PropertyDefinition => reader.GetString(reader.GetPropertyDefinition((PropertyDefinitionHandle)handle).Name),
        HandleKind.EventDefinition => reader.GetString(reader.GetEventDefinition((EventDefinitionHandle)handle).Name),
        _ => "",
    };

    static PEReader Open(string path) => new(File.OpenRead(path));
}
