using System.Buffers.Binary;
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
    public void ExactNamespaceAdmitsOnlyItsTypesSitesAndRelationships()
    {
        const string exactNamespace =
            "ILInspector.Metadata.SignatureUseFixtures.ShardA";
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(FixtureAnchor).Assembly.Location);

        MetadataLibrarySignatureUseResult whole =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        MetadataLibrarySignatureUseResult shard =
            Available(
                session.LibrarySignatureUses(
                    new(
                        MetadataOperationPolicy.Unbounded,
                        exactNamespace),
                    TestContext.Current.CancellationToken));

        Assert.Null(whole.Receipt.ExactNamespace);
        Assert.Equal(exactNamespace, shard.Receipt.ExactNamespace);
        Assert.Equal(
            ["NamespaceSource", "NamespacePeer"],
            shard.Types.Select(
                static type => type.Name.Segments.Single()));
        Assert.All(
            shard.Types,
            type => Assert.Equal(exactNamespace, type.Name.Namespace));
        Assert.All(
            shard.Occurrences,
            occurrence =>
            {
                Assert.Equal(
                    exactNamespace,
                    occurrence.SourceType.Namespace);
                Assert.Equal(
                    exactNamespace,
                    occurrence.TargetType.Namespace);
            });
        Assert.Contains(
            shard.Occurrences,
            static occurrence =>
                occurrence.SourceType.Segments is ["NamespaceSource"]
                && occurrence.TargetType.Segments is ["NamespacePeer"]);
        Assert.Contains(
            shard.Occurrences,
            static occurrence =>
                occurrence.SourceType.Segments is ["NamespacePeer"]
                && occurrence.TargetType.Segments is ["NamespaceSource"]);
        Assert.DoesNotContain(
            shard.Types,
            static type =>
                type.Name.Segments is ["NamespaceExternal"]);
        Assert.True(
            shard.Coverage.Considered < whole.Coverage.Considered);
        Assert.Contains(
            whole.Occurrences,
            static occurrence =>
                occurrence.SourceType.Segments is ["NamespaceSource"]
                && occurrence.TargetType.Segments
                    is ["NamespaceExternal"]);
    }

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

    [Theory]
    [InlineData(TableIndex.Event)]
    [InlineData(TableIndex.GenericParamConstraint)]
    public void MalformedRelationshipRowRetainsHealthyEvidence(
        TableIndex table)
    {
        byte[] image = BuildMalformedRelationshipRowImage(table);
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
                considered: 2,
                examined: 1,
                unavailable: 1,
                limited: 0),
            result.Coverage);
        MetadataLibrarySignatureUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(
            MetadataLibrarySignatureUseSiteKind.FieldType,
            occurrence.SiteKind);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.MalformedMetadata,
            Assert.Single(result.Diagnostics).Kind);
    }

    [Theory]
    [InlineData(HandleKind.TypeSpecification)]
    [InlineData(HandleKind.TypeReference)]
    public void MalformedBaseRetainsHealthyEvidence(
        HandleKind malformedBaseKind)
    {
        byte[] image = BuildMalformedBaseImage(malformedBaseKind);
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
                considered: 2,
                examined: 1,
                unavailable: 1,
                limited: 0),
            result.Coverage);
        MetadataLibrarySignatureUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(
            MetadataLibrarySignatureUseSiteKind.FieldType,
            occurrence.SiteKind);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.MalformedMetadata,
            Assert.Single(result.Diagnostics).Kind);
        Assert.Equal(
            MetadataLibraryTypeClassification.None,
            Assert.Single(
                result.Types,
                static type => type.Name.Segments is ["Owner"])
                .Classification);
    }

    [Fact]
    public void MalformedConstructedBaseArgumentPreventsPositiveClassification()
    {
        byte[] image = BuildMalformedConstructedBaseImage();
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
                considered: 3,
                examined: 2,
                unavailable: 1,
                limited: 0),
            result.Coverage);
        Assert.True(
            Assert.Single(
                result.Types,
                static type =>
                    type.Name.Segments is ["GenericException`1"])
                .Classification.HasFlag(
                    MetadataLibraryTypeClassification.Exception));
        Assert.Equal(
            MetadataLibraryTypeClassification.None,
            Assert.Single(
                result.Types,
                static type => type.Name.Segments is ["Owner"])
                .Classification);
        Assert.Equal(
            MetadataLibrarySignatureUseSiteKind.FieldType,
            Assert.Single(result.Occurrences).SiteKind);
    }

    [Fact]
    public void TypeNameBoundProducesTypedLimitedSite()
    {
        byte[] image = BuildLocallyBoundedFieldImage(
            longTypeNameLength: 5_000);
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        AssertLocalLimit(
            result,
            MetadataOperationDimension.RetainedText,
            MetadataSafetyPolicy.MaxTypeNameCharacters,
            attemptedCharge: 5_001);
    }

    [Fact]
    public void EncodedTypeNameBoundProducesActualTypedLimit()
    {
        byte[] image = BuildLocallyBoundedFieldImage(
            longTypeNameLength: 13_000);
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        AssertLocalLimit(
            result,
            MetadataOperationDimension.RetainedText,
            MetadataTypeNameBudget.MaxEncodedBytes,
            attemptedCharge: 13_001);
    }

    [Fact]
    public void StructuralDepthBoundProducesTypedLimitedSite()
    {
        byte[] image = BuildLocallyBoundedFieldImage(
            longTypeNameLength: null);
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        AssertLocalLimit(
            result,
            MetadataOperationDimension.StructuredNodes,
            SignatureBlobGuard.DefaultMaxDepth);
    }

    [Fact]
    public void BulkStructuralNodeBoundProducesActualAttemptedCharge()
    {
        const int parameterCount = 70_000;
        byte[] image = BuildBulkMethodSignatureImage(parameterCount);
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        AssertLocalLimit(
            result,
            MetadataOperationDimension.StructuredNodes,
            MetadataSafetyPolicy.MaxSignatureTypeNodes,
            attemptedCharge: parameterCount + 1);
    }

    [Fact]
    public void TypeSpecificationDepthProducesTypedLimitedSite()
    {
        byte[] image = BuildTypeSpecificationDepthImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        AssertLocalLimit(
            result,
            MetadataOperationDimension.StructuredNodes,
            TypeSpecGuard.MaxDepth);
        Assert.True(
            result.Receipt.Counters.SignatureBytes
                < TypeSpecGuard.MaxCumulativeBytes);
    }

    [Fact]
    public void ForeignCoreLookalikesDoNotProducePositiveClassification()
    {
        byte[] image = BuildForeignCoreLookalikeImage();
        using var stream = new MemoryStream(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(stream);

        MetadataLibrarySignatureUseResult result =
            Available(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Complete,
            result.Disposition);
        MetadataLibrarySignatureType foreignEnum =
            Type(result, "ForeignEnum");
        Assert.Equal(
            AssemblyTypeDefinitionKind.Enum,
            foreignEnum.DefinitionKind);
        Assert.Equal(
            MetadataLibraryTypeClassification.None,
            foreignEnum.Classification);
        MetadataLibrarySignatureType foreignDelegate =
            Type(result, "ForeignDelegate");
        Assert.Equal(
            AssemblyTypeDefinitionKind.Delegate,
            foreignDelegate.DefinitionKind);
        Assert.Equal(
            MetadataLibraryTypeClassification.None,
            foreignDelegate.Classification);
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
            "packages",
            "System.Text.Json.10.0.0.dll");
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
        Assert.Equal(
            "System.Text.Json",
            result.Receipt.Assembly.Name);
        Assert.Equal(
            new Version(10, 0, 0, 0),
            result.Receipt.Assembly.Version);
        Assert.Equal(
            "cc7b13ffcd2ddd51",
            result.Receipt.Assembly.PublicKeyToken,
            ignoreCase: true);
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

    private static void AssertLocalLimit(
        MetadataLibrarySignatureUseResult result,
        MetadataOperationDimension dimension,
        long limit,
        long? attemptedCharge = null)
    {
        Assert.Equal(
            MetadataLibrarySignatureUseDisposition.Partial,
            result.Disposition);
        MetadataLibrarySignatureUseDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            MetadataLibrarySignatureUseDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(
            new(
                considered: 2,
                examined: 1,
                unavailable: 0,
                limited: 1),
            result.Coverage);
        Assert.Equal(
            MetadataLibrarySignatureUseSiteKind.FieldType,
            Assert.Single(result.Occurrences).SiteKind);
        Assert.Equal(dimension, diagnostic.BudgetDimension);
        Assert.Equal(limit, diagnostic.BudgetLimit);
        Assert.Equal(
            attemptedCharge ?? limit + 1,
            diagnostic.AttemptedCharge);
    }

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

    private static byte[] BuildMalformedRelationshipRowImage(
        TableIndex table)
    {
        var metadata = CreateMetadata("MalformedRelationship");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        AddTypeField(metadata, "Healthy", target);
        if (table == TableIndex.Event)
        {
            EventDefinitionHandle @event =
                metadata.AddEvent(
                    EventAttributes.None,
                    metadata.GetOrAddString("Broken"),
                    target);
            metadata.AddEventMap(owner, @event);
        }
        else
        {
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    owner,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                target);
        }

        byte[] image = Serialize(metadata);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        int rowSize = reader.GetTableRowSize(table);
        int codedIndexSize =
            new[]
            {
                reader.GetTableRowCount(TableIndex.TypeDef),
                reader.GetTableRowCount(TableIndex.TypeRef),
                reader.GetTableRowCount(TableIndex.TypeSpec),
            }.Max() < (1 << 14)
                ? sizeof(ushort)
                : sizeof(uint);
        Assert.Equal(sizeof(ushort), codedIndexSize);
        int codedIndexOffset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(table)
            + rowSize
            - codedIndexSize;
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(codedIndexOffset, codedIndexSize),
            0x0007);
        return image;
    }

    private static byte[] BuildForeignCoreLookalikeImage()
    {
        var metadata = CreateMetadata("ForeignCoreLookalikes");
        AddModuleType(metadata);
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("OrdinaryDependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle foreignEnum =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Enum"));
        TypeReferenceHandle foreignDelegate =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("MulticastDelegate"));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(
                "ILInspector.Metadata.SignatureUseFixtures"),
            metadata.GetOrAddString("ForeignEnum"),
            foreignEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(
                "ILInspector.Metadata.SignatureUseFixtures"),
            metadata.GetOrAddString("ForeignDelegate"),
            foreignDelegate,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    private static byte[] BuildMalformedBaseImage(
        HandleKind malformedBaseKind)
    {
        var metadata = CreateMetadata("MalformedBase");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        EntityHandle malformedBase;
        if (malformedBaseKind == HandleKind.TypeSpecification)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(
                    MetadataTokens.TypeDefinitionHandle(99)));
            malformedBase =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature));
        }
        else
        {
            malformedBase =
                metadata.AddTypeReference(
                    MetadataTokens.AssemblyReferenceHandle(99),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("BrokenBase"));
        }
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            malformedBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AddTypeField(metadata, "Healthy", target);
        return Serialize(metadata);
    }

    private static byte[] BuildLocallyBoundedFieldImage(
        int? longTypeNameLength)
    {
        var metadata = CreateMetadata("LocallyBoundedField");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
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
        AddTypeField(metadata, "Healthy", target);

        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        if (longTypeNameLength is { } nameLength)
        {
            AssemblyReferenceHandle dependency =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Dependency"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle longName =
                metadata.AddTypeReference(
                    dependency,
                    default,
                    metadata.GetOrAddString(
                        new string('N', nameLength)));
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(longName));
        }
        else
        {
            for (int depth = 0;
                depth <= SignatureBlobGuard.DefaultMaxDepth;
                depth++)
            {
                signature.WriteByte(0x1D);
            }
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(target));
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Bounded"),
            metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    private static byte[] BuildBulkMethodSignatureImage(
        int parameterCount)
    {
        var metadata = CreateMetadata("BulkMethodSignature");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
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
        AddTypeField(metadata, "Healthy", target);

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        signature.WriteBytes(0x08, parameterCount);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Bounded"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    private static byte[] BuildMalformedConstructedBaseImage()
    {
        var metadata = CreateMetadata("MalformedConstructedBase");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    Convert.FromHexString("7CEC85D7BEA7798E")),
                default,
                default);
        TypeReferenceHandle exception =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Exception"));
        TypeDefinitionHandle genericException =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("GenericException`1"),
                exception,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            genericException,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);

        var baseSignature = new BlobBuilder();
        baseSignature.WriteByte(0x15);
        baseSignature.WriteByte(0x12);
        baseSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(genericException));
        baseSignature.WriteCompressedInteger(1);
        baseSignature.WriteByte(0x12);
        baseSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(
                MetadataTokens.TypeDefinitionHandle(99)));
        TypeSpecificationHandle constructedBase =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(baseSignature));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            constructedBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AddTypeField(metadata, "Healthy", target);
        return Serialize(metadata);
    }

    private static byte[] BuildTypeSpecificationDepthImage()
    {
        var metadata = CreateMetadata("TypeSpecificationDepth");
        AddModuleType(metadata);
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
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

        for (int row = 1; row <= TypeSpecGuard.MaxDepth; row++)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x1F);
            signature.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(
                    MetadataTokens.TypeSpecificationHandle(row + 1)));
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(target));
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        }
        var terminal = new BlobBuilder();
        terminal.WriteByte(0x12);
        terminal.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(target));
        metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(terminal));

        var bounded = new BlobBuilder();
        bounded.WriteByte(0x06);
        bounded.WriteByte(0x1F);
        bounded.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(
                MetadataTokens.TypeSpecificationHandle(1)));
        bounded.WriteByte(0x12);
        bounded.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(target));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Bounded"),
            metadata.GetOrAddBlob(bounded));
        AddTypeField(metadata, "Healthy", target);
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
