using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class SignatureOccurrenceDecoderTests
{
    [Theory]
    [InlineData(HandleKind.MethodDefinition)]
    [InlineData(HandleKind.FieldDefinition)]
    [InlineData(HandleKind.PropertyDefinition)]
    public void OrdinarySignatures_PreserveDuplicateOccurrences(HandleKind kind)
    {
        var fixture = new Fixture();
        var member = fixture.Member(kind, kind == HandleKind.FieldDefinition
            ? [0x06, 0x08]
            : [kind == HandleKind.MethodDefinition ? (byte)0x00 : (byte)0x08, 2, 8, 8, 8]);
        using var image = fixture.Open();
        var decoded = Decoded(SignatureOccurrenceDecoder.Decode(image, member));
        Assert.Equal(kind == HandleKind.FieldDefinition ? 1 : 3, decoded.Occurrences.Length);
        Assert.All(decoded.Occurrences, occurrence =>
        {
            Assert.Equal("System", occurrence.Reference.Type.Namespace);
            Assert.Equal(["Int32"], occurrence.Reference.Type.Segments);
            Assert.IsType<MetadataTypeReferenceScope.IntrinsicCoreLibrary>(occurrence.Reference.Scope);
            Assert.True(occurrence.Participates);
            Assert.Null(occurrence.DefinitionOrigin);
        });
    }

    [Fact]
    public void LocalNestedDefinition_PreservesContainingNamesAndSourceToken()
    {
        var fixture = new Fixture();
        var outer = fixture.Type("Outer", "N");
        var inner = fixture.Type("Inner");
        fixture.Metadata.AddNestedType(inner, outer);
        var member = fixture.Member(HandleKind.FieldDefinition, FieldType(inner));
        var bytes = ImmutableArray.Create(fixture.Bytes());
        using var first = new PEReader(bytes);
        using var second = new PEReader(bytes);
        var occurrence = Assert.Single(Decoded(SignatureOccurrenceDecoder.Decode(first, member)).Occurrences);
        Assert.Equal(["Outer", "Inner"], occurrence.Reference.Type.Segments);
        Assert.Equal("N", occurrence.Reference.Type.Namespace);
        Assert.IsType<MetadataTypeReferenceScope.CurrentAssembly>(occurrence.Reference.Scope);
        Assert.NotNull(occurrence.DefinitionOrigin);
        Assert.Same(first, occurrence.DefinitionOrigin.Image);
        Assert.Equal(inner, occurrence.DefinitionOrigin.Handle);
        var other = Assert.Single(Decoded(SignatureOccurrenceDecoder.Decode(second, member)).Occurrences);
        Assert.NotEqual(occurrence.DefinitionOrigin, other.DefinitionOrigin);
    }

    [Fact]
    public void SameNamesInDifferentExternalScopes_RemainDistinct()
    {
        var fixture = new Fixture();
        var left = fixture.Reference("Same", fixture.Assembly("Left"), "N");
        var right = fixture.Reference("Same", fixture.Assembly("Right"), "N");
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0x00, 0x01]);
        Type(signature, left);
        Type(signature, right);
        var member = fixture.Member(HandleKind.MethodDefinition, signature.ToArray());
        using var image = fixture.Open();
        var occurrences = Decoded(SignatureOccurrenceDecoder.Decode(image, member)).Occurrences;
        Assert.Equal(occurrences[0].Reference.Type, occurrences[1].Reference.Type);
        Assert.False(MetadataNamedTypeReference.EquivalentComparer.Equals(
            occurrences[0].Reference, occurrences[1].Reference));
        Assert.All(occurrences, item => Assert.Null(item.DefinitionOrigin));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModifierContents_AreRetainedWithParticipation(bool required)
    {
        var fixture = new Fixture();
        var modifier = fixture.Reference("Modifier", fixture.Assembly("A"), "N");
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(required ? (byte)0x1f : (byte)0x20);
        signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(modifier));
        signature.WriteByte(0x08);
        var member = fixture.Member(HandleKind.FieldDefinition, signature.ToArray());
        using var image = fixture.Open();
        var occurrences = Decoded(SignatureOccurrenceDecoder.Decode(image, member)).Occurrences;
        Assert.Equal(2, occurrences.Length);
        Assert.Equal(required, occurrences[0].Participates);
        Assert.True(occurrences[1].Participates);
        Assert.Equal("Modifier", occurrences[0].Reference.Type.Segments[0]);
    }

    [Fact]
    public void OptionalModifier_NestedMalformedEvidenceCannotDisappear()
    {
        var fixture = new Fixture();
        var spec = fixture.Metadata.AddTypeSpecification(fixture.Metadata.GetOrAddBlob((byte[])[0x08, 0x08]));
        var member = fixture.Member(HandleKind.FieldDefinition, ModifiedField(spec));
        using var image = fixture.Open();
        Rejected(SignatureOccurrenceDecoder.Decode(image, member), SignatureOccurrenceRejectionReason.UnsafeSignature);
    }

    [Fact]
    public void OptionalModifier_NestedBudgetRefusalCannotDisappear()
    {
        var fixture = new Fixture();
        var reference = fixture.Reference("Modifier", fixture.Assembly(new string('A', 100)), "N");
        var member = fixture.Member(HandleKind.FieldDefinition, ModifiedField(reference));
        using var image = fixture.Open();
        Rejected(SignatureOccurrenceDecoder.Decode(image, member, new(),
            SignatureOccurrenceLimits.Default with { Work = 32 }), SignatureOccurrenceRejectionReason.WorkBudget);
    }

    [Fact]
    public void FunctionPointerGenericArrayAndGenericParameters_ComposeOccurrences()
    {
        var fixture = new Fixture();
        var generic = fixture.Reference("Container`1", fixture.Assembly("A"), "N");
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0x00, 0x03, 0x01, 0x1b, 0x00, 0x01, 0x08, 0x15, 0x12]);
        signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(generic));
        signature.WriteBytes((byte[])[0x01, 0x14, 0x0e, 0x02, 0x01, 0x03, 0x01, 0x00, 0x13, 0x00, 0x1e, 0x01]);
        var member = fixture.Member(HandleKind.MethodDefinition, signature.ToArray());
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        var occurrences = Decoded(SignatureOccurrenceDecoder.Decode(image, member, metrics)).Occurrences;
        Assert.Equal(["Void", "Int32", "Container`1", "String"],
            occurrences.Select(item => item.Reference.Type.Segments[0]));
        Assert.Equal(9, metrics.Nodes);
        Assert.Equal(1, metrics[SignatureOccurrenceMetric.ArrayShapeSizes].Total);
        Assert.Equal(1, metrics[SignatureOccurrenceMetric.ArrayShapeLowerBounds].Total);
    }

    [Fact]
    public void CompleteGuard_RejectsWideAndDeepBeforeProviderCallbacks()
    {
        var fixture = new Fixture();
        var deep = fixture.Member(HandleKind.FieldDefinition,
            [0x06, .. Enumerable.Repeat((byte)0x0f, SignatureBlobGuard.DefaultMaxDepth), 0x08]);
        var signature = new BlobBuilder();
        signature.WriteByte(0);
        signature.WriteCompressedInteger(MetadataSafetyPolicy.MaxSignatureTypeNodes);
        signature.WriteBytes(0x08, MetadataSafetyPolicy.MaxSignatureTypeNodes + 1);
        var wide = fixture.Member(HandleKind.MethodDefinition, signature.ToArray());
        using var image = fixture.Open();
        foreach (var member in new[] { deep, wide })
        {
            var metrics = new SignatureOccurrenceMetrics();
            Rejected(SignatureOccurrenceDecoder.Decode(image, member, metrics),
                SignatureOccurrenceRejectionReason.UnsafeSignature);
            Assert.Equal(0, metrics.Nodes);
        }
    }

    [Fact]
    public void GuardArrayAllowance_IsIndependentOfProviderNodeBudget()
    {
        var fixture = new Fixture();
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0x06, 0x14, 0x08]);
        signature.WriteCompressedInteger(MetadataSafetyPolicy.MaxSignatureTypeNodes);
        signature.WriteCompressedInteger(MetadataSafetyPolicy.MaxSignatureTypeNodes);
        signature.WriteBytes(1, MetadataSafetyPolicy.MaxSignatureTypeNodes);
        signature.WriteByte(0);
        var member = fixture.Member(HandleKind.FieldDefinition, signature.ToArray());
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        Rejected(SignatureOccurrenceDecoder.Decode(image, member, metrics),
            SignatureOccurrenceRejectionReason.UnsafeSignature);
        Assert.Equal(0, metrics.Nodes);
        Assert.Equal(1, metrics[SignatureOccurrenceMetric.ArrayShapeSizes].Count);
        Assert.Equal(MetadataSafetyPolicy.MaxSignatureTypeNodes,
            metrics[SignatureOccurrenceMetric.ArrayShapeSizes].Total);
        Assert.Equal(0, metrics[SignatureOccurrenceMetric.ArrayShapeLowerBounds].Count);
    }

    [Theory]
    [InlineData(3, 6, true)]
    [InlineData(2, 6, false)]
    [InlineData(3, 5, false)]
    public void NodeAndCopyBudgets_HaveExactInclusiveBoundaries(int nodes, int copies, bool succeeds)
    {
        var fixture = new Fixture();
        var member = fixture.Member(HandleKind.MethodDefinition, [0, 2, 8, 8, 8]);
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        var result = SignatureOccurrenceDecoder.Decode(image, member, metrics,
            SignatureOccurrenceLimits.Default with { Nodes = nodes, Copies = copies });
        if (succeeds)
        {
            Decoded(result);
            Assert.Equal(3, metrics.Nodes);
            Assert.Equal(6, metrics.Copies);
        }
        else
            Rejected(result, nodes < 3 ? SignatureOccurrenceRejectionReason.NodeBudget
                : SignatureOccurrenceRejectionReason.OccurrenceCopyBudget);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(5, false)]
    public void WorkBudget_HasExactInclusiveBoundary(int work, bool succeeds)
    {
        var fixture = new Fixture();
        var reference = fixture.Reference("C", fixture.Assembly("A"), "N");
        var member = fixture.Member(HandleKind.FieldDefinition, FieldType(reference));
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        var result = SignatureOccurrenceDecoder.Decode(image, member, metrics,
            SignatureOccurrenceLimits.Default with { Work = work });
        if (succeeds)
        {
            Decoded(result);
            Assert.Equal(6, metrics.Work);
        }
        else
            Rejected(result, SignatureOccurrenceRejectionReason.WorkBudget);
    }

    [Fact]
    public void ProductionCeilings_AreEnforcedWithoutOverflow()
    {
        var metrics = new SignatureOccurrenceMetrics();
        var limits = SignatureOccurrenceLimits.Default;
        Assert.Equal(65_536, limits.Nodes);
        Assert.Equal(524_288, limits.Copies);
        Assert.Equal(262_144, limits.Work);
        var budget = new SignatureOccurrenceWorkBudget(limits, metrics);
        for (int i = 0; i < limits.Nodes; i++)
            budget.Node();
        Assert.Equal(SignatureOccurrenceRejectionReason.NodeBudget,
            Assert.Throws<SignatureOccurrenceRejectedException>(budget.Node).Reason);
        budget.Copies(limits.Copies);
        Assert.Equal(SignatureOccurrenceRejectionReason.OccurrenceCopyBudget,
            Assert.Throws<SignatureOccurrenceRejectedException>(() => budget.Copies(1)).Reason);
        budget.Work(SignatureOccurrenceMetric.TypeNameCharacters, limits.Work);
        Assert.Equal(SignatureOccurrenceRejectionReason.WorkBudget,
            Assert.Throws<SignatureOccurrenceRejectedException>(() =>
                budget.Work(SignatureOccurrenceMetric.TypeNameCharacters, int.MaxValue)).Reason);
        Assert.Equal(limits.Work, metrics.Work);
    }

    [Fact]
    public void EveryNameChainWalk_IsChargedIncludingContainingTypes()
    {
        var fixture = new Fixture();
        var outer = fixture.Type("O", "N");
        var inner = fixture.Type("I");
        fixture.Metadata.AddNestedType(inner, outer);
        var outerReference = fixture.Reference("O", fixture.Assembly("A"), "N");
        var innerReference = fixture.Reference("I", outerReference);
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0, 1]);
        Type(signature, inner);
        Type(signature, innerReference);
        var member = fixture.Member(HandleKind.MethodDefinition, signature.ToArray());
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        Decoded(SignatureOccurrenceDecoder.Decode(image, member, metrics));
        Assert.Equal(new SignatureOccurrenceMeasurement(1, 2, 2),
            metrics[SignatureOccurrenceMetric.TypeDefinitionChainNodes]);
        Assert.Equal(new SignatureOccurrenceMeasurement(1, 2, 2),
            metrics[SignatureOccurrenceMetric.TypeReferenceNameChainNodes]);
        Assert.Equal(new SignatureOccurrenceMeasurement(1, 2, 2),
            metrics[SignatureOccurrenceMetric.TypeReferenceScopeChainNodes]);
        Assert.Equal(10, metrics[SignatureOccurrenceMetric.TypeNameCharacters].Total);
        Assert.Equal(17, metrics.Work);
    }

    [Fact]
    public void TypeSpec_ReentryChargesEachOccurrenceAndRetainsAllOptionalContents()
    {
        var fixture = new Fixture();
        var spec = fixture.Metadata.AddTypeSpecification(fixture.Metadata.GetOrAddBlob((byte[])[0x1d, 0x08]));
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0, 1]);
        signature.WriteBytes(ModifiedField(spec).AsSpan(1).ToArray());
        signature.WriteBytes(ModifiedField(spec).AsSpan(1).ToArray());
        var member = fixture.Member(HandleKind.MethodDefinition, signature.ToArray());
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        var occurrences = Decoded(SignatureOccurrenceDecoder.Decode(image, member, metrics)).Occurrences;
        Assert.Equal([false, true, false, true], occurrences.Select(item => item.Participates));
        Assert.Equal(new SignatureOccurrenceMeasurement(2, 4, 2),
            metrics[SignatureOccurrenceMetric.TypeSpecificationBytes]);
    }

    [Fact]
    public void TypeSpec_SingleBlobCapPrecedesScan()
    {
        var fixture = new Fixture();
        var tail = fixture.Metadata.AddTypeSpecification(
            fixture.Metadata.GetOrAddBlob(new byte[TypeSpecGuard.MaxCumulativeBytes + 1]));
        var tooLarge = fixture.Member(HandleKind.FieldDefinition, ModifiedField(tail));
        using var image = fixture.Open();
        Rejected(SignatureOccurrenceDecoder.Decode(image, tooLarge),
            SignatureOccurrenceRejectionReason.TypeSpecificationBudget);
    }

    [Fact]
    public void TypeSpec_ActiveByteClosureIsBoundedRatherThanOnlyIndividualBlobs()
    {
        var fixture = new Fixture();
        var next = fixture.Metadata.AddTypeSpecification(fixture.Metadata.GetOrAddBlob((byte[])[0x08]));
        for (int i = 0; i < 16; i++)
        {
            var blob = new BlobBuilder();
            blob.WriteByte(0x20);
            blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(next));
            blob.WriteBytes((byte[])[0x14, 0x08]);
            blob.WriteCompressedInteger(300);
            blob.WriteCompressedInteger(300);
            blob.WriteBytes(1, 300);
            blob.WriteByte(0);
            Assert.True(blob.Count < TypeSpecGuard.MaxCumulativeBytes);
            next = fixture.Metadata.AddTypeSpecification(fixture.Metadata.GetOrAddBlob(blob));
        }
        var member = fixture.Member(HandleKind.FieldDefinition, ModifiedField(next));
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        Rejected(SignatureOccurrenceDecoder.Decode(image, member, metrics),
            SignatureOccurrenceRejectionReason.TypeSpecificationBudget);
        Assert.True(metrics[SignatureOccurrenceMetric.TypeSpecificationBytes].Total
            > TypeSpecGuard.MaxCumulativeBytes);
    }

    [Fact]
    public void CyclicTypeSpec_RefusesAndRestoresActiveGuard()
    {
        var fixture = new Fixture();
        var next = MetadataTokens.TypeSpecificationHandle(1);
        fixture.Metadata.AddTypeSpecification(fixture.Metadata.GetOrAddBlob(ModifiedField(next)[1..]));
        var member = fixture.Member(HandleKind.FieldDefinition, ModifiedField(next));
        var ordinary = fixture.Member(HandleKind.FieldDefinition, [0x06, 0x08]);
        using var image = fixture.Open();
        Rejected(SignatureOccurrenceDecoder.Decode(image, member), SignatureOccurrenceRejectionReason.TypeSpecificationBudget);
        Decoded(SignatureOccurrenceDecoder.Decode(image, ordinary));
    }

    [Theory]
    [InlineData(false, 8, true)]
    [InlineData(true, 8, true)]
    [InlineData(false, 9, false)]
    [InlineData(true, 9, true)]
    public void KeyFlag_NotLength_SelectsTheCostClass(bool fullKey, int length, bool succeeds)
    {
        var fixture = new Fixture();
        var assembly = fixture.Assembly("A", key: Enumerable.Repeat((byte)1, length).ToArray(),
            flags: fullKey ? AssemblyFlags.PublicKey : 0);
        var member = fixture.Member(HandleKind.FieldDefinition,
            FieldType(fixture.Reference("C", assembly, "N")));
        using var image = fixture.Open();
        var metrics = new SignatureOccurrenceMetrics();
        var result = SignatureOccurrenceDecoder.Decode(image, member, metrics);
        if (!succeeds)
        {
            Rejected(result, SignatureOccurrenceRejectionReason.MalformedMetadata);
            return;
        }
        var occurrence = Assert.Single(Decoded(result).Occurrences);
        var identity = Assert.IsType<MetadataTypeReferenceScope.AssemblyReference>(occurrence.Reference.Scope).Assembly;
        Assert.Equal(fullKey ? AssemblyReferenceIdentity.ComputePublicKeyToken(
            Enumerable.Repeat((byte)1, length).ToArray()) : "0101010101010101", identity.PublicKeyToken);
        Assert.Equal(length, metrics[fullKey ? SignatureOccurrenceMetric.AssemblyReferenceFullKeyBytes
            : SignatureOccurrenceMetric.AssemblyReferenceTokenBytes].Total);
        Assert.Equal(0, metrics[fullKey ? SignatureOccurrenceMetric.AssemblyReferenceTokenBytes
            : SignatureOccurrenceMetric.AssemblyReferenceFullKeyBytes].Count);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("culture")]
    [InlineData("key")]
    [InlineData("module")]
    public void ClassB_RefusesBeforeCopyingAuthorSizedStorage(string site)
    {
        var fixture = new Fixture();
        string large = new('A', 65_536);
        EntityHandle scope = site == "module"
            ? fixture.Metadata.AddModuleReference(fixture.Metadata.GetOrAddString(large))
            : fixture.Assembly(site == "name" ? large : "A",
                culture: site == "culture" ? large : "",
                key: site == "key" ? new byte[65_536] : null,
                flags: site == "key" ? AssemblyFlags.PublicKey : 0);
        var member = fixture.Member(HandleKind.FieldDefinition, FieldType(fixture.Reference("C", scope, "N")));
        using var image = fixture.Open();
        var limits = SignatureOccurrenceLimits.Default with { Work = 32 };
        Rejected(SignatureOccurrenceDecoder.Decode(image, member, new(), limits),
            SignatureOccurrenceRejectionReason.WorkBudget);
        var metrics = new SignatureOccurrenceMetrics();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = SignatureOccurrenceDecoder.Decode(image, member, metrics, limits);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Rejected(result, SignatureOccurrenceRejectionReason.WorkBudget);
        Assert.True(allocated < 32_768, $"Precharge refusal allocated {allocated} bytes.");
        var metric = site switch
        {
            "name" => SignatureOccurrenceMetric.AssemblyReferenceNameBytes,
            "culture" => SignatureOccurrenceMetric.AssemblyReferenceCultureBytes,
            "key" => SignatureOccurrenceMetric.AssemblyReferenceFullKeyBytes,
            _ => SignatureOccurrenceMetric.ModuleReferenceNameBytes,
        };
        Assert.Equal(65_536, metrics[metric].Total);
    }

    [Fact]
    public void AssemblyAndModuleStoragePricing_UsesBytesNotDecodedCharacters()
    {
        var fixture = new Fixture();
        var assembly = fixture.Assembly("名", culture: "文", key: new byte[8], flags: AssemblyFlags.PublicKey);
        var module = fixture.Metadata.AddModuleReference(fixture.Metadata.GetOrAddString("模"));
        var assemblyMember = fixture.Member(HandleKind.FieldDefinition,
            FieldType(fixture.Reference("C", assembly, "N")));
        var moduleMember = fixture.Member(HandleKind.FieldDefinition,
            FieldType(fixture.Reference("C", module, "N")));
        using var image = fixture.Open();
        var assemblyMetrics = new SignatureOccurrenceMetrics();
        var moduleMetrics = new SignatureOccurrenceMetrics();
        Decoded(SignatureOccurrenceDecoder.Decode(image, assemblyMember, assemblyMetrics));
        var occurrence = Assert.Single(Decoded(SignatureOccurrenceDecoder.Decode(image, moduleMember, moduleMetrics)).Occurrences);
        Assert.Equal("模", Assert.IsType<MetadataTypeReferenceScope.ModuleReference>(occurrence.Reference.Scope).Name);
        Assert.Equal(3, assemblyMetrics[SignatureOccurrenceMetric.AssemblyReferenceNameBytes].Total);
        Assert.Equal(3, assemblyMetrics[SignatureOccurrenceMetric.AssemblyReferenceCultureBytes].Total);
        Assert.Equal(8, assemblyMetrics[SignatureOccurrenceMetric.AssemblyReferenceFullKeyBytes].Total);
        Assert.Equal(19, assemblyMetrics.Work);
        Assert.Equal(3, moduleMetrics[SignatureOccurrenceMetric.ModuleReferenceNameBytes].Total);
        Assert.Equal(8, moduleMetrics.Work);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NameChainCap_IsInclusiveAndRejectedWalkIsMetered(bool reference)
    {
        var fixture = new Fixture();
        EntityHandle parent = reference ? fixture.Assembly("A") : default;
        EntityHandle atCap = default;
        for (int i = 0; i <= MetadataSafetyPolicy.MaxRelationshipNodes; i++)
        {
            EntityHandle current;
            if (reference)
                current = fixture.Reference("T", parent);
            else
            {
                current = fixture.Type("T");
                if (!parent.IsNil)
                    fixture.Metadata.AddNestedType((TypeDefinitionHandle)current, (TypeDefinitionHandle)parent);
            }
            if (i == MetadataSafetyPolicy.MaxRelationshipNodes - 1)
                atCap = current;
            parent = current;
        }
        var accepted = fixture.Member(HandleKind.FieldDefinition, FieldType(atCap));
        var refused = fixture.Member(HandleKind.FieldDefinition, FieldType(parent));
        using var image = fixture.Open();
        Assert.Equal(256, Assert.Single(Decoded(SignatureOccurrenceDecoder.Decode(image, accepted))
            .Occurrences).Reference.Type.Segments.Length);
        var metrics = new SignatureOccurrenceMetrics();
        Rejected(SignatureOccurrenceDecoder.Decode(image, refused, metrics),
            SignatureOccurrenceRejectionReason.RelationshipTraversal);
        Assert.Equal(256, metrics[reference ? SignatureOccurrenceMetric.TypeReferenceNameChainNodes
            : SignatureOccurrenceMetric.TypeDefinitionChainNodes].Total);
    }

    [Fact]
    public void NameCapsAndRelationshipCaps_ArePreserved()
    {
        var fixture = new Fixture();
        var longName = fixture.Type(new string('N', MetadataSafetyPolicy.MaxTypeNameCharacters));
        var longMember = fixture.Member(HandleKind.FieldDefinition, FieldType(longName));
        var cyclic = fixture.Type("Cycle");
        fixture.Metadata.AddNestedType(cyclic, cyclic);
        var cycleMember = fixture.Member(HandleKind.FieldDefinition, FieldType(cyclic));
        using var image = fixture.Open();
        Rejected(SignatureOccurrenceDecoder.Decode(image, longMember), SignatureOccurrenceRejectionReason.TypeNameBudget);
        var metrics = new SignatureOccurrenceMetrics();
        Rejected(SignatureOccurrenceDecoder.Decode(image, cycleMember, metrics),
            SignatureOccurrenceRejectionReason.RelationshipTraversal);
        Assert.Equal(1, metrics[SignatureOccurrenceMetric.TypeDefinitionChainNodes].Total);
    }

    [Theory]
    [InlineData("WindowsRuntime 1.4")]
    [InlineData("WindowsRuntime 1.4;CLR v4.0.30319")]
    public void WindowsMetadata_IsRefusedBeforeReaderConstruction(string version)
    {
        var fixture = new Fixture();
        var member = fixture.Member(HandleKind.FieldDefinition, [0x06, 0x08]);
        byte[] bytes = fixture.Bytes(version);
        using (var original = new PEReader(ImmutableArray.Create(bytes)))
        {
            int metadataStart = original.PEHeaders.MetadataStartOffset;
            int versionLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metadataStart + 12, 4));
            bytes.AsSpan(metadataStart + 16 + versionLength, 4).Fill(0xff);
        }
        using var image = new PEReader(ImmutableArray.Create(bytes));
        Assert.Throws<OverflowException>(() => image.GetMetadataReader());
        Rejected(SignatureOccurrenceDecoder.Decode(image, member),
            SignatureOccurrenceRejectionReason.UnsupportedWindowsMetadata);
    }

    [Fact]
    public void NoMetadataAndMalformedImage_AreTypedOutcomes()
    {
        var fixture = new Fixture();
        var member = fixture.Member(HandleKind.FieldDefinition, [0x06, 0x08]);
        byte[] bytes = fixture.Bytes();
        using (var original = new PEReader(ImmutableArray.Create(bytes)))
        {
            int directories = original.PEHeaders.PEHeaderStartOffset
                + (original.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96);
            bytes.AsSpan(directories + 14 * 8, 8).Clear();
        }
        using var native = new PEReader(ImmutableArray.Create(bytes));
        Rejected(SignatureOccurrenceDecoder.Decode(native, member), SignatureOccurrenceRejectionReason.NoMetadata);
        using var malformed = new PEReader(ImmutableArray.Create<byte>(1, 2, 3));
        Rejected(SignatureOccurrenceDecoder.Decode(malformed, member), SignatureOccurrenceRejectionReason.MalformedMetadata);
    }

    [Fact]
    public void ReaderConstructionOverflow_IsMappedOnlyAtTheMetadataConstructionBoundary()
    {
        var fixture = new Fixture();
        var member = fixture.Member(HandleKind.FieldDefinition, [0x06, 0x08]);
        byte[] bytes = fixture.Bytes();
        using (var original = new PEReader(ImmutableArray.Create(bytes)))
        {
            int metadataStart = original.PEHeaders.MetadataStartOffset;
            int versionLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metadataStart + 12, 4));
            bytes.AsSpan(metadataStart + 16 + versionLength, 4).Fill(0xff);
        }
        using var image = new PEReader(ImmutableArray.Create(bytes));
        Rejected(SignatureOccurrenceDecoder.Decode(image, member), SignatureOccurrenceRejectionReason.MalformedMetadata);
    }

    [Fact]
    public void CallerErrors_AreNotArtifactRefusals()
    {
        using var image = new Fixture().Open();
        Assert.Throws<ArgumentNullException>(() =>
            SignatureOccurrenceDecoder.Decode(null!, MetadataTokens.FieldDefinitionHandle(1)));
        Assert.Throws<ArgumentException>(() => SignatureOccurrenceDecoder.Decode(image, default));
        Assert.Throws<ArgumentException>(() =>
            SignatureOccurrenceDecoder.Decode(image, MetadataTokens.TypeDefinitionHandle(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SignatureOccurrenceDecoder.Decode(image, MetadataTokens.FieldDefinitionHandle(1), new(),
                SignatureOccurrenceLimits.Default with { Work = int.MaxValue }));
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            SignatureOccurrenceDecoder.Decode(image, MetadataTokens.FieldDefinitionHandle(1)));
    }

    static SignatureOccurrenceDecodeResult.Decoded Decoded(SignatureOccurrenceDecodeResult result) =>
        Assert.IsType<SignatureOccurrenceDecodeResult.Decoded>(result);

    static void Rejected(SignatureOccurrenceDecodeResult result, SignatureOccurrenceRejectionReason reason) =>
        Assert.Equal(reason, Assert.IsType<SignatureOccurrenceDecodeResult.Rejected>(result).Reason);

    static byte[] FieldType(EntityHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        Type(signature, type);
        return signature.ToArray();
    }

    static void Type(BlobBuilder signature, EntityHandle type)
    {
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(type));
    }

    static byte[] ModifiedField(EntityHandle modifier)
    {
        var signature = new BlobBuilder();
        signature.WriteBytes((byte[])[0x06, 0x20]);
        signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(modifier));
        signature.WriteByte(0x08);
        return signature.ToArray();
    }

    sealed class Fixture
    {
        internal MetadataBuilder Metadata { get; } = new();

        internal Fixture()
        {
            Metadata.AddModule(0, Metadata.GetOrAddString("Occurrences.dll"),
                Metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            Metadata.AddAssembly(Metadata.GetOrAddString("Occurrences"), new Version(1, 0),
                default, default, 0, AssemblyHashAlgorithm.None);
            Type("<Module>");
        }

        internal TypeDefinitionHandle Type(string name, string ns = "") =>
            Metadata.AddTypeDefinition(TypeAttributes.Public, Metadata.GetOrAddString(ns),
                Metadata.GetOrAddString(name), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        internal AssemblyReferenceHandle Assembly(
            string name, string culture = "", byte[]? key = null, AssemblyFlags flags = 0) =>
            Metadata.AddAssemblyReference(Metadata.GetOrAddString(name), new Version(1, 0),
                Metadata.GetOrAddString(culture), key is null ? default : Metadata.GetOrAddBlob(key), flags, default);

        internal TypeReferenceHandle Reference(string name, EntityHandle scope, string ns = "") =>
            Metadata.AddTypeReference(scope, Metadata.GetOrAddString(ns), Metadata.GetOrAddString(name));

        internal EntityHandle Member(HandleKind kind, byte[] signature)
        {
            var blob = Metadata.GetOrAddBlob(signature);
            var name = Metadata.GetOrAddString("M");
            return kind switch
            {
                HandleKind.MethodDefinition => Metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL, name,
                    blob, 0, MetadataTokens.ParameterHandle(1)),
                HandleKind.FieldDefinition => Metadata.AddFieldDefinition(FieldAttributes.Public, name, blob),
                HandleKind.PropertyDefinition => Metadata.AddProperty(PropertyAttributes.None, name, blob),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        internal PEReader Open() => new(ImmutableArray.Create(Bytes()));

        internal byte[] Bytes(string version = "v4.0.30319")
        {
            var builder = new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
                new MetadataRootBuilder(Metadata, version), new BlobBuilder(), flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            builder.Serialize(image);
            return image.ToArray();
        }
    }
}
