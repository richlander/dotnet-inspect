using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using InertText;

namespace ILInspector.Metadata.Tests;

public sealed class FindingDiffAnyTypeMemberTests
{
    [Fact]
    public void EveryPairedFixtureType_PartitionsTheLibraryAxes_AndReadersAgree()
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        var library = FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken).Types.ToDictionary(t => t.FullName);
        var walk = new FindingDiffAnyTypePairing(before, after);
        var lockstep = new FindingDiffAnyTypePairing(before, after, FindingDiffAnyBodyReaderTests.Lockstep(before, after));
        Assert.NotEmpty(walk.Types);
        foreach (string name in walk.Types)
        {
            FindingDiffAnyTypeMembers w = walk.Members(name);
            FindingDiffAnyTypeMembers l = lockstep.Members(name);
            Assert.Equal((library[name].Api, library[name].Body), (w.Api, w.Body));
            Assert.Equal(w.Members, l.Members);
            Assert.Equal((w.ResidualApi, w.ResidualBody), (l.ResidualApi, l.ResidualBody));
            Assert.Equal(w.Api == FindingDiffAnyState.Unchanged,
                w.ResidualApi == FindingDiffAnyState.Unchanged && w.Members.All(m => m.Api == FindingDiffAnyState.Unchanged));
            Assert.Equal(w.Body == FindingDiffAnyState.Unchanged,
                w.ResidualBody == FindingDiffAnyState.Unchanged && w.Members.All(m => m.Body == FindingDiffAnyState.Unchanged));
            Assert.Equal(w.Members.Length, w.Members.Select(m => m.Key).Distinct().Count());
        }
        walk.Release();
        Assert.Throws<InvalidOperationException>(() => walk.Members(walk.Types[0]));
    }

    [Theory]
    [InlineData("MemberFoldAddedProperty", "Added", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("MethodConstraint", "Value", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("DefaultParameter", "Value", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("ParameterName", "Value", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("MemberFoldInternalAttribute", "Value", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("MemberFoldEvent", "Changed", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("PrivateFieldAdded", "_added", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("PublicMemberAdded", "Added", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    public void ChangedPhysicalRows_AreOwnedByTheDeclaredMember(string type, string member,
        FindingDiffAnyState api, FindingDiffAnyState body)
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("FindingDiffAnyFixture." + type);
        FindingDiffAnyMemberState row = Assert.Single(result.Members, m => Name(after.GetMetadataReader(), m.After) == member);
        Assert.Equal((api, body), (row.Api, row.Body));
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualApi);
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualBody);
        Assert.All(result.Members.Where(m => m != row), m =>
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (m.Api, m.Body)));
    }

    [Theory]
    [InlineData("MemberFoldEnum", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("MemberFoldStatic", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("MemberFoldBase", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("MemberFoldMarker", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("NullableOuter.Inner", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    public void TypeDeclarationChange_BelongsToResidual(string type, FindingDiffAnyState api, FindingDiffAnyState body)
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("FindingDiffAnyFixture." + type);
        Assert.Equal((api, body), (result.ResidualApi, result.ResidualBody));
        // Enum literals record their primitive; a base change also changes
        // the constructor's call operand.
        if (type is not "MemberFoldEnum" and not "MemberFoldBase")
            Assert.All(result.Members, m => Assert.Equal(FindingDiffAnyState.Unchanged, m.Body));
        if (type == "MemberFoldMarker")
            Assert.All(result.Members, m => Assert.Equal(FindingDiffAnyState.Unchanged, m.Api));
    }

    [Fact]
    public void MethodImplRows_AreResidualRatherThanOwnedByTheirBodyReference()
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after)
            .Members("FindingDiffAnyFixture.MemberFoldMethodImpl");
        // Both interfaces and the Type declaration stay the same. The explicit
        // implementation moves between interfaces, changing MethodImpl rows.
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualApi);
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualBody);
    }

    [Fact]
    public void HiddenType_MarksEveryPublicMemberOnApi()
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("FindingDiffAnyFixture.MemberFoldHidden");
        Assert.NotEmpty(result.Members);
        Assert.All(result.Members, m => Assert.Equal((FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged), (m.Api, m.Body)));
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualApi);
    }

    [Fact]
    public void GeneratedBody_IsResidualInThisSlice()
    {
        using var before = Open(FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath());
        using var after = Open(FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath());
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("FindingDiffAnyFixture.LambdaOnly");
        Assert.Equal(FindingDiffAnyState.Changed, result.ResidualBody);
        Assert.All(result.Members, m => Assert.Equal(FindingDiffAnyState.Unchanged, m.Body));
    }

    [Fact]
    public void UndefinedOpcode_IsIndeterminateForItsMember()
    {
        byte[] image = FindingDiffAnyIdentityTests.Image([new("N", "C", null, [0x24, 0x2A])]);
        using var before = new PEReader(new MemoryStream(image));
        using var after = new PEReader(new MemoryStream(image));
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("N.C");
        Assert.Equal(FindingDiffAnyState.Indeterminate, Assert.Single(result.Members).Body);
        Assert.Equal(FindingDiffAnyState.Unchanged, result.ResidualBody);
    }

    [Fact]
    public void PinnedJsonProperty_IsOneMemberWithItsAccessorsRaised()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", "packages");
        using var before = Open(Path.Combine(root, "System.Text.Json.9.0.0.dll"));
        using var after = Open(Path.Combine(root, "System.Text.Json.10.0.0.dll"));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeMembers result = pairing.Members("System.Text.Json.JsonSerializerOptions");
        FindingDiffAnyMemberState property = Assert.Single(result.Members,
            m => Name(after.GetMetadataReader(), m.After) == "AllowDuplicateProperties");
        Assert.Equal(HandleKind.PropertyDefinition, property.After.Kind);
        Assert.True(property.Before.IsNil);
        Assert.Equal((FindingDiffAnyState.Changed, FindingDiffAnyState.Changed), (property.Api, property.Body));
        Assert.DoesNotContain(result.Members, m => Name(after.GetMetadataReader(), m.After) == "get_AllowDuplicateProperties");
        OpaqueByteRange key = property.Key;
        pairing.Release();
        Assert.False(key.IsEmpty);
    }

    [Fact]
    public void DuplicateTypeRows_HaveNoMemberRows()
    {
        byte[] image = FindingDiffAnyIdentityTests.Image([
            new("N", "C", null, [0x17, 0x2A]),
            new("N", "C", null, [0x17, 0x2A])]);
        using var before = new PEReader(new MemoryStream(image));
        using var after = new PEReader(new MemoryStream(image));
        FindingDiffAnyTypeMembers result = new FindingDiffAnyTypePairing(before, after).Members("N.C");
        Assert.Empty(result.Members);
        Assert.Equal((FindingDiffAnyState.Indeterminate, FindingDiffAnyState.Indeterminate),
            (result.ResidualApi, result.ResidualBody));
    }

    static string? Name(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        _ when handle.IsNil => null,
        HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
        HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
        HandleKind.PropertyDefinition => reader.GetString(reader.GetPropertyDefinition((PropertyDefinitionHandle)handle).Name),
        HandleKind.EventDefinition => reader.GetString(reader.GetEventDefinition((EventDefinitionHandle)handle).Name),
        _ => null,
    };

    static PEReader Open(string path) => new(new MemoryStream(File.ReadAllBytes(path), writable: false));
}
