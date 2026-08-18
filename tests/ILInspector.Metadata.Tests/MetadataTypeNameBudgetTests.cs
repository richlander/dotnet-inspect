using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
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
    public void GlobalNamespaceNestedNameAtTheCharacterBudget_IsAccepted()
    {
        // The policy reserves one root delimiter plus the visible nested
        // separator, so the flattened spelling is one character below the cap.
        ImmutableArray<string> segments =
        [
            new string('a', 2048),
            new string('b', 2046),
        ];

        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("", segments));

        Assert.Equal(
            MetadataSafetyPolicy.MaxTypeNameCharacters - 1,
            valid.Name.ToNestedMetadataName().Length);
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
    public void SegmentCountIsBoundedAtTheRelationshipNodeLimit()
    {
        ImmutableArray<string> atLimit =
        [
            .. Enumerable.Range(
                0,
                MetadataSafetyPolicy.MaxRelationshipNodes)
                .Select(static index => index.ToString()),
        ];
        ImmutableArray<string> overLimit = [.. atLimit, "overflow"];

        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", atLimit));
        var rejected =
            Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
                MetadataTypeDefinitionName.Create("N", overLimit));
        Assert.Equal(
            MetadataTypeNameRejectionKind.TooManySegments,
            rejected.Rejection.Kind);
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
        // A heap #Strings handle is not a WinRT projection. The gate below
        // uses a real Managed Windows Metadata projection so GetBlobReader
        // can answer a virtual handle whose Length is larger than the raw
        // heap entry.
        const int rawLength = 64 * 1024;
        TypeDefinitionHandle projectedType = default;
        StringHandle rawName = default;
        using var image = BuildMetadata(
            metadata =>
            {
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("mscorlib"),
                    new Version(4, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
                AssemblyReferenceHandle assembly = AddAssemblyReference(metadata);
                TypeReferenceHandle baseType = metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("Base"));
                rawName = metadata.GetOrAddString(new string('V', rawLength));
                projectedType = metadata.AddTypeDefinition(
                    TypeAttributes.Public | TypeAttributes.WindowsRuntime,
                    metadata.GetOrAddString("Samples"),
                    rawName,
                    baseType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            },
            countingDecoder: true,
            metadataVersion: "WindowsRuntime 1.4;CLR v4.0.30319");

        Assert.Equal(MetadataKind.ManagedWindowsMetadata, image.Reader.MetadataKind);
        StringHandle projectedName = image.Reader.GetTypeDefinition(projectedType).Name;
        Assert.NotEqual(rawName, projectedName);
        int projectedLength = image.Reader.GetBlobReader(projectedName).Length;
        Assert.True(projectedLength > rawLength);

        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, projectedType));
        Assert.DoesNotContain(projectedLength, image.Decoder!.DecodedByteCounts);
        Assert.Throws<BadImageFormatException>(
            () => TypeResolver.GetTypeNameFromDefinition(image.Reader, projectedType));
    }

    [Fact]
    public void DisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap()
    {
        // Queries classifiers need the display spelling of a 5,030-character
        // logging/AI fixture name; structured Read/Resolve still refuse it.
        string name = new string('A', 5_030);
        TypeDefinitionHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = AddTypeDefinition(metadata, TypeAttributes.Public, "", name);
        });

        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, handle));
        Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, handle));
        Assert.Equal(name, TypeResolver.GetTypeNameFromDefinition(image.Reader, handle));
        Assert.Equal(
            name,
            TypeResolver.GetFullName(image.Reader, image.Reader.GetTypeDefinition(handle)));
    }

    [Fact]
    public void TypeSpecNameBudget_IsPreservedAsTypedEvidence()
    {
        TypeSpecificationHandle specification = default;
        using var image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly = AddAssemblyReference(metadata);
            TypeReferenceHandle argument = metadata.AddTypeReference(
                assembly,
                default,
                metadata.GetOrAddString(new string('A', 64 * 1024)));
            var signature = new BlobBuilder();
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(argument) << 2 | 1);
            specification = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        });

        var rejected = Assert.IsType<MetadataTypeNameResult.Rejected>(
            TypeResolver.ResolveTypeName(image.Reader, specification));
        Assert.Equal(
            nameof(SignatureDecodeRejectionKind.NameBudget),
            rejected.Failure.Kind);
        Assert.Equal(
            SignatureDecodeRejectionKind.NameBudget,
            rejected.Failure.SignatureKind);
        Assert.Equal(
            SignatureDecodeRejectionKind.NameBudget,
            Assert.IsType<SignatureDecodeResult<string>.Rejected>(
                    TypeResolver.DecodeTypeNameFromSpecification(
                        image.Reader,
                        specification))
                .Rejection.Kind);
    }

    [Fact]
    public void AppendLeaf_PreflightsActualUtf8OfMaterializedDeclaringName()
    {
        TypeDefinitionHandle leaf = default;
        using var image = BuildMetadata(
            metadata =>
            {
                TypeDefinitionHandle declaring = AddTypeDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "",
                    new string('\u4E00', 4_000));
                leaf = AddTypeDefinition(
                    metadata,
                    TypeAttributes.NestedPublic,
                    "",
                    new string('L', 8_000));
                metadata.AddNestedType(leaf, declaring);
            },
            countingDecoder: true);

        AssertNameBudget(
            TypeResolver.ResolveFullName(
                image.Reader,
                image.Reader.GetTypeDefinition(leaf)));
        Assert.DoesNotContain(8_000, image.Decoder!.DecodedByteCounts);
    }

    [Fact]
    public void EmptyNestedNameSegment_KeepsTheDelimiter()
    {
        TypeDefinitionHandle leaf = default;
        using var image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle outer = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                "Outer");
            TypeDefinitionHandle empty = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "");
            leaf = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "Inner");
            metadata.AddNestedType(empty, outer);
            metadata.AddNestedType(leaf, empty);
        });

        Assert.Equal(
            "Outer..Inner",
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, leaf)
                .GetValueOrThrow());
    }

    [Fact]
    public void NestedDisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap()
    {
        string leafName = new string('A', 5_030);
        TypeDefinitionHandle leaf = default;
        using var image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle outer = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "Ns",
                "Outer");
            leaf = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                leafName);
            metadata.AddNestedType(leaf, outer);
        });

        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, leaf));
        Assert.Equal(
            "Ns.Outer." + leafName,
            TypeResolver.GetTypeNameFromDefinition(image.Reader, leaf));
        Assert.Equal(
            "Ns.Outer." + leafName,
            TypeResolver.GetFullName(image.Reader, image.Reader.GetTypeDefinition(leaf)));
    }

    [Fact]
    public void EmptyLeadingNameSegment_DoesNotCollideWithTopLevelName()
    {
        TypeDefinitionHandle nested = default;
        TypeDefinitionHandle topLevel = default;
        using var image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle emptyOuter = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                "");
            nested = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "Inner");
            metadata.AddNestedType(nested, emptyOuter);
            topLevel = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                "Inner");
        });

        Assert.Equal(
            ".Inner",
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, nested)
                .GetValueOrThrow());
        Assert.Equal(
            "Inner",
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, topLevel)
                .GetValueOrThrow());
    }

    [Fact]
    public void TypeSpecNameBudget_SurvivesLaterMalformedArgument()
    {
        TypeSpecificationHandle specification = default;
        using var image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly = AddAssemblyReference(metadata);
            TypeReferenceHandle oversize = metadata.AddTypeReference(
                assembly,
                default,
                metadata.GetOrAddString(new string('A', 64 * 1024) + "`1"));
            var signature = new BlobBuilder();
            signature.WriteByte(0x15);
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(oversize) << 2 | 1);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger((0x7FFFF << 2) | 1);
            specification = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        });

        Assert.Equal(
            SignatureDecodeRejectionKind.NameBudget,
            Assert.IsType<SignatureDecodeResult<string>.Rejected>(
                    TypeResolver.DecodeTypeNameFromSpecification(
                        image.Reader,
                        specification))
                .Rejection.Kind);
        Assert.Equal(
            nameof(SignatureDecodeRejectionKind.NameBudget),
            Assert.IsType<MetadataTypeNameResult.Rejected>(
                    TypeResolver.ResolveTypeName(image.Reader, specification))
                .Failure.Kind);
    }

    [Fact]
    public void TypeSpecDisplayNameApis_AdmitCharacterOverBudgetNamesUnderTheEncodedCap()
    {
        string name = new string('A', 5_030);
        TypeSpecificationHandle specification = default;
        TypeReferenceHandle typeRef = default;
        using var image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly = AddAssemblyReference(metadata);
            typeRef = metadata.AddTypeReference(
                assembly,
                default,
                metadata.GetOrAddString(name));
            var signature = new BlobBuilder();
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(typeRef) << 2 | 1);
            specification = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        });

        Assert.Equal(
            SignatureDecodeRejectionKind.NameBudget,
            Assert.IsType<SignatureDecodeResult<string>.Rejected>(
                    TypeResolver.DecodeTypeNameFromSpecification(
                        image.Reader,
                        specification))
                .Rejection.Kind);
        Assert.Equal(
            name,
            TypeResolver.GetTypeNameFromSpecification(image.Reader, specification));
        Assert.Equal(
            name,
            TypeResolver.GetTypeName(image.Reader, specification));
        Assert.Equal(
            name,
            TypeResolver.GetTypeNameFromReference(image.Reader, typeRef));
    }

    [Fact]
    public void EmptyNamespaceExactCharacterBudget_AgreesWithCreate()
    {
        int budget = MetadataSafetyPolicy.MaxTypeNameCharacters;
        string accepted = new string('E', budget - 1);
        string rejected = new string('E', budget);
        TypeDefinitionHandle acceptedHandle = default;
        TypeDefinitionHandle rejectedHandle = default;
        using var image = BuildMetadata(metadata =>
        {
            acceptedHandle = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                accepted);
            rejectedHandle = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                rejected);
        });

        Assert.Equal(
            accepted,
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, acceptedHandle)
                .GetValueOrThrow());
        Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, acceptedHandle));
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("", [accepted]));

        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, rejectedHandle));
        Assert.Equal(
            RelationshipTraversalRejectionKind.NameBudget,
            Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(
                    MetadataTypeDefinitionNameReader.Read(
                        image.Reader,
                        rejectedHandle))
                .Failure.RelationshipKind);
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
                    MetadataTypeDefinitionName.Create("", [rejected]))
                .Rejection.Kind);
    }

    [Fact]
    public void EmptyNamespaceNestedResolveFullName_AgreesWithCreate()
    {
        string outer = new string('O', 2_047);
        string acceptedLeaf = new string('I', 2_047);
        string rejectedLeaf = new string('I', 2_048);
        TypeDefinitionHandle accepted = default;
        TypeDefinitionHandle rejected = default;
        using var image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle acceptedOuter = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                outer);
            accepted = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                acceptedLeaf);
            metadata.AddNestedType(accepted, acceptedOuter);
            TypeDefinitionHandle rejectedOuter = AddTypeDefinition(
                metadata,
                TypeAttributes.Public,
                "",
                outer);
            rejected = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                rejectedLeaf);
            metadata.AddNestedType(rejected, rejectedOuter);
        });

        Assert.Equal(
            outer + "." + acceptedLeaf,
            TypeResolver.ResolveFullName(
                    image.Reader,
                    image.Reader.GetTypeDefinition(accepted))
                .GetValueOrThrow());
        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, rejected));
        AssertNameBudget(
            TypeResolver.ResolveFullName(
                image.Reader,
                image.Reader.GetTypeDefinition(rejected)));
        Assert.Equal(
            MetadataTypeNameRejectionKind.SegmentsTooLong,
            Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
                    MetadataTypeDefinitionName.Create("", [outer, rejectedLeaf]))
                .Rejection.Kind);
    }

    [Fact]
    public void NilNameAfterEncodedCap_IsRejectedOnDisplayPath()
    {
        string ns = new string(
            'N',
            MetadataSafetyPolicy.MaxTypeNameCharacters * 3);
        TypeDefinitionHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = AddTypeDefinition(metadata, TypeAttributes.Public, ns, "");
        });

        Assert.Throws<BadImageFormatException>(
            () => TypeResolver.GetTypeNameFromDefinition(image.Reader, handle));
        AssertNameBudget(
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, handle));
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

    static MetadataImage BuildMetadata(
        Action<MetadataBuilder> addRows,
        bool countingDecoder = false,
        string? metadataVersion = null)
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

        var rootBuilder = new MetadataRootBuilder(
            metadata,
            metadataVersion,
            suppressValidation: true);
        var image = new BlobBuilder();
        rootBuilder.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return new MetadataImage(
            image.ToImmutableArray(),
            countingDecoder ? new CountingDecoder() : null);
    }

    sealed class MetadataImage : IDisposable
    {
        readonly MetadataReaderProvider provider;

        public MetadataImage(ImmutableArray<byte> image, CountingDecoder? decoder)
        {
            provider = MetadataReaderProvider.FromMetadataImage(image);
            Decoder = decoder;
            Reader = provider.GetMetadataReader(
                MetadataReaderOptions.Default,
                decoder);
        }

        public MetadataReader Reader { get; }
        public CountingDecoder? Decoder { get; }

        public void Dispose() => provider.Dispose();
    }

    unsafe sealed class CountingDecoder : MetadataStringDecoder
    {
        public CountingDecoder()
            : base(new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true))
        {
        }

        public List<int> DecodedByteCounts { get; } = [];

        public override string GetString(byte* bytes, int byteCount)
        {
            DecodedByteCounts.Add(byteCount);
            return base.GetString(bytes, byteCount);
        }
    }
}
