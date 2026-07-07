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
