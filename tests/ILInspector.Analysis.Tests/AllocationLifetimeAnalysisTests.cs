using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using DotnetInspector.Fixtures;

namespace ILInspector.Analysis.Tests;

public sealed class AllocationLifetimeAnalysisTests
{
    const string JurassicSha256 =
        "cfcd1b03b23dc061bd1abd05713903e0382ba47249b16c89b86a025fee119ac4";

    [Fact]
    public void CompiledFixture_UsesOneLifetimeVerdictForFactsAndTriage()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence local = Allocation(
            index,
            "ConstructFromLocalChars");
        AllocationOccurrence returned = Allocation(
            index,
            "ReturnLocalChars");

        Assert.Equal(AllocationEscape.LocalOnly, local.Escape);
        Assert.Equal(AllocationEscape.Escapes, returned.Escape);
        Assert.Equal(
            AllocationEscapeKind.Return,
            returned.EscapeKind);
        Assert.Contains(
            local.LifetimeEvidence.Uses,
            use => use.Kind
                == AllocationLifetimeUseKind
                    .TrustedNonCapturingCall);
        Assert.Equal(
            4,
            local.LifetimeEvidence.Uses.Count(
                use => use.Kind
                    == AllocationLifetimeUseKind.ElementWrite));
        Assert.Empty(local.LifetimeEvidence.Limitations);
        Assert.Contains(
            returned.LifetimeEvidence.Uses,
            use => use.Kind
                == AllocationLifetimeUseKind.Return);
        Assert.Empty(returned.LifetimeEvidence.Limitations);
        Assert.Contains(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "ConstructFromLocalChars"
                && candidate.Shape
                    == "stackalloc-candidate"
                && candidate.ILOffset == local.ILOffset);
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name == "ReturnLocalChars"
                && candidate.Shape
                    == "stackalloc-candidate");

        OptimizationOpportunity inLoop = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "AllocateInsideLoop"
                && candidate.Shape is
                    "small-array"
                    or "stackalloc-candidate");
        Assert.Equal("small-array", inLoop.Shape);
        Assert.Contains(
            "inside a loop",
            inLoop.Caveat,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledFixture_RequiresCoreLibraryPrimitiveIdentity()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence primitive = Allocation(
            index,
            "GenuinePrimitiveStaysLocal");
        AllocationOccurrence lookalike = Allocation(
            index,
            "PrimitiveLookalikeStaysLocal");

        Assert.Equal(AllocationEscape.LocalOnly, primitive.Escape);
        Assert.Equal(AllocationEscape.LocalOnly, lookalike.Escape);
        Assert.Contains(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "GenuinePrimitiveStaysLocal"
                && candidate.Shape
                    == "stackalloc-candidate"
                && candidate.ILOffset
                    == primitive.ILOffset);
        OptimizationOpportunity rejected = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "PrimitiveLookalikeStaysLocal"
                && candidate.ILOffset
                    == lookalike.ILOffset);
        Assert.Equal("small-array", rejected.Shape);
        Assert.Contains(
            "element type",
            rejected.Caveat,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledFixture_TracksByReferenceConsumer()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence local = Allocation(
            index,
            "ReadThroughRefLocal");
        AllocationOccurrence transferred = Allocation(
            index,
            "PassArrayByReference");

        Assert.Equal(AllocationEscape.LocalOnly, local.Escape);
        Assert.Equal(
            AllocationLifetimeUseKind.LengthRead,
            Assert.Single(local.LifetimeEvidence.Uses).Kind);
        Assert.Empty(local.LifetimeEvidence.Limitations);

        Assert.Equal(
            AllocationEscape.Escapes,
            transferred.Escape);
        Assert.Equal(
            AllocationLifetimeUseKind.ByReferenceTransfer,
            Assert.Single(transferred.LifetimeEvidence.Uses)
                .Kind);
        Assert.Empty(
            transferred.LifetimeEvidence.Limitations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataBackedObjectReadMatchesTokenType(
        bool valueType)
    {
        ImmutableArray<byte> image =
            ManagedObjectReadImage(valueType);
        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            "ManagedObjectRead.dll",
            image,
            LibraryBodyAnalysisFeatures.Allocations);
        MethodIdentity method = Assert.Single(
            index.Methods,
            candidate => candidate.Name == "ReadLength");
        AllocationOccurrence allocation = Assert.Single(
            index.GetAllocationOccurrences()[
                method.MetadataToken],
            occurrence =>
                occurrence.Kind == AllocationKind.Array);

        Assert.Equal(
            AllocationEscape.LocalOnly,
            allocation.Escape);
        Assert.Equal(
            new AllocationLifetimeUse(
                14,
                AllocationLifetimeUseKind.LengthRead),
            Assert.Single(
                allocation.LifetimeEvidence.Uses));
        Assert.Empty(
            allocation.LifetimeEvidence.Limitations);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Jurassic_LocalSurrogateArrayBecomesStackallocCandidate()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "AllocationLifetime",
            "Jurassic.dll");
        using (FileStream stream = File.OpenRead(path))
        {
            Assert.Equal(
                JurassicSha256,
                Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        var index = LibraryBodyIndex.Open(path);
        MethodIdentity method = Assert.Single(
            index.Methods,
            candidate =>
                candidate.DeclaringType
                    .ToQualifiedDisplayString()
                    == "Jurassic.Compiler.Lexer"
                && candidate.Name
                    == "ReadExtendedUnicodeSequence");
        AllocationOccurrence allocation = Assert.Single(
            index.GetAllocationOccurrences()[
                method.MetadataToken],
            occurrence =>
                occurrence.Kind == AllocationKind.Array
                && occurrence.ILOffset == 0x00cf);

        Assert.Equal(AllocationEscape.LocalOnly, allocation.Escape);
        Assert.Contains(
            new AllocationLifetimeUse(
                0x0102,
                AllocationLifetimeUseKind
                    .TrustedNonCapturingCall),
            allocation.LifetimeEvidence.Uses);
        Assert.Empty(allocation.LifetimeEvidence.Limitations);
        OptimizationOpportunity opportunity = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.MetadataToken
                    == method.MetadataToken
                && candidate.ILOffset
                    == allocation.ILOffset
                && candidate.Shape is
                    "small-array"
                    or "stackalloc-candidate");
        Assert.Equal(
            "stackalloc-candidate",
            opportunity.Shape);
        Assert.False(opportunity.InLoop);
        Assert.Null(opportunity.Caveat);
    }

    static AllocationOccurrence Allocation(
        LibraryBodyIndex index,
        string methodName)
    {
        MethodIdentity method = Assert.Single(
            index.Methods,
            candidate =>
                candidate.DeclaringType.Name
                    == "AllocationLifetimeSamples"
                && candidate.Name == methodName);
        return Assert.Single(
            index.GetAllocationOccurrences()[
                method.MetadataToken],
            occurrence =>
                occurrence.Kind == AllocationKind.Array);
    }

    static ImmutableArray<byte> ManagedObjectReadImage(
        bool valueType)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "ManagedObjectRead.dll"),
            metadata.GetOrAddGuid(
                new Guid(
                    "43d86b6b-84a0-47a3-9d74-3aa879467c6c")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "ManagedObjectRead"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);

        AssemblyName coreAssembly =
            typeof(object).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    coreAssembly.Name!),
                coreAssembly.Version!,
                default,
                metadata.GetOrAddBlob(
                    coreAssembly.GetPublicKeyToken()!),
                default,
                default);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle valueTypeBase =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("ValueType"));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Holder"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle elementType =
            metadata.AddTypeDefinition(
                valueType
                    ? TypeAttributes.Public
                        | TypeAttributes.Sealed
                        | TypeAttributes.SequentialLayout
                    : TypeAttributes.Public,
                metadata.GetOrAddString("Fixtures"),
                metadata.GetOrAddString(
                    valueType ? "Value" : "Item"),
                valueType ? valueTypeBase : objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        int codedElement =
            MetadataTokens.GetRowNumber(elementType) << 2;
        var arraySignature = new BlobBuilder();
        arraySignature.WriteByte(0x1D);
        arraySignature.WriteByte(
            valueType ? (byte)0x11 : (byte)0x12);
        arraySignature.WriteCompressedInteger(
            codedElement);
        TypeSpecificationHandle arrayType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(arraySignature));

        var localSignature = new BlobBuilder();
        localSignature.WriteByte(0x07);
        localSignature.WriteByte(0x01);
        localSignature.WriteByte(0x1D);
        localSignature.WriteByte(
            valueType ? (byte)0x11 : (byte)0x12);
        localSignature.WriteCompressedInteger(
            codedElement);
        StandaloneSignatureHandle locals =
            metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(localSignature));

        var il = new BlobBuilder();
        il.WriteByte((byte)ILOpCode.Ldc_i4_1);
        il.WriteByte((byte)ILOpCode.Newarr);
        il.WriteInt32(
            MetadataTokens.GetToken(elementType));
        il.WriteByte((byte)ILOpCode.Stloc_0);
        il.WriteByte((byte)ILOpCode.Ldloca_s);
        il.WriteByte(0);
        il.WriteByte((byte)ILOpCode.Ldobj);
        il.WriteInt32(
            MetadataTokens.GetToken(arrayType));
        il.WriteByte((byte)ILOpCode.Ldlen);
        il.WriteByte((byte)ILOpCode.Pop);
        il.WriteByte((byte)ILOpCode.Ret);
        var bodies = new BlobBuilder();
        int body = new MethodBodyStreamEncoder(bodies)
            .AddMethodBody(
                new InstructionEncoder(il),
                maxStack: 1,
                localVariablesSignature: locals,
                attributes:
                    MethodBodyAttributes.InitLocals);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadLength"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            body,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToImmutableArray();
    }
}
