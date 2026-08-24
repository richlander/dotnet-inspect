using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Metadata-layer coverage for the VB.NET-shape implicit <c>object.Finalize</c>
/// override: a virtual, reuse-slot, parameterless <c>void Finalize()</c> that
/// carries NO <c>.override</c> MethodImpl (unlike the C#/Roslyn destructor). The
/// synthetic images below emit that exact shape and its close negatives so the
/// classifier proves the slot roots at a strong-name-anchored <c>System.Object</c>
/// over metadata alone, with no inspected-assembly loading. The compiler-produced
/// VB shape is verified end-to-end separately; these seam-isolation fixtures let
/// every close negative run without a VB compiler or ilasm on the box.
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ExplicitObjectFinalizeOverride_RequiresExactBodyAndDeclarationSignatures(
        bool malformedBody,
        bool malformedDeclaration)
    {
        byte[] image = BuildImage(
            new TypeSpec(
                "Handle",
                BaseKind.Object,
                new MethodSpec(
                    "Finalize",
                    malformedDeclaration ? NewSlot : ReuseSlot,
                    malformedBody ? VoidOneParam : VoidNullary),
                OverrideDeclarationSignature:
                    malformedDeclaration ? VoidOneParam : VoidNullary));

        Assert.False(ExtractMember(image, "Handle", "Finalize").IsFinalizer);
    }

    [Fact]
    public void ExplicitObjectFinalizeOverride_WithExactSignatures_IsClassifiedAsFinalizer()
    {
        byte[] image = BuildImage(
            new TypeSpec(
                "Handle",
                BaseKind.Object,
                new MethodSpec("Finalize", ReuseSlot, VoidNullary),
                OverrideDeclarationSignature: VoidNullary));

        Assert.True(ExtractMember(image, "Handle", "Finalize").IsFinalizer);
    }

    [Fact]
    public void InterfaceMethodImplTargetingObjectFinalize_IsNotRetainedAsFinalizer()
    {
        byte[] image = BuildImage(
            new TypeSpec(
                "IHandle",
                BaseKind.Nil,
                new MethodSpec("Finalize", ReuseSlot, VoidNullary),
                Attributes: TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                OverrideDeclarationSignature: VoidNullary));

        using var fullStream = new MemoryStream(image);
        using var fullReader = new PEReader(fullStream);
        var full = ApiSurfaceExtractor.Extract(fullReader);
        Assert.Empty(Assert.Single(full.Types, type => type.Name == "IHandle").Members);

        using var summaryStream = new MemoryStream(image);
        using var summaryReader = new PEReader(summaryStream);
        var summary = ApiSurfaceExtractor.ExtractSummary(summaryReader);
        Assert.Equal(0, summary.PublicMethodCount);
    }

    [Fact]
    public void MalformedValueTypeAndDelegateOwnersAreNotFinalizers()
    {
        foreach (var (name, baseKind) in new[]
                 {
                     ("ValueLike", BaseKind.ValueType),
                     ("DelegateLike", BaseKind.MulticastDelegate),
                 })
        {
            byte[] image = BuildImage(
                new TypeSpec(
                    name,
                    baseKind,
                    new MethodSpec("Finalize", ReuseSlot, VoidNullary),
                    OverrideDeclarationSignature: VoidNullary));
            using var stream = new MemoryStream(image);
            using var reader = new PEReader(stream);
            var metadata = reader.GetMetadataReader();
            MethodDefinitionHandle finalizer = metadata
                .GetTypeDefinition(
                    metadata.TypeDefinitions.Single(
                        handle => metadata.StringComparer.Equals(
                            metadata.GetTypeDefinition(handle).Name,
                            name)))
                .GetMethods()
                .Single();

            // PDB projection calls IsFinalizerMethod directly, while API
            // extraction already has a class-kind gate. Both must reject the
            // malformed finalizer-shaped MethodImpl owner.
            Assert.False(ApiSurfaceExtractor.IsFinalizerMethod(metadata, finalizer));
            Assert.False(ExtractMember(image, name, "Finalize").IsFinalizer);
        }
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
    public void ModuleScopedLocalBase_IsFollowedToObjectFinalizeSlot()
    {
        byte[] image = BuildImage(
            new TypeSpec(
                "VbBase",
                BaseKind.Object,
                new MethodSpec("Finalize", ReuseSlot, VoidNullary)),
            new TypeSpec(
                "Derived",
                BaseKind.ModuleRef("VbBase"),
                new MethodSpec("Finalize", ReuseSlot, VoidNullary)));

        using (var stream = new MemoryStream(image))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            TypeDefinitionHandle derived = reader.TypeDefinitions.Single(
                handle => reader.StringComparer.Equals(
                    reader.GetTypeDefinition(handle).Name,
                    "Derived"));
            MethodDefinitionHandle finalizer = reader
                .GetTypeDefinition(derived)
                .GetMethods()
                .Single(handle => reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    "Finalize"));
            Assert.True(
                ApiSurfaceExtractor.IsFinalizerMethod(reader, finalizer));
        }
        Assert.True(ExtractMember(image, "Derived", "Finalize").IsFinalizer);
    }

    [Fact]
    public void CyclicModuleScopedBaseReference_IsRejectedVisibly()
    {
        byte[] image = BuildImage(
            new TypeSpec(
                "Derived",
                BaseKind.MalformedModuleRef("Cycle"),
                new MethodSpec("Finalize", ReuseSlot, VoidNullary)));
        using var stream = new MemoryStream(image);
        using var pe = new PEReader(stream);

        var reader = pe.GetMetadataReader();
        TypeDefinitionHandle derived = reader.TypeDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                "Derived"));
        MethodDefinitionHandle finalizer = reader
            .GetTypeDefinition(derived)
            .GetMethods()
            .Single();
        Assert.False(
            ApiSurfaceExtractor.IsFinalizerMethod(reader, finalizer));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: true);

        Assert.DoesNotContain(
            surface.Types,
            type => type.Name == "Derived");
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.Operation == "type name");
    }

    static ApiMember ExtractMember(byte[] image, string typeName, string memberName)
    {
        using var stream = new MemoryStream(image);
        var surface = AssemblyReader.ExtractApiSurface(stream, includeAll: true, typesOnly: false);
        Assert.NotNull(surface);
        var type = Assert.Single(surface!.Types, t => t.Name == typeName);
        return Assert.Single(type.Members, m => m.Name == memberName);
    }

    enum BaseTag
    {
        Object,
        Exception,
        ValueType,
        MulticastDelegate,
        Def,
        ModuleRef,
        MalformedModuleRef,
        Nil
    }

    readonly record struct BaseKind(BaseTag Tag, string? DefName)
    {
        public static readonly BaseKind Object = new(BaseTag.Object, null);
        public static readonly BaseKind Exception = new(BaseTag.Exception, null);
        public static readonly BaseKind ValueType = new(BaseTag.ValueType, null);
        public static readonly BaseKind MulticastDelegate =
            new(BaseTag.MulticastDelegate, null);
        public static readonly BaseKind Nil = new(BaseTag.Nil, null);
        public static BaseKind Def(string name) => new(BaseTag.Def, name);
        public static BaseKind ModuleRef(string name) =>
            new(BaseTag.ModuleRef, name);
        public static BaseKind MalformedModuleRef(string name) =>
            new(BaseTag.MalformedModuleRef, name);
    }

    sealed record MethodSpec(string Name, MethodAttributes Attributes, byte[] Signature);

    sealed record TypeSpec(
        string Name,
        BaseKind Base,
        MethodSpec Method,
        string? Namespace = null,
        TypeAttributes Attributes = TypeAttributes.Public | TypeAttributes.Class,
        byte[]? OverrideDeclarationSignature = null);

    static byte[] BuildImage(params TypeSpec[] types)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
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
        EntityHandle valueTypeRef = default;
        EntityHandle multicastDelegateRef = default;
        if (Array.Exists(
                types,
                static type => type.Base.Tag is
                    BaseTag.Object
                    or BaseTag.Exception
                    or BaseTag.ValueType
                    or BaseTag.MulticastDelegate
                    || type.OverrideDeclarationSignature is not null))
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
            valueTypeRef = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("ValueType"));
            multicastDelegateRef = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("MulticastDelegate"));
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
                BaseTag.ValueType => valueTypeRef,
                BaseTag.MulticastDelegate => multicastDelegateRef,
                BaseTag.Def => defHandles[types[i].Base.DefName!],
                BaseTag.ModuleRef => metadata.AddTypeReference(
                    module,
                    default,
                    metadata.GetOrAddString(types[i].Base.DefName!)),
                BaseTag.MalformedModuleRef => metadata.AddTypeReference(
                    MetadataTokens.TypeReferenceHandle(1),
                    default,
                    metadata.GetOrAddString(types[i].Base.DefName!)),
                _ => default,
            };
            var handle = metadata.AddTypeDefinition(
                types[i].Attributes,
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
            if (types[i].OverrideDeclarationSignature is not { } declarationSignature)
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
