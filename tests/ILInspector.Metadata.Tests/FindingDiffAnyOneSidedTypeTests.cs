using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class FindingDiffAnyOneSidedTypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentMembersUseTheSameKeysAsSelfPairing_WithoutComparingAnything(bool reverse)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var old = Open(pair.OldAssemblyPath());
        using var added = Open(pair.NewAssemblyPath());
        PEReader before = reverse ? added : old, after = reverse ? old : added;
        var pairing = new FindingDiffAnyTypePairing(before, after,
            (_, _, _, _) => throw new InvalidOperationException("One-sided Type must not read IL."),
            (_, _, _, _, _) => throw new InvalidOperationException("One-sided Type must not settle API."));
        FindingDiffAnyTypeMembers result = pairing.OneSidedMembers("FindingDiffAnyFixture.Added");
        var self = new FindingDiffAnyTypePairing(added, added);
        FindingDiffAnyTypeMembers paired = self.Members(result.FullName);
        Assert.Equal(paired.Members.Select(m => m.Key), result.Members.Select(m => m.Key));
        Assert.All(result.Members, member => Assert.True(reverse ? member.After.IsNil : member.Before.IsNil));
        FindingDiffAnyTypeState library = Assert.Single(FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken).Types,
            type => type.FullName == result.FullName);
        Assert.Equal((library.Api, library.Body), (result.Api, result.Body));
        Assert.Equal((FindingDiffAnyState.Changed, FindingDiffAnyState.Changed), (result.ResidualApi, result.ResidualBody));
        foreach (var (name, api, body) in new[]
        {
            ("Field", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged),
            ("Value", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed),
            ("Property", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed),
            ("InternalValue", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed),
            ("_value", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed),
            ("PrivateExtern", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed),
        })
        {
            FindingDiffAnyMemberState member = Assert.Single(result.Members,
                row => Name(added.GetMetadataReader(), reverse ? row.Before : row.After) == name);
            Assert.Equal((api, body), (member.Api, member.Body));
        }
        Assert.Equal(0, pairing.BodiesCompared);
        Assert.Equal(0, pairing.IlBytesCompared);
        pairing.Release(); self.Release();
        Assert.All(result.Members, row => Assert.False(row.Key.IsEmpty));
    }

    [Fact]
    public void InternalEnumHasOnlyBodyFacts_AndApiOnlySkipsEveryBodyState()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = Open(pair.OldAssemblyPath());
        using var after = Open(pair.NewAssemblyPath());
        var pairing = new FindingDiffAnyTypePairing(before, after);
        const string name = "FindingDiffAnyFixture.AddedInternalEnum";
        FindingDiffAnyTypeMembers both = pairing.OneSidedMembers(name);
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed), (both.Api, both.Body));
        Assert.Equal((both.Api, both.Body), (both.ResidualApi, both.ResidualBody));
        Assert.NotEmpty(both.Members);
        Assert.All(both.Members, m => Assert.Equal((both.Api, both.Body), (m.Api, m.Body)));
        FindingDiffAnyTypeMembers api = pairing.OneSidedMembers(name, FindingDiffAnyAxes.Api);
        Assert.Equal(FindingDiffAnyState.NotCompared, api.Body);
        Assert.Equal(FindingDiffAnyState.NotCompared, api.ResidualBody);
        Assert.All(api.Members, m => Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.NotCompared), (m.Api, m.Body)));
        pairing.Release();
    }

    [Fact]
    public void UndefinedIlInAnAddedTypeIsNeverRead()
    {
        using var before = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([])));
        using var after = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([new("N", "C", null, [0x24, 0x2A])])));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.OneSidedMembers("N.C");
        Assert.Equal(FindingDiffAnyState.Changed, Assert.Single(result.Members).Body);
        Assert.Equal(0, pairing.BodiesCompared);
        Assert.Equal(0, pairing.IlBytesCompared);
        pairing.Release();
    }

    [Theory]
    [InlineData(FindingDiffAnyAxes.ApiAndBody)]
    [InlineData(FindingDiffAnyAxes.Api)]
    public void IncompleteCensusPreservesPresenceAndMarksUnsettledOwners(FindingDiffAnyAxes axes)
    {
        using var before = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([])));
        using var after = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([
            new("N", "C", null, [0x2A], Methods: ["M", "M"])])));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.OneSidedMembers("N.C", axes);
        Assert.Equal(FindingDiffAnyState.Changed, result.Api);
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualApi);
        if (axes == FindingDiffAnyAxes.ApiAndBody)
        {
            Assert.Equal(FindingDiffAnyState.Changed, result.ResidualBody);
            Assert.All(result.Members, member => Assert.Equal(FindingDiffAnyState.Indeterminate, member.Body));
        }
        else
            Assert.All(result.Members, member => Assert.Equal(FindingDiffAnyState.NotCompared, member.Body));
        pairing.Release();
    }

    [Fact]
    public void IncompletePrivateBodyCensusDoesNotEraseKnownApiAbsence()
    {
        using var before = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([])));
        using var after = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([
            new("N", "C", null, [0x2A], Methods: ["M", "M"], Public: false)])));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.OneSidedMembers("N.C");
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed), (result.Api, result.Body));
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualApi);
        Assert.All(result.Members, member => Assert.Equal(FindingDiffAnyState.Unchanged, member.Api));
        pairing.Release();
    }

    [Theory]
    [InlineData(FindingDiffAnyAxes.ApiAndBody)]
    [InlineData(FindingDiffAnyAxes.Api)]
    public void AmbiguousTypeRowsHaveNoMemberRows(FindingDiffAnyAxes axes)
    {
        using var before = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([])));
        using var after = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([
            new("N", "C", null, [0x2A]), new("N", "C", null, [0x2A])])));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.OneSidedMembers("N.C", axes);
        Assert.Empty(result.Members);
        Assert.Equal(FindingDiffAnyState.Indeterminate, result.Api);
        Assert.Equal(axes == FindingDiffAnyAxes.Api ? FindingDiffAnyState.NotCompared : FindingDiffAnyState.Indeterminate, result.Body);
        Assert.Equal(result.Body, result.ResidualBody);
        pairing.Release();
    }

    static string Name(MetadataReader md, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
        HandleKind.FieldDefinition => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
        HandleKind.PropertyDefinition => md.GetString(md.GetPropertyDefinition((PropertyDefinitionHandle)handle).Name),
        _ => throw new InvalidOperationException(),
    };
    static PEReader Open(string path) => new(new MemoryStream(File.ReadAllBytes(path), writable: false));
}
