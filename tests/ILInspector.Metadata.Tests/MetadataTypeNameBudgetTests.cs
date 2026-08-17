using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The structured type-name identity carries an artifact-authored namespace and up to
/// <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/> artifact-authored segments, each of
/// unbounded length. These tests pin the cumulative character budget that bounds the aggregate,
/// and the linear flattening that the identity owner — not each consumer — performs.
/// </summary>
public class MetadataTypeNameBudgetTests
{
    [Fact]
    public void NameAtTheCharacterBudget_IsAccepted()
    {
        // namespace + one delimiter per segment + segment text == the budget exactly.
        const string ns = "N";
        int budget = MetadataSafetyPolicy.MaxTypeNameCharacters;
        var segments = ImmutableArray.Create(new string('a', budget - ns.Length - 1));

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(ns, segments);

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(result);
        Assert.Equal(segments[0], valid.Name.ToNestedMetadataName());
    }

    [Fact]
    public void NameOneCharacterOverTheBudget_IsRejectedWithTypedEvidence()
    {
        const string ns = "N";
        int budget = MetadataSafetyPolicy.MaxTypeNameCharacters;
        var segments = ImmutableArray.Create(new string('a', budget - ns.Length));

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(ns, segments);

        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(result);
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            rejected.Rejection.Kind);
        Assert.Equal(0, rejected.Rejection.SegmentIndex);
    }

    [Fact]
    public void ManySegmentsWithinTheNodeBudget_AreRejectedOnAggregateSize()
    {
        // Every individual segment is ordinary and the segment count is inside the relationship
        // node budget; only the aggregate is absurd. The node budget alone would accept this.
        ImmutableArray<string> segments =
        [
            .. Enumerable
                .Range(0, MetadataSafetyPolicy.MaxRelationshipNodes)
                .Select(index => new string('s', 64)),
        ];

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create("N", segments);

        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(result);
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            rejected.Rejection.Kind);

        // Refused before the remaining segments were measured, and long before any flattened
        // spelling could be built.
        Assert.NotNull(rejected.Rejection.SegmentIndex);
        Assert.True(rejected.Rejection.SegmentIndex < segments.Length - 1);
    }

    [Fact]
    public void DeepNestedName_FlattensToTheExactMetadataSpelling()
    {
        ImmutableArray<string> segments =
        [
            .. Enumerable.Range(0, 32).Select(index => $"Level{index}`1"),
        ];

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Deep.Space", segments));

        Assert.Equal(string.Join('+', segments), valid.Name.ToNestedMetadataName());
        Assert.Equal(
            $"Deep.Space.{string.Join('+', segments)}",
            valid.Name.ToEscapedFullName());
    }

    [Fact]
    public void FlattenedSpellingAndEscapedIdentityRemainDistinct()
    {
        var nested = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Outer", "Inner"])).Name;
        var literalPlus = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Outer+Inner"])).Name;

        // The flattened metadata spelling is ambiguous by construction; the escaped identity is
        // not, which is why identity keys use the latter.
        Assert.Equal(nested.ToNestedMetadataName(), literalPlus.ToNestedMetadataName());
        Assert.NotEqual(nested.ToEscapedFullName(), literalPlus.ToEscapedFullName());
        Assert.Equal(@"N.Outer\+Inner", literalPlus.ToEscapedFullName());
    }

    [Fact]
    public void SharedOversizeHeapString_IsRejectedBeforeAggregateMaterialization()
    {
        string oversize = new string('A', 64 * 1024);
        TypeDefinitionHandle leaf = default;
        using var image = BuildNestedDefinitions(
            MetadataSafetyPolicy.MaxRelationshipNodes,
            oversize,
            sharedName: true,
            out leaf);

        _ = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            image.Reader,
            leaf);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = TypeResolver.ResolveTypeNameFromDefinition(image.Reader, leaf);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var rejected = Assert.IsType<RelationshipTraversalResult<string>.Rejected>(result);
        Assert.Equal(RelationshipTraversalRejectionKind.NameBudget, rejected.Rejection.Kind);
        Assert.True(
            allocated < 256 * 1024,
            $"Allocated {allocated} bytes concatenating a shared {oversize.Length}-character name.");
    }

    [Fact]
    public void CjkNameUnderTheCharacterBudget_IsAccepted()
    {
        // 2,000 BMP ideographs are 6,000 UTF-8 bytes and 2,000 UTF-16 units.
        // Encoded preflight at 4,096 bytes would have rejected a legal name.
        string name = new string('\u4E00', 2_000);
        TypeDefinitionHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = AddTypeDefinition(metadata, TypeAttributes.Public, "", name);
        });

        Assert.Equal(
            name,
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, handle)
                .GetValueOrThrow());
        var read = Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, handle));
        Assert.Equal(name, read.Name.Segments[0]);
    }

    [Fact]
    public void ManySmallSegments_AreRejectedOnAggregateEncodedLength()
    {
        const string small = "SmallSegmentName20!";
        TypeDefinitionHandle leaf = default;
        using var image = BuildNestedDefinitions(
            MetadataSafetyPolicy.MaxRelationshipNodes,
            small,
            sharedName: true,
            out leaf);

        var result = TypeResolver.ResolveTypeNameFromDefinition(image.Reader, leaf);

        var rejected = Assert.IsType<RelationshipTraversalResult<string>.Rejected>(result);
        Assert.Equal(RelationshipTraversalRejectionKind.NameBudget, rejected.Rejection.Kind);
        Assert.True(rejected.Rejection.ConsumedNodes < MetadataSafetyPolicy.MaxRelationshipNodes);
    }

    [Fact]
    public void LeafAppendOverBudget_IsRejectedBeforeLeafMaterialization()
    {
        string declaring = new string('D', MetadataSafetyPolicy.MaxTypeNameCharacters - 8);
        string leafName = new string('L', 64 * 1024);
        TypeDefinitionHandle declaringHandle = default;
        TypeDefinitionHandle leaf = default;
        using var image = BuildMetadata(metadata =>
        {
            declaringHandle = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                declaring);
            leaf = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                leafName);
            metadata.AddNestedType(leaf, declaringHandle);
        });

        TypeDefinition leafDef = image.Reader.GetTypeDefinition(leaf);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = TypeResolver.ResolveFullName(image.Reader, leafDef);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var rejected = Assert.IsType<RelationshipTraversalResult<string>.Rejected>(result);
        Assert.Equal(RelationshipTraversalRejectionKind.NameBudget, rejected.Rejection.Kind);
        Assert.True(
            allocated < 256 * 1024,
            $"Allocated {allocated} bytes appending a {leafName.Length}-character leaf.");
    }

    [Fact]
    public void StructuredRead_ReportsNameBudgetNotMalformed()
    {
        string oversize = new string('S', 64 * 1024);
        TypeDefinitionHandle leaf = default;
        using var image = BuildNestedDefinitions(2, oversize, sharedName: true, out leaf);

        var result = MetadataTypeDefinitionNameReader.Read(image.Reader, leaf);

        var rejected = Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(result);
        Assert.Equal(MetadataTypeNameFailureMechanism.Relationship, rejected.Failure.Mechanism);
        Assert.Equal(
            RelationshipTraversalRejectionKind.NameBudget,
            rejected.Failure.RelationshipKind);
        Assert.NotEqual("MalformedMetadata", rejected.Failure.Kind);
    }

    [Fact]
    public void TypeRefAndExportedType_ShareTheSameBudget()
    {
        string oversize = new string('R', 64 * 1024);
        TypeReferenceHandle typeRef = default;
        ExportedTypeHandle exported = default;
        using var image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly = AddAssemblyReference(metadata);
            typeRef = metadata.AddTypeReference(
                assembly,
                default,
                metadata.GetOrAddString(oversize));
            exported = metadata.AddExportedType(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString(oversize),
                assembly,
                typeDefinitionId: 0);
        });

        var typeRefResult = TypeResolver.ResolveTypeNameFromReference(image.Reader, typeRef);
        var exportedResult = TypeResolver.ResolveTypeNameFromExportedType(image.Reader, exported);
        var typeRefRead = MetadataTypeDefinitionNameReader.Read(image.Reader, typeRef);
        var exportedRead = MetadataTypeDefinitionNameReader.Read(image.Reader, exported);

        AssertNameBudget(typeRefResult);
        AssertNameBudget(exportedResult);
        Assert.Equal(
            RelationshipTraversalRejectionKind.NameBudget,
            Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(typeRefRead)
                .Failure.RelationshipKind);
        Assert.Equal(
            RelationshipTraversalRejectionKind.NameBudget,
            Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(exportedRead)
                .Failure.RelationshipKind);
    }

    [Fact]
    public void ProjectedVirtualStringLength_IsRecheckedAfterBlobReader()
    {
        // SRM may materialize a projected virtual string while answering
        // GetBlobReader.Length. The same Length-then-decode admission must
        // still refuse the part and must not treat the materialized value as
        // success-shaped output.
        string oversize = new string('V', 64 * 1024);
        TypeDefinitionHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = AddTypeDefinition(metadata, TypeAttributes.Public, "", oversize);
        });

        TypeDefinition definition = image.Reader.GetTypeDefinition(handle);
        int blobLength = image.Reader.GetBlobReader(definition.Name).Length;
        Assert.True(blobLength > MetadataSafetyPolicy.MaxTypeNameCharacters);

        var result = TypeResolver.ResolveTypeNameFromDefinition(image.Reader, handle);
        AssertNameBudget(result);
        Assert.Throws<BadImageFormatException>(
            () => TypeResolver.GetTypeNameFromDefinition(image.Reader, handle));
    }

    [Fact]
    public void ExactBudgetNameFromMetadata_IsAccepted()
    {
        string name = new string(
            'E',
            MetadataSafetyPolicy.MaxTypeNameCharacters - 1);
        TypeDefinitionHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = AddTypeDefinition(metadata, TypeAttributes.Public, "", name);
        });

        Assert.Equal(
            name,
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, handle)
                .GetValueOrThrow());
        var read = Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, handle));
        Assert.Equal(name, read.Name.Segments[0]);
    }

    static void AssertNameBudget(RelationshipTraversalResult<string> result)
    {
        var rejected = Assert.IsType<RelationshipTraversalResult<string>.Rejected>(result);
        Assert.Equal(RelationshipTraversalRejectionKind.NameBudget, rejected.Rejection.Kind);
    }

    static MetadataImage BuildNestedDefinitions(
        int depth,
        string name,
        bool sharedName,
        out TypeDefinitionHandle leaf)
    {
        TypeDefinitionHandle localLeaf = default;
        var image = BuildMetadata(metadata =>
        {
            StringHandle shared = metadata.GetOrAddString(name);
            TypeDefinitionHandle parent = default;
            for (int i = 0; i < depth; i++)
            {
                StringHandle nameHandle = sharedName
                    ? shared
                    : metadata.GetOrAddString(name);
                TypeDefinitionHandle current = metadata.AddTypeDefinition(
                    i == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                    default,
                    nameHandle,
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1));
                if (i > 0)
                    metadata.AddNestedType(current, parent);
                parent = current;
                localLeaf = current;
            }
        });
        leaf = localLeaf;
        return image;
    }

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string ns,
        string name)
        => metadata.AddTypeDefinition(
            attributes,
            ns.Length == 0 ? default : metadata.GetOrAddString(ns),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static AssemblyReferenceHandle AddAssemblyReference(MetadataBuilder metadata)
        => metadata.AddAssemblyReference(
            metadata.GetOrAddString("Reference"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

    static MetadataImage BuildMetadata(Action<MetadataBuilder> addRows)
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
        AddTypeDefinition(metadata, default, "", "<Module>");
        addRows(metadata);

        var rootBuilder = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        rootBuilder.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return new MetadataImage(image.ToImmutableArray());
    }

    sealed class MetadataImage(ImmutableArray<byte> image) : IDisposable
    {
        readonly MetadataReaderProvider provider =
            MetadataReaderProvider.FromMetadataImage(image);

        public MetadataReader Reader => provider.GetMetadataReader();

        public void Dispose() => provider.Dispose();
    }
}
