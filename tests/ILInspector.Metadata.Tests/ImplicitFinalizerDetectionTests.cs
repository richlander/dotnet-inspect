using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Metadata-layer coverage for explicit C#/Roslyn and implicit VB.NET
/// <c>object.Finalize</c> overrides. The synthetic images below emit each exact
/// shape and close negatives so the classifier proves the slot and signature
/// over metadata alone, with no inspected-assembly loading. Compiler-produced
/// C# and VB shapes are verified end-to-end separately; these seam-isolation
/// fixtures let malformed negatives run without a VB compiler or ilasm.
/// </summary>
public class ImplicitFinalizerDetectionTests
{
    // Instance (HASTHIS) signature blobs: [callconv, paramCount, retType, params...].
    static readonly byte[] VoidNullary = [0x20, 0x00, 0x01];       // void Finalize()
    static readonly byte[] VoidOneParam = [0x20, 0x01, 0x01, 0x08]; // void Finalize(int)
    static readonly byte[] IntNullary = [0x20, 0x00, 0x08];         // int Finalize()
    static readonly byte[] VarargNullary = [0x25, 0x00, 0x01];      // vararg void Finalize()

    const MethodAttributes ReuseSlot =
        MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig;
    const MethodAttributes NewSlot = ReuseSlot | MethodAttributes.NewSlot;

