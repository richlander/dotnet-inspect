using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using static ILInspector.Metadata.Tests.FindingDiffAnyIdentityTests;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates <see cref="FindingDiffAnyTypePairing.ForType"/>: pairing one addressed
/// Type by identity decides every row the whole-image pairing decides, and
/// reads that Type alone (docs/design/diff-capabilities.md#demand-shaped).
/// </summary>
public sealed class FindingDiffAnySelectedTypeTests
{
    static readonly byte[] ReturnOne = [0x17, 0x2A];
    static readonly byte[] ReturnTwo = [0x18, 0x2A];

    [Fact]
    [Trait("Speed", "Slow")]
    public void EveryFixtureType_AgreesWithWholeImagePairing()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        byte[] old = File.ReadAllBytes(pair.OldAssemblyPath());
        byte[] current = File.ReadAllBytes(pair.NewAssemblyPath());
        using var before = Open(old);
        using var after = Open(current);
        string[] names =
        [
            .. FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken)
                .Types.Select(type => type.FullName).Distinct(),
        ];
        Assert.Contains("FindingDiffAnyFixture.Added", names);
        Assert.Contains("FindingDiffAnyFixture.Removed", names);
        foreach (string name in names)
        {
            AssertAgrees(old, current, name);
            AssertAgrees(current, old, name);
        }
    }

    [Fact]
    public void ReferenceSwitchingBetweenDuplicateTypeRows_IsIndeterminateWithoutReadingThem()
    {
        // MethodDef 1 and 2 are N.C.M on two Type rows that share one name.
        TypeSpec callee = new("N", "C", null, ReturnOne);
        byte[] before = Image([callee, callee, new("N", "D", null, [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A])]);
        byte[] after = Image([callee, callee, new("N", "D", null, [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A])]);
        FindingDiffAnyTypeInventory inventory = AssertAgrees(before, after, "N.D");
        Assert.Equal(FindingDiffAnyState.Indeterminate, inventory.Body);
    }

    [Fact]
    public void DuplicateGeneratedTypeRows_AgreeWithWholeImagePairing()
    {
        TypeSpec owner = new("N", "C", null, ReturnOne);
        byte[] before = Image([owner, new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"]), new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"])]);
        byte[] after = Image([owner, new(null, "<>c", 0, ReturnTwo, ["<M>b__0_0"]), new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"])]);
        FindingDiffAnyTypeInventory inventory = AssertAgrees(before, after, "N.C");
        Assert.Equal(FindingDiffAnyState.Indeterminate, inventory.Body);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SelectedRowWhoseKeyAnotherRowShares_IsIndeterminate(int row)
    {
        byte[] image = Image([new("N", "C", null, ReturnOne), new("N", "C", null, ReturnTwo)]);
        using var before = Open(image);
        using var after = Open(image);
        TypeDefinitionHandle handle = MetadataTokens.TypeDefinitionHandle(row);
        FindingDiffAnyTypePairing pairing = FindingDiffAnyTypePairing.ForType(before, handle, after, handle);
        FindingDiffAnyTypeMembers result = pairing.Members(pairing.Selected!);
        Assert.Empty(result.Members);
        Assert.Equal((FindingDiffAnyState.Indeterminate, FindingDiffAnyState.Indeterminate), (result.Api, result.Body));
        pairing.Release();

        using var absent = Open(Image([]));
        pairing = FindingDiffAnyTypePairing.ForType(absent, default, after, handle);
        result = pairing.OneSidedMembers(pairing.Selected!);
        Assert.Empty(result.Members);
        Assert.Equal(FindingDiffAnyState.Indeterminate, result.Api);
        pairing.Release();
    }

    [Fact]
    public void SameNameInAnotherNamespaceOrOwner_IsNotShared()
    {
        byte[] image = Image([
            new("N", "C", null, ReturnOne),
            new("M", "C", null, ReturnOne),
            new(null, "C", 0, ReturnOne),
            new(null, "C", 1, ReturnOne)]);
        using var before = Open(image);
        using var after = Open(image);
        for (int row = 2; row <= 5; row++)
        {
            TypeDefinitionHandle handle = MetadataTokens.TypeDefinitionHandle(row);
            FindingDiffAnyTypePairing pairing = FindingDiffAnyTypePairing.ForType(before, handle, after, handle);
            FindingDiffAnyTypeInventory inventory = pairing.Inventory(pairing.Selected!);
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (inventory.Api, inventory.Body));
            pairing.Release();
        }
    }

    [Fact]
    public void GeneratedRow_HasNoTypeLevel()
    {
        byte[] image = Image([new("N", "C", null, ReturnOne), new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"])]);
        using var before = Open(image);
        using var after = Open(image);
        TypeDefinitionHandle generated = MetadataTokens.TypeDefinitionHandle(3);
        Assert.Throws<ArgumentException>(() => FindingDiffAnyTypePairing.ForType(before, generated, after, generated));
    }

    /// <summary>
    /// Compares the selected pairing with the whole-image pairing for one
    /// name: states, facts, and body rows for a paired Type, and each Member's
    /// declarations and states for either form. Returns the paired inventory.
    /// </summary>
    static FindingDiffAnyTypeInventory AssertAgrees(byte[] beforeImage, byte[] afterImage, string name)
    {
        using var before = Open(beforeImage);
        using var after = Open(afterImage);
        TypeDefinitionHandle a = Find(before, name), b = Find(after, name);
        Assert.False(a.IsNil && b.IsNil, name);

        FindingDiffAnyTypePairing selected = FindingDiffAnyTypePairing.ForType(before, a, after, b);
        Assert.Equal(name, selected.Selected);
        Assert.Equal(a.IsNil ? [] : [name], selected.UnitsBefore);
        Assert.Equal(b.IsNil ? [] : [name], selected.UnitsAfter);
        var whole = new FindingDiffAnyTypePairing(before, after);
        FindingDiffAnyTypeInventory inventory = Indeterminate(name);
        FindingDiffAnyTypeMembers expected, actual;
        if (!a.IsNil && !b.IsNil)
        {
            inventory = whole.Inventory(name);
            FindingDiffAnyTypeInventory read = selected.Inventory(name);
            Assert.Equal((inventory.Api, inventory.Body), (read.Api, read.Body));
            Assert.Equal(inventory.Facts, read.Facts);
            Assert.Equal(inventory.Bodies, read.Bodies);
            expected = whole.Members(name);
            actual = selected.Members(name);
        }
        else
        {
            expected = whole.OneSidedMembers(name);
            actual = selected.OneSidedMembers(name);
        }
        Assert.Equal((expected.Api, expected.Body, expected.ResidualApi, expected.ResidualBody),
            (actual.Api, actual.Body, actual.ResidualApi, actual.ResidualBody));
        Assert.Equal(
            expected.Members.Select(member => (member.Before, member.After, member.Api, member.Body)),
            actual.Members.Select(member => (member.Before, member.After, member.Api, member.Body)));
        whole.Release();
        selected.Release();
        return inventory;
    }

    static FindingDiffAnyTypeInventory Indeterminate(string name)
        => new(name, FindingDiffAnyState.Indeterminate, FindingDiffAnyState.Indeterminate, [], [], 0, 0, 0, 0);

    /// <summary>The one row the whole-image pairing's display name selects, or nil.</summary>
    static TypeDefinitionHandle Find(PEReader image, string name)
    {
        MetadataReader md = image.GetMetadataReader();
        TypeDefinitionHandle[] rows = [.. md.TypeDefinitions.Where(handle => Display(md, handle) == name)];
        Assert.True(rows.Length <= 1, $"'{name}' names {rows.Length} rows.");
        return rows.Length == 0 ? default : rows[0];
    }

    static string Display(MetadataReader md, TypeDefinitionHandle handle)
    {
        TypeDefinition type = md.GetTypeDefinition(handle);
        TypeDefinitionHandle owner = type.GetDeclaringType();
        string name = md.GetString(type.Name);
        if (!owner.IsNil)
            return Display(md, owner) + "." + name;
        return type.Namespace.IsNil ? name : md.GetString(type.Namespace) + "." + name;
    }

    static PEReader Open(byte[] image) => new(new MemoryStream(image, writable: false));
}
