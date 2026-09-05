using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

// SignatureBlobGuard bounds the structural nesting depth of a signature blob before SRM decodes
// it, so a malformed deep/cyclic signature degrades gracefully instead of overflowing the native
// stack. These tests craft deep synthetic blobs (must be rejected), shallow/wide ones (must be
// accepted), and sweep a real assembly (no false-positives).
public class SignatureBlobGuardTests
{
    const byte Ptr = 0x0f, ByRef = 0x10, ValueType = 0x11, Class = 0x12, Var = 0x13, Array = 0x14,
        GenericInst = 0x15, FnPtr = 0x1b, SzArray = 0x1d, CmodReqd = 0x1f, CmodOpt = 0x20, I4 = 0x08;

    [Theory]
    [InlineData(new byte[] { 6, I4 }, true, 0, 0, 0, 0)]
    [InlineData(new byte[] { 6, Array, I4, 2, 2, 3, 4, 1, 0 }, true, 1, 2, 1, 1)]
    [InlineData(new byte[] { 6, Array, Array, I4, 1, 1, 3, 1, 0, 2, 2, 3, 4, 0 }, true, 2, 3, 2, 1)]
    [InlineData(new byte[] { 6, Array, I4, 2, 2, 3 }, false, 1, 2, 0, 0)]
    [InlineData(new byte[] { 6, Array, I4, 1, 0, 0, I4 }, false, 1, 0, 1, 0)]
    public unsafe void ArrayMeasurements_PreserveAdmissionAndPartialScan(
        byte[] signature, bool accepted, int sizeCount, long sizeTotal, int lowerCount, long lowerTotal)
    {
        fixed (byte* bytes = signature)
        {
            var blob = new BlobReader(bytes, signature.Length);
            Assert.Equal(accepted, SignatureBlobGuard.IsSafeAndCompleteToDecode(blob, SignatureBlobGuard.Kind.Field));
            Assert.Equal(accepted, SignatureBlobGuard.IsSafeAndCompleteToDecode(
                blob, SignatureBlobGuard.Kind.Field, out var measurements));
            Assert.Equal(sizeCount, measurements.Sizes.Count);
            Assert.Equal(sizeTotal, measurements.Sizes.Total);
            Assert.Equal(lowerCount, measurements.LowerBounds.Count);
            Assert.Equal(lowerTotal, measurements.LowerBounds.Total);
        }
    }

    [Fact]
    public void ShallowType_IsSafe()
    {
        Assert.True(GuardTypeSpec(Nested(SzArray, 5)));
        Assert.True(GuardTypeSpec(Nested(Ptr, 10)));
        Assert.True(GuardTypeSpec([I4]));
    }

    [Fact]
    public void DepthAtLimit_IsSafe_JustOver_IsUnsafe()
    {
        // Depth = number of wrappers + 1 for the leaf. 511 wrappers -> leaf at depth 512 (== limit).
        Assert.True(GuardTypeSpec(Nested(SzArray, 511)));
        Assert.False(GuardTypeSpec(Nested(SzArray, 512)));
    }

    [Fact]
    public void DeepPointerArraySzArray_AreUnsafe()
    {
        Assert.False(GuardTypeSpec(Nested(SzArray, 100_000)));
        Assert.False(GuardTypeSpec(Nested(Ptr, 100_000)));
        Assert.False(GuardTypeSpec(NestedArray(2_000)));
    }

    [Fact]
    public void DeepGenericInstantiationNesting_IsUnsafe()
    {
        // List`1<List`1<... I4 ...>> nested 2000 deep.
        var blob = new List<byte>();
        for (int i = 0; i < 2_000; i++)
        {
            blob.Add(GenericInst);
            blob.Add(Class);
            blob.Add(0x06); // TypeDefOrRefOrSpec coded token (some TypeRef row)
            blob.Add(0x01); // one generic argument follows
        }
        blob.Add(I4);
        Assert.False(GuardTypeSpec(blob.ToArray()));
    }

    [Fact]
    public void SelfModreqCycleShape_IsUnsafe()
    {
        // The shape that bypassed the earlier per-blob cap: 1000 SZARRAY then a modreq + I4. As a
        // single blob its structural depth is ~1001, which the depth guard rejects outright.
        var blob = new List<byte>();
        for (int i = 0; i < 1_000; i++)
            blob.Add(SzArray);
        blob.Add(CmodReqd);
        blob.Add(0x06);
        blob.Add(I4);
        Assert.False(GuardTypeSpec(blob.ToArray()));
    }

