using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class TypeGeneratedMethodOwnershipTests
{
    [Theory]
    [InlineData("LambdaOnly")]
    [InlineData("AsyncOnly")]
    [InlineData("IteratorOnly")]
    [InlineData("LocalFunctionOnly")]
    public void CompiledGeneratedBodies_FollowTheirDeclaredMember(string name)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        var beforeOwners = Resolve(before, "FindingDiffAnyFixture", name);
        var afterOwners = Resolve(after, "FindingDiffAnyFixture", name);
        Assert.False(beforeOwners.Coverage.IsBounded);
        Assert.NotEmpty(beforeOwners.Owners);
        Assert.All(beforeOwners.Owners.Values, owner =>
            Assert.Equal("Value", before.GetMetadataReader().GetString(before.GetMetadataReader().GetMethodDefinition(owner).Name)));
        Assert.All(afterOwners.Owners.Values, owner =>
            Assert.Equal("Value", after.GetMetadataReader().GetString(after.GetMetadataReader().GetMethodDefinition(owner).Name)));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.Members(
            "FindingDiffAnyFixture." + name, beforeOwners.Owners, afterOwners.Owners);
        Assert.Equal(FindingDiffAnyState.Changed, result.Body);
        Assert.Contains(result.Members, member =>
            member.After.Kind == HandleKind.MethodDefinition
            && after.GetMetadataReader().GetString(after.GetMetadataReader().GetMethodDefinition((MethodDefinitionHandle)member.After).Name) == "Value"
            && member.Body == FindingDiffAnyState.Changed);
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualBody);
    }

    [Fact]
    public void SharedGeneratedType_DoesNotAssignItsFieldsToOneGuessedMember()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        var ownersA = Resolve(before, "FindingDiffAnyFixture", "SharedGeneratedOwners");
        var ownersB = Resolve(after, "FindingDiffAnyFixture", "SharedGeneratedOwners");
        Assert.Equal(2, ownersB.Owners.Values.Distinct().Count());
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.Members("FindingDiffAnyFixture.SharedGeneratedOwners", ownersA.Owners, ownersB.Owners);
        MetadataReader reader = after.GetMetadataReader();
        Assert.Single(result.Members, member => member.Body == FindingDiffAnyState.Changed);
        Assert.Equal(FindingDiffAnyState.Changed, Assert.Single(result.Members, member =>
            member.After.Kind == HandleKind.MethodDefinition && reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)member.After).Name) == "First").Body);
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualBody);
        pairing.Release();
    }

    [Fact]
    public void UnchangedType_HasNoGeneratedOwners()
    {
        using var image = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        Assert.Empty(Resolve(image, "FindingDiffAnyFixture", "Unchanged").Owners);
    }

    [Fact]
    public void PinnedJson_FlushAsyncStateMachineAndHelpersBelongToFlushAsync()
    {
        using var image = Open(Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", "packages", "System.Text.Json.10.0.0.dll"));
        var ownership = Resolve(image, "System.Text.Json", "Utf8JsonWriter");
        MetadataReader reader = image.GetMetadataReader();
        var bodies = ownership.Owners.Where(pair =>
            reader.GetString(reader.GetMethodDefinition(pair.Value).Name) == "FlushAsync").ToArray();
        Assert.Contains(bodies, pair => reader.GetString(reader.GetMethodDefinition(pair.Key).Name) == "MoveNext");
        Assert.Contains(bodies, pair => reader.GetString(reader.GetMethodDefinition(pair.Key).Name) == "SetStateMachine");
    }

    static TypeGeneratedMethodOwnership Resolve(PEReader image, string space, string name)
    {
        MetadataReader reader = image.GetMetadataReader();
        TypeDefinitionHandle type = Assert.Single(reader.TypeDefinitions, handle =>
            reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Namespace, space)
            && reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Name, name));
        return TypeGeneratedMethodOwnership.Resolve(image, type, TestContext.Current.CancellationToken);
    }

    static PEReader Open(string path) => new(new MemoryStream(File.ReadAllBytes(path), writable: false));
}
