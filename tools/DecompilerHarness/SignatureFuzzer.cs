using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

// Malformed-metadata signature fuzzer (#2499). It generates adversarial signature blobs — deep
// nesting, wide/huge declared counts, prefix chains, truncation, random bytes — and drives each
// through SignatureBlobGuard and (when the guard reports it safe) a real SRM signature decode.
//
// The guard's contract is: IsSafeToDecode == true implies SRM can decode the blob without an
// *uncatchable* StackOverflowException or an unbounded pre-allocation. A generated blob that
// violates that contract aborts the process — which is the fuzzing signal. Each blob is logged
// (kind, seed, iteration, hex) *before* it is decoded, so the last line before an abort is the
// reproducer. The run is deterministic given --seed, and finishing all iterations is the pass.
//
// The decode uses a trivial provider on purpose: the StackOverflow (native DecodeType recursion)
// and the count-driven pre-allocation (e.g. array-shape sizes) both happen inside SRM's decoder,
// independent of the provider, so this stays self-contained (no dependency on the product decoders).
internal static class SignatureFuzzer
{
    public static int Run(int iterations, int seed, int? logEvery, bool unguarded = false)
    {
        Console.Error.WriteLine($"signature fuzzer: iterations={iterations} seed={seed}{(unguarded ? " UNGUARDED (positive control — expected to crash)" : "")}");
        var rng = new Random(seed);

        long guardSafe = 0, guardRejected = 0, decodeThrew = 0;
        var provider = new FuzzProvider();

        for (int i = 0; i < iterations; i++)
        {
            var kind = (SignatureBlobGuard.Kind)rng.Next(0, 5);
            byte[] blob = GenerateSignature(rng, kind);

            // Log before every decode so a process abort leaves the culprit as the last line.
            if (logEvery is { } n && n > 0 && i % n == 0)
                Console.Error.WriteLine($"[{i}] kind={kind} len={blob.Length} {Convert.ToHexString(blob.AsSpan(0, Math.Min(blob.Length, 48)))}");

            MetadataReader reader;
            BlobHandle sig;
            try
            {
                (reader, sig) = BuildImage(kind, blob);
            }
            catch
            {
                // A blob the metadata writer itself rejects is not interesting; skip it.
                continue;
            }

            if (!unguarded)
            {
                bool safe;
                try
                {
                    safe = SignatureBlobGuard.IsSafeToDecode(reader, sig, kind);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"GUARD THREW on kind={kind} len={blob.Length} {Convert.ToHexString(blob)}: {ex.GetType().Name}");
                    return 1;
                }

                if (!safe)
                {
                    guardRejected++;
                    continue;
                }
                guardSafe++;
            }

            // The guard promised this is safe: a real decode must terminate. If it StackOverflows or
            // OOMs, the process aborts and the logged blob above is the reproducer. In --fuzz-unguarded
            // mode the guard is skipped, so this is the positive control that demonstrates the guard is
            // necessary — it is expected to abort on a deep/huge blob.
            try
            {
                DecodeForContract(reader, sig, kind, provider);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or NotSupportedException or OverflowException)
            {
                // A catchable malformed-signature exception is fine — the guard only promises no
                // *uncatchable* crash, not that every "safe" blob is well-formed.
                decodeThrew++;
            }
        }

