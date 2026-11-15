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

    /// <summary>
    /// The NestedClass table may list one row under several enclosing rows;
    /// a generated row still belongs only beneath its declaring parent, as
    /// the whole-image pairing attributes it, and the walk stays linear.
    /// Rows: 1 &lt;Module&gt;, 2 N.C, 3 &lt;&gt;c, 4 &lt;&gt;d, 5 &lt;&gt;e, 6 N.D, then &lt;&gt;x rows.
    /// </summary>
    [Theory]
    [InlineData("several-parents")]
    [InlineData("listed-cycle")]
    [InlineData("foreign-parent")]
    [InlineData("layered")]
    [InlineData("duplicate-entry")]
    [InlineData("layered-duplicates")]
    public void NestedRowListedUnderSeveralParents_AgreesAndStaysLinear(string shape)
    {
        (int Nested, int Enclosing)[] entries = shape switch
        {
            "several-parents" => [(3, 2), (4, 3), (4, 4), (4, 5), (5, 4), (5, 5)],
            "listed-cycle" => [(3, 2), (4, 3), (4, 5), (5, 4)],
            "foreign-parent" => [(3, 2), (4, 3), (4, 6)],
            // One (nested, declaring parent) pair listed twice.
            "duplicate-entry" => [(3, 2), (4, 3), (4, 3)],
            // A chain of 40 generated rows, each pair listed twice: 2^40 paths.
            "layered-duplicates" => [(3, 2), (3, 2), .. Enumerable.Range(7, 40).SelectMany(row => new[] { (row, row == 7 ? 3 : row - 1), (row, row == 7 ? 3 : row - 1) })],
            _ => Layers(40),
        };
        byte[] image = NestedImage(entries, shape.StartsWith("layered", StringComparison.Ordinal) ? 80 : 0);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        AssertAgrees(image, image, "N.C");
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(10));

        // Each of 40 layers lists both its rows under both rows of the layer
        // above, 2^40 downward paths over 80 rows.
        static (int, int)[] Layers(int layers)
        {
            var list = new List<(int, int)> { (3, 2), (7, 3), (8, 3) };
            for (int layer = 1; layer < layers; layer++)
            {
                int a = 7 + 2 * layer;
                list.AddRange([(a, a - 2), (a, a - 1), (a + 1, a - 2), (a + 1, a - 1)]);
            }
            return [.. list.OrderBy(entry => entry.Item1)];
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

    // A crafted image may have a declaring cycle; such a row has no name.
    static string? Display(MetadataReader md, TypeDefinitionHandle handle, int depth = 0)
    {
        if (depth > md.TypeDefinitions.Count)
            return null;
        TypeDefinition type = md.GetTypeDefinition(handle);
        TypeDefinitionHandle owner = type.GetDeclaringType();
        string name = md.GetString(type.Name);
        if (!owner.IsNil)
            return Display(md, owner, depth + 1) is { } enclosing ? enclosing + "." + name : null;
        return type.Namespace.IsNil ? name : md.GetString(type.Namespace) + "." + name;
    }

    static PEReader Open(byte[] image) => new(new MemoryStream(image, writable: false));

    static byte[] NestedImage((int Nested, int Enclosing)[] entries, int extra)
    {
        var metadata = new MetadataBuilder();
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        metadata.AddModule(0, metadata.GetOrAddString("P.dll"), metadata.GetOrAddGuid(new Guid(1, 2, 3, [4, 5, 6, 7, 8, 9, 10, 11])), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("P"), new Version(1, 0, 0, 0), default, default, 0, System.Reflection.AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidMethod = metadata.GetOrAddBlob(signature);
        (string? Namespace, string Name)[] types =
        [
            ("N", "C"), (null, "<>c"), (null, "<>d"), (null, "<>e"), ("N", "D"),
            .. Enumerable.Range(0, extra).Select(i => ((string?)null, "<>x" + i)),
        ];
        int method = 1;
        foreach ((string? space, string name) in types)
        {
            metadata.AddTypeDefinition(
                (space is null ? System.Reflection.TypeAttributes.NestedPublic : System.Reflection.TypeAttributes.Public) | System.Reflection.TypeAttributes.Class,
                space is null ? default : metadata.GetOrAddString(space), metadata.GetOrAddString(name), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(method));
            var code = new InstructionEncoder(new BlobBuilder());
            code.CodeBuilder.WriteByte(0x2A);
            metadata.AddMethodDefinition(System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                System.Reflection.MethodImplAttributes.IL, metadata.GetOrAddString("M"), voidMethod, bodies.AddMethodBody(code), default);
            method++;
        }
        foreach ((int nested, int enclosing) in entries)
            metadata.AddNestedType(MetadataTokens.TypeDefinitionHandle(nested), MetadataTokens.TypeDefinitionHandle(enclosing));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata, suppressValidation: true), il).Serialize(image);
        return image.ToArray();
    }
}
