using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Fixtures;
using Inspector.Artifacts;

namespace ILInspector.Metadata.Tests;

public class PdbSourceProvenanceTests
{
    [Fact]
    public void PathOpenedContext_RequiresArtifactBinding()
    {
        using PdbContext context = PdbContext.OpenEmbeddedPdbOnly(
            typeof(EmbeddedSourceFixture).Assembly.Location);

        var unavailable =
            Assert.IsType<PdbSourceProvenanceOutcome.Unavailable>(
                context.InspectSourceProvenance());

        Assert.Equal(
            PdbSourceProvenanceUnavailableReason.MissingArtifactBinding,
            unavailable.Reason);
    }

    [Fact]
    public void ArtifactBoundContext_ProducesCompleteDetachedPopulations()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        PdbSourceProvenanceResult result;

        using (PdbContext context =
               PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly))
        {
            PdbSourceProvenanceOutcome outcome =
                context.InspectSourceProvenance();
            Assert.True(
                outcome is PdbSourceProvenanceOutcome.Available,
                outcome.ToString());
            result =
                ((PdbSourceProvenanceOutcome.Available)outcome).Result;
        }

        Assert.Same(artifact.Registration.Artifact, result.Binding.Artifact);
        Assert.Same(
            artifact.Registration.Generation,
            result.Binding.ArtifactGeneration);
        Assert.NotEqual(Guid.Empty, result.Binding.ModuleVersionId);
        Assert.True(result.Binding.PortablePdbContentId.Length >= 20);
        Assert.Equal(
            Enumerable.Range(1, result.Documents.Length),
            result.Documents.Select(static document =>
                document.DocumentRowId));
        Assert.Equal(result.Documents.Length, result.Receipt.DocumentCount);
        Assert.Equal(result.Types.Length, result.Receipt.TypeCount);
        Assert.Equal(
            result.Types.Length,
            result.Receipt.OrdinaryEvidenceOnlyCount
                + result.Receipt.GeneratedEvidenceOnlyCount
                + result.Receipt.MixedEvidenceCount
                + result.Receipt.UnknownCount);
        Assert.All(
            result.Types,
            type => Assert.Equal(
                result.Binding.ModuleVersionId,
                type.Type.ModuleVersionId));
    }

    [Fact]
    public void MetadataOnlyContext_ReportsPdbUnavailable()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenMetadataOnly(artifact.Assembly);

        var unavailable =
            Assert.IsType<PdbSourceProvenanceOutcome.Unavailable>(
                context.InspectSourceProvenance());

        Assert.Equal(
            PdbSourceProvenanceUnavailableReason.PdbNotLoaded,
            unavailable.Reason);
    }

    [Fact]
    public void RejectedPdbs_RemainDistinctFromAvailableUnknownEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        byte[] foreignPdb = BuildMarkerMetadata().Pdb;
        using (PdbContext mismatch =
               PdbContext.OpenMetadataOnly(artifact.Assembly))
        {
            mismatch.LoadPdbFromStream(
                new MemoryStream(foreignPdb, writable: false));
            AssertUnavailable(
                mismatch,
                PdbSourceProvenanceUnavailableReason.IdentityMismatch);
        }

        using (PdbContext malformed =
               PdbContext.OpenMetadataOnly(artifact.Assembly))
        {
            malformed.LoadPdbFromStream(
                new MemoryStream(
                    new byte[] { (byte)'B', (byte)'S', (byte)'J', (byte)'B', 0 },
                    writable: false));
            AssertUnavailable(
                malformed,
                PdbSourceProvenanceUnavailableReason.MalformedPdb);
        }

        using (PdbContext unsupported =
               PdbContext.OpenMetadataOnly(artifact.Assembly))
        {
            unsupported.LoadPdbFromStream(
                new MemoryStream(new byte[] { 1, 2, 3, 4 }, writable: false));
            AssertUnavailable(
                unsupported,
                PdbSourceProvenanceUnavailableReason.UnsupportedPdb);
        }

        static void AssertUnavailable(
            PdbContext context,
            PdbSourceProvenanceUnavailableReason expected)
        {
            var unavailable =
                Assert.IsType<PdbSourceProvenanceOutcome.Unavailable>(
                    context.InspectSourceProvenance());
            Assert.Equal(expected, unavailable.Reason);
        }
    }

    [Fact]
    public void TypeBound_ReturnsIncompleteInsteadOfPartialSuccess()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly);

        var incomplete =
            Assert.IsType<PdbSourceProvenanceOutcome.Incomplete>(
                context.InspectSourceProvenance(
                    limits: new(maxTypes: 1)));

        Assert.Equal(
            PdbSourceProvenanceIncompleteReason.TypeLimitExceeded,
            incomplete.Reason);
    }

    [Fact]
    public void GlobalBounds_ReturnTypedIncompleteOutcomes()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.MetadataSourceProvenance.AssemblyPath());
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly);

        AssertIncomplete(
            new(maxDocuments: 1),
            PdbSourceProvenanceIncompleteReason.DocumentLimitExceeded);
        AssertIncomplete(
            new(maxTypes: 1),
            PdbSourceProvenanceIncompleteReason.TypeLimitExceeded);
        AssertIncomplete(
            new(maxAssociations: 1),
            PdbSourceProvenanceIncompleteReason.AssociationLimitExceeded);
        AssertIncomplete(
            new(maxMarkerRows: 1),
            PdbSourceProvenanceIncompleteReason.MarkerLimitExceeded);
        AssertIncomplete(
            new(maxTotalPathCharacters: 1),
            PdbSourceProvenanceIncompleteReason
                .TotalPathCharacterLimitExceeded);

        void AssertIncomplete(
            PdbSourceProvenanceLimits limits,
            PdbSourceProvenanceIncompleteReason expected)
        {
            var incomplete =
                Assert.IsType<PdbSourceProvenanceOutcome.Incomplete>(
                    context.InspectSourceProvenance(limits: limits));
            Assert.Equal(expected, incomplete.Reason);
        }
    }

    [Fact]
    public void PerRowBounds_KeepCompletePopulationsQualifiedAsUnknown()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.MetadataSourceProvenance.AssemblyPath());
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly);

        PdbSourceProvenanceResult pathCharacters = Available(
            new(maxPathCharacters: 1));
        Assert.Contains(
            pathCharacters.Documents,
            document =>
                document.UnknownReason
                    == PdbSourceDocumentUnknownReason
                        .CharacterLimitExceeded);
        Assert.Equal(
            pathCharacters.Types.Length,
            pathCharacters.Receipt.TypeCount);

        PdbSourceProvenanceResult pathSegments = Available(
            new(maxPathSegments: 1));
        Assert.Contains(
            pathSegments.Documents,
            document =>
                document.UnknownReason
                    == PdbSourceDocumentUnknownReason
                        .SegmentLimitExceeded);

        PdbSourceProvenanceResult nesting = Available(
            new(maxNestingDepth: 1));
        PdbTypeSourceEvidence deep = Assert.Single(
            nesting.Types,
            type =>
                type.MetadataName.ToString()
                    == "DeepInheritedCompilerSynthesizedType");
        Assert.Equal(PdbTypeSourceDisposition.Unknown, deep.Disposition);
        Assert.Contains(
            deep.Contributions,
            contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.Unknown);

        PdbSourceProvenanceResult Available(
            PdbSourceProvenanceLimits limits) =>
            Assert.IsType<PdbSourceProvenanceOutcome.Available>(
                context.InspectSourceProvenance(limits: limits)).Result;
    }

    [Fact]
    public void CompilerProducedFixture_PreservesIndependentEvidenceKinds()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.MetadataSourceProvenance.AssemblyPath());
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly);
        PdbSourceProvenanceOutcome outcome =
            context.InspectSourceProvenance();
        Assert.True(
            outcome is PdbSourceProvenanceOutcome.Available,
            outcome.ToString());
        PdbSourceProvenanceResult result =
            ((PdbSourceProvenanceOutcome.Available)outcome).Result;

        PdbSourceDocumentEvidence[] generatedDocuments =
        [
            .. result.Documents.Where(document =>
                document.GeneratedPath?.GeneratorAssembly.ToString()
                    == "System.Text.Json.SourceGeneration"),
        ];
        Assert.NotEmpty(generatedDocuments);
        Assert.All(
            generatedDocuments,
            document =>
            {
                Assert.Equal(
                    PdbSourceDocumentDisposition.GeneratedPathEvidence,
                    document.Disposition);
                Assert.Equal(
                    "System.Text.Json.SourceGeneration.JsonSourceGenerator",
                    document.GeneratedPath!.GeneratorType.ToString());
            });
        Assert.Contains(
            result.Documents,
            document =>
                document is
                {
                    IsEmbedded: true,
                    Disposition:
                        PdbSourceDocumentDisposition.UnknownDocumentEvidence,
                    UnknownReason:
                        PdbSourceDocumentUnknownReason
                            .NoEligibleDecomposition,
                });

        AssertDisposition(
            "OrdinaryType",
            PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
        AssertDisposition(
            "OrdinaryGType",
            PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
        PdbTypeSourceEvidence bodylessMarker = AssertDisposition(
            "MixedMethodMarkerType",
            PdbTypeSourceDisposition.MixedEvidence);
        Assert.Contains(
            bodylessMarker.Contributions,
            contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.MarkerGenerated
                && contribution.Method is not null
                && contribution.DocumentRowId is null);
        AssertDisposition(
            "MixedTypeMarkerType",
            PdbTypeSourceDisposition.MixedEvidence);
        AssertDisposition(
            "CompilerSynthesizedType",
            PdbTypeSourceDisposition.GeneratedEvidenceOnly);
        PdbTypeSourceEvidence inherited = AssertDisposition(
            "InheritedCompilerSynthesizedType",
            PdbTypeSourceDisposition.GeneratedEvidenceOnly);
        Assert.Contains(
            inherited.Contributions,
            contribution =>
                contribution.Inherited
                && contribution.Kind
                    == PdbTypeSourceContributionKind.CompilerSynthesized);
        AssertDisposition(
            "NoDocumentType",
            PdbTypeSourceDisposition.Unknown);
        AssertDisposition(
            "LookalikeMarkedType",
            PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
        PdbTypeSourceEvidence directiveMapped = AssertDisposition(
            "DirectiveMappedType",
            PdbTypeSourceDisposition.GeneratedEvidenceOnly);
        PdbTypeSourceContribution directiveContribution =
            Assert.Single(
                directiveMapped.Contributions,
                contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.MappedGenerated
                && contribution.Marker is null);
        PdbSourceDocumentEvidence directiveDocument =
            result.Documents[
                directiveContribution.DocumentRowId!.Value - 1];
        Assert.True(directiveDocument.IsEmbedded);
        Assert.Equal(
            "/_/evidence/obj/Fixture/Fixture.ExampleGenerator/MappedDirective.cs",
            directiveDocument.Path.ToString());
        Assert.Equal("SHA256", directiveDocument.ChecksumAlgorithm);
        Assert.NotEmpty(directiveDocument.Checksum);
        Assert.Contains(
            result.Types,
            type =>
                type.MetadataName.ToString().Contains(
                    "<AsyncValue>d__",
                    StringComparison.Ordinal)
                && type.Disposition
                    == PdbTypeSourceDisposition.GeneratedEvidenceOnly);

        PdbTypeSourceEvidence AssertDisposition(
            string metadataName,
            PdbTypeSourceDisposition expected)
        {
            PdbTypeSourceEvidence type = Assert.Single(
                result.Types,
                candidate =>
                    candidate.MetadataName.ToString() == metadataName);
            Assert.Equal(expected, type.Disposition);
            return type;
        }
    }

    [Fact]
    public void ResultConstruction_RejectsIncompleteOrForeignRows()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.MetadataSourceProvenance.AssemblyPath());
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenEmbeddedPdbOnly(artifact.Assembly);
        PdbSourceProvenanceOutcome outcome =
            context.InspectSourceProvenance();
        Assert.True(
            outcome is PdbSourceProvenanceOutcome.Available,
            outcome.ToString());
        PdbSourceProvenanceResult result =
            ((PdbSourceProvenanceOutcome.Available)outcome).Result;

        ImmutableArray<PdbSourceDocumentEvidence> duplicateDocuments =
            result.Documents.SetItem(
                0,
                result.Documents[0] with { DocumentRowId = 2 });
        Assert.Throws<ArgumentException>(
            () => new PdbSourceProvenanceResult(
                result.Binding,
                duplicateDocuments,
                result.Types,
                result.Receipt));

        PdbTypeSourceEvidence firstType = result.Types[0];
        ImmutableArray<PdbTypeSourceEvidence> foreignTypes =
            result.Types.SetItem(
                0,
                firstType with
                {
                    Type = MetadataTypeDefinitionAddress.FromToken(
                        Guid.NewGuid(),
                        firstType.Type.Definition.Value),
                });
        Assert.Throws<ArgumentException>(
            () => new PdbSourceProvenanceResult(
                result.Binding,
                result.Documents,
                foreignTypes,
                result.Receipt));

        Assert.Throws<ArgumentException>(
            () => new PdbSourceProvenanceResult(
                result.Binding,
                result.Documents,
                result.Types,
                result.Receipt with
                {
                    UnknownCount = result.Receipt.UnknownCount + 1,
                }));

        int associatedDocumentIndex = Enumerable.Range(
                0,
                result.Documents.Length)
            .First(index => !result.Documents[index].Methods.IsEmpty);
        PdbSourceDocumentEvidence associatedDocument =
            result.Documents[associatedDocumentIndex];
        ImmutableArray<PdbSourceDocumentEvidence> mismatchedAssociations =
            result.Documents.SetItem(
                associatedDocumentIndex,
                associatedDocument with
                {
                    Methods = associatedDocument.Methods.SetItem(
                        0,
                        associatedDocument.Methods[0] with
                        {
                            MetadataToken = 0x0600FFFF,
                        }),
                });
        Assert.Throws<ArgumentException>(
            () => new PdbSourceProvenanceResult(
                result.Binding,
                mismatchedAssociations,
                result.Types,
                result.Receipt));

        Assert.Throws<ArgumentException>(
            () => new PdbSourceProvenanceResult(
                result.Binding,
                result.Documents,
                result.Types,
                result.Receipt with
                {
                    DirectMarkerCount =
                        result.Receipt.DirectMarkerCount + 1,
                }));
    }

    [Fact]
    public void AuthenticMarkerRows_RetainDuplicatesConflictsAndMalformedEvidence()
    {
        (byte[] image, byte[] pdb) = BuildMarkerMetadata();
        ArtifactBoundAssembly artifact = CreateArtifact(image);
        using PdbContext context =
            PdbContext.OpenMetadataOnly(artifact.Assembly);
        context.LoadPdbFromStream(
            new MemoryStream(pdb, writable: false));

        PdbSourceProvenanceOutcome outcome =
            context.InspectSourceProvenance();
        Assert.True(
            outcome is PdbSourceProvenanceOutcome.Available,
            outcome.ToString());
        PdbSourceProvenanceResult result =
            ((PdbSourceProvenanceOutcome.Available)outcome).Result;

        PdbTypeSourceEvidence direct = Type("DirectMarkers");
        Assert.Equal(PdbTypeSourceDisposition.Unknown, direct.Disposition);
        Assert.Equal(5, direct.DirectMarkers.Length);
        Assert.Equal(
            2,
            direct.DirectMarkers.Count(marker =>
                marker is
                {
                    Kind: PdbGenerationMarkerKind.GeneratedCode,
                    Disposition:
                        PdbGenerationMarkerDisposition.Valid,
                }
                && marker.DeclaredTool?.ToString() == "ToolA"
                && marker.DeclaredVersion?.ToString() == "1.0"));
        Assert.Contains(
            direct.DirectMarkers,
            marker =>
                marker is
                {
                    Kind: PdbGenerationMarkerKind.GeneratedCode,
                    Disposition:
                        PdbGenerationMarkerDisposition.Valid,
                }
                && marker.DeclaredTool?.ToString() == "ToolB"
                && marker.DeclaredVersion?.ToString() == "2.0");
        Assert.Equal(
            2,
            direct.Contributions.Count(contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.Unknown));

        PdbTypeSourceEvidence bodyless = Type("BodylessMarker");
        Assert.Equal(
            PdbTypeSourceDisposition.GeneratedEvidenceOnly,
            bodyless.Disposition);
        Assert.Contains(
            bodyless.Contributions,
            contribution =>
                contribution is
                {
                    Kind:
                        PdbTypeSourceContributionKind.MarkerGenerated,
                    Method: not null,
                    DocumentRowId: null,
                });

        PdbTypeSourceEvidence malformedMethod = Type(
            "MalformedMethodMarker");
        Assert.Equal(
            PdbTypeSourceDisposition.Unknown,
            malformedMethod.Disposition);
        Assert.Contains(
            malformedMethod.Contributions,
            contribution =>
                contribution is
                {
                    Kind: PdbTypeSourceContributionKind.Unknown,
                    Method: not null,
                    Marker:
                    {
                        Kind:
                            PdbGenerationMarkerKind.CompilerGenerated,
                        Disposition:
                            PdbGenerationMarkerDisposition.Malformed,
                    },
                });
        PdbSourceDocumentEvidence duplicateEmbedded =
            Assert.Single(result.Documents);
        Assert.True(duplicateEmbedded.IsEmbedded);
        Assert.Equal(
            PdbSourceDocumentDisposition.UnknownDocumentEvidence,
            duplicateEmbedded.Disposition);
        Assert.Equal(
            PdbSourceDocumentUnknownReason
                .DuplicateEmbeddedSourceEvidence,
            duplicateEmbedded.UnknownReason);

        PdbTypeSourceEvidence Type(string name) =>
            Assert.Single(
                result.Types,
                type => type.MetadataName.ToString() == name);
    }

    private static ArtifactBoundAssembly CreateArtifact(byte[] image)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                _ => new MemoryStream(image, writable: false));
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => new MemoryStream(image, writable: false));
        authority.CompleteAdmission(admission);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                contribution.Registration,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("test"))
            ?? throw new InvalidOperationException(
                "The fixture was expected to contain managed metadata.");
        return new(
            authority,
            contribution.Registration,
            assembly);
    }

    private static (byte[] Image, byte[] Pdb) BuildMarkerMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Markers.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Markers"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    Convert.FromHexString("b03f5f7f11d50a3a")),
                default,
                default);
        TypeReferenceHandle systemObject = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        TypeReferenceHandle generatedCode = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System.CodeDom.Compiler"),
            metadata.GetOrAddString("GeneratedCodeAttribute"));
        TypeReferenceHandle compilerGenerated = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerGeneratedAttribute"));

        MemberReferenceHandle generatedConstructor =
            AddConstructor(metadata, generatedCode, stringCount: 2);
        MemberReferenceHandle compilerConstructor =
            AddConstructor(metadata, compilerGenerated, stringCount: 0);
        BlobHandle methodSignature =
            metadata.GetOrAddBlob(MethodSignature());
        MethodDefinitionHandle bodylessMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.Managed,
                metadata.GetOrAddString("Generated"),
                methodSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle malformedMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.Managed,
                metadata.GetOrAddString("Malformed"),
                methodSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            bodylessMethod);
        TypeDefinitionHandle directMarkers =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Fixture"),
                metadata.GetOrAddString("DirectMarkers"),
                systemObject,
                MetadataTokens.FieldDefinitionHandle(1),
                bodylessMethod);
        TypeDefinitionHandle bodyless =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Fixture"),
                metadata.GetOrAddString("BodylessMarker"),
                systemObject,
                MetadataTokens.FieldDefinitionHandle(1),
                bodylessMethod);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Fixture"),
            metadata.GetOrAddString("MalformedMethodMarker"),
            systemObject,
            MetadataTokens.FieldDefinitionHandle(1),
            malformedMethod);

        BlobHandle toolA = metadata.GetOrAddBlob(
            GeneratedCodeValue("ToolA", "1.0"));
        metadata.AddCustomAttribute(
            directMarkers,
            generatedConstructor,
            toolA);
        metadata.AddCustomAttribute(
            directMarkers,
            generatedConstructor,
            toolA);
        metadata.AddCustomAttribute(
            directMarkers,
            generatedConstructor,
            metadata.GetOrAddBlob(
                GeneratedCodeValue("ToolB", "2.0")));
        metadata.AddCustomAttribute(
            directMarkers,
            generatedConstructor,
            metadata.GetOrAddBlob(new byte[] { 1, 0 }));
        metadata.AddCustomAttribute(
            directMarkers,
            compilerConstructor,
            metadata.GetOrAddBlob(new byte[] { 1, 0, 1 }));
        metadata.AddCustomAttribute(
            bodylessMethod,
            generatedConstructor,
            toolA);
        metadata.AddCustomAttribute(
            malformedMethod,
            compilerConstructor,
            metadata.GetOrAddBlob(new byte[] { 1, 0, 1 }));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        pe.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();

        int[] rowCounts = new int[64];
        using (var reader = new PEReader(
                   new MemoryStream(image, writable: false)))
        {
            MetadataReader imageMetadata = reader.GetMetadataReader();
            foreach (TableIndex table in Enum.GetValues<TableIndex>())
            {
                int index = (int)table;
                if ((uint)index < (uint)rowCounts.Length)
                {
                    rowCounts[index] =
                        imageMetadata.GetTableRowCount(table);
                }
            }
        }
        var pdbMetadata = new MetadataBuilder();
        DocumentHandle document = pdbMetadata.AddDocument(
            pdbMetadata.GetOrAddDocumentName(
                "/repo/obj/Tool/Tool.ExampleGenerator/Hint.g.cs"),
            default,
            default,
            default);
        GuidHandle embeddedSource = pdbMetadata.GetOrAddGuid(
            new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE"));
        BlobHandle embeddedValue = pdbMetadata.GetOrAddBlob(
            new byte[] { 0, 0, 0, 0 });
        pdbMetadata.AddCustomDebugInformation(
            document,
            embeddedSource,
            embeddedValue);
        pdbMetadata.AddCustomDebugInformation(
            document,
            embeddedSource,
            embeddedValue);
        pdbMetadata.AddMethodDebugInformation(default, default);
        pdbMetadata.AddMethodDebugInformation(default, default);
        var pdbBuilder = new PortablePdbBuilder(
            pdbMetadata,
            ImmutableArray.Create(rowCounts),
            default);
        var pdbImage = new BlobBuilder();
        pdbBuilder.Serialize(pdbImage);
        return (image, pdbImage.ToArray());

        static MemberReferenceHandle AddConstructor(
            MetadataBuilder metadata,
            TypeReferenceHandle type,
            int stringCount)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true).Parameters(
                stringCount,
                returnType => returnType.Void(),
                parameters =>
                {
                    for (int index = 0; index < stringCount; index++)
                        parameters.AddParameter().Type().String();
                });
            return metadata.AddMemberReference(
                type,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(signature));
        }

        static BlobBuilder MethodSignature()
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
            return signature;
        }

        static BlobBuilder GeneratedCodeValue(
            string tool,
            string version)
        {
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteSerializedString(tool);
            value.WriteSerializedString(version);
            value.WriteUInt16(0);
            return value;
        }
    }

    private sealed record ArtifactBoundAssembly(
        ArtifactGenerationAuthority Authority,
        ArtifactAcquisitionRegistration Registration,
        ResolvedAssemblyReference Assembly);

    private sealed class TestArtifactProvenance : IArtifactProvenance
    {
        public static TestArtifactProvenance Instance { get; } = new();
    }
}