        Console.Error.WriteLine($"signature fuzzer: OK — {iterations} iterations, guard-safe={guardSafe} guard-rejected={guardRejected} decode-threw={decodeThrew}");
        return 0;
    }

    static void DecodeForContract(MetadataReader reader, BlobHandle sig, SignatureBlobGuard.Kind kind, FuzzProvider provider)
    {
        var decoder = new SignatureDecoder<int, object?>(provider, reader, null);
        var blob = reader.GetBlobReader(sig);
        switch (kind)
        {
            case SignatureBlobGuard.Kind.TypeSpecification:
                decoder.DecodeType(ref blob);
                break;
            case SignatureBlobGuard.Kind.Field:
                decoder.DecodeFieldSignature(ref blob);
                break;
            case SignatureBlobGuard.Kind.LocalVariables:
                decoder.DecodeLocalSignature(ref blob);
                break;
            case SignatureBlobGuard.Kind.MethodSpecification:
                decoder.DecodeMethodSpecificationSignature(ref blob);
                break;
            default:
                decoder.DecodeMethodSignature(ref blob);
                break;
        }
    }

    // --- Metadata image: one TypeSpec or StandaloneSignature per generated blob ---

    static (MetadataReader Reader, BlobHandle Signature) BuildImage(SignatureBlobGuard.Kind kind, byte[] blob)
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("f.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("f"), new Version(1, 0, 0, 0), default, default, default, default);
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var bb = new BlobBuilder();
        bb.WriteBytes(blob);
        BlobHandle blobHandle = md.GetOrAddBlob(bb);
        EntityHandle handle = kind == SignatureBlobGuard.Kind.TypeSpecification
            ? md.AddTypeSpecification(blobHandle)
            : md.AddStandaloneSignature(blobHandle);

        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        var reader = MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader();

        BlobHandle signature = kind == SignatureBlobGuard.Kind.TypeSpecification
            ? reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature
            : reader.GetStandaloneSignature((StandaloneSignatureHandle)handle).Signature;
        return (reader, signature);
    }

    // --- Adversarial signature generation ---

    static byte[] GenerateSignature(Random rng, SignatureBlobGuard.Kind kind)
    {
        var type = GenerateType(rng);
        var blob = new List<byte>();
        switch (kind)
        {
            case SignatureBlobGuard.Kind.TypeSpecification:
                blob.AddRange(type);
                break;
            case SignatureBlobGuard.Kind.Field:
                blob.Add(0x06); // FIELD header
                blob.AddRange(type);
                break;
            case SignatureBlobGuard.Kind.LocalVariables:
                blob.Add(0x07); // LOCAL_SIG header
                WriteCount(rng, blob);
                blob.AddRange(type);
                break;
            case SignatureBlobGuard.Kind.MethodSpecification:
                blob.Add(0x0a); // GENRICINST header
                WriteCount(rng, blob);
                blob.AddRange(type);
                break;
            default: // Method
                blob.Add((byte)rng.Next(0, 0x40)); // calling convention (occasionally IsGeneric etc.)
                WriteCount(rng, blob); // param count
                blob.Add(0x01);        // return type VOID
                blob.AddRange(type);   // one parameter
                break;
        }
        return [.. blob];
    }

    // A count that is sometimes valid-small, sometimes a huge compressed integer (amplification).
    static void WriteCount(Random rng, List<byte> blob)
    {
        switch (rng.Next(0, 4))
        {
            case 0: blob.Add(0x01); break;                                   // 1
            case 1: blob.Add((byte)rng.Next(0, 0x7f)); break;               // small
            case 2: blob.AddRange([0xdf, 0xff, 0xff, 0xff]); break;          // ~536M (huge)
            default: blob.AddRange([0x81, (byte)rng.Next(0, 256)]); break;  // 2-byte compressed
        }
    }

    // Emits a Type blob. Iterative (no generator-side recursion), with adversarial strategies —
    // including *recursively* nested shapes (deep inside FNPTR return / GENERICINST arg / ARRAY
    // element), emitted as a flat byte sequence so the generator's own stack is never at risk.
    static List<byte> GenerateType(Random rng)
    {
        var blob = new List<byte>();
        switch (rng.Next(0, 11))
        {
            case 0: // deep wrapper chain (StackOverflow probe)
            {
                byte wrapper = PickWrapper(rng);
                int depth = rng.Next(1, 200_000);
                for (int i = 0; i < depth; i++)
                {
                    blob.Add(wrapper);
                    if (wrapper is 0x1f or 0x20) // CMOD_REQD/OPT carry a token
                        blob.Add(0x06);
                }
                blob.Add(0x08); // I4 leaf
                break;
            }
            case 1: // GENERICINST with a huge or wide arg count
                blob.Add(0x15);
                blob.Add(0x12); // CLASS
                blob.Add(0x06); // generic type token
                WriteCount(rng, blob);
                blob.Add(0x08); // one concrete arg follows (rest are "declared but absent")
                break;
            case 2: // general ARRAY with adversarial shape counts
                blob.Add(0x14);
                blob.Add(0x08); // element I4
                blob.Add((byte)rng.Next(1, 4)); // rank
                WriteCount(rng, blob);          // numSizes (sometimes huge)
                WriteCount(rng, blob);          // numLoBounds (sometimes huge)
                break;
            case 3: // FNPTR with adversarial nested method sig
                blob.Add(0x1b);
                blob.Add(0x00);
                WriteCount(rng, blob);
                blob.Add(0x01);
                break;
            case 4: // wide GENERICINST with many real 1-byte args
            {
                blob.Add(0x15);
                blob.Add(0x12);
                blob.Add(0x06);
                int args = rng.Next(1, 100_000);
                WriteCompressed(blob, args);
                for (int i = 0; i < args; i++)
                    blob.Add(0x08);
                break;
            }
            case 5: // truncated: a valid prefix then nothing
                blob.Add(PickWrapper(rng));
                break;
            case 6: // random bytes
            {
                int n = rng.Next(1, 4_000);
                for (int i = 0; i < n; i++)
                    blob.Add((byte)rng.Next(0, 256));
                break;
            }
            case 7: // deep FNPTR-return chain: FNPTR() -> FNPTR() -> ... -> I4
            {
                int depth = rng.Next(1, 60_000);
                for (int i = 0; i < depth; i++)
                {
                    blob.Add(0x1b); // FNPTR
                    blob.Add(0x00); // default calling convention
                    blob.Add(0x00); // 0 parameters; the return type continues the chain
                }
                blob.Add(0x08); // I4 return type at the bottom
                break;
            }
            case 8: // deep GENERICINST single-arg chain: G<G<...<I4>>>
            {
                int depth = rng.Next(1, 60_000);
                for (int i = 0; i < depth; i++)
                {
                    blob.Add(0x15); // GENERICINST
                    blob.Add(0x12); // CLASS
                    blob.Add(0x06); // generic type token
                    blob.Add(0x01); // one argument, which continues the chain
                }
                blob.Add(0x08);
                break;
            }
            case 9: // deep ARRAY-element chain: I4[]...[] (shapes trail in the blob)
            {
                int depth = rng.Next(1, 60_000);
                for (int i = 0; i < depth; i++)
                    blob.Add(0x14); // ARRAY
                blob.Add(0x08);     // innermost element I4
                for (int i = 0; i < depth; i++)
                {
                    blob.Add(0x01); // rank 1
                    blob.Add(0x00); // 0 sizes
                    blob.Add(0x00); // 0 lower bounds
                }
                break;
            }
            default: // a simple leaf
                blob.Add((byte)new byte[] { 0x08, 0x0e, 0x1c, 0x02, 0x18, 0x11, 0x12 }[rng.Next(0, 7)]);
                if (blob[^1] is 0x11 or 0x12) // VALUETYPE/CLASS carry a token
                    blob.Add(0x06);
                break;
        }
        return blob;
    }

    static byte PickWrapper(Random rng)
        => new byte[] { 0x0f /*PTR*/, 0x1d /*SZARRAY*/, 0x10 /*BYREF*/, 0x45 /*PINNED*/, 0x1f /*CMOD_REQD*/, 0x20 /*CMOD_OPT*/ }[rng.Next(0, 6)];

    static void WriteCompressed(List<byte> blob, int value)
    {
        if (value < 0x80)
            blob.Add((byte)value);
        else if (value < 0x4000)
        {
            blob.Add((byte)(0x80 | (value >> 8)));
            blob.Add((byte)value);
        }
        else
        {
            blob.Add((byte)(0xc0 | (value >> 24)));
            blob.Add((byte)(value >> 16));
            blob.Add((byte)(value >> 8));
            blob.Add((byte)value);
        }
    }

    // Trivial provider: the SO / pre-allocation both live in SRM's decoder, so results are irrelevant.
    sealed class FuzzProvider : ISignatureTypeProvider<int, object?>
    {
        public int GetPrimitiveType(PrimitiveTypeCode typeCode) => 0;
        public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => 0;
        public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => 0;
        public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => 0;
        public int GetSZArrayType(int elementType) => 0;
        public int GetArrayType(int elementType, ArrayShape shape) => 0;
        public int GetByReferenceType(int elementType) => 0;
        public int GetPointerType(int elementType) => 0;
        public int GetPinnedType(int elementType) => 0;
        public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments) => 0;
        public int GetGenericTypeParameter(object? genericContext, int index) => 0;
        public int GetGenericMethodParameter(object? genericContext, int index) => 0;
        public int GetFunctionPointerType(MethodSignature<int> signature) => 0;
        public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => 0;
    }
}