    [Fact]
    public void WideButShallowMethodSignature_IsSafe()
    {
        // void M(int, int, ... 5000 params): long blob, but structurally shallow. A length cap
        // would false-reject this; the depth guard must not.
        var sig = new BlobBuilder();
        new BlobEncoder(sig)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(5_000, ret => ret.Void(), pars =>
            {
                for (int i = 0; i < 5_000; i++)
                    pars.AddParameter().Type().Int32();
            });
        Assert.True(GuardMethodSig(sig));
    }

    [Fact]
    public void CompleteMethodSignature_RejectsTruncationAndTrailingBytes()
    {
        Assert.True(GuardCompleteMethodSig([0x00, 0x00, 0x01]));
        Assert.False(GuardCompleteMethodSig([0x00]));
        Assert.False(GuardCompleteMethodSig([0x00, 0x00, 0x01, 0xFF]));

        var truncated = new BlobBuilder();
        truncated.WriteByte(0x00);
        var (reader, handle) = BuildStandaloneSig(truncated);
        Assert.True(
            SignatureBlobGuard.IsSafeToDecode(
                reader,
                reader.GetStandaloneSignature(handle).Signature,
                SignatureBlobGuard.Kind.Method));
    }

    [Fact]
    public void TrailingBytes_AreUnsafe()
    {
        Assert.False(GuardTypeSpec([I4, 0x0e]));

        var method = new BlobBuilder();
        method.WriteByte(0x00);
        method.WriteByte(0x00);
        method.WriteByte(0x01);
        method.WriteByte(I4);
        Assert.False(GuardMethodSig(method));

        var defaultWithSentinel = new BlobBuilder();
        defaultWithSentinel.WriteByte(0x00);
        defaultWithSentinel.WriteByte(0x00);
        defaultWithSentinel.WriteByte(0x01);
        defaultWithSentinel.WriteByte(0x41);
        Assert.False(GuardMethodSig(defaultWithSentinel));
    }

    [Theory]
    [InlineData((int)SignatureCallingConvention.CDecl)]
    [InlineData((int)SignatureCallingConvention.StdCall)]
    [InlineData((int)SignatureCallingConvention.ThisCall)]
    [InlineData((int)SignatureCallingConvention.FastCall)]
    [InlineData((int)SignatureCallingConvention.VarArgs)]
    public void TerminalSentinel_IsUnsafeForEveryCallingConvention(
        int callingConvention)
    {
        var signature = new BlobBuilder();
        signature.WriteByte((byte)callingConvention);
        signature.WriteByte(0x02);
        signature.WriteByte(0x01);
        signature.WriteByte(I4);
        signature.WriteByte(I4);
        signature.WriteByte(0x41);

        Assert.False(GuardMethodSig(signature));
        Assert.False(GuardStandaloneMethodSig(signature));
    }

    [Fact]
    public void MidSignatureSentinel_RequiresApplicableVarArgConventionAndOccursOnce()
    {
        static BlobBuilder Signature(
            SignatureCallingConvention convention,
            bool repeatSentinel)
        {
            var signature = new BlobBuilder();
            signature.WriteByte((byte)convention);
            signature.WriteByte(0x02);
            signature.WriteByte(0x01);
            signature.WriteByte(I4);
            signature.WriteByte(0x41);
            if (repeatSentinel)
                signature.WriteByte(0x41);
            signature.WriteByte(I4);
            return signature;
        }

        Assert.True(GuardMethodSig(
            Signature(
                SignatureCallingConvention.VarArgs,
                repeatSentinel: false)));
        Assert.True(GuardStandaloneMethodSig(
            Signature(
                SignatureCallingConvention.CDecl,
                repeatSentinel: false)));
        Assert.False(GuardMethodSig(
            Signature(
                SignatureCallingConvention.CDecl,
                repeatSentinel: false)));
        Assert.False(GuardMethodSig(
            Signature(
                SignatureCallingConvention.Default,
                repeatSentinel: false)));
        Assert.False(GuardMethodSig(
            Signature(
                SignatureCallingConvention.VarArgs,
                repeatSentinel: true)));
        Assert.False(GuardStandaloneMethodSig(
            Signature(
                SignatureCallingConvention.CDecl,
                repeatSentinel: true)));
    }

