using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class MetadataTypeDeclarationProbeTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void StructuredName_HasOrdinalStructuralValueSemantics()
    {
        MetadataTypeDefinitionName first = Name("N", "Outer`1", "Inner`2");
        MetadataTypeDefinitionName second = Name("N", "Outer`1", "Inner`2");
        MetadataTypeDefinitionName differentCase = Name("N", "Outer`1", "inner`2");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, differentCase);
        Assert.Equal(["Outer`1", "Inner`2"], first.Segments);

        var cache = new Dictionary<MetadataTypeDefinitionName, string>
        {
            [first] = "value",
        };
        Assert.Equal("value", cache[second]);
    }

    [Fact]
    public void StructuredName_RejectsMissingNamespace()
    {
        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
            MetadataTypeDefinitionName.Create(null, ["Type"]));

        Assert.Equal(
            MetadataTypeNameRejectionKind.MissingNamespace,
            rejected.Rejection.Kind);
    }

    [Fact]
    public void StructuredName_RejectsMissingSegments()
    {
        var nullSegments = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
            MetadataTypeDefinitionName.Create("N", default));
        var emptySegments = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
            MetadataTypeDefinitionName.Create("N", []));

        Assert.Equal(
            MetadataTypeNameRejectionKind.MissingSegments,
            nullSegments.Rejection.Kind);
        Assert.Equal(
            MetadataTypeNameRejectionKind.MissingSegments,
            emptySegments.Rejection.Kind);
    }

    [Fact]
    public void StructuredName_RejectsEmptySegment()
    {
        var rejected = Assert.IsType<MetadataTypeDefinitionNameResult.Rejected>(
            MetadataTypeDefinitionName.Create("N", ["Outer", ""]));

        Assert.Equal(
            MetadataTypeNameRejectionKind.MissingSegment,
            rejected.Rejection.Kind);
        Assert.Equal(1, rejected.Rejection.SegmentIndex);
    }

    [Fact]
    public void StructuredNameReader_PreservesNestedTypeReferenceSegments()
    {
        TypeReferenceHandle leaf = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle target = AddAssemblyReference(metadata, "Target");
            TypeReferenceHandle root = metadata.AddTypeReference(
                target,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer`1"));
            leaf = metadata.AddTypeReference(
                root,
                default,
                metadata.GetOrAddString("Inner`2"));
        });

        var read = Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, leaf));

        Assert.Equal(Name("N", "Outer`1", "Inner`2"), read.Name);
    }

    [Fact]
    public void StructuredNameReader_RejectsEmptyTypeReferenceName()
    {
        TypeReferenceHandle reference = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            reference = metadata.AddTypeReference(
                AddAssemblyReference(metadata, "Target"),
                metadata.GetOrAddString("N"),
                name: default);
        });

        var rejected = Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, reference));

        Assert.Equal(
            MetadataTypeNameFailureMechanism.Metadata,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Probe_ReturnsTopLevelDefinition()
    {
        TypeDefinitionHandle expected = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            expected = AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Type");
        });

        var defined = Assert.IsType<TypeDeclarationResult.Defined>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(MetadataTokens.GetToken(expected), defined.Definition.Value);
    }

    [Fact]
    public void Probe_ReturnsNestedDefinition()
    {
        TypeDefinitionHandle inner = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle outer =
                AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Outer`1");
            inner = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "Inner`2");
            metadata.AddNestedType(inner, outer);
        });

        var defined = Assert.IsType<TypeDeclarationResult.Defined>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "Outer`1", "Inner`2")));

        Assert.Equal(MetadataTokens.GetToken(inner), defined.Definition.Value);
    }

    [Fact]
    public void Probe_DoesNotMatchCloseNestedDefinitionNames()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle outer =
                AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Outer`1");
            TypeDefinitionHandle inner = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "Leaf");
            metadata.AddNestedType(inner, outer);
        });

        AssertMissing(image.Reader, Name("Other", "Outer`1", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Other`1", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Outer`2", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Leaf"));
    }

    [Fact]
    public void Probe_ReturnsTopLevelForwarder()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle target = AddAssemblyReference(metadata, "Target");
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                target,
                typeDefinitionId: 0);
        });

        var forwarded = Assert.IsType<TypeDeclarationResult.Forwarded>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal("Target", forwarded.Target.Name);
        Assert.Single(forwarded.Declarations);
        Assert.Equal(
            (int)TableIndex.ExportedType,
            forwarded.Declarations[0].Value >> 24);
    }

    [Fact]
    public void Probe_ReturnsNestedForwarderWithFullDeclarationChain()
    {
        ExportedTypeHandle outer = default;
        ExportedTypeHandle inner = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle target = AddAssemblyReference(metadata, "Target");
            outer = metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"),
                target,
                typeDefinitionId: 0);
            inner = metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Inner"),
                outer,
                typeDefinitionId: 0);
        });

        var forwarded = Assert.IsType<TypeDeclarationResult.Forwarded>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "Outer", "Inner")));

        Assert.Equal(
            [MetadataTokens.GetToken(outer), MetadataTokens.GetToken(inner)],
            forwarded.Declarations.Select(token => token.Value));
    }

    [Fact]
    public void Probe_DoesNotMatchCloseNestedForwarderNames()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle target = AddAssemblyReference(metadata, "Target");
            ExportedTypeHandle outer = metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer`1"),
                target,
                typeDefinitionId: 0);
            metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Leaf"),
                outer,
                typeDefinitionId: 0);
        });

        AssertMissing(image.Reader, Name("Other", "Outer`1", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Other`1", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Outer`2", "Leaf"));
        AssertMissing(image.Reader, Name("N", "Leaf"));
    }

    [Fact]
    public void Probe_RejectsAssemblyReferenceExportWithoutForwarderFlag()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                AddAssemblyReference(metadata, "Target"),
                typeDefinitionId: 0);
        });

        var rejected = Assert.IsType<TypeDeclarationResult.Rejected>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(
            MetadataTypeNameFailureMechanism.Metadata,
            rejected.Rejection.Mechanism);
    }

    [Fact]
    public void Probe_CoalescesDuplicateForwardersToSameCompleteIdentity()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle target = AddAssemblyReference(metadata, "Target");
            AddForwarder(metadata, target, "N", "Type");
            AddForwarder(metadata, target, "N", "Type");
        });

        var forwarded = Assert.IsType<TypeDeclarationResult.Forwarded>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal("Target", forwarded.Target.Name);
        Assert.Equal(2, forwarded.Declarations.Length);
    }

    [Fact]
    public void Probe_ReturnsAmbiguousForDifferentForwarderTargets()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddForwarder(
                metadata,
                AddAssemblyReference(metadata, "TargetA"),
                "N",
                "Type");
            AddForwarder(
                metadata,
                AddAssemblyReference(metadata, "TargetB"),
                "N",
                "Type");
        });

        var ambiguous = Assert.IsType<TypeDeclarationResult.Ambiguous>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(2, ambiguous.Candidates.Length);
        Assert.All(
            ambiguous.Candidates,
            candidate => Assert.IsType<TypeDeclarationCandidate.Forwarder>(candidate));
    }

    [Fact]
    public void Probe_UsesCompleteAssemblyIdentityWhenCoalescingForwarders()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddForwarder(
                metadata,
                AddAssemblyReference(metadata, "Target", new Version(1, 0, 0, 0)),
                "N",
                "Type");
            AddForwarder(
                metadata,
                AddAssemblyReference(metadata, "Target", new Version(2, 0, 0, 0)),
                "N",
                "Type");
        });

        var ambiguous = Assert.IsType<TypeDeclarationResult.Ambiguous>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(2, ambiguous.Candidates.Length);
    }

    [Fact]
    public void Probe_ReturnsAmbiguousForDefinitionForwarderConflict()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Type");
            AddForwarder(
                metadata,
                AddAssemblyReference(metadata, "Target"),
                "N",
                "Type");
        });

        var ambiguous = Assert.IsType<TypeDeclarationResult.Ambiguous>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Collection(
            ambiguous.Candidates,
            candidate => Assert.IsType<TypeDeclarationCandidate.Definition>(candidate),
            candidate => Assert.IsType<TypeDeclarationCandidate.Forwarder>(candidate));
    }

    [Fact]
    public void Probe_ReturnsAmbiguousForCompetingDefinitions()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Type");
            AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Type");
        });

        var ambiguous = Assert.IsType<TypeDeclarationResult.Ambiguous>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(2, ambiguous.Candidates.Length);
        Assert.All(
            ambiguous.Candidates,
            candidate => Assert.IsType<TypeDeclarationCandidate.Definition>(candidate));
    }

    [Fact]
    public void Probe_ReturnsCopiedModuleExportEvidence()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyFileHandle module = metadata.AddAssemblyFile(
                metadata.GetOrAddString("Part.netmodule"),
                metadata.GetOrAddBlob(new byte[] { 1, 2, 3 }),
                containsMetadata: true);
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                module,
                typeDefinitionId: 1);
        });

        var exported = Assert.IsType<TypeDeclarationResult.ExportedFromModule>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal("Part.netmodule", exported.Module.Name);
        Assert.True(exported.Module.ContainsMetadata);
        Assert.Equal([1, 2, 3], exported.Module.Hash);
        Assert.Single(exported.Declarations);
    }

    [Fact]
    public void Probe_ReturnsMissingForReadableImageWithoutDeclaration()
    {
        using MetadataImage image = BuildMetadata(_ => { });

        Assert.IsType<TypeDeclarationResult.Missing>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Missing")));
    }

    [Fact]
    public void Probe_RejectsCyclicExportedTypeRelationship()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            metadata.AddExportedType(
                Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("First"),
                MetadataTokens.ExportedTypeHandle(2),
                typeDefinitionId: 0);
            metadata.AddExportedType(
                Forwarder,
                default,
                metadata.GetOrAddString("Second"),
                MetadataTokens.ExportedTypeHandle(1),
                typeDefinitionId: 0);
        });

        var rejected = Assert.IsType<TypeDeclarationResult.Rejected>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "First")));

        Assert.Equal(RelationshipTraversalRejectionKind.Cycle, rejected.Rejection.RelationshipKind);
    }

    [Fact]
    public void StructuredNameReader_RejectsEmptyExportedTypeName()
    {
        ExportedTypeHandle exported = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            exported = metadata.AddExportedType(
                Forwarder,
                metadata.GetOrAddString("N"),
                name: default,
                AddAssemblyReference(metadata, "Target"),
                typeDefinitionId: 0);
        });

        var rejected = Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, exported));

        Assert.Equal(
            MetadataTypeNameFailureMechanism.Metadata,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void StructuredNameReader_RejectsEmptyTypeDefinitionName()
    {
        TypeDefinitionHandle definition = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            definition = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                name: default,
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        });

        var rejected = Assert.IsType<MetadataTypeDefinitionNameReadResult.Rejected>(
            MetadataTypeDefinitionNameReader.Read(image.Reader, definition));

        Assert.Equal(
            MetadataTypeNameFailureMechanism.Metadata,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void Probe_RejectsUnsupportedExportedTypeTerminal()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            metadata.AddExportedType(
                Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                implementation: default(AssemblyFileHandle),
                typeDefinitionId: 0);
        });

        var rejected = Assert.IsType<TypeDeclarationResult.Rejected>(
            MetadataTypeDeclarationProbe.Probe(image.Reader, Name("N", "Type")));

        Assert.Equal(
            MetadataTypeNameFailureMechanism.Metadata,
            rejected.Rejection.Mechanism);
    }

    [Fact]
    public void Probe_RejectsExportedTypeRelationshipOverNodeBudget()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            for (int row = 1; row <= MetadataSafetyPolicy.MaxRelationshipNodes + 1; row++)
            {
                metadata.AddExportedType(
                    Forwarder,
                    row == MetadataSafetyPolicy.MaxRelationshipNodes + 1
                        ? metadata.GetOrAddString("N")
                        : default,
                    metadata.GetOrAddString($"Type{row}"),
                    row == MetadataSafetyPolicy.MaxRelationshipNodes + 1
                        ? AddAssemblyReference(metadata, "Target")
                        : MetadataTokens.ExportedTypeHandle(row + 1),
                    typeDefinitionId: 0);
            }
        });

        var rejected = Assert.IsType<TypeDeclarationResult.Rejected>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "Type1")));

        Assert.Equal(
            RelationshipTraversalRejectionKind.NodeBudget,
            rejected.Rejection.RelationshipKind);
        Assert.Equal(
            MetadataSafetyPolicy.MaxRelationshipNodes,
            rejected.Rejection.ConsumedNodes);
    }

    [Fact]
    public void Probe_SkipsUnrelatedMalformedRelationship()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddTypeDefinition(metadata, TypeAttributes.Public, "N", "ValidType");
            metadata.AddExportedType(
                Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("First"),
                MetadataTokens.ExportedTypeHandle(2),
                typeDefinitionId: 0);
            metadata.AddExportedType(
                Forwarder,
                default,
                metadata.GetOrAddString("Second"),
                MetadataTokens.ExportedTypeHandle(1),
                typeDefinitionId: 0);
        });

        Assert.IsType<TypeDeclarationResult.Defined>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "ValidType")));
    }

    [Fact]
    public void Probe_RejectsSameLeafMalformedRelationshipBesideDefinition()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddTypeDefinition(metadata, TypeAttributes.Public, "N", "First");
            metadata.AddExportedType(
                Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("First"),
                MetadataTokens.ExportedTypeHandle(2),
                typeDefinitionId: 0);
            metadata.AddExportedType(
                Forwarder,
                default,
                metadata.GetOrAddString("Second"),
                MetadataTokens.ExportedTypeHandle(1),
                typeDefinitionId: 0);
        });

        var rejected = Assert.IsType<TypeDeclarationResult.Rejected>(
            MetadataTypeDeclarationProbe.Probe(
                image.Reader,
                Name("N", "First")));

        Assert.Equal(
            RelationshipTraversalRejectionKind.Cycle,
            rejected.Rejection.RelationshipKind);
    }

    [Fact]
    public void Probe_AbsentNameAllocationDoesNotScaleWithRowCount()
    {
        using MetadataImage image = BuildMetadata(metadata =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                AddTypeDefinition(
                    metadata,
                    TypeAttributes.Public,
                    "N",
                    $"Type{i}");
            }
        });
        MetadataTypeDefinitionName missing = Name("N", "Missing");

        MetadataTypeDeclarationProbe.Probe(image.Reader, missing);
        const int iterations = 20;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            MetadataTypeDeclarationProbe.Probe(image.Reader, missing);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, iterations * 2_048);
    }

    [Fact]
    public void MaterializedTokens_RejectRowsOutsideSuppliedReader()
    {
        using MetadataImage image = BuildMetadata(_ => { });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypeDefinitionToken.FromHandle(
                image.Reader,
                MetadataTokens.TypeDefinitionHandle(2)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExportedTypeToken.FromHandle(
                image.Reader,
                MetadataTokens.ExportedTypeHandle(1)));
    }

    [Fact]
    public void Probe_RecognizesCompilerProducedForwarder()
    {
        using var stream = File.OpenRead(typeof(MetadataTypeDeclarationProbeTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        var forwarded = Assert.IsType<TypeDeclarationResult.Forwarded>(
            MetadataTypeDeclarationProbe.Probe(
                peReader.GetMetadataReader(),
                Name("ILInspector.Metadata", "MetadataTableProjector")));

        Assert.Equal("ILInspector.Metadata", forwarded.Target.Name);
    }

    [Fact]
    public void DeclarationResults_DoNotExposeReadersOrMetadataHandles()
    {
        Type[] resultTypes =
        [
            typeof(TypeDeclarationResult.Defined),
            typeof(TypeDeclarationResult.Forwarded),
            typeof(TypeDeclarationResult.ExportedFromModule),
            typeof(TypeDeclarationResult.Missing),
            typeof(TypeDeclarationResult.Ambiguous),
            typeof(TypeDeclarationResult.Rejected),
            typeof(TypeDeclarationCandidate.Definition),
            typeof(TypeDeclarationCandidate.Forwarder),
            typeof(TypeDeclarationCandidate.ModuleExport),
            typeof(ModuleFileReference),
        ];

        foreach (Type type in resultTypes)
        {
            foreach (PropertyInfo property in type.GetProperties())
                AssertClosedPropertyType(property.PropertyType);
        }
    }

    static void AssertClosedPropertyType(Type type)
    {
        Assert.NotEqual(typeof(MetadataReader), type);
        Assert.NotEqual(typeof(PEReader), type);
        Assert.False(
            type.Namespace == "System.Reflection.Metadata"
            && type.Name.EndsWith("Handle", StringComparison.Ordinal));

        if (type.HasElementType)
            AssertClosedPropertyType(type.GetElementType()!);

        foreach (Type argument in type.GetGenericArguments())
            AssertClosedPropertyType(argument);
    }

    static MetadataTypeDefinitionName Name(string @namespace, params string[] segments)
    {
        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [.. segments]));
        return valid.Name;
    }

    static void AssertMissing(
        MetadataReader reader,
        MetadataTypeDefinitionName name) =>
        Assert.IsType<TypeDeclarationResult.Missing>(
            MetadataTypeDeclarationProbe.Probe(reader, name));

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string @namespace,
        string name) =>
        metadata.AddTypeDefinition(
            attributes,
            @namespace.Length == 0 ? default : metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        string name,
        Version? version = null) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            version ?? new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

    static ExportedTypeHandle AddForwarder(
        MetadataBuilder metadata,
        AssemblyReferenceHandle target,
        string @namespace,
        string name) =>
        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            target,
            typeDefinitionId: 0);

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
