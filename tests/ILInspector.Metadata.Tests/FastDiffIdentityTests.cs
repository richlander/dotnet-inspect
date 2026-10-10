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
    public void PrimitiveAndGlobalTypeOfOneName_AreDistinct()
    {
        // M(int) and M(global::Int32) are different public signatures.
        FastDiffResult result = Compare(
            [new(null, "Int32", null, ReturnOne), new("N", "C", null, ReturnOne, Parameter: Int32Primitive)],
            [new(null, "Int32", null, ReturnOne), new("N", "C", null, ReturnOne, Parameter: 0)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.C").Api);
    }

    [Fact]
    public void FunctionPointerReturnAndPointerToFunctionPointer_AreDistinct()
    {
        // M(delegate*<void*>) and M(delegate*<void>*) are different public
        // signatures.
        FastDiffResult result = Compare(
            [new("N", "C", null, ReturnOne, Parameter: FunctionPointerToVoidPointer)],
            [new("N", "C", null, ReturnOne, Parameter: PointerToFunctionPointer)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.C").Api);
    }

    [Fact]
    public void AttributeMovedFromBackingFieldToEvent_IsChanged()
    {
        // A field-like event E and its backing field E share a name and Type;
        // [field: A] becoming [A] on the event moves the attribute between
        // two declarations of an internal Type.
        FastDiffResult result = Compare(
            [new("N", "C", null, ReturnOne, Public: false, FieldLikeEventAttribute: AttributeTarget.Field)],
            [new("N", "C", null, ReturnOne, Public: false, FieldLikeEventAttribute: AttributeTarget.Event)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.C").Body);
    }

    [Fact]
    public void ExternalVarargOverloadSwitch_IsChanged()
    {
        // VLib.C.M(int, __arglist) called with a long, then VLib.C.M(int, long,
        // __arglist) called with nothing: both call sites carry (int, long),
        // and only the sentinel position differs.
        byte[] callExternal = [0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A]; // call MemberRef 1; ret
        FastDiffResult result = Compare(
            [new("N", "Caller", null, callExternal, VarargRequired: 1)],
            [new("N", "Caller", null, callExternal, VarargRequired: 2)]);

        Assert.Equal(FastDiffState.Changed, Single(result, "N.Caller").Body);
    }

    [Fact]
    public void CallerSwitchingBetweenCompilerControlledMethods_IsIndeterminate()
    {
        // ECMA-335 lets PrivateScope methods share a name and signature; a call
        // to MethodDef 1 or 2 cannot say which one it names.
        byte[] callFirst = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        byte[] callSecond = [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A];
        TypeSpec callee = new("N", "C", null, ReturnOne, ["M", "M"], PrivateScope: true);
        FastDiffResult result = Compare(
            [callee, new("N", "D", null, callFirst)],
            [callee, new("N", "D", null, callSecond)]);

        Assert.Equal(FastDiffState.Indeterminate, Single(result, "N.D").Body);
    }

    [Fact]
    public void CallerSwitchingBetweenDuplicateTypeRows_IsIndeterminate()
    {
        // MethodDef 1 and 2 are N.C.M on two Type rows that share one name.
        byte[] callFirst = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        byte[] callSecond = [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A];
        TypeSpec callee = new("N", "C", null, ReturnOne);
        FastDiffResult result = Compare(
            [callee, callee, new("N", "D", null, callFirst)],
            [callee, callee, new("N", "D", null, callSecond)]);

        Assert.Equal(FastDiffState.Indeterminate, Single(result, "N.D").Body);
    }

    [Fact]
    public void DuplicateGeneratedTypeNames_AreIndeterminate()
    {
        // Two nested <>c rows spell one key, so neither lambda body can be
        // told apart from the other.
        TypeSpec owner = new("N", "C", null, ReturnOne);
        FastDiffResult result = Compare(
            [owner, new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"]), new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"])],
            [owner, new(null, "<>c", 0, ReturnTwo, ["<M>b__0_0"]), new(null, "<>c", 0, ReturnOne, ["<M>b__0_0"])]);

        Assert.Equal(FastDiffState.Indeterminate, Single(result, "N.C").Body);
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

    enum AttributeTarget { Field, Event }

    const int Int32Primitive = -1;
    const int FunctionPointerToVoidPointer = -2;
    const int PointerToFunctionPointer = -3;

    [Fact]
    public void IllFormedNamesThatDecodeAlike_AreDistinct()
    {
        // Both names decode to U+FFFD U+FFFD X; their stored bytes differ.
        TypeSpec[] types = [new("N", "C", null, ReturnOne, ["M", "QQX"])];
        FastDiffResult result = CompareImages(
            Patch(Image(types), "QQX"u8, [0xFF, 0xFE, (byte)'X']),
            Patch(Image(types), "QQX"u8, [0xFE, 0xFF, (byte)'X']));

        Assert.Equal(FastDiffState.Changed, Single(result, "N.C").Api);
    }

    [Fact]
    public void StoredNames_AreTheUtf8OfTheDecodedNames()
    {
        using var pe = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        MetadataReader md = pe.GetMetadataReader();
        var names = new SymbolKey.Utf8Names(pe, md);
        IEnumerable<StringHandle> handles = md.TypeDefinitions
            .SelectMany(type => (StringHandle[])[md.GetTypeDefinition(type).Namespace, md.GetTypeDefinition(type).Name])
            .Concat(md.MethodDefinitions.Select(method => md.GetMethodDefinition(method).Name))
            .Concat(md.FieldDefinitions.Select(field => md.GetFieldDefinition(field).Name));
        int compared = 0;
        foreach (StringHandle handle in handles)
        {
            int length = names.Length(handle, out string? decoded);
            Assert.Null(decoded);
            byte[] stored = new byte[length];
            names.Copy(handle, decoded, stored, 0, length);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(md.GetString(handle)), stored);
            compared++;
        }
        Assert.True(compared > 10_000);
    }

    static byte[] Patch(byte[] image, ReadOnlySpan<byte> marker, byte[] replacement)
    {
        int index = image.AsSpan().IndexOf(marker);
        Assert.True(index >= 0 && image.AsSpan(index + 1).IndexOf(marker) < 0);
        replacement.CopyTo(image, index);
        return image;
    }

    static FastDiffTypeState Single(FastDiffResult result, string fullName)
        => Assert.Single(result.Types, type => type.FullName == fullName);

    /// <summary>
    /// One Type declaration: a namespace, a name, the index of its declaring
    /// Type, and the IL of each of its public static void methods, which take
    /// no parameter, an <c>int</c>, a function pointer, or an instance of an
    /// earlier Type.
    /// </summary>
    sealed record TypeSpec(
        string? Namespace,
        string Name,
        int? DeclaringType,
        byte[] Body,
        string[]? Methods = null,
        int? Parameter = null,
        bool Public = true,
        AttributeTarget? FieldLikeEventAttribute = null,
        int? VarargRequired = null,
        bool PrivateScope = false);

    static FastDiffResult Compare(TypeSpec[] before, TypeSpec[] after)
        => CompareImages(Image(before), Image(after));

    static FastDiffResult CompareImages(byte[] beforeImage, byte[] afterImage)
    {
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

        BlobHandle emptyAttribute = metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });

        // One external vararg call site, MemberRef 1: VLib.C.M(int, long) with
        // the sentinel after the given number of required parameters.
        if (types.Select(type => type.VarargRequired).OfType<int>().FirstOrDefault() is int required and > 0)
        {
            AssemblyReferenceHandle library = metadata.AddAssemblyReference(
                metadata.GetOrAddString("VLib"), new Version(1, 0, 0, 0), default, default, default, default);
            TypeReferenceHandle parent = metadata.AddTypeReference(
                library, metadata.GetOrAddString("VLib"), metadata.GetOrAddString("C"));
            var callSite = new BlobBuilder();
            new BlobEncoder(callSite)
                .MethodSignature(SignatureCallingConvention.VarArgs)
                .Parameters(
                    2,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                        if (required == 1)
                            parameters = parameters.StartVarArgs();
                        parameters.AddParameter().Type().Int64();
                    });
            metadata.AddMemberReference(parent, metadata.GetOrAddString("M"), metadata.GetOrAddBlob(callSite));
        }

        var handles = new List<TypeDefinitionHandle>();
        int nextMethod = 1;
        int nextField = 1;
        foreach (TypeSpec type in types)
        {
            string[] methods = type.Methods ?? ["M"];
            TypeAttributes visibility = type.DeclaringType is null
                ? type.Public ? TypeAttributes.Public : TypeAttributes.NotPublic
                : type.Public ? TypeAttributes.NestedPublic : TypeAttributes.NestedAssembly;
            TypeDefinitionHandle typeHandle = metadata.AddTypeDefinition(
                visibility | TypeAttributes.Class,
                type.Namespace is null ? default : metadata.GetOrAddString(type.Namespace),
                metadata.GetOrAddString(type.Name),
                default,
                MetadataTokens.FieldDefinitionHandle(nextField),
                MetadataTokens.MethodDefinitionHandle(nextMethod));
            handles.Add(typeHandle);
            if (type.FieldLikeEventAttribute is AttributeTarget target)
            {
                // As Roslyn emits a field-like event, the backing field has the
                // event's name and Type: here the declaring Type itself. The
                // attribute's constructor is the Type's first method; only the
                // compared facts matter.
                var fieldSignature = new BlobBuilder();
                new BlobEncoder(fieldSignature).Field().Type().Type(typeHandle, isValueType: false);
                FieldDefinitionHandle field = metadata.AddFieldDefinition(
                    FieldAttributes.Private,
                    metadata.GetOrAddString("E"),
                    metadata.GetOrAddBlob(fieldSignature));
                nextField++;
                EventDefinitionHandle value = metadata.AddEvent(
                    EventAttributes.None, metadata.GetOrAddString("E"), typeHandle);
                metadata.AddEventMap(typeHandle, value);
                metadata.AddCustomAttribute(
                    target == AttributeTarget.Field ? field : value,
                    MetadataTokens.MethodDefinitionHandle(nextMethod),
                    emptyAttribute);
            }
            BlobHandle methodSignature = voidSignature;
            if (type.Parameter is int parameter)
            {
                var parameterSignature = new BlobBuilder();
                new BlobEncoder(parameterSignature)
                    .MethodSignature()
                    .Parameters(
                        1,
                        returnType => returnType.Void(),
                        parameters =>
                        {
                            SignatureTypeEncoder encoder = parameters.AddParameter().Type();
                            if (parameter == Int32Primitive)
                                encoder.Int32();
                            else if (parameter == FunctionPointerToVoidPointer)
                                encoder.FunctionPointer().Parameters(0, r => r.Type().VoidPointer(), _ => { });
                            else if (parameter == PointerToFunctionPointer)
                                encoder.Pointer().FunctionPointer().Parameters(0, r => r.Void(), _ => { });
                            else
                                encoder.Type(handles[parameter], isValueType: false);
                        });
                methodSignature = metadata.GetOrAddBlob(parameterSignature);
            }
            for (int i = 0; i < methods.Length; i++)
            {
                var code = new InstructionEncoder(new BlobBuilder());
                code.CodeBuilder.WriteBytes(i == 0 ? type.Body : ReturnOne);
                metadata.AddMethodDefinition(
                    (type.PrivateScope ? MethodAttributes.PrivateScope : MethodAttributes.Public)
                        | MethodAttributes.Static | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(methods[i]),
                    methodSignature,
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
