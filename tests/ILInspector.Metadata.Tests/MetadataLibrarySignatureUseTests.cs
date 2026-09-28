using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.ConsumerCanary;
using ILInspector.Metadata.SignatureUseFixtures;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataLibrarySignatureUseTests
{
    [Fact]
    public void CompletePopulationRetainsEveryDeclarationSiteAndExactEndpoint()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(FixtureAnchor).Assembly.Location);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Complete,
            result.Disposition);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            result.Coverage.Considered,
            result.Coverage.Examined);
        Assert.Equal(0, result.Coverage.Unavailable);
        Assert.Equal(0, result.Coverage.Limited);

        MetadataLibrarySignatureType owner =
            Type(result, "SignatureOwner`1");
        MetadataLibrarySignatureType local =
            Type(result, "LocalTarget");
        MetadataLibrarySignatureType value =
            Type(result, "LocalValue");
        MetadataLibrarySignatureType peer =
            Type(result, "InternalPeer");
        Assert.All(
            result.Types,
            type => Assert.Equal(
                result.Receipt.ModuleVersionId,
                type.Type.ModuleVersionId));
        Assert.Contains(
            result.Types,
            static type =>
                type.Name.Segments is ["InternalPeer"]);
        AssertClassification(
            result,
            "LocalEnum",
            MetadataLibraryTypeClassification.Enum);
        AssertClassification(
            result,
            "LocalDelegate",
            MetadataLibraryTypeClassification.Delegate);
        AssertClassification(
            result,
            "LocalAttribute",
            MetadataLibraryTypeClassification.Attribute);
        AssertClassification(
            result,
            "LocalException",
            MetadataLibraryTypeClassification.Exception);
        AssertClassification(
            result,
            "ConstructedException",
            MetadataLibraryTypeClassification.Exception);
        AssertClassification(
            result,
            "ConstructedExceptionLeaf",
            MetadataLibraryTypeClassification.Exception);
        AssertClassification(
            result,
            "ConstructedAttribute",
            MetadataLibraryTypeClassification.Attribute);

        AssertOccurrence(
            result,
            owner,
            Type(result, "Base`1"),
            MetadataLibrarySignatureUseSiteKind.BaseType);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.BaseType);
        AssertOccurrence(
            result,
            owner,
            Type(result, "IContract`1"),
            MetadataLibrarySignatureUseSiteKind.Interface);
        AssertOccurrence(
            result,
            owner,
            peer,
            MetadataLibrarySignatureUseSiteKind.Interface);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.TypeConstraint);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.FieldType);
        AssertOccurrence(
            result,
            owner,
            owner,
            MetadataLibrarySignatureUseSiteKind.FieldType);
        AssertOccurrence(
            result,
            owner,
            value,
            MetadataLibrarySignatureUseSiteKind.FieldType);
        AssertOccurrence(
            result,
            owner,
            peer,
            MetadataLibrarySignatureUseSiteKind.PropertyType);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.PropertyParameter);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.EventType);
        AssertOccurrence(
            result,
            owner,
            peer,
            MetadataLibrarySignatureUseSiteKind.EventType);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.MethodReturn);
        AssertOccurrence(
            result,
            owner,
            peer,
            MetadataLibrarySignatureUseSiteKind.MethodParameter);
        AssertOccurrence(
            result,
            owner,
            local,
            MetadataLibrarySignatureUseSiteKind.MethodConstraint);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.Source == owner.Type
                && occurrence.Target == local.Type
                && occurrence.SiteKind
                    == MetadataLibrarySignatureUseSiteKind.EventType
                && occurrence.OccurrenceOrdinal > 0);
        Assert.Equal(
            3,
            result.Occurrences.Count(
                occurrence =>
                    occurrence.Source == owner.Type
                    && occurrence.Target == local.Type
                    && occurrence.SiteKind
                        == MetadataLibrarySignatureUseSiteKind.FieldType));
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.Source == owner.Type
                && occurrence.Target == local.Type
                && occurrence.SiteKind
                    == MetadataLibrarySignatureUseSiteKind.MethodReturn);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.Source == owner.Type
                && occurrence.Target == local.Type
                && occurrence.SiteKind
                    == MetadataLibrarySignatureUseSiteKind.MethodParameter);
    }

    [Fact]
    public void RelationshipLimitProducesQualifiedPartialPopulation()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(FixtureAnchor).Assembly.Location);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(
                        new MetadataOperationPolicy(
                            maxMetadataRows: long.MaxValue,
                            maxRelationshipEdges: 1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Partial,
            result.Disposition);
        Assert.True(result.Coverage.Limited > 0);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.Limit,
            Assert.Single(result.Diagnostics).Kind);
        Assert.True(result.Occurrences.Length <= 1);
    }

    [Theory]
    [InlineData(MetadataOperationDimension.SignatureBytes)]
    [InlineData(MetadataOperationDimension.StructuredNodes)]
    [InlineData(MetadataOperationDimension.RetainedText)]
    [InlineData(
        MetadataOperationDimension.InterfaceImplementationRows)]
    public void ProducerWorkParticipatesInOperationLimits(
        MetadataOperationDimension dimension)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(FixtureAnchor).Assembly.Location);
        MetadataLibrarySignatureUseResult baseline =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        long exactLimit = dimension switch
        {
            MetadataOperationDimension.SignatureBytes =>
                baseline.Receipt.Counters.SignatureBytes,
            MetadataOperationDimension.StructuredNodes =>
                baseline.Receipt.Counters.StructuredNodes,
            MetadataOperationDimension.RetainedText =>
                baseline.Receipt.Counters.RetainedText,
            MetadataOperationDimension.InterfaceImplementationRows =>
                baseline.Receipt.Counters.InterfaceImplementationRows,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension)),
        };
        Assert.True(exactLimit > 0);

        MetadataLibrarySignatureUseResult exact =
            Available(
                session.LibrarySignatureUses(
                    new(Limit(dimension, exactLimit)),
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Complete,
            exact.Disposition);
        MetadataLibrarySignatureUseResult limited =
            Available(
                session.LibrarySignatureUses(
                    new(Limit(dimension, exactLimit - 1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Partial,
            limited.Disposition);
        MetadataLibrarySignatureUseDiagnostic diagnostic =
            Assert.Single(limited.Diagnostics);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(dimension, diagnostic.BudgetDimension);
        Assert.True(limited.Coverage.Limited > 0);
    }

    [Fact]
    public void RepeatedExecutionIsCanonicalAndDetached()
    {
        MetadataLibrarySignatureUseResult first;
        MetadataLibrarySignatureUseResult second;
        using (AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(FixtureAnchor).Assembly.Location))
        {
            first = Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
            second = Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        }

        Assert.Equal(first.Types, second.Types);
        Assert.Equal(first.Occurrences, second.Occurrences);
        Assert.Equal(first.Coverage, second.Coverage);
        Assert.NotEmpty(first.Types);
        Assert.NotEmpty(first.Occurrences);
    }

    [Fact]
    public void PublicContractHasIndependentConsumerCanary()
    {
        MetadataLibrarySignatureUseOutcome outcome =
            MetadataMethodImplementationConsumerCanary
                .InspectLibrarySignatureUses(
                    typeof(FixtureAnchor).Assembly.Location);

        Assert.True(
            MetadataMethodImplementationConsumerCanary.Consume(outcome));
    }

    [Fact]
    public void MalformedSignatureProducesVisiblePartialPopulation()
    {
        byte[] image = BuildMalformedSignatureImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(1, result.Coverage.Unavailable);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.MalformedMetadata,
            Assert.Single(result.Diagnostics).Kind);
        Assert.Contains(
            result.Types,
            static type => type.Name.Segments is ["Owner"]);
    }

    [Fact]
    public void MalformedEventTypeSpecificationProducesVisiblePartialPopulation()
    {
        byte[] image = BuildMalformedEventTypeSpecificationImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(
            new(
                considered: 1,
                examined: 0,
                unavailable: 1,
                limited: 0),
            result.Coverage);
        MetadataLibrarySignatureUseDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.MalformedMetadata,
            diagnostic.Kind);
        Assert.Equal(0x14000001, diagnostic.MetadataToken);
        Assert.Contains(
            result.Types,
            static type => type.Name.Segments is ["Owner"]);
    }

    [Fact]
    public void DuplicateExactTypeNameRejectsPopulation()
    {
        byte[] image = BuildDuplicateTypeNameImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        var rejected =
            Assert.IsType<MetadataLibrarySignatureUseOutcome.Rejected>(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseRejectionKind.TypeInventory,
            rejected.Kind);
        Assert.Contains(
            "same exact",
            rejected.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModifierParticipationPreservesPhysicalOccurrenceOrdinals()
    {
        byte[] image = BuildModifierImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        MetadataLibrarySignatureType owner =
            Assert.Single(
                result.Types,
                static type => type.Name.Segments is ["Owner"]);
        MetadataLibrarySignatureType modifier =
            Assert.Single(
                result.Types,
                static type => type.Name.Segments is ["Modifier"]);
        MetadataLibrarySignatureType target =
            Assert.Single(
                result.Types,
                static type => type.Name.Segments is ["Target"]);
        MetadataLibrarySignatureUseOccurrence[] fields =
            result.Occurrences
                .Where(occurrence =>
                    occurrence.Source == owner.Type
                    && occurrence.SiteKind
                        == MetadataLibrarySignatureUseSiteKind.FieldType)
                .ToArray();

        Assert.Contains(
            fields,
            occurrence =>
                occurrence.Target == modifier.Type
                && occurrence.OccurrenceOrdinal == 0);
        Assert.Equal(
            2,
            fields.Count(
                occurrence =>
                    occurrence.Target == target.Type
                    && occurrence.OccurrenceOrdinal == 1));
        Assert.Single(
            fields,
            occurrence => occurrence.Target == modifier.Type);
        int foreignFieldToken =
            MetadataTokens.GetToken(
                MetadataTokens.FieldDefinitionHandle(3));
        Assert.DoesNotContain(
            result.Occurrences,
            occurrence => occurrence.MetadataToken == foreignFieldToken);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SystemTextJsonProducesCompleteAllAccessibilityPopulation()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "runtime",
            "System.Text.Json.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Complete,
            result.Disposition);
        Assert.True(result.Types.Length > 100);
        Assert.True(result.Occurrences.Length > 1_000);
        Assert.Contains(
            result.Types,
            static type =>
                type.Name.ToMetadataFullName()
                    == "System.Text.Json.JsonSerializerOptions");
        Assert.Contains(
            result.Occurrences,
            static occurrence =>
                occurrence.TargetType.ToMetadataFullName()
                    == "System.Text.Json.JsonSerializerOptions");
    }

    private static MetadataLibrarySignatureUseResult Available(
        MetadataLibrarySignatureUseOutcome outcome) =>
        Assert.IsType<
            MetadataLibrarySignatureUseOutcome.Available>(outcome).Result;

    private static MetadataLibrarySignatureType Type(
        MetadataLibrarySignatureUseResult result,
        string terminalName) =>
        Assert.Single(
            result.Types,
            type =>
                type.Name.Namespace
                    == "ILInspector.Metadata.SignatureUseFixtures"
                && type.Name.Segments[^1] == terminalName);

    private static void AssertOccurrence(
        MetadataLibrarySignatureUseResult result,
        MetadataLibrarySignatureType source,
        MetadataLibrarySignatureType target,
        MetadataLibrarySignatureUseSiteKind kind) =>
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.Source == source.Type
                && occurrence.Target == target.Type
                && occurrence.SiteKind == kind);

    private static void AssertClassification(
        MetadataLibrarySignatureUseResult result,
        string terminalName,
        MetadataLibraryTypeClassification classification) =>
        Assert.True(
            Type(result, terminalName).Classification.HasFlag(
                classification));

    private static byte[] BuildMalformedSignatureImage()
    {
        var metadata = CreateMetadata("MalformedSignature");
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Broken"),
            metadata.GetOrAddBlob((byte[])[0x06, 0x12, 0xFF]));
        return Serialize(metadata);
    }

    private static byte[] BuildDuplicateTypeNameImage()
    {
        var metadata = CreateMetadata("DuplicateTypeName");
        AddModuleType(metadata);
        for (int i = 0; i < 2; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Duplicate"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        return Serialize(metadata);
    }

    private static byte[] BuildMalformedEventTypeSpecificationImage()
    {
        var metadata = CreateMetadata("MalformedEventType");
        AddModuleType(metadata);
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeSpecificationHandle type =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob((byte[])[0xFF]));
        EventDefinitionHandle @event =
            metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Broken"),
                type);
        metadata.AddEventMap(owner, @event);
        return Serialize(metadata);
    }

    private static byte[] BuildModifierImage()
    {
        var metadata = CreateMetadata("Modifiers");
        AddModuleType(metadata);
        TypeDefinitionHandle modifier = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Modifier"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AddModifiedField(
            metadata,
            "Required",
            required: true,
            modifier,
            target);
        AddModifiedField(
            metadata,
            "Optional",
            required: false,
            modifier,
            target);
        AssemblyReferenceHandle external =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle sameDisplayExternal =
            metadata.AddTypeReference(
                external,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"));
        AddTypeField(
            metadata,
            "ForeignSameDisplay",
            sameDisplayExternal);
        return Serialize(metadata);
    }

    private static MetadataBuilder CreateMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        return metadata;
    }

    private static void AddModuleType(MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    private static void AddModifiedField(
        MetadataBuilder metadata,
        string name,
        bool required,
        TypeDefinitionHandle modifier,
        TypeDefinitionHandle target)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(required ? (byte)0x1F : (byte)0x20);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(modifier));
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(target));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature));
    }

    private static void AddTypeField(
        MetadataBuilder metadata,
        string name,
        EntityHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(type));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature));
    }

    private static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static MetadataOperationPolicy Limit(
        MetadataOperationDimension dimension,
        long limit) =>
        new(
            long.MaxValue,
            maxSignatureBytes:
                dimension == MetadataOperationDimension.SignatureBytes
                    ? limit
                    : long.MaxValue,
            maxStructuredNodes:
                dimension == MetadataOperationDimension.StructuredNodes
                    ? limit
                    : long.MaxValue,
            maxRetainedText:
                dimension == MetadataOperationDimension.RetainedText
                    ? limit
                    : long.MaxValue,
            maxInterfaceImplementationRows:
                dimension
                    == MetadataOperationDimension
                        .InterfaceImplementationRows
                    ? limit
                    : long.MaxValue);
}
