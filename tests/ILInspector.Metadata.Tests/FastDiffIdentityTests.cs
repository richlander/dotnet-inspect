using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Distinct metadata declarations never share a compared identity, and IL
/// that cannot be decoded is never <c>Unchanged</c>.
/// </summary>
public sealed class FastDiffIdentityTests
{
    static readonly byte[] ReturnOne = [0x17, 0x26, 0x2A]; // ldc.i4.1; pop; ret
    static readonly byte[] ReturnTwo = [0x18, 0x26, 0x2A]; // ldc.i4.2; pop; ret

    [Fact]
    public void TopLevelNameWithSlash_DoesNotHideNestedTypeChange()
    {
        FastDiffResult result = Compare(
            [new("N", "A", null, ReturnOne), new("N", "A/B", null, ReturnOne), new(null, "B", 0, ReturnOne)],
            [new("N", "A", null, ReturnOne), new("N", "A/B", null, ReturnOne), new(null, "B", 0, ReturnTwo)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.A.B").Body);
        Assert.Equal(FastDiffState.Unchanged, Single(result, "N.A/B").Body);
    }

    [Fact]
    public void NamespaceAndDottedName_DoNotHideEachOthersChange()
    {
        FastDiffResult result = Compare(
            [new("N", "A.B", null, ReturnOne), new("N.A", "B", null, ReturnOne)],
            [new("N", "A.B", null, ReturnOne), new("N.A", "B", null, ReturnTwo)]);

        FastDiffTypeState[] rows = [.. result.Types.Where(type => type.FullName == "N.A.B")];
        Assert.Equal(2, rows.Length);
        Assert.Equal(
            [FastDiffState.Unchanged, FastDiffState.Changed],
            rows.Select(row => row.Body).Order());
    }

    [Fact]
    public void MemberNamesWithSeparators_DoNotHideCalleeChange()
    {
        // Method D::M of N.C (MethodDef 1) and method M of N.C::D (MethodDef 2)
        // are distinct callees; a caller that switches between them changed.
        byte[] callFirst = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        byte[] callSecond = [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A];
        TypeSpec[] callees = [new("N", "C", null, ReturnOne, ["D::M"]), new("N", "C::D", null, ReturnOne)];
        FastDiffResult result = Compare(
            [.. callees, new("N", "Caller", null, callFirst)],
            [.. callees, new("N", "Caller", null, callSecond)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.Caller").Body);
    }

    [Fact]
    public void DuplicateTypeNames_AreIndeterminate()
    {
        FastDiffResult result = Compare(
            [new("N", "C", null, ReturnOne), new("N", "C", null, ReturnOne)],
            [new("N", "C", null, ReturnOne), new("N", "C", null, ReturnTwo)]);

        Assert.NotEmpty(result.Types);
        Assert.All(result.Types.Where(type => type.FullName == "N.C"), type =>
        {
            Assert.Equal(FastDiffState.Indeterminate, type.Api);
            Assert.Equal(FastDiffState.Indeterminate, type.Body);
        });
    }

    [Fact]
    public void UnknownOpcode_IsIndeterminate()
    {
        byte[] invalid = [0x24, 0x2A]; // 0x24 is not an opcode; ret
        FastDiffResult result = Compare(
            [new("N", "C", null, invalid)],
            [new("N", "C", null, invalid)]);

        FastDiffTypeState state = Single(result, "N.C");
        Assert.Equal(FastDiffState.Unchanged, state.Api);
        Assert.Equal(FastDiffState.Indeterminate, state.Body);
    }

    [Fact]
    public void UnknownTwoByteOpcode_IsIndeterminate()
    {
        byte[] invalid = [0xFE, 0x08, 0x2A]; // 0xFE 0x08 is not an opcode; ret
        FastDiffResult result = Compare(
            [new("N", "C", null, invalid)],
            [new("N", "C", null, invalid)]);

        Assert.Equal(FastDiffState.Indeterminate, Single(result, "N.C").Body);
    }

    static FastDiffTypeState Single(FastDiffResult result, string fullName)
        => Assert.Single(result.Types, type => type.FullName == fullName);

    /// <summary>
    /// One Type declaration: a namespace, a name, the index of its declaring
    /// Type, and the IL of each of its public static void methods.
    /// </summary>
    sealed record TypeSpec(
        string? Namespace,
        string Name,
        int? DeclaringType,
        byte[] Body,
        string[]? Methods = null);

    static FastDiffResult Compare(TypeSpec[] before, TypeSpec[] after)
    {
        byte[] beforeImage = Image(before);
        byte[] afterImage = Image(after);
        using var beforeReader = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var afterReader = new PEReader(new MemoryStream(afterImage, writable: false));
        FastDiffResult whole = FastDiff.Compare(beforeReader, afterReader);

        var stepped = new FastDiffComparison();
        bool complete = false;
        while (!complete)
        {
            using var stepBefore = new PEReader(new MemoryStream(beforeImage, writable: false));
            using var stepAfter = new PEReader(new MemoryStream(afterImage, writable: false));
            complete = stepped.Step(stepBefore, stepAfter, TimeSpan.Zero);
        }
        Assert.Equal(whole.Types, stepped.Result!.Types);
        return whole;
    }

    static byte[] Image(TypeSpec[] types)
    {
        var metadata = new MetadataBuilder();
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Identity.dll"),
            metadata.GetOrAddGuid(new Guid("6a1f3f5e-0d7e-4b8f-9b8e-1f2c3d4e5f60")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Identity"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidSignature = metadata.GetOrAddBlob(signature);

        var handles = new List<TypeDefinitionHandle>();
        int nextMethod = 1;
        foreach (TypeSpec type in types)
        {
            string[] methods = type.Methods ?? ["M"];
            handles.Add(metadata.AddTypeDefinition(
                (type.DeclaringType is null ? TypeAttributes.Public : TypeAttributes.NestedPublic)
                    | TypeAttributes.Class,
                type.Namespace is null ? default : metadata.GetOrAddString(type.Namespace),
                metadata.GetOrAddString(type.Name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(nextMethod)));
            for (int i = 0; i < methods.Length; i++)
            {
                var code = new InstructionEncoder(new BlobBuilder());
                code.CodeBuilder.WriteBytes(i == 0 ? type.Body : ReturnOne);
                metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(methods[i]),
                    voidSignature,
                    bodies.AddMethodBody(code),
                    default);
                nextMethod++;
            }
        }
        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].DeclaringType is int declaring)
                metadata.AddNestedType(handles[i], handles[declaring]);
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                il)
            .Serialize(image);
        return image.ToArray();
    }
}