    [Fact]
    public void OuterSentinelAfterFunctionPointer_DoesNotBelongToTheNestedMethod()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(
            (byte)SignatureCallingConvention.VarArgs);
        signature.WriteByte(0x02);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1b);
        signature.WriteByte(
            (byte)SignatureCallingConvention.VarArgs);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(I4);
        signature.WriteByte(0x41);
        signature.WriteByte(I4);

        Assert.True(GuardMethodSig(signature));
    }

    [Fact]
    public void NestedMethodCannotConsumeAnOuterTrailingSentinel()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1b);
        signature.WriteByte(
            (byte)SignatureCallingConvention.VarArgs);
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(0x41);

        Assert.False(GuardMethodSig(signature));
    }

    [Fact]
    public void DeeplyNestedMethodParameter_IsUnsafe()
    {
        // void M(int[][]...[]) with a 2000-deep array parameter: shallow arity, deep structure.
        var sig = new BlobBuilder();
        new BlobEncoder(sig)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(1, ret => ret.Void(), pars =>
            {
                var t = pars.AddParameter().Type();
                for (int i = 0; i < 2_000; i++)
                    t = t.SZArray();
                t.Int32();
            });
        Assert.False(GuardMethodSig(sig));
    }

    [Fact]
    public void RealAssembly_HasNoFalsePositives()
    {
        // Every real signature in this test assembly must be accepted.
        using var pe = new PEReader(File.OpenRead(typeof(SignatureBlobGuardTests).Assembly.Location));
        var r = pe.GetMetadataReader();
        int rejected = 0, total = 0;

        foreach (var mh in r.MethodDefinitions)
        {
            var sig = r.GetMethodDefinition(mh).Signature;
            if (sig.IsNil) continue;
            total++;
            if (!SignatureBlobGuard.IsSafeToDecode(r, sig, SignatureBlobGuard.Kind.Method)) rejected++;
        }
        foreach (var fh in r.FieldDefinitions)
        {
            var sig = r.GetFieldDefinition(fh).Signature;
            if (sig.IsNil) continue;
            total++;
            if (!SignatureBlobGuard.IsSafeToDecode(r, sig, SignatureBlobGuard.Kind.Field)) rejected++;
        }

        Assert.True(total > 0);
        Assert.Equal(0, rejected);
    }

    [Fact]
    public void TypeSpecScope_EmptyBlob_DoesNotLeakBudget()
    {
        var (reader, handle) = BuildTypeSpec([]);

        for (int i = 0; i < 300; i++)
        {
            Assert.True(TypeSpecGuard.TryEnter(reader, handle, out var scope));
            scope.Dispose();
            scope.Dispose();
        }
    }

    [Fact]
    public void DeepPrefixChains_AreUnsafe()
    {
        // SRM recurses natively through by-ref, pinned, and custom-modifier prefixes, so a long
        // chain of them must be rejected exactly like a chain of pointers.
        Assert.False(GuardTypeSpec(Nested(ByRef, 1_000)));
        Assert.False(GuardTypeSpec(Nested(0x45 /* PINNED */, 1_000)));

        var cmods = new List<byte>();
        for (int i = 0; i < 1_000; i++)
        {
            cmods.Add(CmodReqd);
            cmods.Add(0x06); // modifier's TypeDefOrRefOrSpec coded token
        }
        cmods.Add(I4);
        Assert.False(GuardTypeSpec(cmods.ToArray()));
    }

    [Fact]
    public void HugeDeclaredCount_IsUnsafe_AndDoesNotAllocate()
    {
        // A tiny FNPTR blob declaring ~536M parameters must be rejected without materializing a
        // work item per declared slot (which would OOM). Bounding the count by the remaining blob
        // length makes this cheap.
        byte[] blob = [FnPtr, 0x00, 0xdf, 0xff, 0xff, 0xff];
        Assert.False(GuardTypeSpec(blob));

        // A method signature declaring a huge parameter count is likewise rejected cheaply.
        var method = new BlobBuilder();
        method.WriteByte(0x00);       // default calling convention
        method.WriteByte(0xdf);       // compressed ~536M param count
        method.WriteByte(0xff);
        method.WriteByte(0xff);
        method.WriteByte(0xff);
        method.WriteByte(0x01);       // (truncated) return type VOID
        var (reader, handle) = BuildStandaloneSig(method);
        Assert.False(SignatureBlobGuard.IsSafeToDecode(reader, reader.GetStandaloneSignature(handle).Signature, SignatureBlobGuard.Kind.Method));
    }

    [Fact]
    public void HugeArrayShapeCount_IsUnsafe()
    {
        // ELEMENT_TYPE_ARRAY I4, rank 1, sizesCount ~536M: SRM pre-allocates a builder from the
        // shape counts before reading elements, so an unbounded count OOMs even from a 7-byte blob.
        byte[] blob = [0x14 /* ARRAY */, I4, 0x01 /* rank */, 0xdf, 0xff, 0xff, 0xff /* sizesCount */];
        Assert.False(GuardTypeSpec(blob));
    }

    // The remaining-bytes check alone bounds one shape against the blob, not the work SRM does:
    // it materializes an array per count while decoding the shape, before TypeNodeProvider can
    // charge anything. A blob that is merely long therefore buys unbounded materialization. These
    // three pin the boundary: the shared MetadataSafetyPolicy.MaxSignatureTypeNodes budget bounds
    // each count and their aggregate, and ordinary wide-but-shallow ranks stay accepted.
    [Fact]
    public void ArrayShapeCountWithinTypeNodeBudget_IsSafe()
        => Assert.True(GuardTypeSpec(ArrayShapeWithSizes(1_000)));

    [Fact]
    public void ArrayShapeCountBeyondTypeNodeBudget_IsUnsafe()
        => Assert.False(GuardTypeSpec(
            ArrayShapeWithSizes(
                MetadataSafetyPolicy.MaxSignatureTypeNodes + 1)));

    [Fact]
    public void AggregateArrayShapeCountsBeyondTypeNodeBudget_IsUnsafe()
    {
        // Each count clears the per-shape byte check and the budget on its own; only their sum
        // exceeds it. Under a per-shape-only bound every one of these would be accepted.
        int each = (MetadataSafetyPolicy.MaxSignatureTypeNodes / 3) + 1;
        Assert.True(GuardTypeSpec(NestedArrayShapesWithSizes(1, each)));
        Assert.False(GuardTypeSpec(NestedArrayShapesWithSizes(3, each)));
    }

    static byte[] ArrayShapeWithSizes(int sizes)
        => NestedArrayShapesWithSizes(1, sizes);

    /// <summary>
    /// <paramref name="depth"/> nested ELEMENT_TYPE_ARRAY wrappers around an I4, each declaring
    /// rank 1, <paramref name="sizes"/> one-byte sizes, and no lower bounds.
    /// </summary>
    static byte[] NestedArrayShapesWithSizes(int depth, int sizes)
    {
        var blob = new List<byte>();
        for (int i = 0; i < depth; i++)
            blob.Add(Array);
        blob.Add(I4);
        for (int i = 0; i < depth; i++)
        {
            blob.Add(0x01); // rank 1
            WriteCompressedUnsigned(blob, sizes);
            for (int size = 0; size < sizes; size++)
                blob.Add(0x00);
            blob.Add(0x00); // 0 lo-bounds
        }
        return blob.ToArray();
    }

    // ECMA-335 II.23.2 compressed unsigned integer.
    static void WriteCompressedUnsigned(List<byte> blob, int value)
    {
        if (value < 0x80)
        {
            blob.Add((byte)value);
        }
        else if (value < 0x4000)
        {
            blob.Add((byte)(0x80 | (value >> 8)));
            blob.Add((byte)value);
        }
        else
        {
            blob.Add((byte)(0xC0 | (value >> 24)));
            blob.Add((byte)(value >> 16));
            blob.Add((byte)(value >> 8));
            blob.Add((byte)value);
        }
    }

    [Theory]
    [InlineData(0x06)]
    [InlineData(0x07)]
    [InlineData(0x0a)]
    public void GenericInstanceFunctionPointerWithNonMethodHeader_IsUnsafe(int rawHeader)
        => Assert.False(GuardTypeSpec([GenericInst, FnPtr, (byte)rawHeader, 0x00]));

    [Fact]
    public void GenericInstanceSmuggledHugeArrayShape_IsUnsafe()
        => Assert.False(GuardTypeSpec(
            [GenericInst, Array, I4, 0x04, 0xdf, 0xff, 0xff, 0xff]));

    [Fact]
    public void GenericInstanceClassArgument_IsSafe()
        => Assert.True(GuardTypeSpec([GenericInst, Class, 0x06, 0x01, I4]));

    [Fact]
    public void StackedGenericInstPrefix_IsUnsafe()
    {
        // GENERICINST GENERICINST CLASS token is not a well-formed
        // II.23.2.12 instantiation. The OneDeeplyNestedTypeSpec bounds
        // fixture uses this prefix; rejecting it is what stops the
        // retained-text amplification.
        var blob = new List<byte>();
        for (int i = 0; i < 8; i++)
            blob.Add(GenericInst);
        blob.Add(Class);
        blob.Add(0x06);
        for (int i = 0; i < 8; i++)
        {
            blob.Add(0x01);
            blob.Add(Class);
            blob.Add(0x06);
        }

        Assert.False(GuardTypeSpec(blob.ToArray()));

        var field = new BlobBuilder();
        field.WriteByte(0x06);
        field.WriteBytes(blob.ToArray());
        var (reader, handle) = BuildStandaloneSig(field);
        Assert.False(
            SignatureBlobGuard.IsSafeToDecode(
                reader,
                reader.GetStandaloneSignature(handle).Signature,
                SignatureBlobGuard.Kind.Field));
    }

    [Fact]
    public void MultiByteTypeCodePointerChain_IsUnsafe()
    {
        // SRM reads type codes as compressed integers, so 0x80 0x0F is PTR.
        // A wide GENERICINST argument count would otherwise let the guard treat
        // each 0x80 as a leaf and miss the nested pointer chain.
        var blob = new List<byte> { GenericInst, Class, 0x06, 0x04 };
        for (int i = 0; i < 3; i++)
        {
            blob.Add(0x80);
            blob.Add(Ptr);
        }
        blob.Add(I4);
        Assert.False(GuardTypeSpec(blob.ToArray()));
    }

    static bool GuardTypeSpec(byte[] typeBlob)
    {
        var (reader, handle) = BuildTypeSpec(typeBlob);
        return SignatureBlobGuard.IsSafeToDecode(reader, reader.GetTypeSpecification(handle).Signature, SignatureBlobGuard.Kind.TypeSpecification);
    }

    static bool GuardMethodSig(BlobBuilder sig)
    {
        var (reader, handle) = BuildStandaloneSig(sig);
        return SignatureBlobGuard.IsSafeToDecode(reader, reader.GetStandaloneSignature(handle).Signature, SignatureBlobGuard.Kind.Method);
    }

    static bool GuardCompleteMethodSig(byte[] signature)
    {
        var blob = new BlobBuilder();
        blob.WriteBytes(signature);
        var (reader, handle) = BuildStandaloneSig(blob);
        return SignatureBlobGuard.IsSafeAndCompleteToDecode(
            reader,
            reader.GetStandaloneSignature(handle).Signature,
            SignatureBlobGuard.Kind.Method);
    }

    static bool GuardStandaloneMethodSig(BlobBuilder sig)
    {
        var (reader, handle) = BuildStandaloneSig(sig);
        return SignatureBlobGuard.IsSafeToDecode(
            reader,
            reader.GetStandaloneSignature(handle).Signature,
            SignatureBlobGuard.Kind.StandaloneMethod);
    }

    static byte[] Nested(byte wrapper, int count)
    {
        var blob = new byte[count + 1];
        for (int i = 0; i < count; i++)
            blob[i] = wrapper;
        blob[count] = I4;
        return blob;
    }

    static byte[] NestedArray(int count)
    {
        var blob = new List<byte>();
        for (int i = 0; i < count; i++)
            blob.Add(Array);
        blob.Add(I4);
        for (int i = 0; i < count; i++)
        {
            blob.Add(0x01); // rank 1
            blob.Add(0x00); // 0 sizes
            blob.Add(0x00); // 0 lo-bounds
        }
        return blob.ToArray();
    }

    static (MetadataReader Reader, TypeSpecificationHandle Handle) BuildTypeSpec(byte[] typeBlob)
    {
        var md = NewModule();
        var bb = new BlobBuilder();
        bb.WriteBytes(typeBlob);
        var handle = md.AddTypeSpecification(md.GetOrAddBlob(bb));
        return (Serialize(md), handle);
    }

    static (MetadataReader Reader, StandaloneSignatureHandle Handle) BuildStandaloneSig(BlobBuilder sig)
    {
        var md = NewModule();
        var handle = md.AddStandaloneSignature(md.GetOrAddBlob(sig));
        return (Serialize(md), handle);
    }

    static MetadataBuilder NewModule()
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("m.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("m"), new Version(1, 0, 0, 0), default, default, default, default);
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        return md;
    }

    static MetadataReader Serialize(MetadataBuilder md)
    {
        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader();
    }
}