    [Fact]
    public void ImplicitObjectFinalizeOverride_IsClassifiedAsFinalizer()
    {
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Object, new MethodSpec("Finalize", ReuseSlot, VoidNullary))),
            "Handle",
            "Finalize");

        Assert.True(member.IsFinalizer);
        Assert.Equal("finalizer", member.Kind);
    }

    [Fact]
    public void ImplicitOverride_WalksPastReuseSlotBase_IsClassifiedAsFinalizer()
    {
        // VbBase overrides object.Finalize (reuse-slot); VbDerived overrides again.
        // The walk must step past VbBase's reuse-slot Finalize and reach System.Object.
        byte[] image = BuildImage(
            new TypeSpec("VbBase", BaseKind.Object, new MethodSpec("Finalize", ReuseSlot, VoidNullary)),
            new TypeSpec("VbDerived", BaseKind.Def("VbBase"), new MethodSpec("Finalize", ReuseSlot, VoidNullary)));

        Assert.True(ExtractMember(image, "VbBase", "Finalize").IsFinalizer);
        Assert.True(ExtractMember(image, "VbDerived", "Finalize").IsFinalizer);
    }

    [Fact]
    public void CustomNewVirtualFinalizeSlot_IsNotClassifiedAsFinalizer()
    {
        // CustomBase declares `new virtual void Finalize()`; CustomDerived overrides
        // that custom slot, NOT object.Finalize. Neither is a destructor.
        byte[] image = BuildImage(
            new TypeSpec("CustomBase", BaseKind.Object, new MethodSpec("Finalize", NewSlot, VoidNullary)),
            new TypeSpec("CustomDerived", BaseKind.Def("CustomBase"), new MethodSpec("Finalize", ReuseSlot, VoidNullary)));

        Assert.False(ExtractMember(image, "CustomBase", "Finalize").IsFinalizer);
        Assert.False(ExtractMember(image, "CustomDerived", "Finalize").IsFinalizer);
    }

    [Fact]
    public void ReuseSlotFinalize_WithParameter_IsNotClassifiedAsFinalizer()
    {
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Object, new MethodSpec("Finalize", ReuseSlot, VoidOneParam))),
            "Handle",
            "Finalize");

        Assert.False(member.IsFinalizer);
    }

    [Fact]
    public void ReuseSlotFinalize_WithNonVoidReturn_IsNotClassifiedAsFinalizer()
    {
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Object, new MethodSpec("Finalize", ReuseSlot, IntNullary))),
            "Handle",
            "Finalize");

        Assert.False(member.IsFinalizer);
    }

    [Fact]
    public void ReuseSlotFinalize_OverNonObjectCrossAssemblyBase_IsNotClassifiedAsFinalizer()
    {
        // Base leaves the assembly as System.Exception (a real core-library type,
        // but not System.Object): the slot root cannot be proven, so reject.
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Exception, new MethodSpec("Finalize", ReuseSlot, VoidNullary))),
            "Handle",
            "Finalize");

        Assert.False(member.IsFinalizer);
    }

    [Fact]
    public void NewSlotFinalize_IsNotClassifiedAsFinalizer()
    {
        // A NewSlot virtual Finalize introduces its own slot; it does not override
        // object.Finalize even with the object base and matching signature.
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Object, new MethodSpec("Finalize", NewSlot, VoidNullary))),
            "Handle",
            "Finalize");

        Assert.False(member.IsFinalizer);
    }

    [Fact]
    public void VarargFinalize_IsNotClassifiedAsFinalizer()
    {
        // A vararg calling convention cannot bind object.Finalize's default-convention
        // slot, so even a virtual reuse-slot `vararg void Finalize()` over System.Object
        // must reject — a name-only collision on a different convention is not a finalizer.
        var member = ExtractMember(
            BuildImage(new TypeSpec("Handle", BaseKind.Object, new MethodSpec("Finalize", ReuseSlot, VarargNullary))),
            "Handle",
            "Finalize");

        Assert.False(member.IsFinalizer);
    }

    [Fact]
    public void InAssemblyObjectRoot_DerivedFinalize_IsClassifiedAsFinalizer()
    {
        // Inspecting the core library that defines System.Object itself: the genuine
        // object.Finalize is a NewSlot virtual, so the walk must recognize the in-assembly
        // System.Object root BEFORE applying the custom-slot rejection.
        byte[] image = BuildImage(
            new TypeSpec("Object", BaseKind.Nil, new MethodSpec("Finalize", NewSlot, VoidNullary), Namespace: "System"),
            new TypeSpec("Derived", BaseKind.Def("Object"), new MethodSpec("Finalize", ReuseSlot, VoidNullary)));

        using (var pe = new PEReader(new MemoryStream(image)))
            Assert.Empty(pe.GetMetadataReader().AssemblyReferences);
        Assert.True(ExtractMember(image, "Derived", "Finalize").IsFinalizer);
    }

    [Fact]
    public void ExplicitObjectFinalizeOverride_IsClassifiedAsFinalizer()
    {
        var member = ExtractMember(
            BuildImage(new TypeSpec(
                "Handle",
                BaseKind.Exception,
                new MethodSpec("Finalize", ReuseSlot, VoidNullary),
                ExplicitOverrideSignature: VoidNullary)),
            "Handle",
            "Finalize");

        Assert.True(member.IsFinalizer);
        Assert.Equal("finalizer", member.Kind);
    }

    [Theory]
    [InlineData("Destroy", false, false)]
    [InlineData("Finalize", true, false)]
    [InlineData("Finalize", false, true)]
    public void MalformedExplicitObjectFinalizeOverride_IsNotClassifiedAsFinalizer(
        string bodyName,
        bool malformedBodySignature,
        bool malformedDeclarationSignature)
    {
        var member = ExtractMember(
            BuildImage(new TypeSpec(
                "Handle",
                BaseKind.Exception,
                new MethodSpec(
                    bodyName,
                    ReuseSlot,
                    malformedBodySignature
                        ? VoidOneParam
                        : VoidNullary),
                ExplicitOverrideSignature: malformedDeclarationSignature
                    ? VoidOneParam
                    : VoidNullary)),
            "Handle",
            bodyName);

        Assert.False(member.IsFinalizer);
        Assert.NotEqual("finalizer", member.Kind);
    }

    static ApiMember ExtractMember(byte[] image, string typeName, string memberName)
    {
        using var stream = new MemoryStream(image);
        var surface = AssemblyReader.ExtractApiSurface(stream, includeAll: true, typesOnly: false);
        Assert.NotNull(surface);
        var type = Assert.Single(surface!.Types, t => t.Name == typeName);
        return Assert.Single(type.Members, m => m.Name == memberName);
    }

    enum BaseTag { Object, Exception, Def, Nil }

    readonly record struct BaseKind(BaseTag Tag, string? DefName)
    {
        public static readonly BaseKind Object = new(BaseTag.Object, null);
        public static readonly BaseKind Exception = new(BaseTag.Exception, null);
        public static readonly BaseKind Nil = new(BaseTag.Nil, null);
        public static BaseKind Def(string name) => new(BaseTag.Def, name);
    }

    sealed record MethodSpec(string Name, MethodAttributes Attributes, byte[] Signature);

    sealed record TypeSpec(
        string Name,
        BaseKind Base,
        MethodSpec Method,
        string? Namespace = null,
        byte[]? ExplicitOverrideSignature = null);

    static byte[] BuildImage(params TypeSpec[] types)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        EntityHandle objectRef = default;
        EntityHandle exceptionRef = default;
        if (Array.Exists(
                types,
                static type => type.Base.Tag is BaseTag.Object or BaseTag.Exception))
        {
            // Cross-assembly fixtures use the real core-library identity. The
            // in-assembly System.Object fixture stays reference-free like a corelib.
            var coreLib = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e }),
                flags: default,
                hashValue: default);
            objectRef = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
            exceptionRef = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Exception"));
        }

        // Shared trivial `ret` body; the extractor never reads method bodies.
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(instructions, new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder, maxStack: 0);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var defHandles = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        int methodRow = 1;
        for (int i = 0; i < types.Length; i++)
        {
            EntityHandle baseHandle = types[i].Base.Tag switch
            {
                BaseTag.Object => objectRef,
                BaseTag.Exception => exceptionRef,
                BaseTag.Def => defHandles[types[i].Base.DefName!],
                _ => default,
            };
            var handle = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                types[i].Namespace is { } ns ? metadata.GetOrAddString(ns) : default,
                metadata.GetOrAddString(types[i].Name),
                baseHandle,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(methodRow));
            defHandles[types[i].Name] = handle;
            methodRow++;
        }

        var methodHandles = new List<MethodDefinitionHandle>(types.Length);
        foreach (var type in types)
        {
            var spec = type.Method;
            methodHandles.Add(metadata.AddMethodDefinition(
                spec.Attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(spec.Name),
                metadata.GetOrAddBlob(spec.Signature),
                bodyOffset,
                parameterList: MetadataTokens.ParameterHandle(1)));
        }

        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].ExplicitOverrideSignature is not { } declarationSignature)
                continue;
            var declaration = metadata.AddMemberReference(
                objectRef,
                metadata.GetOrAddString("Finalize"),
                metadata.GetOrAddBlob(declarationSignature));
            metadata.AddMethodImplementation(
                defHandles[types[i].Name],
                methodHandles[i],
                declaration);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
