using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Cyclic and deep metadata graphs must make the affected Type Indeterminate
/// rather than exhaust the stack. The checks run in a child process so a
/// regression fails the test instead of terminating the test host.
/// </summary>
public sealed class FindingDiffAnyMetadataSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_FINDING_DIFF_ANY_METADATA_SAFETY_WORKER";
    const int DeepGraphLength = 100_000;

    [Fact]
    public void CyclicAndDeepMetadataGraphs_AreIndeterminateInChildProcess()
        => RunWorker(nameof(MetadataSafetyWorker));

    [Fact]
    public void MetadataSafetyWorker()
    {
        if (Environment.GetEnvironmentVariable(WorkerVariable) != nameof(MetadataSafetyWorker))
            return;

        AssertIndeterminate(BaseTypeReferenceImage(depth: 1, cyclic: true), "N.C");
        AssertIndeterminate(BaseTypeReferenceImage(DeepGraphLength, cyclic: false), "N.C");
        AssertIndeterminate(BaseTypeSpecificationImage(), "N.C");
        AssertIndeterminate(DeepFieldSignatureImage(), "N.C");
        AssertAllIndeterminateOrAbsent(NestedTypeImage(depth: 1, cyclic: true));
        AssertUnreadableNameContained(UnreadableNameImage(nested: false));
        AssertUnreadableNameContained(UnreadableNameImage(nested: true));
        AssertDeepNestingContained(NestedTypeImage(DeepGraphLength, cyclic: false));
    }

    static void AssertIndeterminate(byte[] image, string fullName)
    {
        FindingDiffAnyTypeState state = Assert.Single(Compare(image).Types, type => type.FullName == fullName);
        Assert.Equal(FindingDiffAnyState.Indeterminate, state.Api);
        Assert.Equal(FindingDiffAnyState.Indeterminate, state.Body);
    }

    static void AssertAllIndeterminateOrAbsent(byte[] image)
    {
        Assert.All(Compare(image).Types, type =>
        {
            Assert.Equal(FindingDiffAnyState.Indeterminate, type.Api);
            Assert.Equal(FindingDiffAnyState.Indeterminate, type.Body);
        });
    }

    static void AssertUnreadableNameContained(byte[] image)
    {
        FindingDiffAnyResult result = Compare(image);
        FindingDiffAnyTypeState good = Assert.Single(result.Types, type => type.FullName == "N.Good");
        Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (good.Api, good.Body));
        Assert.Contains(
            result.Types,
            type => type.Api == FindingDiffAnyState.Indeterminate && type.Body == FindingDiffAnyState.Indeterminate);
    }

    static void AssertDeepNestingContained(byte[] image)
    {
        FindingDiffAnyResult result = Compare(image);
        Assert.Contains(result.Types, type => type.Api == FindingDiffAnyState.Indeterminate);
        Assert.DoesNotContain(
            result.Types,
            type => type.Api == FindingDiffAnyState.Changed || type.Body == FindingDiffAnyState.Changed);
    }

    static FindingDiffAnyResult Compare(byte[] image)
    {
        using var before = new PEReader(new MemoryStream(image, writable: false));
        using var after = new PEReader(new MemoryStream(image, writable: false));
        FindingDiffAnyResult whole = FindingDiffAny.Compare(before, after);

        // Resuming at every row and Type boundary, with fresh readers over the
        // same image each step, reaches the same result.
        var stepped = new FindingDiffAnyComparison();
        bool complete = false;
        while (!complete)
        {
            using var stepBefore = new PEReader(new MemoryStream(image, writable: false));
            using var stepAfter = new PEReader(new MemoryStream(image, writable: false));
            complete = stepped.Step(stepBefore, stepAfter, TimeSpan.Zero);
        }
        Assert.Equal(whole.Types, stepped.Result!.Types);
        return whole;
    }

    static void RunWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(FindingDiffAnyMetadataSafetyTests).Assembly.Location);
        startInfo.ArgumentList.Add("--filter-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[WorkerVariable] = workerMethod;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\n"
                + $"stdout:\n{standardOutput.GetAwaiter().GetResult()}\n"
                + $"stderr:\n{standardError.GetAwaiter().GetResult()}");
    }

    /// <summary>A public <c>N.C</c> whose base Type is a TypeRef chain.</summary>
    static byte[] BaseTypeReferenceImage(int depth, bool cyclic)
    {
        MetadataBuilder metadata = CreateMetadata();
        EntityHandle baseType;
        if (cyclic)
        {
            baseType = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop"));
        }
        else
        {
            var runtime = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
            TypeReferenceHandle reference = metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
            for (int i = 1; i < depth; i++)
                reference = metadata.AddTypeReference(reference, default, metadata.GetOrAddString("T"));
            baseType = reference;
        }
        AddPublicType(metadata, baseType);
        return Serialize(metadata);
    }

    /// <summary>A public <c>N.C</c> whose base Type is a TypeSpec naming itself.</summary>
    static byte[] BaseTypeSpecificationImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        // ELEMENT_TYPE_CLASS followed by the coded TypeDefOrRefOrSpec index of TypeSpec row 1.
        var specification = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(new byte[] { 0x12, (1 << 2) | 2 }));
        AddPublicType(metadata, specification);
        return Serialize(metadata);
    }

    /// <summary>A public <c>N.C</c> with a public field whose Type nests arrays far too deep.</summary>
    static byte[] DeepFieldSignatureImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        for (int i = 0; i < DeepGraphLength; i++)
            signature.WriteByte(0x1D);
        signature.WriteByte(0x08);
        var field = metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Deep"),
            metadata.GetOrAddBlob(signature));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] NestedTypeImage(int depth, bool cyclic)
    {
        MetadataBuilder metadata = CreateMetadata();
        TypeDefinitionHandle parent = default;
        for (int i = 0; i < depth; i++)
        {
            TypeDefinitionHandle current = metadata.AddTypeDefinition(
                i == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                i == 0 ? metadata.GetOrAddString("N") : default,
                metadata.GetOrAddString("T"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            if (!parent.IsNil)
                metadata.AddNestedType(current, parent);
            parent = current;
        }
        if (cyclic)
            metadata.AddNestedType(parent, parent);
        return Serialize(metadata);
    }

    /// <summary>
    /// A well-formed <c>N.Good</c> beside a Type whose Name string offset lies
    /// beyond the #Strings heap, top-level or nested.
    /// </summary>
    static byte[] UnreadableNameImage(bool nested)
    {
        const string Marker = "ZZUNREADABLE";
        MetadataBuilder metadata = CreateMetadata();
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Good"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(nested ? "Outer" : Marker),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (nested)
        {
            TypeDefinitionHandle inner = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic | TypeAttributes.Class,
                default,
                metadata.GetOrAddString(Marker),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(inner, outer);
        }

        byte[] image = Serialize(metadata);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        int table = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.TypeDef);
        int rowSize = reader.GetTableRowSize(TableIndex.TypeDef);
        int row = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (reader.GetString(reader.GetTypeDefinition(handle).Name) == Marker)
            {
                // The Name column follows the 4-byte Flags column; point it past the heap.
                int name = table + row * rowSize + 4;
                image[name] = 0xF0;
                image[name + 1] = 0xFF;
            }
            row++;
        }
        return image;
    }

    // ECMA-335 II.22.28 forbids a MethodSemantics row that names a method of
    // another Type; the reader hands it over, and the comparison tolerates it as
    // a spelled accessor rather than failing. The row is resolved before the
    // property fact opens, so the member key it needs is not built inside it.
    [Fact]
    public void PropertyAccessorOfAnotherType_ComparesWithoutFailing()
    {
        byte[] image = ForeignAccessorImage();
        FindingDiffAnyResult result = Compare(image);
        FindingDiffAnyTypeState holder = Assert.Single(result.Types, type => type.FullName == "N.Holder");
        Assert.Equal(FindingDiffAnyState.Unchanged, holder.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, holder.Body);
        FindingDiffAnyTypeState owner = Assert.Single(result.Types, type => type.FullName == "N.Owner");
        Assert.Equal(FindingDiffAnyState.Unchanged, owner.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, owner.Body);
    }

    // ECMA-335 II.23.2.7 lets a custom modifier name a TypeSpec row, the one
    // place the reader admits one inside a signature. A composite over such a
    // row resolves it before the composite's scratch key opens, so one spelling
    // never opens two scratch keys.
    [Fact]
    public void ModifierOverAGenericInstanceTypeSpecification_ComparesWithoutFailing()
    {
        byte[] image = TypeSpecInSignatureImage();
        FindingDiffAnyResult result = Compare(image);
        FindingDiffAnyTypeState type = Assert.Single(result.Types, type => type.FullName == "N.Holder");
        Assert.Equal(FindingDiffAnyState.Unchanged, type.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, type.Body);
    }

    // N.Holder declares abstract void Take(int32 modreq(List`1<int32>) p):
    // the parameter is CMOD_REQD <TypeSpec row> I4, where the TypeSpec row is
    // GENERICINST CLASS <TypeRef List`1> 1 I4.
    static byte[] TypeSpecInSignatureImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(11, 0), default, default, default, default);
        var list = metadata.AddTypeReference(
            runtime, metadata.GetOrAddString("System.Collections.Generic"), metadata.GetOrAddString("List`1"));
        var specBlob = new BlobBuilder();
        specBlob.WriteByte(0x15);
        specBlob.WriteByte(0x12);
        specBlob.WriteCompressedInteger((MetadataTokens.GetRowNumber(list) << 2) | 1);
        specBlob.WriteCompressedInteger(1);
        specBlob.WriteByte(0x08);
        var spec = metadata.AddTypeSpecification(metadata.GetOrAddBlob(specBlob));
        int encoded = (MetadataTokens.GetRowNumber(spec) << 2) | 2;
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1F);
        signature.WriteCompressedInteger(encoded);
        signature.WriteByte(0x08);
        var method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Take"),
            metadata.GetOrAddBlob(signature),
            -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Holder"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        return Serialize(metadata);
    }

    // A property's signature is decoded by the speller and embedded by Finding
    // Diff Any; a Type the signature names and nothing else has named is spelled
    // during that embed, and a composite later in the signature must survive it.
    [Fact]
    public void PropertyNamingAnUnseenTypeBeforeAComposite_ComparesWithoutFailing()
    {
        byte[] image = UnseenTypeThenCompositePropertyImage();
        FindingDiffAnyResult result = Compare(image);
        FindingDiffAnyTypeState type = Assert.Single(result.Types, type => type.FullName == "N.Holder");
        Assert.Equal(FindingDiffAnyState.Unchanged, type.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, type.Body);
    }

    // N.Holder declares an accessor-less property int32[] P(O.Foo), where O.Foo
    // is a Type reference no other row names: PROPERTY|HASTHIS, 1, SZARRAY I4,
    // CLASS <TypeRef Foo>.
    static byte[] UnseenTypeThenCompositePropertyImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"), new Version(1, 0), default, default, default, default);
        var foo = metadata.AddTypeReference(runtime, metadata.GetOrAddString("O"), metadata.GetOrAddString("Foo"));
        var holder = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Holder"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        signature.WriteByte(0x28);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x1D);
        signature.WriteByte(0x08);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger((MetadataTokens.GetRowNumber(foo) << 2) | 1);
        var property = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("P"), metadata.GetOrAddBlob(signature));
        metadata.AddPropertyMap(holder, property);
        return Serialize(metadata);
    }

    // N.Owner declares abstract int Get(); N.Holder declares property P whose
    // getter row names Owner.Get.
    static byte[] ForeignAccessorImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        var getter = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Get"),
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }),
            -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        var holder = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Holder"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        var property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("P"),
            metadata.GetOrAddBlob(new byte[] { 0x28, 0x00, 0x08 }));
        metadata.AddPropertyMap(holder, property);
        metadata.AddMethodSemantics(property, MethodSemanticsAttributes.Getter, getter);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Safety.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Safety"),
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
        return metadata;
    }

    static void AddPublicType(MetadataBuilder metadata, EntityHandle baseType)
        => metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}
